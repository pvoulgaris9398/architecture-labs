# ADR 0002: Use Seq first for correlated logs and traces

- **Status:** Accepted
- **Date:** 2026-08-02
- **Scope:** `labs/api-load-test-example`

## Context

The API load-test lab already uses Prometheus and Grafana to show aggregate API, database, and k6
behavior. The next requirement is to retain structured errors, logs, and traces and correlate them
with an individual request and k6 test run.

The initial proposal used Loki for logs and Tempo for traces. That stack integrates naturally with
Grafana and is representative of a composable, open-source observability architecture. It also
introduces two storage services, LogQL and TraceQL, Grafana correlation configuration, and careful
Loki label design. In particular, identifiers such as `trace_id` and `request_id` must not become
high-cardinality Loki labels; they belong in structured metadata.

This lab is currently a local, single-user experiment centered on one .NET API. Its immediate need
is an approachable way to search structured properties and move between logs and traces while
learning why individual requests failed or became slow.

## Decision

Use Seq as the first log and trace backend for this lab while retaining:

- Prometheus as the metrics backend;
- Grafana as the primary metrics and experiment dashboard;
- OpenTelemetry and W3C Trace Context as the telemetry and correlation standards; and
- an OpenTelemetry Collector as the routing boundary between the application and storage backend.

Send structured application logs and traces through OTLP to the Collector and then to Seq. Use Seq
to investigate individual requests, errors, SQL spans, and correlated structured properties. Use
Grafana and Prometheus to identify when aggregate behavior changed and link to Seq investigations
where practical.

Pin the Seq container image according to repository image policy when implementation begins. The
lab must explicitly accept the Seq EULA and document that Seq is proprietary and that its free
Individual license permits a single person to access the web interface.

## Rationale

Seq is the better first fit because it:

- combines structured-log and trace investigation in one local service;
- accepts OpenTelemetry logs and traces through native OTLP endpoints;
- provides direct structured-property searches for request and trace identifiers;
- has a strong .NET structured-logging experience;
- supports persistent Docker storage and configurable retention policies; and
- reduces initial operational complexity so the lab can focus on correlation and diagnosis.

The Collector prevents this decision from coupling application instrumentation directly to Seq.
The API should emit standards-based telemetry, not use backend-specific correlation semantics.

## Consequences

### Positive

- One backend replaces the initially proposed Loki and Tempo services.
- Logs and traces can be explored together without first designing Loki streams and Grafana
  cross-data-source navigation.
- The initial implementation remains small enough to understand and operate locally.

### Negative

- Seq is proprietary and requires acceptance of its EULA.
- The free Individual license is not suitable for a shared multi-user deployment.
- Seq investigation occurs primarily in its own UI, so Grafana is not the sole observability UI.
- The lab will not initially demonstrate operating Loki, Tempo, LogQL, or TraceQL.

## Loki and Tempo evolution path

Loki and Tempo remain valid future options rather than rejected technologies. Re-evaluate them
when the goal changes to any of the following:

- demonstrate a fully open-source, Grafana-centered observability stack;
- support multiple users without adopting a commercial Seq subscription;
- compare storage, query, resource, and operational trade-offs between backends;
- practice Loki label and structured-metadata design, LogQL, TraceQL, exemplars, and Grafana
  trace-to-log navigation; or
- model an environment already standardized on the Grafana observability ecosystem.

A future comparison should keep the same API instrumentation, correlation fields, k6 workload,
sampling policy, retention window, and Collector receivers. Add Loki and Tempo exporters to the
Collector, run equivalent workloads, and compare:

- setup and ongoing operational complexity;
- CPU, memory, disk consumption, and ingestion loss under load;
- query usability and latency for `test_id`, `trace_id`, request errors, and slow SQL;
- retention and cleanup behavior;
- Grafana navigation and dashboard integration; and
- licensing, portability, and multi-user considerations.

Do not index `trace_id`, `span_id`, or `request_id` as Loki labels. Store them as structured
metadata and reserve labels for bounded fields such as service and environment.

## References

- [Seq OpenTelemetry log ingestion](https://docs.datalust.co/docs/ingestion-with-opentelemetry)
- [Seq OpenTelemetry tracing](https://docs.datalust.co/docs/tracing-from-opentelemetry-sdks)
- [Seq Docker deployment](https://docs.datalust.co/docs/docker-deployment-overview)
- [Seq retention policies](https://datalust.co/docs/retention-policies)
- [Seq licensing](https://datalust.co/Pricing)
- [Loki label guidance](https://grafana.com/docs/loki/latest/get-started/labels/)
- [Loki OpenTelemetry label guidance](https://grafana.com/docs/loki/latest/get-started/labels/modify-default-labels/)

