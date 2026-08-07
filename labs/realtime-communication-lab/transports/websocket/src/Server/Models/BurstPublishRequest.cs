namespace Server.Models;

public sealed class BurstPublishRequest
{
    public int Count { get; init; } = 750;

    public string MessagePrefix { get; init; } = "slow-client-test";
}
