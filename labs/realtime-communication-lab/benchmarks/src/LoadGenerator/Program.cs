using System.Net.Http.Json;
using System.Text.Json;
using LoadGenerator;

if (args.Length == 2 && args[0].Equals("--summarize", StringComparison.OrdinalIgnoreCase))
    return await ResultSummarizer.RunAsync(args[1]);

var options = BenchmarkOptions.Parse(args);
using var shutdown = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    shutdown.Cancel();
};
var environment = await EnvironmentCollector.CaptureAsync(shutdown.Token);
var measuredScheduleLags = new List<double>();

using var handler = new SocketsHttpHandler
{
    MaxConnectionsPerServer = Math.Max(1024, options.Subscribers + 10),
    PooledConnectionLifetime = TimeSpan.FromMinutes(5),
};
using var publisher = new HttpClient(handler, disposeHandler: false) { BaseAddress = options.BaseUri };

ITransportAdapter adapter = options.Transport switch
{
    TransportKind.WebSocket => new WebSocketAdapter(options.BaseUri),
    TransportKind.Sse => new SseAdapter(options.BaseUri, handler),
    TransportKind.LongPolling => new LongPollingAdapter(
        options.BaseUri,
        handler,
        options.PollTimeout
    ),
    _ => throw new ArgumentOutOfRangeException(),
};

