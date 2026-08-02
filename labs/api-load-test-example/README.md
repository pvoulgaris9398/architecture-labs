# API load test and observability example

> Architecture Labs entry: this lab was migrated from the standalone
> [api-load-test-example repository](https://github.com/pvoulgaris9398/api-load-test-example).
> See [SOURCE.md](SOURCE.md) for migration provenance.

This project demonstrates how API latency, SQL query performance, and ADO.NET connection-pool
behavior interact under concurrent load. It runs an ASP.NET Core API, SQL Server, Prometheus,
and Grafana locally with Docker Compose and uses k6 to generate traffic.

See [next-steps.md](next-steps.md) for the ordered checklist to add correlated logs, traces,
request context, Grafana navigation, and an analysis runbook.
[Observability conventions](doc/observability-conventions.md) define the correlation fields,
cardinality rules, sampling policy, retention limits, and cleanup safeguards for that work.

The API accepts W3C `traceparent` and `tracestate` context plus optional `X-Request-ID`,
`X-Test-ID`, and `X-Test-Scenario` headers. Every response includes `X-Request-ID` and
`X-Trace-ID`; valid test context is echoed for diagnostics. See the conventions for validation and
cardinality rules.

Application logs are emitted as structured JSON. HTTP client/server errors and database-operation
failures are always logged with route, status, duration, and the active correlation scope. To avoid
distorting load results, successful and slow-request records are disabled by default. Enable them
temporarily in `.env` when diagnosing a focused run:

```bash
LOG_SUCCESSFUL_REQUESTS=true
LOG_SLOW_REQUESTS=true
SLOW_REQUEST_THRESHOLD_MS=500
docker compose up -d --build api-service
docker compose logs -f api-service
```

Do not enable successful-request logging for routine high-volume load tests.

## Prerequisites

- Docker Desktop with Docker Compose
- [k6](https://grafana.com/docs/k6/latest/set-up/install-k6/)
- Bash with `curl`

On Windows, install k6 from an elevated terminal:

```powershell
winget install --id GrafanaLabs.k6 -e
```

## Configure and start

Run the following commands from `labs/api-load-test-example` in the Architecture Labs working
copy. This lab uses the shared SQL Server service baseline under `shared/compose/sqlserver` while
retaining its ports, application, telemetry, and experiment-specific configuration locally.

Copy the example environment file and replace its placeholder passwords:

```bash
cp .env.example .env
```

The local `.env` file is ignored by Git. Do not commit real passwords. Then start the stack:

```bash
docker compose up --build -d
docker compose logs -f api-service
```

Wait for `Database seeding complete.` before starting a load test. The initial seed creates a
500,000-row `Orders` table and can take several seconds.

Both k6 scripts also poll `GET /health` for up to 120 seconds before generating load. Override the
defaults with `READINESS_ATTEMPTS` and `READINESS_INTERVAL_SECONDS` when a slower machine needs a
longer startup window. Their `setup()` phase has a three-minute timeout by default; override it
with `SETUP_TIMEOUT` if the readiness window is increased beyond that.

| Service | URL |
| --- | --- |
| API health | http://127.0.0.1:18080/health |
| API Prometheus metrics | http://127.0.0.1:18080/metrics |
| Prometheus | http://localhost:9090 |
| Grafana | http://localhost:3000 |

Sign in to Grafana using `GRAFANA_ADMIN_USER` and `GRAFANA_ADMIN_PASSWORD` from `.env`.

## Run the connection-pool test

The default test calls an endpoint whose SQL query deliberately waits for 100 ms:

```bash
bash run-k6.sh load-test.js
```

Override the API location or endpoint when needed:

```bash
K6_BASE_URL=http://127.0.0.1:18080 \
  bash run-k6.sh load-test.js -e ENDPOINT=v1/admin-report
```

Use a slashless `ENDPOINT` value when running from Git Bash on Windows. A leading `/` can be
mistaken for a filesystem path and rewritten to a path under the Git installation directory.

The wrapper enables k6's experimental Prometheus remote-write output so Grafana can display
client-side failures such as TCP connection refusals. It sends p95, p99, and maximum trend values
to `http://localhost:9090/api/v1/write` and tags each run with a unique local test ID. Running `k6 run`
directly still works, but its client metrics will appear only in the terminal.

The default p95 threshold is 150 ms, intentionally leaving little headroom above the artificial
database delay so saturation becomes visible.

If k6 reports that `127.0.0.1:18080` refused the connection, confirm the stack was started from this
lab directory and inspect readiness and startup logs:

```bash
curl --fail http://127.0.0.1:18080/health
docker compose ps
docker compose logs --tail 100 api-service
```

The API uses host port `18080` by default because port `8080` is commonly occupied by local web
servers. Override it with `API_HOST_PORT` in `.env` and set the matching `K6_BASE_URL` when running
k6. A port collision can be deceptive on Windows: `localhost` may reach the API over IPv6 while
k6 reaches a different process over IPv4. Use the explicit `127.0.0.1` address in both checks.

The API's `/metrics` endpoint contains server-side .NET and OpenTelemetry measurements only. k6
client metrics do not appear there; `run-k6.sh` writes them to Prometheus's remote-write receiver
at `http://localhost:9090/api/v1/write`, where Grafana queries them.

## Compare a table scan with an index

The repository includes a separate experiment that runs the same customer-order query before
and after creating a covering index:

```bash
# Baseline: no CustomerId index
bash run-k6.sh load-test-scan.js

# Add the index, then repeat
curl -X POST http://127.0.0.1:18080/v1/add-index
bash run-k6.sh load-test-scan.js

# Reset for another baseline
curl -X POST http://127.0.0.1:18080/v1/drop-index
```

See [the full table-scan experiment](doc/table-scan-vs-index-load-test.md) for expected signals
and SQL-side diagnostics.

## Observability

Prometheus scrapes the API every five seconds. Grafana automatically provisions the Prometheus
data source and the bundled SQL connection-pool dashboard. The API exposes ASP.NET Core and
SqlClient metrics, including custom gauges bridged from all 16 SqlClient EventCounters.

OTLP export is disabled when `OTEL_EXPORTER_OTLP_ENDPOINT` is blank. To send traces, metrics,
and logs to an external OpenTelemetry Collector, set the endpoint and protocol in `.env`; the
Prometheus endpoint remains enabled independently.

Optional Windows host metrics expect windows_exporter on port 9182. If it is not installed,
only that Prometheus target will show as unavailable; API metrics continue to work.

For proposed server-side SQL telemetry, see
[SQL Server-side monitoring requirements](doc/sql-server-side-monitoring.md).

## Useful commands

```bash
# Inspect currently waiting SQL requests
docker compose exec db-server /bin/bash -c '/opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C \
  -Q "SELECT session_id, wait_type, wait_time, status, blocking_session_id FROM sys.dm_exec_requests WHERE session_id > 50 ORDER BY wait_time DESC"'

# Stop the environment
docker compose down

# Remove the SQL container and its anonymous data along with the rest of the stack
docker compose down --volumes
```

The final command deletes local container data and requires the database to be seeded again on
the next startup.

## Security and reproducibility

This stack is intended for isolated local experimentation. SQL Server is exposed on port 1433,
the API management endpoints are unauthenticated, and TLS certificate validation is disabled for
the database connection. Do not deploy this configuration to a shared or production environment.

Container versions are configured in `.env.example` rather than using mutable Prometheus and
Grafana `latest` tags. Update those values deliberately when testing a newer release.
