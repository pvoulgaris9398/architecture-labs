namespace Server.Models;

public abstract record SocketMessage
{
    public required string Type { get; init; }

    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;
}
