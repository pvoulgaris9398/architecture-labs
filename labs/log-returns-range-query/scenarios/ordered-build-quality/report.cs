#!/usr/bin/env -S dotnet run
#:package Microsoft.Data.SqlClient@7.0.2

using System.Globalization;
using System.Text;
using Microsoft.Data.SqlClient;

internal static class Report
{
    private const string ScenarioId = "ordered-build-quality-sweep";
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static async Task<int> Main()
    {
        try
        {
            CultureInfo.DefaultThreadCurrentCulture = Invariant;
            CultureInfo.DefaultThreadCurrentUICulture = Invariant;
            await Generate();
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Report generation failed: {exception.Message}");
            return 1;
        }
    }

    private static async Task Generate()
    {
        var labDirectory = FindLabDirectory();
        var connectionString = new SqlConnectionStringBuilder
        {
            DataSource = "localhost,1435",
            InitialCatalog = "LogReturnsLab",
            UserID = "sa",
            Password = ReadPassword(labDirectory),
            Encrypt = true,
            TrustServerCertificate = true,
            ConnectTimeout = 10
        }.ConnectionString;

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        var run = await ReadLatestRun(connection)
            ?? throw new InvalidOperationException("No complete ordered-build-quality run was found.");
        var samples = await ReadSamples(connection, run.RunId);
        var builds = await ReadBuilds(connection, run.RunId);
        var segments = await ReadSegments(connection, run.RunId);
        var summaries = Summarize(samples);

        if (samples.Count == 0 || builds.Count != 2 || segments.Count == 0)
            throw new InvalidOperationException("The selected run is missing required comparison evidence.");

        var outputDirectory = Path.Combine(
            labDirectory,
            "scenarios",
            "ordered-build-quality",
            "results",
            "local",
            run.RunId.ToString("D"));
        Directory.CreateDirectory(outputDirectory);

        var chartPath = Path.Combine(outputDirectory, "query-comparison.svg");
        var reportPath = Path.Combine(outputDirectory, "summary.md");
        await File.WriteAllTextAsync(chartPath, BuildSvg(summaries));
        await File.WriteAllTextAsync(
            reportPath,
            BuildMarkdown(run, samples, builds, segments, summaries));

        Console.WriteLine($"Report: {reportPath}");
        Console.WriteLine($"Chart:  {chartPath}");
    }