var measurements = new MeasurementStore();
var subscribers = new List<ISubscriber>(options.Subscribers);
try
{
    Console.WriteLine(
        $"Connecting {options.Subscribers} {options.Transport} subscribers to {options.BaseUri}..."
    );
    for (var index = 0; index < options.Subscribers; index++)
    {
        subscribers.Add(
            await adapter.StartSubscriberAsync(index, measurements.Delivered, shutdown.Token)
        );
    }
    await Task.WhenAll(subscribers.Select(item => item.Ready));

    var runId = Guid.NewGuid().ToString("N");
    Console.WriteLine($"Warming up for {options.Warmup.TotalSeconds:N0} seconds...");
    var warmupPosts = await SchedulePublishesAsync(
        "warmup",
        options.Warmup,
        recordMeasurements: false
    );
    await Task.WhenAll(warmupPosts);
    adapter.Diagnostics.ResetMeasurementCounters();

    Console.WriteLine(
        $"Measuring {options.Rate} messages/second for {options.Duration.TotalSeconds:N0} seconds..."
    );
    var metricsBeforeCapturedAt = DateTimeOffset.UtcNow;
    var metricsBefore = await publisher.GetStringAsync("/metrics", shutdown.Token);
    using var samplingCancellation = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);
    var sampler = string.IsNullOrWhiteSpace(options.ContainerName)
        ? null
        : new DockerStatsSampler(options.ContainerName);
    var samplingTask = sampler?.RunAsync(samplingCancellation.Token) ?? Task.CompletedTask;
    var measuredPosts = await SchedulePublishesAsync(
        "measured",
        options.Duration,
        recordMeasurements: true
    );
    samplingCancellation.Cancel();
    try
    {
        await samplingTask;
    }
    catch (OperationCanceledException) when (samplingCancellation.IsCancellationRequested) { }

    var drainStarted = System.Diagnostics.Stopwatch.StartNew();
    bool[] publishOutcomes;
    try
    {
        publishOutcomes = options.Drain > TimeSpan.Zero
            ? await Task.WhenAll(measuredPosts).WaitAsync(options.Drain, shutdown.Token)
            : await Task.WhenAll(measuredPosts).WaitAsync(TimeSpan.Zero, shutdown.Token);
    }
    catch (TimeoutException)
    {
        throw new InvalidOperationException(
            $"Measured publisher requests did not finish within the {options.Drain.TotalSeconds:N0}-second drain period."
        );
    }

    var remainingDrain = options.Drain - drainStarted.Elapsed;
    if (remainingDrain > TimeSpan.Zero)
    {
        Console.WriteLine(
            $"Draining in-flight deliveries for up to {remainingDrain.TotalSeconds:N1} more seconds..."
        );
        await Task.Delay(remainingDrain, shutdown.Token);
    }
    var failures = publishOutcomes.Count(succeeded => !succeeded);
    var requestSnapshot = adapter.Diagnostics.Snapshot(measuredPosts.Count, failures);
    var metricsAfterCapturedAt = DateTimeOffset.UtcNow;
    var metricsAfter = await publisher.GetStringAsync("/metrics", shutdown.Token);

    var subscriberFailures = subscribers
        .Select(
            (subscriber, index) =>
                (subscriber, index)
        )
        .Where(item => item.subscriber.Completion.IsCompleted)
        .Select(item =>
        {
            var completion = item.subscriber.Completion;
            var status = completion.IsFaulted
                ? "faulted"
                : completion.IsCanceled
                    ? "cancelled"
                    : "disconnected";
            var error = completion.Exception?.GetBaseException().Message;
            return new SubscriberFailure(item.index, status, error);
        })
        .ToArray();

    var result = measurements.Build(
        options,
        failures,
        sampler?.Samples ?? [],
        new PrometheusEvidence(
            metricsBeforeCapturedAt,
            metricsBefore,
            metricsAfterCapturedAt,
            metricsAfter
        ),
        subscriberFailures,
        requestSnapshot,
        environment,
        measuredScheduleLags
    );
    var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
    if (!string.IsNullOrWhiteSpace(options.OutputPath))
    {
        var fullPath = Path.GetFullPath(options.OutputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporaryPath = fullPath + ".tmp";
        await File.WriteAllTextAsync(temporaryPath, json + Environment.NewLine, shutdown.Token);
        File.Move(temporaryPath, fullPath, overwrite: true);
        Console.WriteLine($"Wrote {fullPath}");
        Console.WriteLine(
            $"Result: delivered {result.UniqueDeliveries}/{result.ExpectedDeliveries}, "
                + $"missing {result.MissingDeliveries}, duplicates {result.DuplicateDeliveries}, "
                + $"out-of-order {result.OutOfOrderDeliveries}, schedule valid {result.PublisherSchedule.Passed}."
        );
    }
    else
        Console.WriteLine(json);

    if (!result.PublisherSchedule.Passed)
        return 3;
    return options.FailOnReliabilityIssue && !result.ReliabilityPassed ? 2 : 0;

    async Task<List<Task<bool>>> SchedulePublishesAsync(
        string phase,
        TimeSpan duration,
        bool recordMeasurements
    )
    {
        if (duration == TimeSpan.Zero)
            return [];
        var interval = TimeSpan.FromSeconds(1d / options.Rate);
        var started = System.Diagnostics.Stopwatch.StartNew();
        var next = TimeSpan.Zero;
        var index = 0;
        var posts = new List<Task<bool>>((int)Math.Ceiling(duration.TotalSeconds * options.Rate));
        while (started.Elapsed < duration && !shutdown.IsCancellationRequested)
        {
            if (recordMeasurements)
            {
                measuredScheduleLags.Add(
                    Math.Max(0, (started.Elapsed - next).TotalMilliseconds)
                );
            }
            var identifier = $"benchmark-{runId}-{phase}-{index++:D8}";
            if (identifier.Length > options.PayloadBytes)
                throw new InvalidOperationException(
                    $"--payload-bytes must be at least {identifier.Length} for this run."
                );
            var message = identifier.PadRight(options.PayloadBytes, 'x');
            if (recordMeasurements)
                measurements.Published(message);
            posts.Add(PublishOneAsync(message));

            next += interval;
            var delay = next - started.Elapsed;
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, shutdown.Token);
        }
        return posts;
    }

    async Task<bool> PublishOneAsync(string message)
    {
        try
        {
            using var response = await publisher.PostAsJsonAsync(
                "/api/events",
                new PublishRequest(message),
                shutdown.Token
            );
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }
}
finally
{
    foreach (var subscriber in subscribers)
        await subscriber.DisposeAsync();
}
