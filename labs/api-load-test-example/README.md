# API load test and observability example

> Architecture Labs entry: this lab was migrated from the standalone
> [api-load-test-example repository](https://github.com/pvoulgaris9398/api-load-test-example).
> See [SOURCE.md](SOURCE.md) for migration provenance.

This project demonstrates how API latency, SQL query performance, and ADO.NET connection-pool
behavior interact under concurrent load. It runs an ASP.NET Core API, SQL Server, Prometheus,
and Grafana locally with Docker Compose and uses k6 to generate traffic.

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

| Service | URL |
| --- | --- |
| API health | http://localhost:8080/health |
| API Prometheus metrics | http://localhost:8080/metrics |
| Prometheus | http://localhost:9090 |
| Grafana | http://localhost:3000 |

Sign in to Grafana using `GRAFANA_ADMIN_USER` and `GRAFANA_ADMIN_PASSWORD` from `.env`.

## Run the connection-pool test

The default test calls an endpoint whose SQL query deliberately waits for 100 ms:

```bash
k6 run load-test.js
```

Override the API location or endpoint when needed:

```bash
k6 run -e BASE_URL=http://localhost:8080 -e ENDPOINT=v1/admin-report load-test.js
```

Use a slashless `ENDPOINT` value when running from Git Bash on Windows. A leading `/` can be
mistaken for a filesystem path and rewritten to a path under the Git installation directory.

The default p95 threshold is 150 ms, intentionally leaving little headroom above the artificial
database delay so saturation becomes visible.

## Compare a table scan with an index

The repository includes a separate experiment that runs the same customer-order query before
and after creating a covering index:

```bash
# Baseline: no CustomerId index
k6 run load-test-scan.js

# Add the index, then repeat
curl -X POST http://localhost:8080/v1/add-index
k6 run load-test-scan.js

# Reset for another baseline
curl -X POST http://localhost:8080/v1/drop-index
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
