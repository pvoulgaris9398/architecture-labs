namespace Server.Models;

public sealed record BurstPublishResult(int Count, long FirstSequence, long LastSequence);
