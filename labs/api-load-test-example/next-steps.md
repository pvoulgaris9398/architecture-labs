# Observability next steps

This checklist evolves the lab from aggregate dashboards into a correlated workflow for explaining
individual failures and slow requests. Complete the items in order unless a step explicitly states
otherwise.

[ADR 0002](../../docs/decisions/0002-seq-first-observability-backend.md) selects Seq as the first
log and trace backend while preserving the OpenTelemetry Collector as the boundary for a possible
future Loki and Tempo comparison.

## 1. Define correlation and retention conventions

- [x] Document the fields used across the stack: `trace_id`, `span_id`, `request_id`, `test_id`,
  `scenario`, HTTP route, and status code.
- [x] Define which fields may be Prometheus labels; never use per-request or per-trace identifiers
  as metric labels because their cardinality is unbounded.
- [x] Choose local retention limits for metrics, logs, and traces, including disk-size expectations
  and a safe cleanup procedure.
- [x] Define an initial trace-sampling policy: retain all errors and unusually slow requests while
  sampling ordinary successful requests.

Decisions and operating limits are recorded in
[the observability conventions](doc/observability-conventions.md).

**Done when:** the naming, cardinality, sampling, and retention decisions are documented before new
telemetry storage is introduced.

## 2. Add API request correlation

- [ ] Accept and propagate the W3C `traceparent` and `tracestate` headers.
- [ ] Generate a request identifier when the caller does not supply one.
- [ ] Accept bounded `test_id` and `scenario` metadata from k6 without treating arbitrary input as
  a metric label.
- [ ] Return the request and trace identifiers in response headers so a failing caller can record
  them.
- [ ] Add correlation values to the ASP.NET Core logging scope for the full request lifetime.

**Done when:** a single API response can be matched to the corresponding structured application
log entries and server trace.

## 3. Add structured application logging

- [ ] Emit JSON logs with timestamp, severity, message, exception, route, status, duration, and the
  agreed correlation fields.
- [ ] Log request failures and unexpected exceptions without recording credentials, connection
  strings, or sensitive request data.
- [ ] Add focused database-operation logs that preserve the active trace context.
- [ ] Avoid per-request informational logging that would distort the load experiment unless it is
  explicitly enabled for diagnostics.

**Done when:** error logs are machine-queryable and a `trace_id` search finds every relevant log
entry for one failed request.

## 4. Introduce an OpenTelemetry Collector

- [ ] Add a lab-owned collector service and configuration to Docker Compose.
- [ ] Route application traces and optional logs through OTLP while keeping the existing Prometheus
  scrape path functional.
- [ ] Add health checks, bounded queues, retry behavior, and useful collector self-telemetry.
- [ ] Pin the current LTS collector image, or the current stable production release if no LTS
  channel exists, following repository policy.

**Done when:** the API exports telemetry to the collector and a collector outage does not prevent
the API from serving requests.

## 5. Add Seq log and trace storage

- [ ] Add a locally persisted Seq service with bounded retention.
- [ ] Pin the image according to repository policy, explicitly accept the Seq EULA, and document
  the single-user limitation of the free Individual license.
- [ ] Export structured API logs and API and SQL client spans from the Collector to Seq over OTLP.
- [ ] Verify traces include HTTP route, response status, SQL operation, duration, and error status
  without exposing SQL credentials or sensitive statement parameters.
- [ ] Confirm parent-child relationships connect the inbound HTTP request to its database work.
- [ ] Verify error logs can be filtered by time range, service, test run, and correlation fields.

**Done when:** a known request can be found by `trace_id` in Seq and its structured logs, API span,
and database timings can be investigated together.

## 6. Propagate test context from k6

- [ ] Send the wrapper's `test_id` and the active scenario with each HTTP request.
- [ ] Generate or propagate valid W3C trace context from k6 using a documented, maintainable
  approach.
- [ ] Record returned request and trace identifiers when a k6 check fails.
- [ ] Ensure diagnostic logging is rate-limited so a failure storm does not overwhelm the terminal
  or telemetry pipeline.
- [ ] Apply the same behavior to `load-test.js` and `load-test-scan.js`.

**Done when:** a failed k6 request provides enough identifiers to locate its API trace and logs.

## 7. Connect Grafana investigations to Seq

- [ ] Keep Prometheus provisioned as Grafana's metrics source and Seq as the detailed investigation
  interface.
- [ ] Add dashboard data links to Seq searches for the selected time range and bounded test context
  where practical.
- [ ] Evaluate whether exemplars or another stable link can open a representative trace in Seq
  without adding high-cardinality metric labels.
- [ ] Add dashboard variables for bounded fields such as `test_id`, scenario, and route.
- [ ] Validate every PromQL expression and generated Seq link against the running services.

**Done when:** an operator can move from a Grafana metric spike to a focused Seq investigation and
then navigate between a trace and its correlated logs.

## 8. Improve the dashboard investigation workflow

- [ ] Separate client-side k6 failures from API HTTP failures and database errors.
- [ ] Add panels for slow requests, recent error logs, representative traces, collector health,
  dropped telemetry, and storage pressure.
- [ ] Make panel descriptions explain what each signal means and what to inspect next.
- [ ] Keep dotted OpenTelemetry metric and label names correctly quoted in PromQL.
- [ ] Ensure an empty panel clearly distinguishes “no errors” from “telemetry unavailable.”

**Done when:** the dashboard tells a coherent story from load generation through API and database
behavior and exposes telemetry-pipeline failures.

## 9. Validate sampling, load impact, and failure behavior

- [ ] Compare baseline throughput and latency with telemetry disabled and enabled.
- [ ] Measure log and trace volume during both load-test scenarios.
- [ ] Verify all errors and slow traces survive the sampling policy.
- [ ] Test Collector, Seq, and Prometheus outages independently and document their effects.
- [ ] Confirm retention limits prevent unbounded local disk growth.

**Done when:** the cost and limitations of the observability stack are measured and documented, not
assumed.

## 10. Add an analysis runbook

- [ ] Document how to investigate connection refusals, HTTP errors, connection-pool saturation,
  slow SQL, table scans, telemetry gaps, and port collisions.
- [ ] Provide queries for selecting a `test_id`, finding errors, locating slow traces, and pivoting
  between signals.
- [ ] Document what evidence to preserve with experiment results and what must remain untracked.
- [ ] Include a repeatable lightweight smoke test that proves metrics, logs, and traces are flowing
  without running the full load profile.

**Done when:** someone without prior conversation context can reproduce a test and explain a
representative failure using the runbook.

## 11. Optionally compare Loki and Tempo

- [ ] Revisit ADR 0002 when the lab needs a fully open-source Grafana-centered stack, multi-user
  access, or an explicit backend comparison.
- [ ] Add Loki and Tempo as an alternative profile without changing the application's
  standards-based OpenTelemetry instrumentation.
- [ ] Reuse the same Collector receivers, workload, correlation fields, sampling policy, and
  retention window so the comparison is controlled.
- [ ] Store `trace_id`, `span_id`, and `request_id` as Loki structured metadata, never as indexed
  labels; keep Loki labels bounded and low-cardinality.
- [ ] Configure Grafana trace-to-logs and logs-to-trace navigation and validate the LogQL and
  TraceQL queries.
- [ ] Compare setup effort, query usability, ingestion loss, CPU, memory, disk use, retention,
  licensing, portability, and multi-user operation against Seq.

**Done when:** the comparison produces reproducible evidence explaining when Seq or Loki plus Tempo
is the better fit, without presenting either backend as a universal choice.
