# ADR 0003: Run a standalone shared observability platform

- Status: Accepted
- Date: 2026-08-05

## Context

The API load-test and distributed-app labs both run the OpenTelemetry Collector, but they use it
for different experiments. The load-test lab sends sampled traces and logs to Seq and exposes
collector health and metrics. The distributed-app lab sends traces to Jaeger and application
metrics to Prometheus for Grafana dashboards. Their application instrumentation, processors,
exporters, ports, and backends are therefore not interchangeable.

Embedding complete support stacks in each lab duplicates long-running services, credentials,
ports, and retained data. A shared runtime is desirable, but combining both telemetry policies in
one Collector would couple otherwise independent experiments and obscure variables that affect
their results.

## Decision

Run `shared/observability` as a standalone Compose project containing Prometheus, Grafana, Seq,
Jaeger, persistent telemetry volumes, provisioning, dashboards, and two collectors. Preserve one
collector per lab policy: `api-load-test-collector` and `distributed-app-collector`.

The support project creates the named `architecture-labs-observability` Docker network. Consuming
lab services join it as an external network and use Docker DNS URLs supplied through their `.env`
files. Prometheus reaches the API load-test service over that network and continues scraping its
`/metrics` endpoint directly.

Standardize the collector's in-container configuration path as
`/etc/otelcol-contrib/config.yaml`. Keep .NET, Python, and other SDK setup with the application that
owns it because instrumentation is compiled, versioned, and validated with that application.

Keep application SDK instrumentation in the owning lab. Keep distinct collector configurations
and lab-named Grafana folders inside the support project so shared lifecycle does not imply a
shared telemetry policy.

## Consequences

- Observability services and retained data have an independent lifecycle.
- Labs no longer duplicate backend containers, ports, or credentials.
- Collector pipelines and backend choices remain explicit per-lab policies.
- The support stack must start before a lab because Compose cannot create an external network.
- A support-stack outage affects telemetry for every connected lab but should not stop application
  request processing.
- A shared configuration edit requires validation of the support stack and both consuming labs.
