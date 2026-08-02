---
inclusion: always
---

# OpenTelemetry Conventions

## Metric Naming — Dots, Not Underscores

This project uses the OTel semantic convention of dot-separated metric names throughout.
The OpenTelemetry Prometheus exporter in this repo is configured to **preserve dots** in
instrument names rather than converting them to underscores.

**Always use dot notation when defining instruments:**

```csharp
// Correctv
_meter.CreateObservableGauge("sqlclient.pool.connections_free", ...);

// Wrong — underscores are not the OTel standard
_meter.CreateObservableGauge("sqlclient_pool_connections_free", ...);
```

## Querying Dotted Metric Names in Prometheus / Grafana

Because Prometheus label selectors treat dots as special regex characters, dotted metric names
**must** be queried using the `{__name__="..."}` label selector syntax — not bare metric name
syntax. This applies to all PromQL expressions in Grafana dashboard JSON.

```promql
# Correct — use __name__ label selector
{__name__="sqlclient.pool.connections_free"}
{__name__="http.server.request.duration_seconds_bucket", http.route=~"$endpoint"}

# Wrong — bare name with dots is a regex and will not match correctly
sqlclient.pool.connections_free
```

This also applies to `label_values()` calls in Grafana template variable queries:
```promql
# Correct
label_values({__name__="http.server.request.duration_seconds_count"}, http.route)

# Wrong
label_values(http.server.request.duration_seconds_count, http.route)
```

## Attribute / Label Naming

Span tags and metric labels also follow dot notation per OTel semantic conventions:

- `http.route`, `http.response.status_code` — HTTP attributes
- `db.error.number`, `db.error.class` — SQL error attributes
- `api.endpoint.route`, `api.path` — custom enrichment attributes
- `deployment.environment` — resource attributes

When writing Grafana PromQL, label names with dots must be quoted in selector syntax:

```promql
{__name__="http.server.request.duration_seconds_bucket", http.route=~"$endpoint"}
```

## Telemetry Architecture

The project emits three signals via OpenTelemetry, all configured in `AddAppTelemetryV2()`:

**Traces** — via `AddSqlClientInstrumentation()` and `AddAspNetCoreInstrumentation()`.
SQL spans are enriched with the calling HTTP route via the `Enrich` callback, reading
`http.route` from the parent `Activity` to add `api.endpoint.route` to each SQL span.

**Metrics** — two sources:
1. Built-in OTel instrumentation (`AddAspNetCoreInstrumentation`, `AddSqlClientInstrumentation`)
   emits `http.server.request.duration` and `db.client.*` histograms/counters automatically.
2. `SqlClientEventBridge` — a custom `EventListener` that bridges all 16
   `Microsoft.Data.SqlClient` EventSource counters into OTel `ObservableGauge<double>`
   instruments published through a `Meter` named `"sqlclient"`.

**Logs** — structured logging via `AddOpenTelemetry()` with `IncludeFormattedMessage = true`.

## SqlClientEventBridge Pattern

When bridging EventSource counters to OTel metrics:

- Prefer `Mean` over `Increment` from the counter payload (rate counters expose both)
- Use `ObservableGauge` for snapshot values (current pool state)
- Protect the shared `_latestValues` dictionary with a `Lock` — the EventListener callback
  runs on a thread pool thread separate from the OTel collection callback
- Register the bridge as both a `Singleton` and a `HostedService` so it starts with the app

## Prometheus Scrape Interval

The Prometheus scrape interval is set to `5s` (`scrape_interval: 5s` in `prometheus.yaml`),
matching the `EventCounterIntervalSec = 5` on the SqlClient EventSource. Dashboard panels
use a `[1m]` rate window which provides stable averages at this cadence.

## HTTP Latency Metric

The canonical per-endpoint latency metric is `http.server.request.duration` (emitted by
ASP.NET Core OTel instrumentation). This is the correct metric to use for per-route P95
latency panels because it carries `http.route` as a label. The `db.client.operation.duration`
histogram does not carry HTTP route information and should not be used for per-endpoint
latency panels.

