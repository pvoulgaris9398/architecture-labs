namespace LoadGenerator;

internal sealed record BenchmarkOptions(
    TransportKind Transport,
    Uri BaseUri,
    int Subscribers,
    int Rate,
    int PayloadBytes,
    TimeSpan Warmup,
    TimeSpan Duration,
    TimeSpan Drain,
    TimeSpan PollTimeout,
    string? ContainerName,
    bool FailOnReliabilityIssue,
    string? OutputPath
)
{
    public static BenchmarkOptions Parse(string[] args)
    {
        var values = args
            .Select((value, index) => (value, index))
            .Where(item => item.value.StartsWith("--", StringComparison.Ordinal))
            .ToDictionary(
                item => item.value[2..],
                item => item.index + 1 < args.Length ? args[item.index + 1] : "",
                StringComparer.OrdinalIgnoreCase
            );

        var transport = ParseTransport(Get(values, "transport", "websocket"));
        var defaultUri = transport switch
        {
            TransportKind.WebSocket => "http://127.0.0.1:5000",
            TransportKind.Sse => "http://127.0.0.1:5001",
            TransportKind.LongPolling => "http://127.0.0.1:5002",
            _ => throw new ArgumentOutOfRangeException(),
        };

        return new BenchmarkOptions(
            transport,
            new Uri(Get(values, "base-url", defaultUri)),
            PositiveInt(values, "subscribers", 10),
            PositiveInt(values, "rate", 10),
            PositiveInt(values, "payload-bytes", 256),
            TimeSpan.FromSeconds(NonNegativeInt(values, "warmup-seconds", 30)),
            TimeSpan.FromSeconds(PositiveInt(values, "duration-seconds", 120)),
            TimeSpan.FromSeconds(NonNegativeInt(values, "drain-seconds", 5)),
            TimeSpan.FromSeconds(PositiveInt(values, "poll-timeout-seconds", 30)),
            values.GetValueOrDefault("container-name"),
            Bool(values, "fail-on-reliability-issue", false),
            values.GetValueOrDefault("output")
        );
    }

    private static TransportKind ParseTransport(string value) =>
        value.ToLowerInvariant() switch
        {
            "websocket" => TransportKind.WebSocket,
            "sse" => TransportKind.Sse,
            "long-polling" => TransportKind.LongPolling,
            _ => throw new ArgumentException(
                "--transport must be websocket, sse, or long-polling."
            ),
        };

    private static int PositiveInt(Dictionary<string, string> values, string name, int fallback)
    {
        var parsed = int.Parse(Get(values, name, fallback.ToString()));
        return parsed > 0 ? parsed : throw new ArgumentOutOfRangeException(name);
    }

    private static int NonNegativeInt(
        Dictionary<string, string> values,
        string name,
        int fallback
    )
    {
        var parsed = int.Parse(Get(values, name, fallback.ToString()));
        return parsed >= 0 ? parsed : throw new ArgumentOutOfRangeException(name);
    }

    private static string Get(
        Dictionary<string, string> values,
        string name,
        string fallback
    ) => values.GetValueOrDefault(name, fallback);

    private static bool Bool(
        Dictionary<string, string> values,
        string name,
        bool fallback
    ) => bool.TryParse(Get(values, name, fallback.ToString()), out var parsed)
        ? parsed
        : throw new ArgumentException($"--{name} must be true or false.");
}

internal enum TransportKind
{
    WebSocket,
    Sse,
    LongPolling,
}
