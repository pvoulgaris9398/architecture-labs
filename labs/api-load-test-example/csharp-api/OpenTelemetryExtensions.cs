using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Diagnostics.Tracing;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace PoolMonitoringApi;

public static class OpenTelemetryExtensions
{
    // Short meter name used by SqlClientEventBridge to publish pool metrics.
    // Kept intentionally short — the OTel Prometheus exporter uses this as a
    // instrumentation scope, while instrument names remain queryable in Prometheus using their
    // OpenTelemetry dotted form, for example {__name__="sqlclient.pool.connections_free"}.
    internal const string SqlClientMeterName = "sqlclient";

    public static WebApplicationBuilder AddAppTelemetryV1(
        this WebApplicationBuilder builder,
        string serviceName
    )
    {
        // Configure OpenTelemetry Core
        builder
            .Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName))
            .WithMetrics(metrics =>
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddSqlClientInstrumentation()
                    .AddPrometheusExporter()
            )
            .WithTracing(tracing =>
                tracing
                    .AddAspNetCoreInstrumentation()
                    // FIX: Use the official enrichment hook instead of a global background listener
                    .AddSqlClientInstrumentation(options =>
                    {
                        options.RecordException = true;
                        options.Enrich = (activity, method, cmd) =>
                        {
                            // Safely grab the true HTTP route attribute from the active web execution layer
                            var httpRoute =
                                Activity.Current?.GetTagItem("http.route")?.ToString()
                                ?? "unknown-endpoint";

                            activity.SetTag("api.endpoint.route", httpRoute);
                        };
                    })
            );
        return builder;
    }

    public static WebApplicationBuilder AddAppTelemetryV2(
        this WebApplicationBuilder builder,
        string serviceName
    )
    {
        var otlpEnabled =
            builder.Configuration.GetValue("Telemetry:OtlpEnabled", true)
            && !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        // 1. Define shared resource metadata
        var resourceBuilder = ResourceBuilder
            .CreateDefault()
            .AddService(serviceName, serviceVersion: "1.0.0")
            .AddAttributes(
                new Dictionary<string, object>
                {
                    ["deployment.environment"] = builder.Environment.EnvironmentName,
                }
            );

        // 2. Configure Tracing and Metrics
        builder
            .Services.AddOpenTelemetry()
            .WithTracing(tracing =>
            {
                tracing
                    .SetResourceBuilder(resourceBuilder)
                    // (1a) Enrich HTTP spans with route info and classify SQL-related failures
                    .AddAspNetCoreInstrumentation(options =>
                    {
                        options.RecordException = true;
                        options.EnrichWithHttpRequest = (activity, request) =>
                            activity.SetTag("api.path", request.Path.Value);
                        options.EnrichWithHttpResponse = (activity, response) =>
                            activity.SetTag("http.response.status_code", response.StatusCode);
                        options.EnrichWithException = (activity, ex) =>
                        {
                            // Surface SQL connection / pool errors as first-class span attributes
                            if (ex is SqlException sqlEx)
                            {
                                activity.SetTag("db.error.number", sqlEx.Number);
                                activity.SetTag("db.error.class", sqlEx.Class);
                                activity.SetTag("db.error.server", sqlEx.Server);
                                activity.SetStatus(ActivityStatusCode.Error, sqlEx.Message);
                            }
                            // InvalidOperationException is thrown on pool exhaustion ("Connection pool exhausted")
                            else if (ex is InvalidOperationException)
                            {
                                activity.SetTag("db.error.type", "connection_pool_exhausted");
                                activity.SetStatus(ActivityStatusCode.Error, ex.Message);
                            }
                        };
                    })
                    .AddSqlClientInstrumentation(options =>
                    {
                        options.SetDbStatementForText = true; // Captures raw SQL text
                        options.RecordException = true; // Captures SQL exceptions
                        // (1b) Tag each SQL span with the calling HTTP route for correlation
                        options.Enrich = (activity, eventName, obj) =>
                        {
                            var httpRoute = FindParentTag(activity, "http.route", "api.path");
                            activity.SetTag("api.endpoint.route", httpRoute);
                        };
                    });

                if (otlpEnabled)
                    tracing.AddOtlpExporter();
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .SetResourceBuilder(resourceBuilder)
                    .AddAspNetCoreInstrumentation()
                    .AddSqlClientInstrumentation() // Built-in db.client.connections.* metrics
                                                   // (2) Bridge Microsoft.Data.SqlClient EventSource counters into OTel metrics
                    .AddMeter(SqlClientMeterName)
                    .AddPrometheusExporter(); // Enables /metrics scrape endpoint for Prometheus

                if (otlpEnabled)
                    metrics.AddOtlpExporter();
            }
            );

        // 3. Configure Logging
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.SetResourceBuilder(resourceBuilder);
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
            if (otlpEnabled)
                logging.AddOtlpExporter();
        });

        // (2) Register the EventSource bridge as a hosted singleton so it starts with the app
        builder.Services.AddSingleton<SqlClientEventBridge>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<SqlClientEventBridge>());

        return builder;
    }

    private static string FindParentTag(Activity activity, params string[] tagNames)
    {
        for (Activity? current = activity; current is not null; current = current.Parent)
        {
            foreach (var tagName in tagNames)
            {
                if (current.GetTagItem(tagName)?.ToString() is { Length: > 0 } value)
                    return value;
            }
        }

        return "unknown-endpoint";
    }
}

