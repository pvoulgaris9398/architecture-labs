namespace Server.Models;

public sealed record BurstPublishRequest(int Count = 100, string MessagePrefix = "long-poll-test");
