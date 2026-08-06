using Server.Models;
using Server.Services;

namespace Server.Handlers;

public sealed class AckHandler : MessageHandler<AckMessage>
{
    public override string MessageType => "ack";

    protected override Task HandleAsync(
        ClientConnection connection,
        AckMessage message,
        CancellationToken cancellationToken
    )
    {
        connection.LastAcknowledgedSequence = message.Sequence;

        Console.WriteLine($"Client {connection.Id} acknowledged {message.Sequence}");

        return Task.CompletedTask;
    }
}
