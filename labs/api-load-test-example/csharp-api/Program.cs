using System.Diagnostics;
using Dapper;
using Microsoft.Data.SqlClient;
using PoolMonitoringApi;

const string serviceName = "pool-monitoring-api";
Activity.DefaultIdFormat = ActivityIdFormat.W3C;
Activity.ForceDefaultIdFormat = true;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
    options.TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";
    options.UseUtcTimestamp = true;
});

// Connection strings are read from environment variables:
//   ConnectionStrings__Master   → used by the original endpoints (targets master DB)
//   ConnectionStrings__LoadTest → used by the orders experiment (targets LoadTestDb)
// The double-underscore is the ASP.NET Core config hierarchy separator, which maps
// directly to the ConnectionStrings section so GetConnectionString() resolves them.
var connString =
    builder.Configuration.GetConnectionString("Master")
    ?? throw new InvalidOperationException(
        "Missing required configuration: ConnectionStrings__Master"
    );

// Separate connection string targeting LoadTestDb for the orders experiment
var scanConnString =
    builder.Configuration.GetConnectionString("LoadTest")
    ?? throw new InvalidOperationException(
        "Missing required configuration: ConnectionStrings__LoadTest"
    );

builder.AddAppTelemetryV2(serviceName);

var app = builder.Build();
app.UseMiddleware<RequestCorrelationMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();
app.UseOpenTelemetryPrometheusScrapingEndpoint();
app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }));

// ---------------------------------------------------------------------------
// Database seeding
// Runs the seed script against the SQL Server container on startup.
// Safe to run multiple times — the script is guarded with IF NOT EXISTS.
// ---------------------------------------------------------------------------
await SeedDatabaseAsync(scanConnString, app.Logger);

// ---------------------------------------------------------------------------
// Original endpoints
// ---------------------------------------------------------------------------

// Endpoint Route 1
app.MapGet(
    "/v1/data-endpoint",
    async (ILogger<DatabaseOperations> logger) =>
    {
        using var connection = new SqlConnection(connString);
        var result = await DatabaseOperationLogging.ExecuteAsync(
            logger,
            "delayed-data-query",
            () => connection.QueryAsync<int>("WAITFOR DELAY '00:00:00.100'; SELECT 1;")
        );
        return Results.Ok(new { status = "Success", data = result });
    }
);

// Endpoint Route 2
app.MapGet(
    "/v1/admin-report",
    async (ILogger<DatabaseOperations> logger) =>
    {
        using (var connection = new SqlConnection(connString))
        {
            var result = await DatabaseOperationLogging.ExecuteAsync(
                logger,
                "delayed-admin-query",
                () =>
                    connection.QueryAsync<int>("WAITFOR DELAY '00:00:00.050'; SELECT 2;")
            );
            return Results.Ok(new { status = "Admin Success", data = result });
        }
    }
);

// ---------------------------------------------------------------------------
// Table scan vs index demo endpoint
// ---------------------------------------------------------------------------

// The query is intentionally unchanged between test runs. Without the optional
// CustomerId index it performs a table scan; after POST /v1/add-index it uses the index.
app.MapGet(
    "/v1/orders/by-customer",
    async (ILogger<DatabaseOperations> logger) =>
    {
        // Rotate through customer IDs so each request scans for a different customer,
        // preventing SQL Server from short-circuiting via plan caching tricks.
        var customerId = Random.Shared.Next(1, 10001);
        using var connection = new SqlConnection(scanConnString);
        var orders = await DatabaseOperationLogging.ExecuteAsync(
            logger,
            "orders-by-customer",
            () =>
                connection.QueryAsync<Order>(
                    "SELECT OrderId, CustomerId, OrderDate, Status, Amount FROM Orders WHERE CustomerId = @CustomerId",
                    new { CustomerId = customerId }
                )
        );
        return Results.Ok(new { customerId, orderCount = orders.Count() });
    }
);

// Adds a non-clustered index on CustomerId.
// Call this between load test runs: POST http://127.0.0.1:18080/v1/add-index
app.MapPost(
    "/v1/add-index",
    async (ILogger<DatabaseOperations> logger) =>
    {
        using var connection = new SqlConnection(scanConnString);
        await DatabaseOperationLogging.ExecuteAsync(
            logger,
            "add-orders-customer-index",
            () =>
                connection.ExecuteAsync(
                    """
                    IF NOT EXISTS (
                        SELECT 1 FROM sys.indexes
                        WHERE name = 'IX_Orders_CustomerId' AND object_id = OBJECT_ID('Orders')
                    )
                    BEGIN
                        CREATE NONCLUSTERED INDEX IX_Orders_CustomerId ON Orders (CustomerId)
                        INCLUDE (OrderDate, Status, Amount);
                    END
                    """
                )
        );
        return Results.Ok(new { status = "Index created", index = "IX_Orders_CustomerId" });
    }
);

// Drops the index so you can repeat the unindexed run without recreating the table.
// Call this to reset: POST http://127.0.0.1:18080/v1/drop-index
app.MapPost(
    "/v1/drop-index",
    async (ILogger<DatabaseOperations> logger) =>
    {
        using var connection = new SqlConnection(scanConnString);
        await DatabaseOperationLogging.ExecuteAsync(
            logger,
            "drop-orders-customer-index",
            () =>
                connection.ExecuteAsync(
                    """
                    IF EXISTS (
                        SELECT 1 FROM sys.indexes
                        WHERE name = 'IX_Orders_CustomerId' AND object_id = OBJECT_ID('Orders')
                    )
                    BEGIN
                        DROP INDEX IX_Orders_CustomerId ON Orders;
                    END
                    """
                )
        );
        return Results.Ok(new { status = "Index dropped", index = "IX_Orders_CustomerId" });
    }
);

app.Run();

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

static async Task SeedDatabaseAsync(string connectionString, ILogger logger)
{
    // The seed script targets LoadTestDb which may not exist yet on a fresh container.
    // Connect to master first, then run the full seed SQL.
    var masterConnString = connectionString.Replace("Database=LoadTestDb", "Database=master");
    var seedScriptPath = Path.Combine(AppContext.BaseDirectory, "seed.sql");

    if (!File.Exists(seedScriptPath))
    {
        logger.LogWarning(
            "Seed script not found at {Path} — skipping database seeding.",
            seedScriptPath
        );
        return;
    }

    var sql = await File.ReadAllTextAsync(seedScriptPath);

    // sqlcmd-style GO batch separators are not understood by SqlClient — split manually.
    var batches = sql.Split(["\nGO", "\r\nGO"], StringSplitOptions.RemoveEmptyEntries)
        .Select(b => b.Trim())
        .Where(b => b.Length > 0);

    // Retry a few times to handle the container health-check window.
    const int maxRetries = 10;
    for (int attempt = 1; attempt <= maxRetries; attempt++)
    {
        try
        {
            using var connection = new SqlConnection(masterConnString);
            await connection.OpenAsync();

            foreach (var batch in batches)
            {
                await connection.ExecuteAsync(batch);
            }

            logger.LogInformation("Database seeding complete.");
            return;
        }
        catch (Exception ex) when (attempt < maxRetries)
        {
            logger.LogWarning(
                "Seeding attempt {Attempt}/{Max} failed: {Message}. Retrying in 3s…",
                attempt,
                maxRetries,
                ex.Message
            );
            await Task.Delay(TimeSpan.FromSeconds(3));
        }
    }

    logger.LogError("Database seeding failed after {Max} attempts.", maxRetries);
}

record Order(int OrderId, int CustomerId, DateTime OrderDate, string Status, decimal Amount);
