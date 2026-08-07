using System.Diagnostics;
using System.Threading.Channels;
using Server.Models;

namespace Server.Services;

public sealed class SseBroadcastService
{
    private readonly SseConnectionManager _connections;
    private readonly SseMetrics _metrics;

    public SseBroadcastService(SseConnectionManager connections, SseMetrics metrics)
    {
        _connections = connections;
        _metrics = metrics;
    }

    public Task BroadcastAsync(EventRecord record, CancellationToken cancellationToken = default) =>
        Task.WhenAll(
            _connections.Connections.Select(connection =>
                EnqueueAsync(connection, record, cancellationToken)
            )
        );

    private async Task EnqueueAsync(
        SseConnection connection,
        EventRecord record,
        CancellationToken cancellationToken
    )
    {
        var queued = new QueuedEvent(record, Stopwatch.GetTimestamp());
        if (!connection.Outbound.Writer.TryWrite(queued))
        {
            try
            {
                await connection.Outbound.Writer.WriteAsync(queued, cancellationToken);
            }
            catch (ChannelClosedException)
            {
                return;
            }
            _metrics.BackpressureWaited(connection.Mode);
        }
        _metrics.Enqueued(connection.Mode);
    }
}
