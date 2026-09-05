#!/usr/bin/env -S dotnet run

#:package Microsoft.Data.SqlClient@7.0.2
#:property PublishAot=false

using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Data.SqlClient;

// A bounded diagnostic pass against existing tables; never invokes dataset setup.
internal static class Capture
{
    private const string ScenarioId = "long-asset-history-sweep";
    private static readonly XNamespace PlanNs = "http://schemas.microsoft.com/sqlserver/2004/07/showplan";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static async Task Main(string[] args)
    {
        if (args.Any(arg => arg != "--smoke"))
            throw new ArgumentException("Usage: dotnet run capture.cs [-- --smoke]");
        var smoke = args.Contains("--smoke");
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        var lab = FindLabDirectory();
        var parent = Path.Combine(lab, "scenarios", "long-asset-history");
        var directory = Path.Combine(parent, "diagnostics");
        var password = Environment.GetEnvironmentVariable("MSSQL_SA_PASSWORD");
        if (string.IsNullOrWhiteSpace(password))
            password = File.ReadLines(Path.Combine(lab, ".env"))
                .Select(line => line.Trim())
                .First(line => line.StartsWith("MSSQL_SA_PASSWORD="))
                ["MSSQL_SA_PASSWORD=".Length..].Trim().Trim('"', '\'');
        var connectionString = new SqlConnectionStringBuilder
        {
            DataSource = "localhost,1435", InitialCatalog = "LogReturnsLab",
            UserID = "sa", Password = password, Encrypt = true,
            TrustServerCertificate = true, ConnectTimeout = 10
        };
        await using var connection = new SqlConnection(connectionString.ConnectionString);
        await connection.OpenAsync();
        // Stabilize the STATISTICS IO message labels parsed below; session-local only.
        await using (var language = new SqlCommand("SET LANGUAGE us_english; SET NOCOUNT ON;", connection))
            await language.ExecuteNonQueryAsync();

        var latestSql = await File.ReadAllTextAsync(Path.Combine(parent, "shared", "latest-timing-run.sql"));
        await using var latest = new SqlCommand(latestSql, connection);
        latest.Parameters.AddWithValue("@scenario_id", ScenarioId);
        var latestValue = await latest.ExecuteScalarAsync();
        if (latestValue is not Guid runId)
            throw new InvalidOperationException("Run timing-baseline first: no successful timing run was found.");

        // Reuse the stored ranges, not assumptions about the dataset's starting date.
        var cases = new List<Workload>();
        await using (var command = new SqlCommand("""
            SELECT asset_id, observation_count, start_date, end_date,
                   checksum / executions_per_sample AS expected_checksum
            FROM dbo.BenchmarkSample
            WHERE run_id = @run_id AND scenario_id = @scenario_id
              AND storage_type = 'rowstore' AND repetition = 1
              AND asset_id IN (112, 778) AND observation_count IN (21, 252, 2520, 10000)
              AND (@smoke = 0 OR (asset_id = 112 AND observation_count = 21))
            ORDER BY observation_count, asset_id;
            """, connection))
        {
            command.Parameters.AddWithValue("@run_id", runId);
            command.Parameters.AddWithValue("@scenario_id", ScenarioId);
            command.Parameters.AddWithValue("@smoke", smoke);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                cases.Add(new(reader.GetInt32(0), reader.GetInt64(1), reader.GetDateTime(2),
                    reader.GetDateTime(3), reader.GetDouble(4)));
        }
        if (cases.Count != (smoke ? 1 : 8))
            throw new InvalidOperationException("Latest timing run does not contain the expected diagnostic subset.");

        var capturedAt = DateTimeOffset.UtcNow;
        var output = Path.Combine(directory, "results", "local", runId.ToString("D"),
            $"{capturedAt:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(output);
        await SaveEnvironment(connection, output, runId, capturedAt, smoke);
        var template = await File.ReadAllTextAsync(Path.Combine(directory, "query.sql"));
        await File.WriteAllTextAsync(Path.Combine(output, "query.sql"), template);
        var results = new List<Measurement>();
        foreach (var item in cases)
        {
            var layouts = results.Count % 4 == 0
                ? new[] { "rowstore", "columnstore" } : new[] { "columnstore", "rowstore" };
            foreach (var layout in layouts)
            {
                var table = layout == "rowstore" ? "ReturnsRowstore" : "ReturnsColumnstore";
                var sql = template.Replace("$(TableName)", table);
                await SetStatistics(connection, false);
                await Execute(connection, sql, item, capture: false);
                var messages = new StringBuilder();
                SqlInfoMessageEventHandler handler = (_, e) => messages.AppendLine(e.Message);
                connection.InfoMessage += handler;
                string xml;
                double checksum;
                try
                {
                    await SetStatistics(connection, true);
                    (xml, checksum) = await Execute(connection, sql, item, capture: true);
                }
                finally
                {
                    await SetStatistics(connection, false);
                    connection.InfoMessage -= handler;
                }
                var name = $"asset-{item.AssetId}-observations-{item.Observations}-{layout}";
                await File.WriteAllTextAsync(Path.Combine(output, name + ".sqlplan"), xml);
                await File.WriteAllTextAsync(Path.Combine(output, name + ".messages.txt"), messages.ToString());
                if (!double.IsFinite(checksum) || Math.Abs(checksum - item.ExpectedChecksum) > 1e-10)
                    throw new InvalidOperationException($"{name}: checksum differs from timing baseline; inspect current dataset.");
                var document = XDocument.Parse(xml);
                var statement = document.Descendants(PlanNs + "StmtSimple").Single(node =>
                    ((string?)node.Attribute("StatementText"))?.Contains("SELECT @execution_checksum = SUM(log_return)") == true);
                var plan = statement.Element(PlanNs + "QueryPlan")
                    ?? throw new InvalidOperationException("No query plan found.");
                var operators = plan.Descendants(PlanNs + "RelOp").Select(op => new Operator(
                    Attr(op, "NodeId"), Attr(op, "PhysicalOp"),
                    op.Element(PlanNs + "RunTimeInformation")?.Elements(PlanNs + "RunTimeCountersPerThread")
                        .Select(Attributes).ToArray() ?? [])).ToArray();
                results.Add(new(item, layout, results.Count + 1, checksum, name + ".sqlplan",
                    Attributes(plan.Element(PlanNs + "QueryTimeStats")),
                    Attributes(plan.Element(PlanNs + "MemoryGrantInfo")),
                    Attr(plan, "DegreeOfParallelism"),
                    plan.Descendants(PlanNs + "Warnings").Select(w => w.ToString()).ToArray(),
                    plan.Descendants(PlanNs + "Wait").Select(Attributes).ToArray(),
                    ReadTableIO(messages.ToString(), table), operators));
                Console.WriteLine($"Captured {name}");
            }
        }
        await File.WriteAllTextAsync(Path.Combine(output, "measurements.json"),
            JsonSerializer.Serialize(results, JsonOptions));
        await File.WriteAllTextAsync(Path.Combine(output, "summary.md"), BuildSummary(results, runId, capturedAt, smoke));
        Console.WriteLine($"Report: {Path.Combine(output, "summary.md")}");
    }

    private static async Task<(string Xml, double Checksum)> Execute(
        SqlConnection connection, string sql, Workload item, bool capture)
    {
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 60 };
        command.Parameters.Add("@input_asset", SqlDbType.Int).Value = item.AssetId;
        command.Parameters.Add("@input_start", SqlDbType.Date).Value = item.StartDate;
        command.Parameters.Add("@input_end", SqlDbType.Date).Value = item.EndDate;
        var checksum = command.Parameters.Add("@checksum_output", SqlDbType.Float);
        checksum.Direction = ParameterDirection.Output;
        var xml = "";
        await using (var reader = await command.ExecuteReaderAsync())
        {
            do
            {
                while (await reader.ReadAsync())
                {
                    if (!capture || reader.FieldCount != 1 || reader.IsDBNull(0)) continue;
                    var value = reader.GetValue(0).ToString() ?? "";
                    if (value.Contains("<ShowPlanXML") && value.Contains("SELECT @execution_checksum = SUM(log_return)"))
                        xml = value;
                }
            } while (await reader.NextResultAsync());
        }
        if (capture && xml.Length == 0)
            throw new InvalidOperationException("No actual plan returned; SHOWPLAN permission is required.");
        if (checksum.Value is not double sum)
            throw new InvalidOperationException("Query returned no checksum; check the current dataset.");
        return (xml, sum);
    }

