using System.Diagnostics;
using Microsoft.AspNetCore.Routing;

namespace PoolMonitoringApi;

internal sealed class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;
    private readonly bool _logSuccessfulRequests;
    private readonly bool _logSlowRequests;
    private readonly double _slowRequestThresholdMs;

    public RequestLoggingMiddleware(
        RequestDelegate next,
        ILogger<RequestLoggingMiddleware> logger,
        IConfiguration configuration
    )
    {
        _next = next;
        _logger = logger;
        _logSuccessfulRequests = configuration.GetValue<bool>(
            "Diagnostics:LogSuccessfulRequests"
        );
        _logSlowRequests = configuration.GetValue<bool>("Diagnostics:LogSlowRequests");
        _slowRequestThresholdMs = Math.Max(
            1,
            configuration.GetValue<double?>("Diagnostics:SlowRequestThresholdMs") ?? 500
        );
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var startedAt = Stopwatch.GetTimestamp();

        try
        {
            await _next(context);
        }
        catch (Exception exception)
        {
            var durationMs = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
            _logger.LogError(
                exception,
                "HTTP request failed: {http_request_method} {http_route} returned {http_response_status_code} after {duration_ms} ms",
                context.Request.Method,
                GetRoute(context),
                StatusCodes.Status500InternalServerError,
                durationMs
            );
            throw;
        }

        var statusCode = context.Response.StatusCode;
        var elapsedMs = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
        var route = GetRoute(context);

        if (statusCode >= StatusCodes.Status500InternalServerError)
        {
            _logger.LogError(
                "HTTP request completed with a server error: {http_request_method} {http_route} returned {http_response_status_code} after {duration_ms} ms",
                context.Request.Method,
                route,
                statusCode,
                elapsedMs
            );
        }
        else if (statusCode >= StatusCodes.Status400BadRequest)
        {
            _logger.LogWarning(
                "HTTP request completed with a client error: {http_request_method} {http_route} returned {http_response_status_code} after {duration_ms} ms",
                context.Request.Method,
                route,
                statusCode,
                elapsedMs
            );
        }
        else if (_logSlowRequests && elapsedMs >= _slowRequestThresholdMs)
        {
            _logger.LogWarning(
                "Slow HTTP request: {http_request_method} {http_route} returned {http_response_status_code} after {duration_ms} ms",
                context.Request.Method,
                route,
                statusCode,
                elapsedMs
            );
        }
        else if (_logSuccessfulRequests)
        {
            _logger.LogInformation(
                "HTTP request completed: {http_request_method} {http_route} returned {http_response_status_code} after {duration_ms} ms",
                context.Request.Method,
                route,
                statusCode,
                elapsedMs
            );
        }
    }

    private static string GetRoute(HttpContext context) =>
        (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText
        ?? context.Request.Path.Value
        ?? "unknown";
}

