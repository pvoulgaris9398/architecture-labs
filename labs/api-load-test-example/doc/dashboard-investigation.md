# Dashboard investigation workflow

## Read the dashboard from left to right

Use the same `test_id`, scenario, endpoint, and time window throughout an investigation. Start with
the failure-domain panels before interpreting connection-pool or SQL behavior:

1. **k6 Client Failure Ratio** — what the load generator observed, including timeouts and TCP
   connection refusals that never reached ASP.NET Core.
2. **API HTTP Error Rate** — 4xx and 5xx responses emitted by the API. A 404 proves the API handled
   the request; it is different from a connection refusal.
3. **Database Operation Error Rate** — failed SqlClient operations identified by the quoted
   OpenTelemetry `"error.type"` label.
4. **Slow API Request Rate** — requests exceeding 500 ms, the same threshold used by Collector tail
   sampling.
5. Connection-pool, SQL-duration, active-request, and throughput panels — the resource behavior
   surrounding the failure.
6. Collector and storage panels — whether the evidence pipeline was trustworthy during the event.

## Failure-domain interpretation

| Observation | Meaning | Next action |
| --- | --- | --- |
| k6 failure, no API error | Request may not have reached the API | Inspect k6 error text, API availability, listener, and port ownership |
| API 4xx | API handled and rejected the request | Open Seq and inspect route, request context, and trace |
| API 5xx, no database error | Failure was likely outside the instrumented SQL operation | Inspect the correlated application log and server span in Seq |
| API 5xx plus database error | SqlClient failure likely contributed to the response | Open the trace and inspect the database child span and exception |
| Slow request plus pool saturation | Connections are held long enough to create contention | Compare SQL duration, pool utilization, free connections, and stasis |

These relationships are diagnostic hypotheses, not proof of causality. Confirm them with the
correlated trace and logs.

## Logs and traces

Prometheus cannot list recent structured events or individual traces. The dashboard therefore uses
two explicit launch panels:

- **Recent Warning and Error Logs (Seq)** opens Seq with the selected test/scenario and warning,
  error, or fatal filter. HTTP 4xx records are warnings and therefore remain visible.
- **Representative Traces (Seq)** opens all retained events for the selected run; select an event
  and use Seq's **Trace** menu.

This avoids duplicating telemetry storage or installing an unsupported Seq data source merely to
render a partial event list in Grafana.

## Telemetry-pipeline health

**Collector Availability and Seq Export Queue** combines two different signals:

- `Collector available = 1` means Prometheus can scrape Collector self-telemetry.
- Seq queue utilization should normally stay near zero. A sustained rise means export is slower
  than ingestion or Seq is unavailable. The queue is bounded, so prolonged pressure can become
  telemetry loss.

**Collector Refused, Failed, or Prematurely Dropped Telemetry** excludes ordinary tail-sampling
decisions. It shows receiver refusal, internal receiver failures, and traces discarded before the
configured sampling wait elapsed. Any rate above zero requires investigation.

The Collector version may not emit an exporter-send-failure series until that condition occurs, so
queue pressure, Collector logs, and Seq ingestion results remain part of outage diagnosis.

## Storage pressure

**Host Storage Free (C:)** uses optional windows_exporter metrics and highlights the lab's 10 GiB
free-space guardrail. It is a host warning, not direct measurement of the `seq-data` or
`prometheus-data` Docker volumes. Step 9 must measure those volumes directly and verify retention.

If the panel is blank, windows_exporter is unavailable. Blank does not mean storage is healthy.

## Empty-state contract

Panels that count errors or slow requests use `or vector(0)` so a healthy zero is visible. Interpret
that zero only when the relevant availability panel is healthy:

- API error, database error, and slow-request zero requires **API Scrape Availability = 1**.
- Collector loss zero requires **Collector available = 1**.
- A blank Windows storage panel means the optional exporter is unavailable.
- Seq launch panels contain guidance instead of appearing empty because logs and traces are stored
  outside Prometheus.

If Grafana reports a data-source error, treat all zero-filled panels as untrustworthy until
Prometheus connectivity is restored.

## Lightweight verification

After Grafana reloads the provisioned dashboard:

1. Run the one-iteration 404 smoke test from `observability-troubleshooting.md`.
2. Confirm k6 failure ratio and API 4xx move while database errors remain zero.
3. Follow the error-log link and locate the 404 in Seq.
4. Confirm Collector availability is 1, queue utilization is low, and loss rates are zero.
5. Confirm the storage panel shows bytes or is clearly blank because windows_exporter is absent.

Do not create a deliberate database failure or Collector outage merely to validate this dashboard;
those controlled failure tests belong to Step 9.
