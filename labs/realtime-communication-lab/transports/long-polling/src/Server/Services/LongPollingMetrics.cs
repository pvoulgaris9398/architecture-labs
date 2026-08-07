using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Server.Services;

public sealed class LongPollingMetrics : IHostedService, IDisposable
{
    public const string MeterName = "ArchitectureLabs.Realtime.LongPolling";
    private readonly Meter _meter = new(MeterName, "1.0.0");
    private readonly Counter<long> _completed;
    private readonly Counter<long> _events;
    private readonly Histogram<double> _duration;
    private int _active;

    public LongPollingMetrics()
    {
        _completed = _meter.CreateCounter<long>("long_poll.requests.completed", "{request}");
        _events = _meter.CreateCounter<long>("long_poll.events.returned", "{event}");
        _duration = _meter.CreateHistogram<double>("long_poll.wait.duration", "ms");
        _meter.CreateObservableGauge(
            "long_poll.requests.active",
            () => Volatile.Read(ref _active),
            "{request}"
        );
    }

    public long Start()
    {
        Interlocked.Increment(ref _active);
        return Stopwatch.GetTimestamp();
    }

    public void Complete(long started, string outcome, int count)
    {
        Interlocked.Decrement(ref _active);
        var tag = new KeyValuePair<string, object?>("outcome", outcome);
        _completed.Add(1, tag);
        _events.Add(count);
        _duration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds, tag);
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public void Dispose() => _meter.Dispose();
}
