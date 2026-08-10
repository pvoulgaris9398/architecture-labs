using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LoadGenerator;

internal static partial class ResultSummarizer
{
    public static async Task<int> RunAsync(string sessionDirectory)
    {
        var directory = Path.GetFullPath(sessionDirectory);
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException(directory);

        var runs = new List<RunSummary>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.json").Order())
        {
            if (!CanonicalResultName().IsMatch(Path.GetFileName(path)))
                continue;
            await using var stream = File.OpenRead(path);
            using var document = await JsonDocument.ParseAsync(stream);
            var root = document.RootElement;
            var schedule = root.GetProperty("PublisherSchedule");
            runs.Add(
                new RunSummary(
                    Path.GetFileName(path),
                    root.GetProperty("Transport").GetString()!,
                    root.GetProperty("Subscribers").GetInt32(),
                    root.GetProperty("TargetRate").GetInt32(),
                    root.GetProperty("PayloadBytes").GetInt32(),
                    root.GetProperty("P50Milliseconds").GetDouble(),
                    root.GetProperty("P95Milliseconds").GetDouble(),
                    root.GetProperty("P99Milliseconds").GetDouble(),
                    root.GetProperty("MaximumMilliseconds").GetDouble(),
                    root.GetProperty("MissingDeliveries").GetInt32(),
                    root.GetProperty("DuplicateDeliveries").GetInt32(),
                    root.GetProperty("OutOfOrderDeliveries").GetInt64(),
                    root.GetProperty("PublishFailures").GetInt32(),
                    root.GetProperty("SubscriberFailures").GetArrayLength(),
                    root.GetProperty("ReliabilityPassed").GetBoolean(),
                    schedule.GetProperty("Passed").GetBoolean(),
                    schedule.GetProperty("AchievedRatePerSecond").GetDouble(),
                    schedule.GetProperty("P99LagMilliseconds").GetDouble()
                )
            );
        }

        if (runs.Count == 0)
        {
            Console.Error.WriteLine($"No canonical benchmark results found in {directory}.");
            return 4;
        }

        var groups = runs
            .GroupBy(run => new { run.Transport, run.Subscribers, run.TargetRate, run.PayloadBytes })
            .OrderBy(group => group.Key.Subscribers)
            .ThenBy(group => group.Key.TargetRate)
            .ThenBy(group => group.Key.Transport)
            .Select(group =>
            {
                var items = group.ToArray();
                return new ProfileSummary(
                    group.Key.Transport,
                    group.Key.Subscribers,
                    group.Key.TargetRate,
                    group.Key.PayloadBytes,
                    items.Length,
                    items.Count(item => item.SchedulePassed),
                    items.Count(item => item.ReliabilityPassed),
                    Median(items.Select(item => item.AchievedRate)),
                    Median(items.Select(item => item.P99ScheduleLag)),
                    Median(items.Select(item => item.P50)),
                    Median(items.Select(item => item.P95)),
                    Median(items.Select(item => item.P99)),
                    items.Min(item => item.P95),
                    items.Max(item => item.P95),
                    items.Max(item => item.Maximum),
                    items.Sum(item => item.Missing),
                    items.Sum(item => item.Duplicates),
                    items.Sum(item => item.OutOfOrder),
                    items.Sum(item => item.PublishFailures),
                    items.Sum(item => item.Disconnects)
                );
            })
            .ToArray();

        var summary = new SessionSummary(
            DateTimeOffset.UtcNow,
            directory,
            runs.Count,
            groups,
            runs
        );
        var json = JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true });
        await WriteAtomicallyAsync(Path.Combine(directory, "summary.json"), json + Environment.NewLine);
        await WriteAtomicallyAsync(Path.Combine(directory, "summary.md"), BuildMarkdown(summary));
        Console.WriteLine($"Summarized {runs.Count} runs in {directory}");
        return 0;
    }

    private static string BuildMarkdown(SessionSummary summary)
    {
        var text = new StringBuilder();
        text.AppendLine("# Realtime transport benchmark summary");
        text.AppendLine();
        text.AppendLine($"Generated: {summary.GeneratedAt:O}");
        text.AppendLine();
        text.AppendLine("This is a mechanical aggregation of local results, not a conclusion.");
        text.AppendLine();
        text.AppendLine("| Transport | Subscribers | Rate/s | Runs | Schedule pass | Reliability pass | Median p50 ms | Median p95 ms | Median p99 ms | p95 range ms | Missing | Duplicates | Out of order | Publish failures | Disconnects |");
        text.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
        foreach (var profile in summary.Profiles)
        {
            text.AppendLine(
                $"| {profile.Transport} | {profile.Subscribers} | {profile.TargetRate} | {profile.Runs} | {profile.SchedulePasses}/{profile.Runs} | {profile.ReliabilityPasses}/{profile.Runs} | {profile.MedianP50Milliseconds:F2} | {profile.MedianP95Milliseconds:F2} | {profile.MedianP99Milliseconds:F2} | {profile.MinimumP95Milliseconds:F2}-{profile.MaximumP95Milliseconds:F2} | {profile.MissingDeliveries} | {profile.DuplicateDeliveries} | {profile.OutOfOrderDeliveries} | {profile.PublishFailures} | {profile.Disconnects} |"
            );
        }
        text.AppendLine();
        text.AppendLine("Review the raw JSON, environment metadata, resource samples, and Prometheus snapshots before drawing conclusions.");
        return text.ToString();
    }

    private static double Median(IEnumerable<double> source)
    {
        var values = source.Order().ToArray();
        var midpoint = values.Length / 2;
        return values.Length % 2 == 0
            ? (values[midpoint - 1] + values[midpoint]) / 2
            : values[midpoint];
    }

    private static async Task WriteAtomicallyAsync(string path, string content)
    {
        var temporaryPath = path + ".tmp";
        await File.WriteAllTextAsync(temporaryPath, content);
        File.Move(temporaryPath, path, overwrite: true);
    }

    [GeneratedRegex("^(websocket|sse|long-polling)-s[0-9]+-r[0-9]+-run[0-9]+[.]json$", RegexOptions.IgnoreCase)]
    private static partial Regex CanonicalResultName();

    private sealed record RunSummary(
        string File,
        string Transport,
        int Subscribers,
        int TargetRate,
        int PayloadBytes,
        double P50,
        double P95,
        double P99,
        double Maximum,
        int Missing,
        int Duplicates,
        long OutOfOrder,
        int PublishFailures,
        int Disconnects,
        bool ReliabilityPassed,
        bool SchedulePassed,
        double AchievedRate,
        double P99ScheduleLag
    );

    private sealed record ProfileSummary(
        string Transport,
        int Subscribers,
        int TargetRate,
        int PayloadBytes,
        int Runs,
        int SchedulePasses,
        int ReliabilityPasses,
        double MedianAchievedRate,
        double MedianP99ScheduleLagMilliseconds,
        double MedianP50Milliseconds,
        double MedianP95Milliseconds,
        double MedianP99Milliseconds,
        double MinimumP95Milliseconds,
        double MaximumP95Milliseconds,
        double MaximumLatencyMilliseconds,
        int MissingDeliveries,
        int DuplicateDeliveries,
        long OutOfOrderDeliveries,
        int PublishFailures,
        int Disconnects
    );

    private sealed record SessionSummary(
        DateTimeOffset GeneratedAt,
        string SessionDirectory,
        int RunCount,
        IReadOnlyList<ProfileSummary> Profiles,
        IReadOnlyList<RunSummary> Runs
    );
}
