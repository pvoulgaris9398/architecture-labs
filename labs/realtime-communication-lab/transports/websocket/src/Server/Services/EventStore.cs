using System.Collections.Concurrent;
using Server.Models;

namespace Server.Services;

public sealed class EventStore
{
    private readonly ConcurrentQueue<EventRecord> _events = new();

    private long _nextSequence = 0;

    public EventRecord Append(string message)
    {
        var record = new EventRecord
        {
            Sequence = Interlocked.Increment(ref _nextSequence),
            Timestamp = DateTime.UtcNow,
            Message = message,
        };

        _events.Enqueue(record);

        return record;
    }

    public IReadOnlyList<EventRecord> GetSince(long sequence)
    {
        return _events.Where(e => e.Sequence > sequence).OrderBy(e => e.Sequence).ToList();
    }

    public long LatestSequence => _nextSequence;
}