    private static string FindLabDirectory()
    {
        var directory = new DirectoryInfo(Environment.CurrentDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "docker-compose.yaml"))
                && Directory.Exists(Path.Combine(directory.FullName, "scenarios")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            "Run this file from the log-returns-range-query lab or one of its subdirectories.");
    }

    private static string ReadPassword(string labDirectory)
    {
        var value = Environment.GetEnvironmentVariable("MSSQL_SA_PASSWORD");
        if (!string.IsNullOrWhiteSpace(value))
            return value;

        var envPath = Path.Combine(labDirectory, ".env");
        if (!File.Exists(envPath))
            throw new InvalidOperationException(
                "Set MSSQL_SA_PASSWORD or create the lab's .env file before generating a report.");

        const string key = "MSSQL_SA_PASSWORD=";
        foreach (var rawLine in File.ReadLines(envPath))
        {
            var line = rawLine.Trim();
            if (line.StartsWith(key, StringComparison.Ordinal))
                return line[key.Length..].Trim().Trim('"', '\'');
        }

        throw new InvalidOperationException("MSSQL_SA_PASSWORD was not found in the lab's .env file.");
    }

    private static async Task<RunInfo?> ReadLatestRun(SqlConnection connection)
    {
        const string sql = """
            SELECT TOP (1)
                run.run_id,
                run.started_at,
                run.completed_at,
                run.sql_server_version
            FROM dbo.ExperimentRun run
            CROSS APPLY
            (
                SELECT
                    COUNT(*) AS sample_count,
                    COUNT(DISTINCT sample.asset_id) AS asset_count,
                    COUNT(DISTINCT sample.sample_point) AS point_count,
                    MAX(sample.repetition) AS repetition_count
                FROM dbo.BenchmarkSample sample
                WHERE sample.run_id = run.run_id
                  AND sample.scenario_id = @scenario_id
            ) shape
            WHERE run.status = 'passed'
              AND shape.sample_count > 0
              AND shape.sample_count =
                  shape.asset_count * shape.point_count * shape.repetition_count * 2
              AND NOT EXISTS
              (
                  SELECT sample.asset_id, sample.sample_point, sample.repetition
                  FROM dbo.BenchmarkSample sample
                  WHERE sample.run_id = run.run_id
                    AND sample.scenario_id = @scenario_id
                  GROUP BY sample.asset_id, sample.sample_point, sample.repetition
                  HAVING COUNT(*) <> 2
                      OR ABS(MAX(sample.checksum) - MIN(sample.checksum)) > 1e-10
              )
              AND (SELECT COUNT(*) FROM dbo.OrderedBuildSegmentResult segment
                   WHERE segment.run_id = run.run_id) > 0
            ORDER BY run.completed_at DESC;
            """;

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@scenario_id", ScenarioId);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;

        return new RunInfo(
            reader.GetGuid(0),
            reader.GetDateTime(1),
            reader.GetDateTime(2),
            reader.GetString(3));
    }

    private static async Task<List<Sample>> ReadSamples(SqlConnection connection, Guid runId)
    {
        const string sql = """
            SELECT asset_id, observation_count, repetition, storage_type,
                execution_position, executions_per_sample, elapsed_microseconds, checksum
            FROM dbo.BenchmarkSample
            WHERE run_id = @run_id AND scenario_id = @scenario_id;
            """;
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@run_id", runId);
        command.Parameters.AddWithValue("@scenario_id", ScenarioId);
        await using var reader = await command.ExecuteReaderAsync();
        var values = new List<Sample>();

        while (await reader.ReadAsync())
        {
            var executions = reader.GetInt16(5);
            values.Add(new Sample(
                reader.GetInt32(0),
                reader.GetInt64(1),
                reader.GetInt32(2),
                reader.GetString(3),
                reader.GetByte(4),
                executions,
                reader.GetInt64(6),
                reader.IsDBNull(7) ? null : reader.GetDouble(7),
                (double)reader.GetInt64(6) / executions));
        }

        return values;
    }

    private static async Task<List<BuildResult>> ReadBuilds(SqlConnection connection, Guid runId)
    {
        const string sql = """
            SELECT storage_type, elapsed_ms
            FROM dbo.ScenarioResult
            WHERE run_id = @run_id
              AND scenario_id = 'ordered-build-quality-build'
              AND result_type = 'benchmark';
            """;
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@run_id", runId);
        await using var reader = await command.ExecuteReaderAsync();
        var values = new List<BuildResult>();
        while (await reader.ReadAsync())
            values.Add(new BuildResult(reader.GetString(0), reader.GetInt64(1)));
        return values;
    }

    private static async Task<List<Segment>> ReadSegments(SqlConnection connection, Guid runId)
    {
        const string sql = """
            SELECT storage_type, segment_id, row_count,
                minimum_asset_id, maximum_asset_id,
                minimum_date_id, maximum_date_id, on_disk_size
            FROM dbo.OrderedBuildSegmentResult
            WHERE run_id = @run_id;
            """;
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@run_id", runId);
        await using var reader = await command.ExecuteReaderAsync();
        var values = new List<Segment>();
        while (await reader.ReadAsync())
        {
            values.Add(new Segment(
                reader.GetString(0), reader.GetInt32(1), reader.GetInt32(2),
                reader.GetInt64(3), reader.GetInt64(4), reader.GetInt64(5),
                reader.GetInt64(6), reader.GetInt64(7)));
        }
        return values;
    }

    private static List<PointSummary> Summarize(IReadOnlyCollection<Sample> samples) =>
        samples.GroupBy(sample => new { sample.ObservationCount, sample.StorageType })
            .Select(group =>
            {
                var values = group.Select(sample => sample.MicrosecondsPerExecution)
                    .OrderBy(value => value).ToArray();
                return new PointSummary(
                    group.Key.ObservationCount,
                    group.Key.StorageType,
                    Percentile(values, 0.25),
                    Percentile(values, 0.50),
                    Percentile(values, 0.75));
            })
            .OrderBy(summary => summary.ObservationCount)
            .ThenBy(summary => summary.StorageType)
            .ToList();

    private static double Percentile(IReadOnlyList<double> sortedValues, double percentile)
    {
        var position = (sortedValues.Count - 1) * percentile;
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        return lower == upper
            ? sortedValues[lower]
            : sortedValues[lower]
                + (sortedValues[upper] - sortedValues[lower]) * (position - lower);
    }

    private static string BuildMarkdown(
        RunInfo run,
        IReadOnlyCollection<Sample> samples,
        IReadOnlyCollection<BuildResult> builds,
        IReadOnlyCollection<Segment> segments,
        IReadOnlyCollection<PointSummary> summaries)
    {
        var assets = samples.Select(sample => sample.AssetId).Distinct().OrderBy(id => id).ToArray();
        var points = summaries.Select(summary => summary.ObservationCount).Distinct().OrderBy(x => x);
        var partialBuild = builds.Single(build => build.StorageType == "partial-order");
        var fullBuild = builds.Single(build => build.StorageType == "full-order");
        var checksumMismatches = samples
            .GroupBy(sample => new { sample.AssetId, sample.ObservationCount, sample.Repetition })
            .Count(group => Math.Abs(
                group.Max(sample => sample.Checksum ?? 0)
                - group.Min(sample => sample.Checksum ?? 0)) > 1e-10);
        var builder = new StringBuilder();

        builder.AppendLine("# Ordered Columnstore Build Quality Results");
        builder.AppendLine();
        builder.AppendLine($"- Run ID: `{run.RunId:D}`");
        builder.AppendLine($"- Started: `{run.StartedAt:yyyy-MM-dd HH:mm:ss}`");
        builder.AppendLine($"- Completed: `{run.CompletedAt:yyyy-MM-dd HH:mm:ss}`");
        builder.AppendLine($"- SQL Server: `{run.SqlServerVersion}`");
        builder.AppendLine($"- Assets: {assets.Length} (`{string.Join(", ", assets)}`)");
        builder.AppendLine($"- Retained samples: {samples.Count:N0}");
        builder.AppendLine($"- Paired checksum mismatches: {checksumMismatches}");
        builder.AppendLine();
        builder.AppendLine("## Build cost");
        builder.AppendLine();
        builder.AppendLine("| Design | Build time | Relative to partial order |");
        builder.AppendLine("| --- | ---: | ---: |");
        builder.AppendLine($"| Partial order | {Duration(partialBuild.ElapsedMilliseconds)} | 1.00× |");
        builder.AppendLine($"| Full order | {Duration(fullBuild.ElapsedMilliseconds)} | {(double)fullBuild.ElapsedMilliseconds / partialBuild.ElapsedMilliseconds:F2}× |");
        builder.AppendLine();
        builder.AppendLine("## Segment elimination opportunity");
        builder.AppendLine();
        builder.AppendLine("Candidate segments are segments whose stored asset bounds include the sampled asset.");
        builder.AppendLine();
        builder.AppendLine("| Asset | Partial segments | Partial candidate rows | Full segments | Full candidate rows |");
        builder.AppendLine("| ---: | ---: | ---: | ---: | ---: |");
        foreach (var asset in assets)
        {
            var partial = Coverage(segments, "partial-order", asset);
            var full = Coverage(segments, "full-order", asset);
            builder.AppendLine($"| {asset} | {partial.Segments} | {partial.Rows:N0} | {full.Segments} | {full.Rows:N0} |");
        }

        builder.AppendLine();
        builder.AppendLine("![Partial-order and full-order query comparison](query-comparison.svg)");
        builder.AppendLine();
        builder.AppendLine("## Pooled query results");
        builder.AppendLine();
        builder.AppendLine("Medians and quartiles pool retained samples from all sampled assets.");
        builder.AppendLine();
        builder.AppendLine("| Observations | Partial median (µs) | Full median (µs) | Full/partial | Faster |");
        builder.AppendLine("| ---: | ---: | ---: | ---: | --- |");
        foreach (var point in points)
        {
            var partial = summaries.Single(value =>
                value.ObservationCount == point && value.StorageType == "partial-order");
            var full = summaries.Single(value =>
                value.ObservationCount == point && value.StorageType == "full-order");
            builder.AppendLine(
                $"| {point:N0} | {partial.Median:F2} | {full.Median:F2} "
                + $"| {full.Median / partial.Median:F2}× "
                + $"| {(full.Median <= partial.Median ? "full order" : "partial order")} |");
        }

        builder.AppendLine();
        builder.AppendLine("## Interpretation limits");
        builder.AppendLine();
        builder.AppendLine(
            "This run compares initial index construction and warm-cache, single-asset queries with "
            + "`MAXDOP 1`. It does not measure incremental loading, later DML, rebuild maintenance, "
            + "concurrent workloads, parallel queries, or `tempdb` resource consumption.");
        return builder.ToString();
    }

    private static CoverageResult Coverage(
        IReadOnlyCollection<Segment> segments,
        string storageType,
        int assetId)
    {
        var candidates = segments.Where(segment =>
            segment.StorageType == storageType
            && assetId >= segment.MinimumAssetId
            && assetId <= segment.MaximumAssetId).ToArray();
        return new CoverageResult(candidates.Length, candidates.Sum(segment => (long)segment.RowCount));
    }

    private static string Duration(long milliseconds) =>
        TimeSpan.FromMilliseconds(milliseconds).ToString(@"hh\:mm\:ss\.fff", Invariant);

    private static string BuildSvg(IReadOnlyCollection<PointSummary> summaries)
    {
        const int width = 1000, height = 600, left = 90, right = 35, top = 55, bottom = 80;
        var points = summaries.Select(summary => summary.ObservationCount).Distinct().OrderBy(x => x).ToArray();
        var maximum = summaries.Max(summary => summary.P75) * 1.08;
        double X(long value) => left + (double)(value - points.Min()) / (points.Max() - points.Min())
            * (width - left - right);
        double Y(double value) => height - bottom - value / maximum * (height - top - bottom);
        string Number(double value) => value.ToString("0.##", Invariant);
        var builder = new StringBuilder();

        builder.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width}\" height=\"{height}\" viewBox=\"0 0 {width} {height}\">");
        builder.AppendLine("<rect width=\"100%\" height=\"100%\" fill=\"#ffffff\"/>");
        builder.AppendLine("<style>text{font-family:Segoe UI,Arial,sans-serif;fill:#24292f}.grid{stroke:#d8dee4}.axis{stroke:#57606a;stroke-width:1.5}.label{font-size:13px}.title{font-size:21px;font-weight:600}.legend{font-size:14px}</style>");
        builder.AppendLine($"<text x=\"{width / 2}\" y=\"30\" text-anchor=\"middle\" class=\"title\">Partial-order vs. full-order columnstore</text>");

        for (var tick = 0; tick <= 5; tick++)
        {
            var value = maximum * tick / 5;
            var y = Y(value);
            builder.AppendLine($"<line x1=\"{left}\" y1=\"{Number(y)}\" x2=\"{width - right}\" y2=\"{Number(y)}\" class=\"grid\"/>");
            builder.AppendLine($"<text x=\"{left - 12}\" y=\"{Number(y + 5)}\" text-anchor=\"end\" class=\"label\">{value:F0}</text>");
        }

        foreach (var point in points)
        {
            var x = X(point);
            builder.AppendLine($"<line x1=\"{Number(x)}\" y1=\"{top}\" x2=\"{Number(x)}\" y2=\"{height - bottom}\" class=\"grid\"/>");
            builder.AppendLine($"<text x=\"{Number(x)}\" y=\"{height - bottom + 24}\" text-anchor=\"middle\" class=\"label\">{point:N0}</text>");
        }

        builder.AppendLine($"<line x1=\"{left}\" y1=\"{height - bottom}\" x2=\"{width - right}\" y2=\"{height - bottom}\" class=\"axis\"/>");
        builder.AppendLine($"<line x1=\"{left}\" y1=\"{top}\" x2=\"{left}\" y2=\"{height - bottom}\" class=\"axis\"/>");
        builder.AppendLine($"<text x=\"{(left + width - right) / 2}\" y=\"{height - 22}\" text-anchor=\"middle\" class=\"label\">Observations per asset</text>");
        builder.AppendLine($"<text x=\"20\" y=\"{(top + height - bottom) / 2}\" transform=\"rotate(-90 20 {(top + height - bottom) / 2})\" text-anchor=\"middle\" class=\"label\">Microseconds per execution</text>");
        AppendSeries(builder, summaries, "partial-order", "#8250df", X, Y, Number);
        AppendSeries(builder, summaries, "full-order", "#1a7f37", X, Y, Number);
        builder.AppendLine($"<line x1=\"{width - 270}\" y1=\"22\" x2=\"{width - 235}\" y2=\"22\" stroke=\"#8250df\" stroke-width=\"3\"/><text x=\"{width - 227}\" y=\"27\" class=\"legend\">partial order</text>");
        builder.AppendLine($"<line x1=\"{width - 130}\" y1=\"22\" x2=\"{width - 95}\" y2=\"22\" stroke=\"#1a7f37\" stroke-width=\"3\"/><text x=\"{width - 87}\" y=\"27\" class=\"legend\">full order</text>");
        builder.AppendLine("</svg>");
        return builder.ToString();
    }

    private static void AppendSeries(
        StringBuilder builder,
        IReadOnlyCollection<PointSummary> summaries,
        string storageType,
        string color,
        Func<long, double> x,
        Func<double, double> y,
        Func<double, string> number)
    {
        var series = summaries.Where(summary => summary.StorageType == storageType)
            .OrderBy(summary => summary.ObservationCount).ToArray();
        var upper = series.Select(point => $"{number(x(point.ObservationCount))},{number(y(point.P75))}");
        var lower = series.Reverse().Select(point => $"{number(x(point.ObservationCount))},{number(y(point.P25))}");
        builder.AppendLine($"<polygon points=\"{string.Join(" ", upper.Concat(lower))}\" fill=\"{color}\" opacity=\"0.14\"/>");
        var median = series.Select(point => $"{number(x(point.ObservationCount))},{number(y(point.Median))}");
        builder.AppendLine($"<polyline points=\"{string.Join(" ", median)}\" fill=\"none\" stroke=\"{color}\" stroke-width=\"3\"/>");
        foreach (var point in series)
            builder.AppendLine($"<circle cx=\"{number(x(point.ObservationCount))}\" cy=\"{number(y(point.Median))}\" r=\"4\" fill=\"{color}\"/>");
    }

    private sealed record RunInfo(Guid RunId, DateTime StartedAt, DateTime CompletedAt, string SqlServerVersion);
    private sealed record Sample(int AssetId, long ObservationCount, int Repetition,
        string StorageType, byte ExecutionPosition, short ExecutionsPerSample,
        long ElapsedMicroseconds, double? Checksum, double MicrosecondsPerExecution);
    private sealed record BuildResult(string StorageType, long ElapsedMilliseconds);
    private sealed record Segment(string StorageType, int SegmentId, int RowCount,
        long MinimumAssetId, long MaximumAssetId, long MinimumDateId,
        long MaximumDateId, long OnDiskSize);
    private sealed record PointSummary(long ObservationCount, string StorageType,
        double P25, double Median, double P75);
    private sealed record CoverageResult(int Segments, long Rows);
}
