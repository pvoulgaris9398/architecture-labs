---
inclusion: always
---

# Project Architecture

## Stack Overview

This is a load testing and observability lab in the Architecture Labs monorepo. The full stack
runs via `docker compose up --build -d` from `labs/api-load-test-example` and consists of four
services:

| Service         | Image source                              | Port | Purpose                         |
|-----------------|-------------------------------------------|------|---------------------------------|
| `db-server`     | `SQLSERVER_IMAGE` from `.env`             | 1433 | SQL Server database             |
| `api-service`   | Built from `./csharp-api/Dockerfile`      | 8080 | ASP.NET Core 10 minimal API     |
| `prometheus`    | Pinned `PROMETHEUS_IMAGE` from `.env`     | 9090 | Metrics scraping and storage    |
| `grafana`       | Pinned `GRAFANA_IMAGE` from `.env`        | 3000 | Provisioned dashboards          |

## C# API Project

- **Framework:** ASP.NET Core 10 minimal API (`net10.0`)
- **Namespace:** `PoolMonitoringApi`
- **Data access:** Dapper over `Microsoft.Data.SqlClient` — no EF Core
- **Telemetry:** Configured via `AddAppTelemetryV2()` extension method in `OpenTelemetryExtensions.cs`
- **Endpoints:** Defined directly in `Program.cs` using `app.MapGet` / `app.MapPost`
- **Connection strings:** Two distinct strings — one targeting `master` (original endpoints),
  one targeting `LoadTestDb` (orders experiment). Never share connection strings across
  logically separate concerns.
- **Pool size:** `Max Pool Size=150` on both connection strings

## Telemetry Extension Pattern

OpenTelemetry setup lives in `OpenTelemetryExtensions.cs` as a static extension method
`AddAppTelemetryV2(this WebApplicationBuilder builder, string serviceName)`.

`AddAppTelemetryV1` is retained as a teaching artifact showing an earlier, less complete
approach. New telemetry features go into `AddAppTelemetryV2` or a successor version.

Always call `app.UseOpenTelemetryPrometheusScrapingEndpoint()` after `builder.Build()` to
expose the `/metrics` scrape endpoint.

## Database Seeding

The `Orders` table seed runs at API startup via `SeedDatabaseAsync()` in `Program.cs`.

Key behaviours:

- Connects to `master` first (LoadTestDb may not exist yet), then runs batches split on `GO`
- Guarded with `IF NOT EXISTS` — safe to run on every startup
- Retries up to 10 times with 3s delay to handle the SQL Server container health-check window
- Seed script lives at `csharp-api/seed.sql` (copied into the Docker image via Dockerfile)
- A copy also lives at `sqlserver-init/seed.sql` for reference

## Docker Compose Conventions

- The lab extends `../../shared/compose/sqlserver/service.yaml` for stable SQL Server environment
  and health-check mechanics; ports and experiment-specific settings remain in this lab.
- Do not add fixed `container_name` values. Use Compose service names and `docker compose exec`.
- The SQL Server healthcheck uses `/opt/mssql-tools18/bin/sqlcmd` with the `-C` flag (trust cert)
- Grafana provisioning files use `.yml` extension even though the source files are `.yaml` —
  this is a Grafana container requirement, noted in comments in `docker-compose.yaml`
- Prometheus config similarly expects `prometheus.yml` inside the container
- All volume mounts for config are read-only (`:ro`)

## Dockerfile Pattern

Two-stage build:

1. `mcr.microsoft.com/dotnet/sdk:10.0` — restore, publish Release
2. `mcr.microsoft.com/dotnet/aspnet:10.0` — runtime only image

Copy the `.csproj` first and restore before copying remaining source files — this preserves
Docker layer caching for the NuGet restore step. Files needed at runtime (e.g. `seed.sql`)
are explicitly copied into the final image with a second `COPY --from=build-env` line.

Run as non-root: `USER $APP_UID` is set in the final stage.

## Load Test Scripts

Two k6 scripts live at the lab root:

- `load-test.js` — original pool saturation demo, hits `/v1/data-endpoint` with 1,000 VUs
- `load-test-scan.js` — table scan vs index comparison; both runs hit
  `/v1/orders/by-customer`, with index state as the only intentional difference

All scripts use a three-stage ramp pattern: ramp up → sustained load → cool down.
Thresholds declare the pass/fail criteria — a threshold breach means the API missed its
performance target, not that k6 itself failed.

## API Endpoint Conventions

- All endpoints are versioned under `/v1/`
- Management/admin endpoints (index creation, drops) use `POST` not `GET`
- Endpoints that demonstrate a specific scenario are kept separate from the original endpoints
  so both can be observed simultaneously in Grafana
- Queries randomise their filter values (`Random.Shared.Next`) to prevent SQL Server from
  serving all requests from a single cached plan

## Grafana Dashboard

Dashboard JSON lives at `grafana/dashboards/sql-connection-pool-dashboard.json` and is
provisioned automatically via `grafana/provider.yaml`. Changes to the JSON are picked up on
container restart — no manual import needed.

The dashboard refresh rate is `5s` to match the Prometheus scrape and EventCounter intervals.
