using System.Net.WebSockets;
using System.Threading.Channels;

namespace Server.Models;

public sealed class ClientConnection
{
    public const int OutboundCapacity = 500;

    public Guid Id { get; } = Guid.NewGuid();

    public required WebSocket Socket { get; init; }

    public TimeSpan OutboundSendDelay { get; init; }

    public string Mode => OutboundSendDelay > TimeSpan.Zero ? "slow" : "normal";

    public DateTime ConnectedUtc { get; } = DateTime.UtcNow;

    public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;

    public long LastAcknowledgedSequence { get; set; }

    /// <summary>
    /// Outbound message queue for this client.
    /// Every component in the application will eventually enqueue
    /// SocketMessage instances here instead of writing directly to the socket.
    /// </summary>
    public Channel<QueuedSocketMessage> Outbound { get; } =
        Channel.CreateBounded<QueuedSocketMessage>(
            new BoundedChannelOptions(OutboundCapacity)
            {
                SingleReader = true,
                SingleWriter = false,

                // Block producers until there is room.
                FullMode = BoundedChannelFullMode.Wait,
            }
        );

    /// <summary>
    /// Used to stop the sender task.
    /// </summary>
    public CancellationTokenSource Cancellation { get; } = new();
}

public sealed record QueuedSocketMessage(SocketMessage Message, long EnqueuedTimestamp);
