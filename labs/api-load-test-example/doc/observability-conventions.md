# Observability correlation, sampling, and retention conventions

This document defines the telemetry contract for the lab before logs and traces are added. It is a
design baseline: later implementation steps must either follow it or update it with the reason for
the change.

## Correlation contract

Use W3C Trace Context for distributed trace propagation. Use lowercase `snake_case` property names
in structured logs and Seq searches. OpenTelemetry semantic attributes keep their standard dotted
names. Do not put secrets, connection strings, request bodies, SQL parameters, or customer data in
any correlation field.

| Concept | Structured property | Transport | Format and scope |
| --- | --- | --- | --- |
| Trace | `trace_id` | W3C `traceparent` | 32 lowercase hexadecimal characters; one distributed trace |
| Span | `span_id` | W3C `traceparent` | 16 lowercase hexadecimal characters; one operation in a trace |
| Trace state | OpenTelemetry context | W3C `tracestate` | Forward unchanged unless an instrumentation library updates it |
| Request | `request_id` | `X-Request-ID` | Opaque value, at most 128 characters; one inbound API request |
| Test run | `test_id` | `X-Test-ID` | At most 64 characters matching `[A-Za-z0-9][A-Za-z0-9._-]{0,63}` |
| Scenario | `scenario` | `X-Test-Scenario` | One value from the controlled scenario list below |
| HTTP route | `http.route` | OpenTelemetry span attribute | Matched route template such as `/v1/data-endpoint`, never the raw URL |
| HTTP status | `http.response.status_code` | OpenTelemetry span attribute | Integer HTTP response status |

The controlled `scenario` values are:

- `api-readiness`
- `connection-pool`
- `table-scan-comparison`

The API must generate `request_id` when it is absent or invalid. Trace and span identifiers come
from the active `System.Diagnostics.Activity`; the application must not invent parallel trace
identifiers. The API must return `X-Request-ID` and `X-Trace-ID` response headers for diagnostics.
It may echo valid `X-Test-ID` and `X-Test-Scenario` values.

k6 uses the canonical `test_id` Prometheus tag and sends the same value as `X-Test-ID`. The wrapper
generates and prints a valid ID unless `K6_TEST_ID` is supplied explicitly.

## Prometheus cardinality policy

Prometheus metrics describe aggregates, not individual requests. A value is eligible as a label
only when its possible values are controlled and its operational value justifies the additional
series.

| Label | Policy | Reason |
| --- | --- | --- |
| `service.name` or scrape `job` | Allow | Small, stable service set |
| `http.request.method` | Allow | Small HTTP method set |
| `http.route` | Allow | Fixed route templates owned by the API |
| `http.response.status_code` | Allow | Small bounded status-code set |
| `scenario` | Allow for k6 metrics | Three controlled values |
| `test_id` | Allow only for k6 run metrics | One value per deliberate run; seven-day retention bounds active series |
| `error.type` | Allow only when normalized | Framework-controlled categories, not exception messages |
| `trace_id`, `span_id`, `request_id` | Never | Unique per trace, span, or request |
| Raw URL, query string, SQL text | Never | Unbounded and may contain sensitive values |
| Customer, order, connection, process, or thread ID | Never | High-cardinality implementation or domain identifiers |
| Exception message or stack trace | Never | Unbounded log content |

Do not add `test_id` or `scenario` to server-side HTTP metrics merely because the values arrive in
headers. That would multiply every API metric series by test run. Correlate server metrics by time
range and use `test_id` in k6 metrics, logs, and traces instead.

Seq structured properties may contain high-cardinality correlation identifiers because they are
used for event search rather than Prometheus series identity. If Loki is evaluated later, store
these identifiers as structured metadata, not indexed labels.

## Initial trace-sampling policy

The API should create and export every candidate span to the OpenTelemetry Collector. Make the
retention decision in the Collector with tail sampling so the complete trace can be considered:

1. Retain 100% of traces containing an exception, OpenTelemetry error status, or HTTP response
   outside the 200-399 range.
2. Retain 100% of traces whose end-to-end duration is at least 500 ms.
3. Retain 1% of otherwise successful `/health` traces.
4. Retain 10% of all other successful traces.

The 500 ms boundary aligns with the table-scan experiment's current latency threshold and can be
changed only with the experiment documentation. Sampling does not replace metrics: Prometheus
continues to represent every recorded request. A connection failure that occurs before k6 reaches
the API cannot produce an API trace and must remain visible through k6 metrics and diagnostics.

Tail sampling buffers telemetry and can drop spans if the Collector is undersized. Step 4 must set
bounded queues and expose accepted, sampled, refused, and dropped telemetry counts. Step 9 must
measure the policy under both workloads and revise the percentages if it loses errors, slow traces,
or materially changes the experiment.

## Retention and disk guardrails

These are local-lab defaults, not production recommendations:

| Data | Retention | Storage budget | Notes |
| --- | --- | --- | --- |
| Prometheus metrics | 7 days | 5 GiB target | Already configured with `--storage.tsdb.retention.time=7d` |
| Seq debug/verbose events | 24 hours | Included in Seq budget | Short-lived diagnostic detail |
| Seq ordinary successful logs and traces | 3 days | Included in Seq budget | Enough for recent run analysis |
| Seq errors and traces lasting at least 500 ms | 7 days | Included in Seq budget | Preserve the most useful evidence longer |
| All remaining Seq events | 14-day hard time cap | 5 GiB target | Safety-net retention policy |

The two 5 GiB values are operational budgets, not guaranteed Docker volume quotas or predictions.
Stop generating load and inspect storage when either volume exceeds its target or the Docker host
has less than 10 GiB free. Step 9 must record actual bytes per test, verify deletion behavior, and
adjust the budgets using measured evidence. Large raw exports and captured results must remain
untracked unless intentionally reduced to a safe summary.

## Safe cleanup

Routine shutdown preserves all data:

```bash
docker compose down
```

Inspect the lab's volumes and their resolved names before deleting anything:

```bash
docker volume ls --filter label=com.docker.compose.project=api-load-test-example
docker volume inspect api-load-test-example_prometheus-data
```

When Seq is implemented, its Compose volume must be named `seq-data`, which normally resolves to
`api-load-test-example_seq-data`. To reset one telemetry backend, first stop the stack, verify the
exact volume belongs to this lab, and remove only that explicit volume:

```bash
docker compose down
docker volume inspect api-load-test-example_seq-data
docker volume rm api-load-test-example_seq-data
```

Replace the final two commands with the verified Prometheus volume name only when intentionally
resetting metrics. Do not use `docker compose down --volumes` for routine cleanup: it removes every
Compose-managed volume, including Prometheus, Seq, and any database volume introduced later. Volume
deletion is irreversible unless the data was exported separately.

## References

- [W3C Trace Context](https://www.w3.org/TR/trace-context/)
- [OpenTelemetry semantic conventions](https://opentelemetry.io/docs/specs/semconv/)
- [OpenTelemetry Collector tail sampling](https://github.com/open-telemetry/opentelemetry-collector-contrib/tree/main/processor/tailsamplingprocessor)
- [Prometheus metric and label practices](https://prometheus.io/docs/practices/naming/)
- [Seq retention policies](https://datalust.co/docs/retention-policies)
- [ADR 0002: Use Seq first for correlated logs and traces](../../../docs/decisions/0002-seq-first-observability-backend.md)
