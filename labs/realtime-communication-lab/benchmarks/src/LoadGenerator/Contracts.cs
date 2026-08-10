using System.Collections.Concurrent;
using System.Diagnostics;

namespace LoadGenerator;

internal sealed record EventRecord(long Sequence, DateTime Timestamp, string Message);

internal sealed record PublishRequest(string Message);

internal sealed record Delivery(int Subscriber, EventRecord Event, long ReceivedTimestamp);

internal interface ISubscriber : IAsyncDisposable
{
    Task Ready { get; }
    Task Completion { get; }
}

internal interface ITransportAdapter
{
    TransportDiagnostics Diagnostics { get; }

    Task<ISubscriber> StartSubscriberAsync(
        int subscriber,
        Action<Delivery> onDelivery,
        CancellationToken cancellationToken
    );
}

internal sealed class TransportDiagnostics
{
    private long _webSocketUpgradeAttempts;
    private long _sseStreamRequests;
    private long _longPollRequests;
    private long _longPollTimeoutResponses;

    public void WebSocketUpgradeAttempted() => Interlocked.Increment(ref _webSocketUpgradeAttempts);
    public void SseStreamRequested() => Interlocked.Increment(ref _sseStreamRequests);
    public void LongPollRequested() => Interlocked.Increment(ref _longPollRequests);
    public void LongPollTimedOut() => Interlocked.Increment(ref _longPollTimeoutResponses);

    public void ResetMeasurementCounters()
    {
        Interlocked.Exchange(ref _longPollRequests, 0);
        Interlocked.Exchange(ref _longPollTimeoutResponses, 0);
    }

    public TransportRequestSnapshot Snapshot(int publishAttempts, int publishFailures) =>
        new(
            publishAttempts,
            publishAttempts - publishFailures,
            publishFailures,
            Interlocked.Read(ref _webSocketUpgradeAttempts),
            Interlocked.Read(ref _sseStreamRequests),
            Interlocked.Read(ref _longPollRequests),
            Interlocked.Read(ref _longPollTimeoutResponses)
        );
}

internal sealed record TransportRequestSnapshot(
    int PublisherPostAttempts,
    int PublisherPostSuccesses,
    int PublisherPostFailures,
    long WebSocketUpgradeAttempts,
    long SseStreamRequests,
    long LongPollRequests,
    long LongPollTimeoutResponses
);

internal sealed class MeasurementStore
{
    private readonly ConcurrentDictionary<string, long> _published = new();
    private readonly ConcurrentDictionary<(int Subscriber, string Message), int> _deliveries = new();
    private readonly ConcurrentBag<double> _latencies = [];
    private long _outOfOrder;
    private readonly ConcurrentDictionary<int, long> _lastSequence = new();

    public void Published(string message) => _published[message] = Stopwatch.GetTimestamp();

    public void Delivered(Delivery delivery)
    {
        if (!_published.TryGetValue(delivery.Event.Message, out var publishedAt))
            return;

        var count = _deliveries.AddOrUpdate(
            (delivery.Subscriber, delivery.Event.Message),
            1,
            (_, current) => current + 1
        );
        if (count == 1)
        {
            _latencies.Add(
                Stopwatch.GetElapsedTime(publishedAt, delivery.ReceivedTimestamp).TotalMilliseconds
            );
        }

        _lastSequence.AddOrUpdate(
            delivery.Subscriber,
            delivery.Event.Sequence,
            (_, previous) =>
            {
                if (delivery.Event.Sequence <= previous)
                    Interlocked.Increment(ref _outOfOrder);
                return Math.Max(previous, delivery.Event.Sequence);
            }
        );
    }

