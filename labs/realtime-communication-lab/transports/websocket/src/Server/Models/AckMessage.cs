namespace Server.Models;

public sealed record AckMessage : SocketMessage
{
    public long Sequence { get; init; }
}
