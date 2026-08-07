using System.Diagnostics;
using System.Diagnostics.Metrics;
using Server.Models;

namespace Server.Services;

public sealed class WebSocketMetrics : IHostedService, IDisposable
{
    public const string MeterName = "ArchitectureLabs.Realtime.WebSocket";

    private readonly Meter _meter = new(MeterName, "1.0.0");
    private readonly Counter<long> _enqueued;
    private readonly Counter<long> _sent;
    private readonly Counter<long> _sendFailures;
    private readonly Counter<long> _backpressureWaits;
    private readonly Histogram<double> _queueDelay;
    private readonly Histogram<double> _backpressureDuration;

    public WebSocketMetrics(ConnectionManager connections)
    {
        _enqueued = _meter.CreateCounter<long>(
            "websocket.outbound.messages.enqueued",
            unit: "{message}"
        );
        _sent = _meter.CreateCounter<long>("websocket.outbound.messages.sent", unit: "{message}");
        _sendFailures = _meter.CreateCounter<long>(
            "websocket.outbound.send.failures",
            unit: "{failure}"
        );
        _backpressureWaits = _meter.CreateCounter<long>(
            "websocket.outbound.backpressure.waits",
            unit: "{wait}"
        );
        _queueDelay = _meter.CreateHistogram<double>("websocket.outbound.queue.delay", unit: "ms");
        _backpressureDuration = _meter.CreateHistogram<double>(
            "websocket.outbound.backpressure.duration",
            unit: "ms"
        );

        _meter.CreateObservableGauge(
            "websocket.connections.active",
            () => CreateMeasurements(connections, _ => 1),
            unit: "{connection}"
        );
        _meter.CreateObservableGauge(
            "websocket.outbound.queue.depth",
            () => CreateMeasurements(connections, connection => connection.Outbound.Reader.Count),
            unit: "{message}"
        );
        _meter.CreateObservableGauge(
            "websocket.outbound.queue.capacity",
            () => CreateMeasurements(connections, _ => ClientConnection.OutboundCapacity),
            unit: "{message}"
        );
    }

    public void MessageEnqueued(string connectionMode, string messageType) =>
        _enqueued.Add(1, CreateTags(connectionMode, messageType));

    public void MessageSent(string connectionMode, string messageType, long enqueuedTimestamp)
    {
        var tags = CreateTags(connectionMode, messageType);
        _sent.Add(1, tags);
        _queueDelay.Record(Stopwatch.GetElapsedTime(enqueuedTimestamp).TotalMilliseconds, tags);
    }

    public void SendFailed(string exceptionType) =>
        _sendFailures.Add(1, new KeyValuePair<string, object?>("error.type", exceptionType));

    public void BackpressureWaited(string connectionMode, string messageType, TimeSpan duration)
    {
        var tags = CreateTags(connectionMode, messageType);
        _backpressureWaits.Add(1, tags);
        _backpressureDuration.Record(duration.TotalMilliseconds, tags);
    }

    private static TagList CreateTags(string connectionMode, string messageType) =>
        new() { { "connection.mode", connectionMode }, { "message.type", messageType } };

    private static Measurement<int>[] CreateMeasurements(
        ConnectionManager connections,
        Func<ClientConnection, int> valueSelector
    ) =>
        new[] { "normal", "slow" }
            .Select(mode => new Measurement<int>(
                connections
                    .Connections.Where(connection => connection.Mode == mode)
                    .Sum(valueSelector),
                new KeyValuePair<string, object?>("connection.mode", mode)
            ))
            .ToArray();

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public void Dispose() => _meter.Dispose();
}
