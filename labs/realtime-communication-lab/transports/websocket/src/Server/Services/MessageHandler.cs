using Server.Models;

namespace Server.Services;

public abstract class MessageHandler<TMessage> : IMessageHandler
    where TMessage : SocketMessage
{
    public abstract string MessageType { get; }

    public Type MessageClrType => typeof(TMessage);

    public Task HandleAsync(
        ClientConnection connection,
        SocketMessage message,
        CancellationToken cancellationToken
    )
    {
        return HandleAsync(connection, (TMessage)message, cancellationToken);
    }

    protected abstract Task HandleAsync(
        ClientConnection connection,
        TMessage message,
        CancellationToken cancellationToken
    );
}
