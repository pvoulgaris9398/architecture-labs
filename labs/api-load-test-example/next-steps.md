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

- [x] Accept and propagate the W3C `traceparent` and `tracestate` headers.
- [x] Generate a request identifier when the caller does not supply one.
- [x] Accept bounded `test_id` and `scenario` metadata from k6 without treating arbitrary input as
  a metric label.
- [x] Return the request and trace identifiers in response headers so a failing caller can record
  them.
- [x] Add correlation values to the ASP.NET Core logging scope for the full request lifetime.

Implemented by `csharp-api/RequestCorrelationMiddleware.cs` using the contract in
[the observability conventions](doc/observability-conventions.md).

**Done when:** a single API response can be matched to the corresponding structured application
log entries and server trace.

## 3. Add structured application logging

- [x] Emit JSON logs with timestamp, severity, message, exception, route, status, duration, and the
  agreed correlation fields.
- [x] Log request failures and unexpected exceptions without recording credentials, connection
  strings, or sensitive request data.
- [x] Add focused database-operation logs that preserve the active trace context.
- [x] Avoid per-request informational logging that would distort the load experiment unless it is
  explicitly enabled for diagnostics.

Implemented with native JSON console logging, correlation scopes, request failure logging, and
database-operation error logging. Successful and slow-request records are opt-in through the
documented `LOG_SUCCESSFUL_REQUESTS` and `LOG_SLOW_REQUESTS` settings.

**Done when:** error logs are machine-queryable and a `trace_id` search finds every relevant log
entry for one failed request.

## 4. Introduce an OpenTelemetry Collector

- [x] Add a lab-owned collector service and configuration to Docker Compose.
- [x] Route application traces and optional logs through OTLP while keeping the existing Prometheus
  scrape path functional.
- [x] Add health checks, bounded queues, retry behavior, and useful collector self-telemetry.
- [x] Pin the current LTS collector image, or the current stable production release if no LTS
  channel exists, following repository policy.

Implemented with the pinned v0.157.0 contrib distribution and documented in
[the Collector operating notes](doc/opentelemetry-collector.md). Seq now stores logs and sampled
traces; the rate-limited debug exporter remains only on the duplicate OTLP metrics pipeline.

- [x] Run the documented health, self-metrics, ingestion, and Collector-outage smoke checks against
  the local Docker stack.

**Done when:** the API exports telemetry to the collector and a collector outage does not prevent
the API from serving requests.

## 5. Add Seq log and trace storage

- [x] Add a locally persisted Seq service and document the required 14-day all-events retention
  policy.
- [x] Pin the image according to repository policy, explicitly accept the Seq EULA, and document
  the single-user limitation of the free Individual license.
- [x] Export structured API logs and API and SQL client spans from the Collector to Seq over OTLP.
- [x] Verify traces include HTTP route, response status, SQL operation, duration, and error status
  without exposing SQL credentials or sensitive statement parameters.
- [x] Confirm parent-child relationships connect the inbound HTTP request to its database work.
- [x] Verify error logs can be filtered by time range, service, test run, and correlation fields.

**Done when:** a known request can be found by `trace_id` in Seq and its structured logs, API span,
and database timings can be investigated together.

Implemented with pinned Seq 2026.1.17004-x64, a persisted `seq-data` volume, and a bounded OTLP/HTTP
exporter. The trace hierarchy, structured fields, sensitive-data exclusions, filtering, and
retention policy were verified against the local stack on August 2, 2026. See
[the Seq operating notes](doc/seq.md).

## 6. Propagate test context from k6

- [x] Send the wrapper's `test_id` and the active scenario with each HTTP request.
- [x] Generate or propagate valid W3C trace context from k6 using a documented, maintainable
  approach.
- [x] Record returned request and trace identifiers when a k6 check fails.
- [x] Ensure diagnostic logging is rate-limited so a failure storm does not overwhelm the terminal
  or telemetry pipeline.
- [x] Apply the same behavior to `load-test.js` and `load-test-scan.js`.

**Done when:** a failed k6 request provides enough identifiers to locate its API trace and logs.

Implemented through `k6-correlation.js`, with a maximum of ten diagnostic lines under default
settings. The one-iteration failure smoke test and its reported `trace_id` were verified in Seq on
August 2, 2026.

## 7. Connect Grafana investigations to Seq

- [x] Keep Prometheus provisioned as Grafana's metrics source and Seq as the detailed investigation
  interface.
- [x] Add dashboard data links to Seq searches for the selected time range and bounded test context
  where practical.
- [x] Evaluate whether exemplars or another stable link can open a representative trace in Seq
  without adding high-cardinality metric labels.
- [x] Add dashboard variables for bounded fields such as `test_id`, scenario, and route.
- [x] Validate every PromQL expression and generated Seq link against the running services.

**Done when:** an operator can move from a Grafana metric spike to a focused Seq investigation and
then navigate between a trace and its correlated logs.

Implemented and documented in [the Grafana-to-Seq workflow](doc/grafana-seq-navigation.md). The
Prometheus expressions, stable one-day Seq deep link, dashboard filtering, and navigation into a
correlated Seq trace and log were verified against the local stack on August 2, 2026.

## 8. Improve the dashboard investigation workflow

- [x] Separate client-side k6 failures from API HTTP failures and database errors.
- [x] Add panels for slow requests, recent error logs, representative traces, collector health,
  dropped telemetry, and storage pressure.
- [x] Make panel descriptions explain what each signal means and what to inspect next.
- [x] Keep dotted OpenTelemetry metric and label names correctly quoted in PromQL.
- [x] Ensure an empty panel clearly distinguishes “no errors” from “telemetry unavailable.”

**Done when:** the dashboard tells a coherent story from load generation through API and database
behavior and exposes telemetry-pipeline failures.

Implemented and documented in [the dashboard investigation workflow](doc/dashboard-investigation.md).
All PromQL must pass service validation and the lightweight 404 workflow must be checked in the
reloaded Grafana dashboard before treating Step 8 as accepted.

## 9. Validate sampling, load impact, and failure behavior

The repeatable disabled-versus-enabled comparison harness is documented in
[the observability validation guide](doc/observability-validation.md). Full load runs and the
remaining evidence are intentionally still pending.

- [x] Compare baseline throughput and latency with telemetry disabled and enabled.
- [ ] Measure log and trace volume during both load-test scenarios.
- [ ] Verify all errors and slow traces survive the sampling policy.
- [ ] Test Collector, Seq, and Prometheus outages independently and document their effects.
- [ ] Confirm retention limits prevent unbounded local disk growth.

**Done when:** the cost and limitations of the observability stack are measured and documented, not
assumed.

The connection-pool comparison is recorded in
[the OTLP export overhead results](results/2026-08-02-telemetry-overhead.md). It found no exporter
penalty distinguishable from run-to-run variation on the documented test machine. Step 9 remains
in progress until the remaining volume, sampling, outage, and retention checks are complete.

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