    private static async Task SetStatistics(SqlConnection connection, bool enabled)
    {
        var state = enabled ? "ON" : "OFF";
        await using var command = new SqlCommand(
            $"SET STATISTICS IO {state}; SET STATISTICS TIME {state}; SET STATISTICS XML {state};", connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task SaveEnvironment(SqlConnection connection, string output,
        Guid runId, DateTimeOffset capturedAt, bool smoke)
    {
        await using var command = new SqlCommand("""
            SELECT @@VERSION AS engine_version, d.compatibility_level,
                   s.cpu_count, s.physical_memory_kb, s.sqlserver_start_time,
                   (SELECT value_in_use FROM sys.configurations WHERE name = 'max server memory (MB)') AS max_server_memory_mb,
                   (SELECT modify_date FROM sys.objects WHERE object_id = OBJECT_ID('dbo.ReturnsColumnstore')) AS columnstore_modify_date
            FROM sys.dm_os_sys_info s CROSS JOIN sys.databases d WHERE d.database_id = DB_ID();
            """, connection);
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        var server = Enumerable.Range(0, reader.FieldCount).ToDictionary(reader.GetName,
            i => reader.IsDBNull(i) ? null : reader.GetValue(i));
        await File.WriteAllTextAsync(Path.Combine(output, "environment.json"), JsonSerializer.Serialize(new
        {
            BaselineRunId = runId, CapturedAtUtc = capturedAt, Smoke = smoke,
            ClientOS = Environment.OSVersion.ToString(), ClientLogicalProcessors = Environment.ProcessorCount,
            Server = server, Note = "Current environment; Docker limits and host load must be recorded separately."
        }, JsonOptions));
    }

    private static string BuildSummary(List<Measurement> results, Guid runId, DateTimeOffset capturedAt, bool smoke)
    {
        var text = new StringBuilder($"# Long Asset History Diagnostics\n\nBaseline run: `{runId}`  \nCaptured (UTC): {capturedAt:O}  \nSmoke check: {smoke}\n\n");
        text.AppendLine("Current tables; one warm-up then one instrumented execution per case, MAXDOP 1. Checksums match the saved baseline within 1e-10.");
        text.AppendLine("These timings include diagnostic overhead and millisecond rounding; use timing-baseline for performance comparisons. Missing attributes are n/a, not zero.");
        text.AppendLine("Memory is workspace grant KB, not buffer-cache usage. Warnings contain spill details when reported; open the actual plan to inspect them.\n");
        text.AppendLine("DOP is the raw plan attribute (0 denotes a serial plan here); the requested limit is MAXDOP 1. Reported waits are retained in JSON and the plan, not inferred from elapsed minus CPU.\n");
        text.AppendLine("| Asset | Observations | Layout / plan | CPU ms | Elapsed ms | Grant KB | Used KB | DOP | Warning nodes |");
        text.AppendLine("| ---: | ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |");
        foreach (var r in results)
            text.AppendLine($"| {r.Workload.AssetId} | {r.Workload.Observations} | [{r.Layout}]({r.PlanFile}) | {Get(r.Time, "CpuTime")} | {Get(r.Time, "ElapsedTime")} | {Get(r.Memory, "GrantedMemory")} | {Get(r.Memory, "MaxUsedMemory")} | {r.Dop ?? "n/a"} | {r.Warnings.Length} |");
        text.AppendLine("\n## Table I/O\n\nFrom STATISTICS IO for the target table. Columnstore LOB reads may be nonzero even when operator read counters are zero; use these table counters alongside the plan.\n");
        text.AppendLine("| Asset / observations / layout | Logical reads | LOB logical reads | Physical reads | LOB physical reads | Segments read | Segments skipped |");
        text.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: | ---: |");
        foreach (var r in results)
            text.AppendLine($"| {r.Workload.AssetId} / {r.Workload.Observations} / {r.Layout} | {Get(r.TableIO, "logical reads")} | {Get(r.TableIO, "lob logical reads")} | {Get(r.TableIO, "physical reads")} | {Get(r.TableIO, "lob physical reads")} | {Get(r.TableIO, "segment reads")} | {Get(r.TableIO, "segment skipped")} |");
        text.AppendLine("\n## Operator counters\n\nRows are operator output, not query result rows. Aggregate pushdown can reduce scan output. Do not sum rows or operator times across the plan. LOB reads are shown separately. Raw per-thread attributes and warnings are in measurements.json; per-table I/O, segment reads/skips and timing messages are in each .messages.txt file.\n");
        text.AppendLine("| Asset / observations / layout | Node / operator | Thread | Mode | Rows read | Rows output | Logical reads | LOB logical reads | Physical reads |");
        text.AppendLine("| --- | --- | ---: | --- | ---: | ---: | ---: | ---: | ---: |");
        foreach (var r in results)
            foreach (var op in r.Operators)
                foreach (var thread in op.Threads)
                    text.AppendLine($"| {r.Workload.AssetId} / {r.Workload.Observations} / {r.Layout} | {op.NodeId} / {op.PhysicalOp} | {Get(thread, "Thread")} | {Get(thread, "ActualExecutionMode")} | {Get(thread, "ActualRowsRead")} | {Get(thread, "ActualRows")} | {Get(thread, "ActualLogicalReads")} | {Get(thread, "ActualLobLogicalReads")} | {Get(thread, "ActualPhysicalReads")} |");
        return text.ToString();
    }

    private static string? Attr(XElement element, string name) => (string?)element.Attribute(name);
    private static Dictionary<string, string> ReadTableIO(string messages, string table)
    {
        var counters = new Dictionary<string, string>();
        foreach (Match line in Regex.Matches(messages, $@"Table '{Regex.Escape(table)}'\. ([^\r\n]+)"))
            foreach (var part in line.Groups[1].Value.Split(','))
            {
                var metric = Regex.Match(part.Trim(), @"^(.+?) (\d+)\.?$");
                if (metric.Success)
                    counters.Add(metric.Groups[1].Value.ToLowerInvariant(), metric.Groups[2].Value);
            }
        if (!counters.ContainsKey("logical reads"))
            throw new InvalidOperationException($"No STATISTICS IO counters parsed for {table}; inspect raw messages.");
        return counters;
    }
    private static Dictionary<string, string> Attributes(XElement? element) =>
        element?.Attributes().ToDictionary(a => a.Name.LocalName, a => a.Value) ?? [];
    private static string Get(Dictionary<string, string> values, string key) => values.GetValueOrDefault(key, "n/a");
    private static string FindLabDirectory()
    {
        for (var directory = new DirectoryInfo(Environment.CurrentDirectory); directory != null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "docker-compose.yaml"))
                && Directory.Exists(Path.Combine(directory.FullName, "scenarios"))) return directory.FullName;
        throw new InvalidOperationException("Run from the log-returns-range-query lab or its subdirectories.");
    }

    private sealed record Workload(int AssetId, long Observations, DateTime StartDate, DateTime EndDate, double ExpectedChecksum);
    private sealed record Operator(string? NodeId, string? PhysicalOp, Dictionary<string, string>[] Threads);
    private sealed record Measurement(Workload Workload, string Layout, int ExecutionOrder, double Checksum,
        string PlanFile, Dictionary<string, string> Time, Dictionary<string, string> Memory,
        string? Dop, string[] Warnings, Dictionary<string, string>[] Waits,
        Dictionary<string, string> TableIO, Operator[] Operators);
}
