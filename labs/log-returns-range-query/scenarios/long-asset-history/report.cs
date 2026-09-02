#!/usr/bin/env -S dotnet run

#:package Microsoft.Data.SqlClient@7.0.2

using System.Globalization;
using System.Text;
using Microsoft.Data.SqlClient;

internal static class Report
{
    private const string ScenarioId = "long-asset-history-sweep";
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static async Task Main()
    {
        CultureInfo.DefaultThreadCurrentCulture = Invariant;
        CultureInfo.DefaultThreadCurrentUICulture = Invariant;
        var labDirectory = FindLabDirectory();
        var password = ReadPassword(labDirectory);
        var connectionString = new SqlConnectionStringBuilder
        {
            DataSource = "localhost,1435",
            InitialCatalog = "LogReturnsLab",
            UserID = "sa",
            Password = password,
            Encrypt = true,
            TrustServerCertificate = true,
            ConnectTimeout = 10
        }.ConnectionString;

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        var run = await ReadLatestRun(connection)
            ?? throw new InvalidOperationException($"No successful {ScenarioId} run was found.");
        var samples = await ReadSamples(connection, run.RunId);

        if (samples.Count == 0)
            throw new InvalidOperationException("The latest run contains no benchmark samples.");

        var summaries = Summarize(samples);
        var assetCrossovers = FindAssetCrossovers(samples);
        var outputDirectory = Path.Combine(
            labDirectory,
            "scenarios",
            "long-asset-history",
            "results",
            "local",
            run.RunId.ToString("D"));

        Directory.CreateDirectory(outputDirectory);
        var chartPath = Path.Combine(outputDirectory, "crossover.svg");
        var reportPath = Path.Combine(outputDirectory, "summary.md");

        await File.WriteAllTextAsync(chartPath, BuildSvg(summaries));
        await File.WriteAllTextAsync(
            reportPath,
            BuildMarkdown(run, samples, summaries, assetCrossovers));

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
        var environmentPassword = Environment.GetEnvironmentVariable("MSSQL_SA_PASSWORD");
        if (!string.IsNullOrWhiteSpace(environmentPassword))
            return environmentPassword;

        var envPath = Path.Combine(labDirectory, ".env");
        if (!File.Exists(envPath))
            throw new InvalidOperationException(
                "Set MSSQL_SA_PASSWORD or create the lab's .env file before generating a report.");

        foreach (var rawLine in File.ReadLines(envPath))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            const string key = "MSSQL_SA_PASSWORD=";
            if (!line.StartsWith(key, StringComparison.Ordinal))
                continue;

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
            WHERE run.status = 'passed'
              AND EXISTS
              (
                  SELECT 1
                  FROM dbo.BenchmarkSample sample
                  WHERE sample.run_id = run.run_id
                    AND sample.scenario_id = @scenario_id
              )
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
            SELECT
                asset_id,
                observation_count,
                repetition,
                storage_type,
                execution_position,
                executions_per_sample,
                elapsed_microseconds,
                checksum
            FROM dbo.BenchmarkSample
            WHERE run_id = @run_id
              AND scenario_id = @scenario_id
            ORDER BY observation_count, asset_id, repetition, storage_type;
            """;

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@run_id", runId);
        command.Parameters.AddWithValue("@scenario_id", ScenarioId);
        await using var reader = await command.ExecuteReaderAsync();
        var samples = new List<Sample>();

        while (await reader.ReadAsync())
        {
            var executions = reader.GetInt16(5);
            samples.Add(new Sample(
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

        return samples;
    }

    private static List<PointSummary> Summarize(IReadOnlyCollection<Sample> samples) =>
        samples
            .GroupBy(sample => new { sample.ObservationCount, sample.StorageType })
            .Select(group =>
            {
                var values = group.Select(sample => sample.MicrosecondsPerExecution)
                    .OrderBy(value => value)
                    .ToArray();
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

    private static List<AssetCrossover> FindAssetCrossovers(IReadOnlyCollection<Sample> samples)
    {
        var medians = samples
            .GroupBy(sample => new
            {
                sample.AssetId,
                sample.ObservationCount,
                sample.StorageType
            })
            .ToDictionary(
                group => (group.Key.AssetId, group.Key.ObservationCount, group.Key.StorageType),
                group => Percentile(
                    group.Select(sample => sample.MicrosecondsPerExecution)
                        .OrderBy(value => value)
                        .ToArray(),
                    0.50));

        return samples.Select(sample => sample.AssetId).Distinct().OrderBy(id => id)
            .Select(assetId =>
            {
                var points = samples.Where(sample => sample.AssetId == assetId)
                    .Select(sample => sample.ObservationCount)
                    .Distinct()
                    .OrderBy(value => value)
                    .ToArray();
                long? previousRowstoreWin = null;
                long? firstColumnstoreWin = null;

                foreach (var point in points)
                {
                    var rowstore = medians[(assetId, point, "rowstore")];
                    var columnstore = medians[(assetId, point, "columnstore")];
                    if (columnstore < rowstore)
                    {
                        firstColumnstoreWin = point;
                        break;
                    }

                    previousRowstoreWin = point;
                }

                return new AssetCrossover(assetId, previousRowstoreWin, firstColumnstoreWin);
            })
            .ToList();
    }

    private static double Percentile(IReadOnlyList<double> sortedValues, double percentile)
    {
        if (sortedValues.Count == 0)
            throw new ArgumentException("At least one value is required.", nameof(sortedValues));

        var position = (sortedValues.Count - 1) * percentile;
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        if (lower == upper)
            return sortedValues[lower];

        return sortedValues[lower]
            + (sortedValues[upper] - sortedValues[lower]) * (position - lower);
    }

    private static string BuildMarkdown(
        RunInfo run,
        IReadOnlyCollection<Sample> samples,
        IReadOnlyCollection<PointSummary> summaries,
        IReadOnlyCollection<AssetCrossover> assetCrossovers)
    {
        var assets = samples.Select(sample => sample.AssetId).Distinct().OrderBy(id => id).ToArray();
        var pairedChecksumMismatches = samples
            .GroupBy(sample => new { sample.AssetId, sample.ObservationCount, sample.Repetition })
            .Count(group =>
            {
                var checksums = group.Where(sample => sample.Checksum.HasValue)
                    .Select(sample => sample.Checksum!.Value)
                    .ToArray();
                return checksums.Length == 2 && Math.Abs(checksums.Max() - checksums.Min()) > 1e-10;
            });
        var points = summaries.Select(summary => summary.ObservationCount).Distinct().OrderBy(x => x);
        var builder = new StringBuilder();

        builder.AppendLine("# Long Asset History Results");
        builder.AppendLine();
        builder.AppendLine($"- Run ID: `{run.RunId:D}`");
        builder.AppendLine($"- Started: `{run.StartedAt:yyyy-MM-dd HH:mm:ss}`");
        builder.AppendLine($"- Completed: `{run.CompletedAt:yyyy-MM-dd HH:mm:ss}`");
        builder.AppendLine($"- SQL Server: `{run.SqlServerVersion}`");
        builder.AppendLine($"- Assets: {assets.Length} (`{string.Join(", ", assets)}`)");
        builder.AppendLine($"- Retained samples: {samples.Count:N0}");
        builder.AppendLine($"- Paired checksum mismatches: {pairedChecksumMismatches}");
        builder.AppendLine();
        builder.AppendLine("![Rowstore and columnstore crossover](crossover.svg)");
        builder.AppendLine();
        builder.AppendLine("## Pooled results");
        builder.AppendLine();
        builder.AppendLine("Medians and quartiles pool the retained timing samples from every sampled asset.");
        builder.AppendLine();
        builder.AppendLine("| Observations | Rowstore median (µs) | Columnstore median (µs) | Column/row | Faster |");
        builder.AppendLine("| ---: | ---: | ---: | ---: | --- |");

        foreach (var point in points)
        {
            var rowstore = summaries.Single(x =>
                x.ObservationCount == point && x.StorageType == "rowstore");
            var columnstore = summaries.Single(x =>
                x.ObservationCount == point && x.StorageType == "columnstore");
            var winner = rowstore.Median <= columnstore.Median ? "rowstore" : "columnstore";
            builder.AppendLine(
                $"| {point:N0} | {rowstore.Median:F2} | {columnstore.Median:F2} "
                + $"| {columnstore.Median / rowstore.Median:F2}× | {winner} |");
        }

        builder.AppendLine();
        builder.AppendLine("## Per-asset crossover");
        builder.AppendLine();
        builder.AppendLine("The interval is bounded by measured points; it is not an interpolated crossover.");
        builder.AppendLine();
        builder.AppendLine("| Asset | Last measured rowstore win | First measured columnstore win |");
        builder.AppendLine("| ---: | ---: | ---: |");
        foreach (var crossover in assetCrossovers)
        {
            builder.AppendLine(
                $"| {crossover.AssetId} | {FormatPoint(crossover.LastRowstoreWin)} "
                + $"| {FormatPoint(crossover.FirstColumnstoreWin)} |");
        }

        builder.AppendLine();
        builder.AppendLine("## Interpretation limits");
        builder.AppendLine();
        builder.AppendLine(
            "This report describes warm-cache, single-asset queries with `MAXDOP 1`. It does not "
            + "establish behavior for multi-asset queries, cold cache, parallelism, concurrency, "
            + "different rowgroup quality, or different host conditions.");
        return builder.ToString();
    }

    private static string FormatPoint(long? point) => point.HasValue ? point.Value.ToString("N0") : "not observed";

    private static string BuildSvg(IReadOnlyCollection<PointSummary> summaries)
    {
        const int width = 1000;
        const int height = 600;
        const int left = 90;
        const int right = 35;
        const int top = 55;
        const int bottom = 80;
        var points = summaries.Select(summary => summary.ObservationCount).Distinct().OrderBy(x => x).ToArray();
        var maximum = summaries.Max(summary => summary.P75) * 1.08;
        var minimumLog = Math.Log10(points.Min());
        var maximumLog = Math.Log10(points.Max());
        double X(long value) => left + (Math.Log10(value) - minimumLog) / (maximumLog - minimumLog)
            * (width - left - right);
        double Y(double value) => height - bottom - value / maximum * (height - top - bottom);
        string Number(double value) => value.ToString("0.##", Invariant);
        var builder = new StringBuilder();

        builder.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width}\" height=\"{height}\" viewBox=\"0 0 {width} {height}\">");
        builder.AppendLine("<rect width=\"100%\" height=\"100%\" fill=\"#ffffff\"/>");
        builder.AppendLine("<style>text{font-family:Segoe UI,Arial,sans-serif;fill:#24292f}.grid{stroke:#d8dee4;stroke-width:1}.axis{stroke:#57606a;stroke-width:1.5}.label{font-size:13px}.title{font-size:21px;font-weight:600}.legend{font-size:14px}</style>");
        builder.AppendLine($"<text x=\"{width / 2}\" y=\"30\" text-anchor=\"middle\" class=\"title\">Long asset history: pooled median and interquartile range</text>");

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
        builder.AppendLine($"<text x=\"{(left + width - right) / 2}\" y=\"{height - 22}\" text-anchor=\"middle\" class=\"label\">Observations per asset (log scale)</text>");
        builder.AppendLine($"<text x=\"20\" y=\"{(top + height - bottom) / 2}\" transform=\"rotate(-90 20 {(top + height - bottom) / 2})\" text-anchor=\"middle\" class=\"label\">Microseconds per execution</text>");

        AppendSeries(builder, summaries, "rowstore", "#0969da", X, Y, Number);
        AppendSeries(builder, summaries, "columnstore", "#cf222e", X, Y, Number);
        builder.AppendLine($"<line x1=\"{width - 250}\" y1=\"22\" x2=\"{width - 215}\" y2=\"22\" stroke=\"#0969da\" stroke-width=\"3\"/><text x=\"{width - 207}\" y=\"27\" class=\"legend\">rowstore</text>");
        builder.AppendLine($"<line x1=\"{width - 125}\" y1=\"22\" x2=\"{width - 90}\" y2=\"22\" stroke=\"#cf222e\" stroke-width=\"3\"/><text x=\"{width - 82}\" y=\"27\" class=\"legend\">columnstore</text>");
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
            .OrderBy(summary => summary.ObservationCount)
            .ToArray();
        var upper = series.Select(point => $"{number(x(point.ObservationCount))},{number(y(point.P75))}");
        var lower = series.Reverse().Select(point => $"{number(x(point.ObservationCount))},{number(y(point.P25))}");
        builder.AppendLine($"<polygon points=\"{string.Join(" ", upper.Concat(lower))}\" fill=\"{color}\" opacity=\"0.14\"/>");
        var median = series.Select(point => $"{number(x(point.ObservationCount))},{number(y(point.Median))}");
        builder.AppendLine($"<polyline points=\"{string.Join(" ", median)}\" fill=\"none\" stroke=\"{color}\" stroke-width=\"3\"/>");
        foreach (var point in series)
            builder.AppendLine($"<circle cx=\"{number(x(point.ObservationCount))}\" cy=\"{number(y(point.Median))}\" r=\"4\" fill=\"{color}\"/>");
    }

    private sealed record RunInfo(Guid RunId, DateTime StartedAt, DateTime CompletedAt, string SqlServerVersion);
    private sealed record Sample(
        int AssetId,
        long ObservationCount,
        int Repetition,
        string StorageType,
        byte ExecutionPosition,
        short ExecutionsPerSample,
        long ElapsedMicroseconds,
        double? Checksum,
        double MicrosecondsPerExecution);
    private sealed record PointSummary(
        long ObservationCount,
        string StorageType,
        double P25,
        double Median,
        double P75);
    private sealed record AssetCrossover(
        int AssetId,
        long? LastRowstoreWin,
        long? FirstColumnstoreWin);
}
