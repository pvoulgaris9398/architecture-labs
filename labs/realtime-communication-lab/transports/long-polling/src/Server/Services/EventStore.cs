using Server.Models;

namespace Server.Services;

public sealed class EventStore
{
    private readonly object _gate = new();
    private readonly List<EventRecord> _events = [];
    private long _nextSequence;
    private TaskCompletionSource _changed = NewSignal();

    public EventRecord Append(string message)
    {
        lock (_gate)
        {
            var record = new EventRecord(++_nextSequence, DateTime.UtcNow, message);
            _events.Add(record);
            _changed.TrySetResult();
            _changed = NewSignal();
            return record;
        }
    }

    public async Task<IReadOnlyList<EventRecord>> PollAsync(
        long since,
        TimeSpan timeout,
        CancellationToken cancellationToken
    )
    {
        Task signal;
        lock (_gate)
        {
            var available = GetSinceUnsafe(since);
            if (available.Count > 0)
                return available;
            signal = _changed.Task;
        }
        try
        {
            await signal.WaitAsync(timeout, cancellationToken);
        }
        catch (TimeoutException)
        {
            return Array.Empty<EventRecord>();
        }
        lock (_gate)
            return GetSinceUnsafe(since);
    }

    private IReadOnlyList<EventRecord> GetSinceUnsafe(long since) =>
        _events.Where(e => e.Sequence > since).ToList();

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
