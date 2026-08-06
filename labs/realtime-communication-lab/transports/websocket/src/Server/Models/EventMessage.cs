namespace Server.Models;

public sealed record EventMessage : SocketMessage
{
    public long Sequence { get; init; }

    public DateTime Timestamp { get; init; }

    public string Message { get; init; } = "";
}
