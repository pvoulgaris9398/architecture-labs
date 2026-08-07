using System.Diagnostics;
using System.Threading.Channels;
using Server.Models;

namespace Server.Services;

public sealed class OutboundQueue
{
    private readonly WebSocketMetrics _metrics;

    public OutboundQueue(WebSocketMetrics metrics)
    {
        _metrics = metrics;
    }

    public async ValueTask EnqueueAsync(
        ClientConnection connection,
        SocketMessage message,
        CancellationToken cancellationToken = default
    )
    {
        var queued = new QueuedSocketMessage(message, Stopwatch.GetTimestamp());

        if (!connection.Outbound.Writer.TryWrite(queued))
        {
            var waitStarted = Stopwatch.GetTimestamp();

            try
            {
                await connection.Outbound.Writer.WriteAsync(queued, cancellationToken);
            }
            catch (ChannelClosedException)
            {
                return;
            }

            _metrics.BackpressureWaited(
                connection.Mode,
                message.Type,
                Stopwatch.GetElapsedTime(waitStarted)
            );
        }

        _metrics.MessageEnqueued(connection.Mode, message.Type);
    }
}
