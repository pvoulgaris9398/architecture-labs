namespace Server.Models;

public sealed class EventRecord
{
    public long Sequence { get; init; }

    public DateTime Timestamp { get; init; }

    public string Message { get; init; } = "";
}
