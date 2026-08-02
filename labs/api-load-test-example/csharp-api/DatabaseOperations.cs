using System.Diagnostics;

namespace PoolMonitoringApi;

internal sealed class DatabaseOperations;

internal static class DatabaseOperationLogging
{
    public static async Task<T> ExecuteAsync<T>(
        ILogger<DatabaseOperations> logger,
        string operation,
        Func<Task<T>> execute
    )
    {
        var startedAt = Stopwatch.GetTimestamp();
        using var scope = logger.BeginScope(
            new Dictionary<string, object> { ["db_operation"] = operation }
        );

        try
        {
            return await execute();
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Database operation {db_operation} failed after {duration_ms} ms",
                operation,
                Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds
            );
            throw;
        }
    }
}

