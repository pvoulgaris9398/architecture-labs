# Distributed app example

Status: **In progress**

This lab is a local polyglot application for exploring synchronous and asynchronous communication
across independently deployed services. It combines a React dashboard, a .NET API gateway, a
Python gRPC inventory service, and a Python RabbitMQ worker with PostgreSQL, Redis, RabbitMQ,
OpenTelemetry traces and metrics, Prometheus, Grafana, and Jaeger.

## Question

How do cache-aside reads, synchronous gRPC calls, and asynchronous message processing interact in
a small observable distributed application?

The current hypothesis is that the application can make those three paths visible in one locally
reproducible environment. No performance or reliability conclusion has been established yet.

## Architecture

```text
Browser -> React dashboard -> .NET API gateway
                              |-> PostgreSQL
                              |-> Redis
                              |-> Python inventory service (gRPC)
                              `-> RabbitMQ -> Python worker

.NET gateway + Python services -- shared network --> distributed-app-collector
                                                     |-> Jaeger (traces)
                                                     `-> Prometheus -> Grafana (metrics)
```

## Prerequisites

- Docker Engine with Docker Compose v2
- The standalone stack under `shared/observability` running first
- Enough local resources to run this lab and the shared support services

The example file contains local demonstration values. Its credentials and open ports are not
suitable for production or a shared environment.

## Configuration

Copy the example file before building or starting the lab:

```bash
cp .env.example .env
```

Replace every demonstration credential in `.env`. The local file is ignored by Git. Compose
requires every declared value, and the .NET gateway, Python services, and dashboard independently
fail at startup or build time when a required application variable is missing or empty. There are
no application fallback values.

`VITE_API_BASE_URL` is embedded into the browser bundle when the dashboard image is built. Rebuild
`typescript-dashboard` after changing it.

The Collector, Prometheus, Grafana, Jaeger, provisioning, and dashboard live under
`shared/observability`. This lab owns its application instrumentation and the required Collector
URL. The services communicate through the external `architecture-labs-observability` network.

## Run

Start the support stack first:

```bash
cd ../../shared/observability
cp .env.example .env
docker compose up -d
```

Then run the lab from this directory:

```bash
docker compose --env-file .env.example config --quiet
docker compose up --build -d
docker compose ps
```

Seed the catalog, then open the dashboard:

```bash
  curl -X POST http://127.0.0.1:5242/products/seed
curl http://127.0.0.1:5242/products/1
```

- Dashboard: <http://127.0.0.1:5173>
- Jaeger: <http://127.0.0.1:16686>
- Prometheus: <http://127.0.0.1:9090>
- Grafana: <http://127.0.0.1:3000> (credentials from `shared/observability/.env`)
- RabbitMQ management: <http://127.0.0.1:15672> (credentials from `.env`)

Grafana provisions Prometheus and Jaeger data sources plus the **Distributed App Telemetry
Overview** dashboard. The dashboard reports collector availability and accepted trace and metric
throughput. Application counters cover gateway product reads and checkouts, inventory stock
checks, and completed worker orders.

Stop the lab without deleting persisted data:

```bash
docker compose down
```

The following cleanup is destructive and deletes only this lab's PostgreSQL and Redis volumes. It
does not delete shared telemetry storage:

```bash
docker compose down --volumes
```

## Validation

```bash
cd ../../shared/observability
docker compose --env-file .env.example config --quiet
cd ../../labs/distributed-app-example
docker compose --env-file .env.example config --quiet
docker compose build
git diff --check
```

Building or starting the stack can download several large container images. It should be done
deliberately; no load test is currently included.

## Experimental controls and success criteria

The intended baseline uses one instance of each application component and the fixed mock inventory
behavior in `PythonInventoryService/main.py`. A first complete experiment should define a repeatable
workload and measurement window before changing a single communication or caching variable.

The lab is considered functionally successful when a seeded product can be read through the
gateway, a repeat read reports a Redis cache hit, checkout publishes an event consumed by the
worker, cross-service traces appear in Jaeger, application metrics reach Prometheus, and the
provisioned Grafana dashboard reports telemetry traffic.

## Known limitations

- The end-to-end workflow has not yet been revalidated after migration.
- The dashboard API URL and host ports are fixed for local development.
- PostgreSQL, Redis, RabbitMQ, and shared observability endpoints are local-only and lack
  production controls.
- Several upstream container tags and application dependencies need a deliberate version and
  reproducibility review before this lab is considered complete.
- There is no automated smoke test, workload, captured result set, or evidence-backed conclusion.

See [SOURCE.md](SOURCE.md) for migration provenance and [doc/worklog.md](doc/worklog.md) for the
original development notes. Use the [exercise runbook](doc/exercise-runbook.md) for an end-to-end
smoke test, observability checks, and safe dependency-failure exercises.
