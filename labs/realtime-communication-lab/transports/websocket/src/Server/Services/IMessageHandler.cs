using Server.Models;

namespace Server.Services;

public interface IMessageHandler
{
    string MessageType { get; }

    Type MessageClrType { get; }

    Task HandleAsync(
        ClientConnection connection,
        SocketMessage message,
        CancellationToken cancellationToken
    );
}
