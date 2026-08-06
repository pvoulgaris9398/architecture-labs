using Server.Models;

namespace Server.Services;

public sealed class BroadcastService
{
    private readonly ConnectionManager _connections;
    private readonly OutboundQueue _outbound;

    public BroadcastService(ConnectionManager connections, OutboundQueue outbound)
    {
        _connections = connections;
        _outbound = outbound;
    }

    public async Task BroadcastAsync(
        EventRecord record,
        CancellationToken cancellationToken = default
    )
    {
        var payload = new EventMessage
        {
            Type = "event",
            Sequence = record.Sequence,
            Timestamp = record.Timestamp,
            Message = record.Message,
        };

        var enqueueTasks = _connections.Connections.Select(connection =>
            _outbound.EnqueueAsync(connection, payload, cancellationToken).AsTask()
        );

        await Task.WhenAll(enqueueTasks);
    }
}
