using System.Threading.Channels;
using Realtime.Grpc;

namespace Server.Services;

public sealed class GrpcEventHub
{
    public const int SubscriberCapacity = 500;
    private const int EventHistoryCapacity = 500;
    private readonly Lock sync = new();
    private readonly SemaphoreSlim publishGate = new(1, 1);
    private readonly List<RealtimeEvent> history = [];
    private readonly Dictionary<Guid, Channel<RealtimeEvent>> subscribers = [];
    private long nextId;

    public Subscription Subscribe(long afterId)
    {
        var channel = Channel.CreateBounded<RealtimeEvent>(
            new BoundedChannelOptions(SubscriberCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false,
            }
        );
        var id = Guid.NewGuid();
        lock (sync)
        {
            foreach (var item in history.Where(item => item.Id > afterId))
            {
                channel.Writer.TryWrite(item);
            }
            subscribers.Add(id, channel);
        }
        return new Subscription(id, channel.Reader, () => Remove(id));
    }

    public async Task<RealtimeEvent> PublishAsync(string message, CancellationToken cancellationToken)
    {
        await publishGate.WaitAsync(cancellationToken);
        try
        {
            RealtimeEvent item;
            ChannelWriter<RealtimeEvent>[] writers;
            lock (sync)
            {
                item = new RealtimeEvent
                {
                    Id = ++nextId,
                    Message = message,
                    CreatedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
                };
                history.Add(item);
                if (history.Count > EventHistoryCapacity)
                {
                    history.RemoveAt(0);
                }
                writers = subscribers.Values.Select(channel => channel.Writer).ToArray();
            }

            foreach (var writer in writers)
            {
                await writer.WriteAsync(item, cancellationToken);
            }
            return item;
        }
        finally
        {
            publishGate.Release();
        }
    }

    public int SubscriberCount
    {
        get
        {
            lock (sync)
            {
                return subscribers.Count;
            }
        }
    }

    private void Remove(Guid id)
    {
        lock (sync)
        {
            if (subscribers.Remove(id, out var channel))
            {
                channel.Writer.TryComplete();
            }
        }
    }
}

public sealed class Subscription(
    Guid id,
    ChannelReader<RealtimeEvent> reader,
    Action unsubscribe
) : IDisposable
{
    public Guid Id { get; } = id;
    public ChannelReader<RealtimeEvent> Reader { get; } = reader;
    public void Dispose() => unsubscribe();
}
