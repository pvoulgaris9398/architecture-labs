# OpenTelemetry Collector operating notes

## Role in this lab

The Collector is the backend-neutral OTLP boundary between the API and telemetry storage. The API
exports traces, logs, and metrics to it over OTLP/HTTP. Prometheus still scrapes the API's
`/metrics` endpoint directly, so a Collector outage does not remove the existing server metrics or
stop the API from serving requests.

The Collector currently sends telemetry to a rate-limited debug exporter. This proves ingestion,
processing, sampling, batching, and queue behavior before Step 5 adds Seq as the durable log and
trace backend. Debug output is not retained and is not an experiment result.

## Version decision

The OpenTelemetry Collector does not publish an LTS channel. Version 0.157.0 was the latest stable
Collector release available when this implementation was written on August 2, 2026, so the contrib
image is pinned as `otel/opentelemetry-collector-contrib:0.157.0`. Treat that pin as intentional and
evaluate later upgrades separately.

References:

- [OpenTelemetry Collector](https://opentelemetry.io/docs/collector/)
- [Collector v0.157.0 release](https://github.com/open-telemetry/opentelemetry-collector-releases/releases/tag/v0.157.0)
- [Collector internal telemetry](https://opentelemetry.io/docs/collector/internal-telemetry/)

## Signal flow

```text
.NET API -- OTLP/HTTP --> Collector -- temporary debug exporter
    |
    +-- /metrics --> Prometheus --> Grafana

Collector /metrics --> Prometheus --> Grafana
```

The API SDK and Collector debug-exporter queues are bounded. OTLP export happens asynchronously;
the .NET exporter uses its in-memory transient retry mode. When the Collector is unavailable, the
SDK retries or drops telemetry according to its bounded in-memory capacity rather than blocking
request processing. Telemetry loss during an outage is expected and must be visible in SDK logs
and Collector self-metrics.

## Processing policy

The Collector applies these processors in order:

1. A memory limiter with a 384 MiB steady limit and 96 MiB spike allowance inside a 512 MiB
   container limit.
2. Tail sampling for traces using the policy defined in `observability-conventions.md`:
   - retain all error-status and HTTP 4xx/5xx traces;
   - retain all traces lasting at least 500 ms;
   - retain 1% of otherwise successful `/health` traces; and
   - retain 10% of other successful traces.
3. Batching with a target of 1,024 records and a maximum batch of 2,048.

Logs and metrics use the memory limiter and batch processor but are not sampled. The debug exporter
has a bounded 512-batch queue and two consumers. Because it is a local diagnostic sink, it does not
have a network retry policy. Step 5 must configure bounded retry behavior on the Collector-to-Seq
network exporter.

## Endpoints

| Purpose | Container endpoint | Default host endpoint |
| --- | --- | --- |
| OTLP gRPC receiver | `otel-collector:4317` | `127.0.0.1:14317` |
| OTLP HTTP receiver | `otel-collector:4318` | `127.0.0.1:14318` |
| Health extension | `otel-collector:13133` | `127.0.0.1:13133` |
| Internal Prometheus metrics | `otel-collector:8888/metrics` | `127.0.0.1:18888/metrics` |

Host ports are configurable in `.env`; internal Compose endpoints remain unchanged.

## Start and verify

```bash
docker compose up -d --build
curl --fail http://127.0.0.1:13133
curl --fail http://127.0.0.1:18888/metrics
curl --fail http://127.0.0.1:18080/health
docker compose logs --tail 100 otel-collector
```

In Prometheus, verify both scrape targets are up:

```promql
up{job=~"csharp-api|otel-collector"}
```

Useful Collector metrics include receiver accepted/refused records, exporter queue size/capacity,
enqueue failures, send failures, processor output, memory use, and process uptime. Metric suffixes
may reflect Prometheus conventions; inspect the stored names before adding dashboard expressions.

## Outage check

This is a lightweight resilience check, not a load test:

```bash
docker compose stop otel-collector
curl --fail http://127.0.0.1:18080/health
docker compose start otel-collector
curl --fail http://127.0.0.1:13133
```

The API health request must succeed while the Collector is stopped. Some telemetry generated
during the outage may be dropped after in-memory retries, bounded queues, or export timeouts are
exhausted. Do not use an unbounded queue to conceal an unavailable backend.

For end-to-end API, Collector, Prometheus, k6, and Grafana diagnosis, use the
[observability troubleshooting runbook](observability-troubleshooting.md).
