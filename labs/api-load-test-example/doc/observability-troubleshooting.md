# Observability connectivity and operation troubleshooting

Use this runbook to verify one layer at a time before running a load test. Run commands from the
lab directory in Git Bash. The checks are intentionally lightweight and do not modify database or
telemetry volumes.

## Signal paths

```text
k6 -- remote write ------------------------------> Prometheus --> Grafana
 |                                                     ^
 +-- HTTP --> API -- /metrics scrape -----------------+
                 |
                 +-- OTLP logs, traces, metrics --> Collector
                                                    |       |
                                                    |       +-- /metrics --> Prometheus
                                                    +-- debug output now; Seq in Step 5
```

The paths are independent. For example, the API and its Prometheus scrape can remain healthy while
the Collector is stopped, and k6 metrics can reach Prometheus even when no API trace is created.

## 1. Confirm container state

```bash
docker compose ps
docker compose logs --tail 100 api-service otel-collector prometheus grafana
```

Expected:

- `api-service`, `otel-collector`, `prometheus`, and `grafana` are running.
- API logs contain `Database seeding complete.` after initial startup.
- Collector logs do not contain configuration parsing or component startup failures.
- Prometheus logs do not show repeated scrape or remote-write receiver errors.

Container output is currently the only log interface. Grafana has a Prometheus data source but no
log data source. Step 5 adds Seq as the searchable log and trace UI.

## 2. Verify host endpoints explicitly over IPv4

```bash
curl --fail --show-error http://127.0.0.1:18080/health
curl --fail --show-error http://127.0.0.1:18080/metrics >/dev/null
curl --fail --show-error http://127.0.0.1:13133
curl --fail --show-error http://127.0.0.1:18888/metrics >/dev/null
curl --fail --show-error http://127.0.0.1:9090/-/ready
curl --fail --show-error http://127.0.0.1:3000/api/health
```

Use `127.0.0.1`, not `localhost`, for host-side API diagnostics. On Windows, different clients can
resolve `localhost` to different IPv4 or IPv6 listeners. A response from the wrong server—such as
Apache instead of Kestrel—indicates a host-port collision, not an API route failure.

Compare responders when a result is suspicious:

```bash
curl -i http://localhost:18080/health
curl -i http://127.0.0.1:18080/health
netstat -ano | grep ':18080'
```

## 3. Verify Prometheus targets

Open `http://127.0.0.1:9090/targets`, or query both required jobs:

```bash
curl --get --silent --show-error \
  --data-urlencode 'query=up{job=~"csharp-api|otel-collector"}' \
  http://127.0.0.1:9090/api/v1/query
```

Expected: `csharp-api` and `otel-collector` each report `1`. The optional `windows-host-os` target
may be down when windows_exporter is not installed; that does not prevent API or Collector metrics.

If the API target is down but its host health check succeeds, inspect container-to-container
connectivity and `prometheus/prometheus.yaml`. Prometheus must scrape `api-service:8080`, not the
host's `127.0.0.1:18080` address.

## 4. Verify API-to-Collector ingestion

Generate one successful API request:

```bash
curl -i \
  -H 'X-Request-ID: collector-check-1' \
  -H 'X-Test-ID: collector-check' \
  -H 'X-Test-Scenario: connection-pool' \
  http://127.0.0.1:18080/health
```

The response should include `X-Request-ID`, `X-Trace-ID`, `X-Test-ID`, and `X-Test-Scenario`.
Inspect Collector receiver totals before and after several requests:

```bash
curl --silent http://127.0.0.1:18888/metrics \
  | grep -E 'otelcol_receiver_(accepted|refused)_(spans|log_records|metric_points)'
```

Accepted counters should increase for signals exported by the API. Refused counters should remain
zero. Depending on Prometheus naming rules, cumulative counters may have a `_total` suffix.

### Why a successful curl may produce no visible Collector log

This is expected with the default configuration:

- successful request logging is disabled;
- otherwise-successful `/health` traces are sampled at 1%;
- the Collector debug exporter uses `basic` verbosity; and
- debug output is rate-limited.

Correlation middleware attaches identifiers to telemetry that exists; it does not create a log
record for every successful request.

## 5. Generate a deterministic error record and retained trace

An unknown route produces a 404 warning and the tail-sampling policy retains HTTP error traces:

```bash
curl -i \
  -H 'X-Request-ID: deliberate-404' \
  -H 'X-Test-ID: correlation-check' \
  -H 'X-Test-Scenario: connection-pool' \
  http://127.0.0.1:18080/v1/not-found

docker compose logs --tail 100 api-service
docker compose logs --tail 100 otel-collector
```

