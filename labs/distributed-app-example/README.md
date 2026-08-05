# Distributed app example

Status: **In progress**

This lab is a local polyglot application for exploring synchronous and asynchronous communication
across independently deployed services. It combines a React dashboard, a .NET API gateway, a
Python gRPC inventory service, and a Python RabbitMQ worker with PostgreSQL, Redis, RabbitMQ, and
OpenTelemetry tracing.

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

.NET gateway + Python services -> OpenTelemetry Collector -> Jaeger
```

## Prerequisites

- Docker Engine with Docker Compose v2
- Enough local resources to run nine containers

All credentials and open ports in this lab are local demonstration defaults. They are not suitable
for production or a shared environment.

The OpenTelemetry Collector extends the reusable container baseline under
`shared/compose/observability`. Its Jaeger exporter pipeline, host ports, and application
instrumentation remain in this lab because they define the experiment's telemetry behavior.

## Run

Run commands from this directory:

```bash
docker compose config --quiet
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
- RabbitMQ management: <http://127.0.0.1:15672> (`guest` / `guest`)

Stop the lab without deleting persisted data:

```bash
docker compose down
```

The following cleanup is destructive and deletes this lab's PostgreSQL and Redis volumes:

```bash
docker compose down --volumes
```

## Validation

```bash
docker compose config --quiet
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
worker, and the cross-service traces appear in Jaeger.

## Known limitations

- The end-to-end workflow has not yet been revalidated after migration.
- The dashboard API URL and host ports are fixed for local development.
- PostgreSQL, Redis, RabbitMQ, and observability endpoints are exposed without production controls.
- Several upstream container tags and application dependencies need a deliberate version and
  reproducibility review before this lab is considered complete.
- There is no automated smoke test, workload, captured result set, or evidence-backed conclusion.

See [SOURCE.md](SOURCE.md) for migration provenance and [doc/worklog.md](doc/worklog.md) for the
original development notes.
