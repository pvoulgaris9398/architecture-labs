# Grafana-to-Seq investigation workflow

## Division of responsibility

Grafana and Prometheus show aggregate behavior across the complete workload. Seq is the detailed
interface for structured logs and sampled traces. Seq is not configured as a Grafana data source;
the dashboard uses browser links to move from an aggregate signal to a focused Seq search.

The dashboard variables are:

| Variable | Source | Effect |
| --- | --- | --- |
| `endpoint` | Server HTTP metric `http.route` values | Filters server latency and HTTP-error panels |
| `test_id` | k6 `test_id` label values | Filters k6 panels and the Seq search link |
| `scenario` | k6 `scenario` values for the selected test | Filters request-based k6 panels and the Seq search link |

`test_id` and `scenario` are intentionally present only on k6 metrics. Adding per-run context to
server metrics would multiply every server series. Server latency, connection-pool, and SQL panels
are correlated to the selected run by the dashboard time window, not by high-cardinality labels.

## Use the links

1. Select the dashboard time range containing the test.
2. Select a `test_id` and, optionally, a scenario. Keep **All** when comparing runs.
3. Use **Investigate selected run in Seq** at dashboard level or on the latency, server-error,
   client-failure, or SQL-duration panel.
4. Sign in to Seq if prompted; Seq preserves the requested event search through login.
5. Refine by `@TraceId`, `request_id`, status, or duration after identifying a representative
   event.

The generated Seq filter is equivalent to:

```sql
test_id = /selected-test-pattern/ and scenario = /selected-scenario-pattern/
```

Grafana's **All** value expands to `.*`, so the same link remains valid without a narrow selection.
The link opens Seq with its stable **Last 1 day** range. Grafana exposes its dashboard duration in
seconds, but Seq does not interpret that URL value compatibly and can round it to the invalid
**Last 0d** range. An exact cross-application time conversion is therefore intentionally avoided.
For a narrower or historical Grafana window, set the matching time range in Seq after opening the
link. The bounded test ID normally remains the more precise selector.

Routes stay in the Grafana `endpoint` control instead of the URL-generated Seq regular expression.
Route values contain `/`, which would require backend-specific regular-expression escaping. In
Seq, add a route constraint interactively when needed, for example:

```sql
http_route = '/v1/orders/by-customer'
```

## Why there is no exemplar-to-trace link yet

Prometheus server metrics do not currently expose a reliable trace exemplar that Grafana can map
directly to Seq, and Seq is not installed as a Grafana trace data source. Adding `trace_id` as a
Prometheus label would create unbounded cardinality and violates the lab's telemetry contract.

The supported pivot is therefore:

```text
Grafana metric window + test_id -> Seq filtered events -> @TraceId -> full trace
```

Re-evaluate exemplar navigation only if the metrics exporter emits standards-compliant exemplars
and Grafana can link their trace IDs to a stable Seq URL without turning identifiers into labels.

## Verification

PromQL expressions were validated against the running Prometheus API on August 2, 2026. The Seq
deep-link route and authentication redirect were validated against the pinned local Seq service.
After dashboard provisioning reloads, verify interactively:

1. `test_id=correlation-smoke` appears in the variable list.
2. Selecting it narrows the k6 request and failure panels.
3. The Seq link opens a filter containing `correlation-smoke` and the selected scenario.
4. The previously verified 404 event and trace are present in the resulting search.

## References

- [Grafana dashboard and panel links](https://grafana.com/docs/grafana/latest/visualizations/dashboards/build-dashboards/manage-dashboard-links/)
- [Grafana variables](https://grafana.com/docs/grafana/latest/visualizations/dashboards/variables/)
- [Seq search expressions](https://datalust.co/docs/query-syntax)