    public BenchmarkResult Build(
        BenchmarkOptions options,
        int publishFailures,
        IReadOnlyList<ResourceSample> resourceSamples,
        PrometheusEvidence prometheus,
        IReadOnlyList<SubscriberFailure> subscriberFailures,
        TransportRequestSnapshot requests,
        EnvironmentEvidence environment,
        IReadOnlyList<double> scheduleLagMilliseconds
    )
    {
        var samples = _latencies.Order().ToArray();
        var expected = _published.Count * options.Subscribers;
        var unique = _deliveries.Count;
        var scheduleLags = scheduleLagMilliseconds.Order().ToArray();
        var achievedRate = _published.Count / options.Duration.TotalSeconds;
        var p99ScheduleLag = Percentile(scheduleLags, 0.99);
        var maximumAllowedScheduleLag = 2_000d / options.Rate;
        var minimumAllowedRate = options.Rate * 0.99;
        var maximumAllowedRate = options.Rate * 1.01;
        return new BenchmarkResult(
            options.Transport.ToString(),
            options.Subscribers,
            options.Rate,
            options.PayloadBytes,
            options.Warmup.TotalSeconds,
            options.Duration.TotalSeconds,
            options.Drain.TotalSeconds,
            _published.Count,
            expected,
            unique,
            Math.Max(0, expected - unique),
            _deliveries.Values.Sum(value => Math.Max(0, value - 1)),
            _outOfOrder,
            publishFailures,
            Percentile(samples, 0.50),
            Percentile(samples, 0.95),
            Percentile(samples, 0.99),
            samples.Length == 0 ? null : samples[^1],
            resourceSamples,
            prometheus,
            subscriberFailures,
            0,
            requests,
            environment,
            new PublisherScheduleEvidence(
                options.Rate,
                achievedRate,
                Percentile(scheduleLags, 0.50),
                Percentile(scheduleLags, 0.95),
                p99ScheduleLag,
                scheduleLags.Length == 0 ? null : scheduleLags[^1],
                maximumAllowedScheduleLag,
                achievedRate >= minimumAllowedRate
                    && achievedRate <= maximumAllowedRate
                    && p99ScheduleLag <= maximumAllowedScheduleLag
            ),
            expected == unique
                && _deliveries.Values.All(value => value == 1)
                && _outOfOrder == 0
                && publishFailures == 0
                && subscriberFailures.Count == 0,
            DateTimeOffset.UtcNow
        );
    }

    private static double? Percentile(double[] values, double percentile)
    {
        if (values.Length == 0)
            return null;
        var index = (int)Math.Ceiling(percentile * values.Length) - 1;
        return values[Math.Clamp(index, 0, values.Length - 1)];
    }
}

internal sealed record BenchmarkResult(
    string Transport,
    int Subscribers,
    int TargetRate,
    int PayloadBytes,
    double WarmupSeconds,
    double DurationSeconds,
    double DrainSeconds,
    int Published,
    int ExpectedDeliveries,
    int UniqueDeliveries,
    int MissingDeliveries,
    int DuplicateDeliveries,
    long OutOfOrderDeliveries,
    int PublishFailures,
    double? P50Milliseconds,
    double? P95Milliseconds,
    double? P99Milliseconds,
    double? MaximumMilliseconds,
    IReadOnlyList<ResourceSample> ResourceSamples,
    PrometheusEvidence Prometheus,
    IReadOnlyList<SubscriberFailure> SubscriberFailures,
    int Reconnects,
    TransportRequestSnapshot Requests,
    EnvironmentEvidence Environment,
    PublisherScheduleEvidence PublisherSchedule,
    bool ReliabilityPassed,
    DateTimeOffset CompletedAt
);

internal sealed record PublisherScheduleEvidence(
    int TargetRatePerSecond,
    double AchievedRatePerSecond,
    double? P50LagMilliseconds,
    double? P95LagMilliseconds,
    double? P99LagMilliseconds,
    double? MaximumLagMilliseconds,
    double MaximumAllowedP99LagMilliseconds,
    bool Passed
);

internal sealed record EnvironmentEvidence(
    DateTimeOffset StartedAt,
    string GitCommit,
    bool GitWorktreeDirty,
    string OperatingSystem,
    string OperatingSystemArchitecture,
    string ProcessArchitecture,
    string Cpu,
    int LogicalProcessors,
    long RuntimeAvailableMemoryBytes,
    string DotnetSdkVersion,
    string DotnetRuntimeVersion,
    string DockerVersion,
    string DockerComposeVersion,
    long? DockerMemoryBytes
);

internal sealed record SubscriberFailure(int Subscriber, string Status, string? Error);

internal sealed record PrometheusEvidence(
    DateTimeOffset BeforeCapturedAt,
    string Before,
    DateTimeOffset AfterCapturedAt,
    string After
);

internal sealed record ResourceSample(
    DateTimeOffset CapturedAt,
    string CpuPercent,
    string MemoryUsage,
    string MemoryPercent,
    string NetworkIo,
    string BlockIo,
    string ProcessCount
);
