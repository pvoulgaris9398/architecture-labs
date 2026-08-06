namespace Server.Models;

public sealed record ReplayRequest : SocketMessage
{
    public long LastSequence { get; init; }
}
