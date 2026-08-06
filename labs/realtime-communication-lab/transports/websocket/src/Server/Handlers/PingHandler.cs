using Server.Models;
using Server.Services;

namespace Server.Handlers;

public sealed class PingHandler : MessageHandler<PingMessage>
{
    public override string MessageType => "ping";

    protected override async Task HandleAsync(
        ClientConnection connection,
        PingMessage message,
        CancellationToken cancellationToken
    )
    {
        var pong = new PongMessage { Type = "pong" };

        await connection.Outbound.Writer.WriteAsync(pong, cancellationToken);

        connection.LastSeenUtc = DateTime.UtcNow;

        Console.WriteLine($"Queued pong response for {connection.Id}");
    }
}
