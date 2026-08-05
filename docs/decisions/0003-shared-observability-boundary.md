# ADR 0003: Share observability service mechanics, not experiment configuration

- Status: Accepted
- Date: 2026-08-05

## Context

The API load-test and distributed-app labs both run the OpenTelemetry Collector, but they use it
for different experiments. The load-test lab sends sampled traces and logs to Seq and exposes
collector health and metrics. The distributed-app lab sends traces to Jaeger and application
metrics to Prometheus for Grafana dashboards. Their application instrumentation, processors,
exporters, ports, and backends are therefore not interchangeable.

Copying the stable collector launch mechanics invites drift, while moving the complete pipelines
or language-specific instrumentation into one shared package would couple otherwise independent
labs and obscure variables that affect experimental results.

## Decision

Create `shared/compose/observability/service.yaml` with base services for the OpenTelemetry
Collector, Prometheus, Grafana, Jaeger, and Seq. Consuming labs use Compose `extends` and retain
their collector configuration, ports, dependencies, resource limits, provisioning, storage, and
credentials locally. A lab may deliberately override the baseline image when validating a
different pinned version.

Standardize the collector's in-container configuration path as
`/etc/otelcol-contrib/config.yaml`. Keep .NET, Python, and other SDK setup with the application that
owns it because instrumentation is compiled, versioned, and validated with that application.

Do not centralize scrape targets, collector pipelines, dashboards, data sources, backend retention,
or application telemetry. Those settings affect a lab's hypothesis or investigation workflow.
Revisit a complete shared subsystem only after multiple labs consume an equivalent stack largely
unchanged.

## Consequences

- Common container images and safe restart behavior have one maintained definition.
- Collector startup behavior and configuration-path conventions have one maintained definition.
- Each lab remains understandable and independently removable.
- Collector pipelines and backend choices remain explicit experimental variables.
- A shared edit requires configuration validation in both consuming labs.
- Reuse is intentionally smaller than a universal observability stack.
