using System.Collections.Concurrent;
using Server.Models;

namespace Server.Services;

public sealed class EventStore
{
    private readonly ConcurrentQueue<EventRecord> _events = new();
    private long _nextSequence;

    public EventRecord Append(string message)
    {
        var record = new EventRecord(
            Interlocked.Increment(ref _nextSequence),
            DateTime.UtcNow,
            message
        );
        _events.Enqueue(record);
        return record;
    }

    public IReadOnlyList<EventRecord> GetSince(long sequence) =>
        _events
            .Where(record => record.Sequence > sequence)
            .OrderBy(record => record.Sequence)
            .ToList();
}