/// <summary>
/// Bridges the 16 Microsoft.Data.SqlClient EventCounters into OpenTelemetry metrics
/// by implementing an in-process EventListener and publishing values through a
/// System.Diagnostics.Metrics.Meter. The OTel SDK picks these up via AddMeter().
/// </summary>
/// <remarks>
/// Counter reference: https://learn.microsoft.com/en-us/sql/connect/ado-net/event-counters
/// EventCounterIntervalSec controls how often SqlClient pushes updates (default: 5 s).
/// </remarks>
internal sealed class SqlClientEventBridge : EventListener, IHostedService
{
    private const string EventSourceName = "Microsoft.Data.SqlClient.EventSource";
    private const int SamplingIntervalSeconds = 5;

    private readonly Meter _meter;

    // Gauges for pool-level counters (current snapshot values)
    private readonly Dictionary<string, ObservableGauge<double>> _gauges = new();

    // Latest values read from the EventSource — written by the EventListener
    // callback thread, read by the OTel observable gauge callbacks.
    private readonly Dictionary<string, double> _latestValues = new(
        StringComparer.OrdinalIgnoreCase
    );
    private readonly Lock _valuesLock = new();

    public SqlClientEventBridge()
    {
        _meter = new Meter(OpenTelemetryExtensions.SqlClientMeterName, "1.0.0");
        RegisterGauges();
    }

    // IHostedService — nothing extra needed; the EventListener is activated in the
    // constructor, and EnableEvents is called from OnEventSourceCreated.
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Dispose();
        return Task.CompletedTask;
    }

    protected override void OnEventSourceCreated(EventSource eventSource)
    {
        if (!eventSource.Name.Equals(EventSourceName, StringComparison.Ordinal))
            return;

        EnableEvents(
            eventSource,
            EventLevel.Informational,
            EventKeywords.None,
            new Dictionary<string, string?>
            {
                ["EventCounterIntervalSec"] = SamplingIntervalSeconds.ToString(),
            }
        );
    }

    protected override void OnEventWritten(EventWrittenEventArgs eventData)
    {
        // EventCounter payloads arrive as a list of IDictionary<string, object>
        if (eventData.Payload is null)
            return;

        foreach (var payload in eventData.Payload)
        {
            if (payload is not IDictionary<string, object> counters)
                continue;

            if (!counters.TryGetValue("Name", out var nameObj) || nameObj is not string name)
                continue;

            // Mean values are already snapshots. Increment values contain the total accumulated
            // during the EventCounter reporting interval, so normalize them to a per-second rate.
            double value = 0;
            if (counters.TryGetValue("Mean", out var meanObj) && meanObj is double mean)
                value = mean;
            else if (counters.TryGetValue("Increment", out var incrObj) && incrObj is double incr)
                value = incr / SamplingIntervalSeconds;

            lock (_valuesLock)
            {
                _latestValues[name] = value;
            }
        }
    }

    private void RegisterGauges()
    {
        // All 16 SqlClient event counter names mapped to instrument names.
        // The OTel Prometheus exporter preserves dots in instrument names (no underscore conversion),
        // so these are queryable in Prometheus/Grafana as: {__name__="sqlclient.pool.connections_free"}
        var counters = new[]
        {
            (
                "active-hard-connections",
                "sqlclient.connections.active_hard",
                "Active physical connections to the server"
            ),
            (
                "hard-connects",
                "sqlclient.connections.hard_connects",
                "Physical connection opens per second"
            ),
            (
                "hard-disconnects",
                "sqlclient.connections.hard_disconnects",
                "Physical connection closes per second"
            ),
            (
                "active-soft-connects",
                "sqlclient.pool.active_soft_connections",
                "Connections retrieved from the pool"
            ),
            (
                "soft-connects",
                "sqlclient.pool.soft_connects",
                "Pool connection checkouts per second"
            ),
            (
                "soft-disconnects",
                "sqlclient.pool.soft_disconnects",
                "Pool connection returns per second"
            ),
            (
                "number-of-non-pooled-connections",
                "sqlclient.pool.non_pooled_connections",
                "Active connections bypassing the pool"
            ),
            (
                "number-of-pooled-connections",
                "sqlclient.pool.pooled_connections",
                "Active connections managed by the pool"
            ),
            (
                "number-of-active-connection-pool-groups",
                "sqlclient.pool.active_pool_groups",
                "Unique connection string groups (active)"
            ),
            (
                "number-of-inactive-connection-pool-groups",
                "sqlclient.pool.inactive_pool_groups",
                "Unique connection string groups awaiting pruning"
            ),
            (
                "number-of-active-connection-pools",
                "sqlclient.pool.active_pools",
                "Total active connection pools"
            ),
            (
                "number-of-inactive-connection-pools",
                "sqlclient.pool.inactive_pools",
                "Inactive pools awaiting disposal"
            ),
            (
                "number-of-active-connections",
                "sqlclient.pool.connections_in_use",
                "Connections currently in use"
            ),
            (
                "number-of-free-connections",
                "sqlclient.pool.connections_free",
                "Connections available in the pool"
            ),
            (
                "number-of-stasis-connections",
                "sqlclient.pool.connections_stasis",
                "Connections awaiting action (unavailable to app)"
            ),
            (
                "number-of-reclaimed-connections",
                "sqlclient.pool.reclaimed_connections",
                "Connections reclaimed by GC (Close/Dispose not called)"
            ),
        };

        foreach (var (eventName, metricName, description) in counters)
        {
            var capturedName = eventName; // Capture for closure
            _gauges[eventName] = _meter.CreateObservableGauge(
                metricName,
                () =>
                {
                    lock (_valuesLock)
                    {
                        return _latestValues.TryGetValue(capturedName, out var v)
                            ? new Measurement<double>(v)
                            : new Measurement<double>(0);
                    }
                },
                description: description
            );
        }
    }

    public override void Dispose()
    {
        base.Dispose();
        _meter.Dispose();
    }
}
