namespace Server.Models;

public sealed record BurstPublishRequest(
    int Count = 750,
    string MessagePrefix = "sse-slow-client-test"
);
