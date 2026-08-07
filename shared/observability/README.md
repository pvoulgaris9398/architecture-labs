# Standalone observability stack

This Compose project provides the shared local observability platform for Architecture Labs. It
runs independently from the labs and owns telemetry routing, storage, visualization, provisioning,
and the named Docker network used by instrumented applications.

Docker Compose v2 is required.

## Services

- `api-load-test-collector` preserves the API load-test lab's tail-sampling and Seq export policy.
- `distributed-app-collector` exports distributed-app traces to Jaeger and metrics to Prometheus.
- Prometheus scrapes both collectors, the API load-test service, and the realtime WebSocket, SSE,
  and long-polling servers directly when their labs are running.
- Grafana provisions the shared Prometheus and Jaeger data sources and lab-specific dashboards.
- Seq stores API load-test logs and traces.
- Jaeger stores distributed-app traces.

Keeping separate collectors prevents one lab's processors and exporters from changing another
lab's experimental behavior.

## Configure and start

Run these commands before starting either consuming lab:

```bash
cp .env.example .env
docker compose config --quiet
docker compose up -d
docker compose ps
```

Replace the demonstration passwords in `.env`; the file is ignored by Git. Every Compose setting
is required and has no fallback.

The stack creates the `architecture-labs-observability` network. Lab Compose projects declare that
network as external, so their configuration validation and startup require the shared stack's
network to exist. Applications use Docker DNS URLs such as
`http://api-load-test-collector:4318` and `http://distributed-app-collector:4317`.

## User interfaces

With the example ports:

- Grafana: <http://127.0.0.1:3000>
- Prometheus: <http://127.0.0.1:9090>
- Seq: <http://127.0.0.1:5341>
- Jaeger: <http://127.0.0.1:16686>
- API collector health: <http://127.0.0.1:13133>
- API collector self-metrics: <http://127.0.0.1:18888/metrics>

Grafana credentials come from this stack's `.env`.

Grafana uses the shared `grafana/grafana.ini` date formats for every provisioned dashboard. Time
axes and the time picker use a 12-hour clock with AM/PM while dashboards continue to display the
browser's local time zone.

## Lab lifecycle

Labs can start and stop independently after this stack is running. A stopped lab appears as a down
Prometheus target but does not affect the support stack or other labs.

The earlier embedded stacks may have left volumes such as `api-load-test-example_seq-data`,
`api-load-test-example_prometheus-data`, or
`architecture-labs-distributed-app-example_prometheus_data`. This project does not attach, migrate,
or delete them automatically. Preserve them until their data has been reviewed and an explicit
migration or deletion decision is made.

Stop the containers without deleting retained data:

```bash
docker compose down
```

This cleanup is destructive and deletes shared Prometheus, Grafana, and Seq data for every lab:

```bash
docker compose down --volumes
```

Do not run the destructive command without explicit approval.

## Validation

```bash
docker compose --env-file .env.example config --quiet
git diff --check
```

After changing shared configuration, validate this stack and both consuming lab Compose models.
