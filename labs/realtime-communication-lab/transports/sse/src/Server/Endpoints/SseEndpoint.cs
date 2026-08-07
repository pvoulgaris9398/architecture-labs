using System.Diagnostics;
using System.Text.Json;
using Server.Models;
using Server.Services;

namespace Server.Endpoints;

public sealed class SseEndpoint
{
    private const int MaximumSendDelayMilliseconds = 2_000;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly EventStore _store;
    private readonly SseConnectionManager _connections;
    private readonly SseMetrics _metrics;

    public SseEndpoint(EventStore store, SseConnectionManager connections, SseMetrics metrics)
    {
        _store = store;
        _connections = connections;
        _metrics = metrics;
    }

    public async Task HandleAsync(HttpContext context)
    {
        context.Response.Headers.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";
        context.Response.Headers.Connection = "keep-alive";
        context.Response.Headers["X-Accel-Buffering"] = "no";
        await context.Response.StartAsync(context.RequestAborted);
        await context.Response.Body.FlushAsync(context.RequestAborted);

        var delay = ParsePositiveInt(context.Request.Query["sendDelayMs"]);
        var lastEventId = ParsePositiveLong(context.Request.Headers["Last-Event-ID"]);
        if (lastEventId == 0)
            lastEventId = ParsePositiveLong(context.Request.Query["lastEventId"]);

        var connection = new SseConnection
        {
            SendDelay = TimeSpan.FromMilliseconds(Math.Min(delay, MaximumSendDelayMilliseconds)),
        };
        _connections.Add(connection);

        try
        {
            var replay = _store.GetSince(lastEventId);
            var latestReplayed = lastEventId;
            foreach (var record in replay)
            {
                await WriteEventAsync(context.Response, record, context.RequestAborted);
                latestReplayed = record.Sequence;
            }

            await foreach (
                var queued in connection.Outbound.Reader.ReadAllAsync(context.RequestAborted)
            )
            {
                if (queued.Record.Sequence <= latestReplayed)
                    continue;
                if (connection.SendDelay > TimeSpan.Zero)
                    await Task.Delay(connection.SendDelay, context.RequestAborted);
                try
                {
                    await WriteEventAsync(context.Response, queued.Record, context.RequestAborted);
                    _metrics.Sent(connection.Mode, queued.EnqueuedTimestamp);
                }
                catch (IOException)
                {
                    _metrics.Failed(connection.Mode);
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
        finally
        {
            _connections.Remove(connection.Id);
            connection.Outbound.Writer.TryComplete();
        }
    }

    private static async Task WriteEventAsync(
        HttpResponse response,
        EventRecord record,
        CancellationToken cancellationToken
    )
    {
        await response.WriteAsync($"id: {record.Sequence}\n", cancellationToken);
        await response.WriteAsync("event: message\n", cancellationToken);
        await response.WriteAsync(
            $"data: {JsonSerializer.Serialize(record, JsonOptions)}\n\n",
            cancellationToken
        );
        await response.Body.FlushAsync(cancellationToken);
    }

    private static int ParsePositiveInt(string? value) =>
        int.TryParse(value, out var parsed) && parsed > 0 ? parsed : 0;

    private static long ParsePositiveLong(string? value) =>
        long.TryParse(value, out var parsed) && parsed > 0 ? parsed : 0;
}