Expected:

- The API response is 404 and includes correlation response headers.
- API JSON output contains a warning with route, status, duration, and correlation scope.
- Collector accepted-span and accepted-log counters increase.
- Collector `basic` debug output may show only batch summaries, not the trace or request ID.

Until Seq is added, use the API container output to inspect individual log fields and Collector
self-metrics to prove pipeline delivery.

## 6. Temporarily increase diagnostic logging

For a short, low-volume diagnostic session, set these values in `.env`:

```dotenv
LOG_SUCCESSFUL_REQUESTS=true
LOG_SLOW_REQUESTS=true
SLOW_REQUEST_THRESHOLD_MS=500
```

Recreate only the API and follow its logs:

```bash
docker compose up -d --build api-service
docker compose logs -f api-service
```

To inspect individual telemetry records at the Collector, temporarily change the debug exporter in
`otel-collector/config.yaml` to `verbosity: detailed`, increase `sampling_initial`, and set
`sampling_thereafter: 1`; then recreate `otel-collector`. Restore the committed configuration
before any load test. Detailed Collector output and successful-request logging can generate an
enormous amount of I/O and materially distort results.

## 7. Verify k6 remote write independently

Run one iteration rather than the normal load profile:

```bash
bash run-k6.sh load-test.js --iterations 1 --vus 1
```

Then query Prometheus:

```bash
curl --get --silent --show-error \
  --data-urlencode 'query=sum(rate(k6_http_reqs_total[1m]))' \
  http://127.0.0.1:9090/api/v1/query
```

k6 client metrics do not appear at the API's `/metrics` endpoint. `run-k6.sh` sends them directly
to Prometheus at `/api/v1/write`. If terminal results appear but Prometheus has no `k6_*` series,
verify that the wrapper—not a direct `k6 run` command—was used and inspect Prometheus logs.

## 8. Verify Grafana

```bash
curl --fail --show-error http://127.0.0.1:3000/api/health
```

In Grafana:

1. Open **Connections > Data sources > Prometheus** and run **Save & test**.
2. Open **Explore**, select Prometheus, and query `up`.
3. Confirm both `csharp-api` and `otel-collector` jobs appear.
4. Use a time range that includes the test and allow for the five-second scrape interval.

An empty panel can mean no matching events, a sampling decision, an incorrect time range, or a
telemetry failure. Check `up`, raw metric names, and Collector accepted/refused counters before
concluding that the application emitted nothing.

## 9. Verify Collector outage isolation

```bash
docker compose stop otel-collector
curl --fail http://127.0.0.1:18080/health
curl --fail http://127.0.0.1:18080/metrics >/dev/null
docker compose start otel-collector
curl --fail http://127.0.0.1:13133
```

The API health and Prometheus endpoint must remain available while the Collector is stopped. The
.NET exporter uses bounded in-memory retry; telemetry can be dropped when the outage exceeds queue
or retry capacity. That loss is preferable to blocking application requests.

## Symptom guide

| Symptom | Most likely layer | First checks |
| --- | --- | --- |
| API connection refused | API container or host port | `docker compose ps`, API logs, IPv4 curl |
| API returns unexpected 404/server header | Host-port collision | Compare `localhost` and `127.0.0.1`; inspect `Server` header |
| API healthy, Grafana empty | Prometheus scrape/query/time range | Prometheus targets, `up`, raw metric names |
| API logs exist, Collector counters stay flat | API OTLP endpoint or Collector receiver | API/Collector logs, ports 4318 and 14318, Collector health |
| Collector counters rise, console shows nothing | Sampling or debug-exporter rate limit | Expected-silence notes, tail-sampling counters |
| k6 terminal works, no `k6_*` metrics | Remote-write path | Use `run-k6.sh`, Prometheus logs, port 9090 |
| Collector down, API also unavailable | Unintended runtime coupling | API logs and Compose configuration; API must not depend on Collector health |
| Only Windows host target is down | Optional windows_exporter | Install/start exporter or ignore that optional target |

## Safe escalation information

When asking for help, preserve these non-secret details:

- exact command and timestamp;
- `docker compose ps` output;
- relevant API, Collector, and Prometheus log excerpts;
- HTTP status and `Server`, `X-Request-ID`, and `X-Trace-ID` headers;
- Prometheus `up` results and relevant Collector accepted/refused/queue counters;
- the selected Grafana time range; and
- whether diagnostic logging or detailed debug export was enabled.

Do not post `.env`, connection strings, passwords, request bodies, SQL parameters, or complete
Docker inspection output that may contain environment secrets.

