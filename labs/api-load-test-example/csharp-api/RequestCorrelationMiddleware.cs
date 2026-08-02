using System.Diagnostics;
using System.Text.RegularExpressions;

namespace PoolMonitoringApi;

internal sealed partial class RequestCorrelationMiddleware
{
    internal const string RequestIdHeader = "X-Request-ID";
    internal const string TraceIdHeader = "X-Trace-ID";
    internal const string TestIdHeader = "X-Test-ID";
    internal const string ScenarioHeader = "X-Test-Scenario";

    private const int MaxRequestIdLength = 128;

    private static readonly HashSet<string> AllowedScenarios = new(StringComparer.Ordinal)
    {
        "api-readiness",
        "connection-pool",
        "table-scan-comparison",
    };

    private readonly RequestDelegate _next;
    private readonly ILogger<RequestCorrelationMiddleware> _logger;

    public RequestCorrelationMiddleware(
        RequestDelegate next,
        ILogger<RequestCorrelationMiddleware> logger
    )
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var requestId = ReadSingleHeader(context, RequestIdHeader, IsValidRequestId)
            ?? Guid.NewGuid().ToString("N");
        var testId = ReadSingleHeader(context, TestIdHeader, IsValidTestId);
        var scenario = ReadSingleHeader(
            context,
            ScenarioHeader,
            value => AllowedScenarios.Contains(value)
        );
        var traceId = Activity.Current?.TraceId.ToHexString() ?? context.TraceIdentifier;
        var spanId = Activity.Current?.SpanId.ToHexString();

        context.TraceIdentifier = requestId;
        AddTraceAttributes(Activity.Current, requestId, testId, scenario);

        context.Response.OnStarting(() =>
        {
            context.Response.Headers[RequestIdHeader] = requestId;
            context.Response.Headers[TraceIdHeader] = traceId;

            if (testId is not null)
                context.Response.Headers[TestIdHeader] = testId;
            if (scenario is not null)
                context.Response.Headers[ScenarioHeader] = scenario;

            return Task.CompletedTask;
        });

        var scopeValues = new Dictionary<string, object>
        {
            ["request_id"] = requestId,
            ["trace_id"] = traceId,
        };

        if (spanId is not null)
            scopeValues["span_id"] = spanId;
        if (testId is not null)
            scopeValues["test_id"] = testId;
        if (scenario is not null)
            scopeValues["scenario"] = scenario;

        using (_logger.BeginScope(scopeValues))
        {
            await _next(context);
        }
    }

    private static string? ReadSingleHeader(
        HttpContext context,
        string headerName,
        Func<string, bool> validator
    )
    {
        if (!context.Request.Headers.TryGetValue(headerName, out var values) || values.Count != 1)
            return null;

        var value = values[0];
        return value is not null && validator(value) ? value : null;
    }

    private static bool IsValidRequestId(string value) =>
        value.Length is > 0 and <= MaxRequestIdLength
        && value.All(character => !char.IsControl(character));

    private static bool IsValidTestId(string value) => TestIdPattern().IsMatch(value);

    private static void AddTraceAttributes(
        Activity? activity,
        string requestId,
        string? testId,
        string? scenario
    )
    {
        if (activity is null)
            return;

        activity.SetTag("request.id", requestId);
        if (testId is not null)
            activity.SetTag("test.id", testId);
        if (scenario is not null)
            activity.SetTag("test.scenario", scenario);
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex TestIdPattern();
}

