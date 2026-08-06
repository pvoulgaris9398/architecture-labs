using Server.Models;
using Server.Services;

namespace Server.Handlers;

public sealed class ReplayHandler : MessageHandler<ReplayRequest>
{
    private readonly EventStore _eventStore;
    private readonly OutboundQueue _outbound;

    public ReplayHandler(EventStore eventStore, OutboundQueue outbound)
    {
        _eventStore = eventStore;
        _outbound = outbound;
    }

    public override string MessageType => "replay";

    protected override async Task HandleAsync(
        ClientConnection connection,
        ReplayRequest message,
        CancellationToken cancellationToken
    )
    {
        var events = _eventStore.GetSince(message.LastSequence);

        foreach (var e in events)
        {
            var eventMessage = new EventMessage
            {
                Type = "event",
                Sequence = e.Sequence,
                Timestamp = e.Timestamp,
                Message = e.Message,
            };

            await _outbound.EnqueueAsync(connection, eventMessage, cancellationToken);
        }

        Console.WriteLine($"Queued {events.Count()} replay events for {connection.Id}");
    }
}
