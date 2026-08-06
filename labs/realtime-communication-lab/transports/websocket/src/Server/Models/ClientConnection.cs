using System.Net.WebSockets;
using System.Threading.Channels;

namespace Server.Models;

public sealed class ClientConnection
{
    public Guid Id { get; } = Guid.NewGuid();

    public required WebSocket Socket { get; init; }

    public DateTime ConnectedUtc { get; } = DateTime.UtcNow;

    public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;

    public long LastAcknowledgedSequence { get; set; }

    /// <summary>
    /// Outbound message queue for this client.
    /// Every component in the application will eventually enqueue
    /// SocketMessage instances here instead of writing directly to the socket.
    /// </summary>
    public Channel<SocketMessage> Outbound { get; } =
        Channel.CreateBounded<SocketMessage>(
            new BoundedChannelOptions(500)
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
