using System.Threading.Channels;

namespace Server.Models;

public sealed class SseConnection
{
    public const int Capacity = 500;
    public Guid Id { get; } = Guid.NewGuid();
    public TimeSpan SendDelay { get; init; }
    public string Mode => SendDelay > TimeSpan.Zero ? "slow" : "normal";
    public Channel<QueuedEvent> Outbound { get; } =
        Channel.CreateBounded<QueuedEvent>(
            new BoundedChannelOptions(Capacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait,
            }
        );
}

public sealed record QueuedEvent(EventRecord Record, long EnqueuedTimestamp);
