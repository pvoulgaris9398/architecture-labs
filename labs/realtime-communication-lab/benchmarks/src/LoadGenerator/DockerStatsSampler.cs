using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

namespace LoadGenerator;

internal sealed class DockerStatsSampler(string containerName)
{
    private readonly ConcurrentBag<ResourceSample> _samples = [];

    public IReadOnlyList<ResourceSample> Samples =>
        _samples.OrderBy(sample => sample.CapturedAt).ToArray();

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var captures = new List<Task> { CaptureAsync(DateTimeOffset.UtcNow) };
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
                captures.Add(CaptureAsync(DateTimeOffset.UtcNow));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }

        await Task.WhenAll(captures);
    }

    private async Task CaptureAsync(DateTimeOffset capturedAt)
    {
        var startInfo = new ProcessStartInfo("docker")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("stats");
        startInfo.ArgumentList.Add("--no-stream");
        startInfo.ArgumentList.Add("--format");
        startInfo.ArgumentList.Add("{{json .}}");
        startInfo.ArgumentList.Add(containerName);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start docker stats.");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"docker stats failed: {error.Trim()}");

        var fields = JsonSerializer.Deserialize<Dictionary<string, string>>(output)
            ?? throw new InvalidOperationException("docker stats returned no data.");
        _samples.Add(
            new ResourceSample(
                capturedAt,
                Get(fields, "CPUPerc"),
                Get(fields, "MemUsage"),
                Get(fields, "MemPerc"),
                Get(fields, "NetIO"),
                Get(fields, "BlockIO"),
                Get(fields, "PIDs")
            )
        );
    }

    private static string Get(Dictionary<string, string> fields, string name) =>
        fields.GetValueOrDefault(name, "unavailable");
}
