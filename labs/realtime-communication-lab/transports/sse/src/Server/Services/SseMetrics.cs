using System.Diagnostics;
using System.Diagnostics.Metrics;
using Server.Models;

namespace Server.Services;

public sealed class SseMetrics : IHostedService, IDisposable
{
    public const string MeterName = "ArchitectureLabs.Realtime.Sse";
    private readonly Meter _meter = new(MeterName, "1.0.0");
    private readonly Counter<long> _enqueued;
    private readonly Counter<long> _sent;
    private readonly Counter<long> _waits;
    private readonly Counter<long> _failures;
    private readonly Histogram<double> _delay;

    public SseMetrics(SseConnectionManager connections)
    {
        _enqueued = _meter.CreateCounter<long>("sse.outbound.messages.enqueued", "{message}");
        _sent = _meter.CreateCounter<long>("sse.outbound.messages.sent", "{message}");
        _waits = _meter.CreateCounter<long>("sse.outbound.backpressure.waits", "{wait}");
        _failures = _meter.CreateCounter<long>("sse.outbound.send.failures", "{failure}");
        _delay = _meter.CreateHistogram<double>("sse.outbound.queue.delay", "ms");
        _meter.CreateObservableGauge("sse.connections.active", () => Measure(connections, _ => 1));
        _meter.CreateObservableGauge(
            "sse.outbound.queue.depth",
            () => Measure(connections, c => c.Outbound.Reader.Count)
        );
        _meter.CreateObservableGauge(
            "sse.outbound.queue.capacity",
            () => Measure(connections, _ => SseConnection.Capacity)
        );
    }

    public void Enqueued(string mode) => _enqueued.Add(1, ModeTag(mode));

    public void BackpressureWaited(string mode) => _waits.Add(1, ModeTag(mode));

    public void Sent(string mode, long timestamp)
    {
        var tag = ModeTag(mode);
        _sent.Add(1, tag);
        _delay.Record(Stopwatch.GetElapsedTime(timestamp).TotalMilliseconds, tag);
    }

    public void Failed(string mode) => _failures.Add(1, ModeTag(mode));

    private static KeyValuePair<string, object?> ModeTag(string mode) =>
        new("connection.mode", mode);

    private static Measurement<int>[] Measure(
        SseConnectionManager manager,
        Func<SseConnection, int> selector
    ) =>
        new[] { "normal", "slow" }
            .Select(mode => new Measurement<int>(
                manager.Connections.Where(connection => connection.Mode == mode).Sum(selector),
                ModeTag(mode)
            ))
            .ToArray();

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public void Dispose() => _meter.Dispose();
}
