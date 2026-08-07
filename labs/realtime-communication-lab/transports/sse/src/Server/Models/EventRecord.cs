namespace Server.Models;

public sealed record EventRecord(long Sequence, DateTime Timestamp, string Message);
