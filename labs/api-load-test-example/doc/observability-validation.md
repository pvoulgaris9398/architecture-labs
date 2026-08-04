# Observability cost and failure validation

Step 9 measures the observability stack instead of assuming its overhead and reliability. Run the
procedures on an otherwise idle machine, preserve the same database state for paired runs, and do
not treat a single pair as a benchmark result.

## OTLP disabled versus enabled

`TELEMETRY_OTLP_ENABLED=false` disables the API's OTLP trace, log, and duplicate metric exporters.
The API instrumentation and Prometheus scrape endpoint remain enabled. This isolates the marginal
cost of exporting telemetry without removing the server metrics needed to compare the runs.

The comparison runner executes the unchanged full k6 profile three times per mode. It alternates
the order to reduce warm-up and thermal bias, recreates only the API container between modes, waits
for API readiness, saves k6 JSON summaries under the ignored `results/local/` directory, and
restores OTLP export when it finishes or exits early.

The runner passes `--no-thresholds` because these measurement runs must finish even when the lab's
deliberately strict performance thresholds are exceeded. The exported summaries still contain the
latency, throughput, and failure measurements used in the comparison.

Before a table-scan comparison, explicitly choose one database state and keep it unchanged for all
paired runs:

```bash
curl -X POST http://127.0.0.1:18080/v1/drop-index
```

Run one scenario at a time:

```bash
RUN_LOAD_TESTS=1 bash compare-telemetry-overhead.sh load-test.js
RUN_LOAD_TESTS=1 bash compare-telemetry-overhead.sh load-test-scan.js
```

The script intentionally refuses to run without `RUN_LOAD_TESTS=1`. Override
`VALIDATION_ROUNDS=3` only when documenting why a different repetition count is sufficient. If a
run is interrupted, restore normal export explicitly:

```bash
TELEMETRY_OTLP_ENABLED=true docker compose up -d --build --force-recreate api-service
```

For each mode, compare the median of the repeated `http_reqs` rate and
`http_req_duration` p50/p95/p99 values. Also record failed-request rate, machine specifications,
Docker resource allocation, software versions, test order, database index state, and any competing
workload. Report both the absolute difference and percentage change; keep the individual run
values visible so variance is not hidden by an average.

This procedure covers only the first Step 9 item. Telemetry volume, sampling survival, independent
backend outages, and retention enforcement still require separate evidence before Step 9 can be
marked complete.

The August 2, 2026 connection-pool execution and its environment, observations, interpretation,
and limitations are recorded in
[the OTLP export overhead results](../results/2026-08-02-telemetry-overhead.md).

## Log and trace volume

Measure each unchanged load profile separately. The runner snapshots Collector counters and the
Seq `/data` directory size, runs k6 once with an explicit test ID, waits for tail-sampling and
export queues to settle, and writes raw and summarized evidence beneath the ignored
`results/local/` directory.

Before the table-scan run, record whether `IX_Orders_CustomerId` exists and keep that state fixed.
The index state affects request duration and therefore the number of traces retained by the
500 ms slow-trace policy.

```bash
RUN_LOAD_TESTS=1 \
  K6_TEST_ID=volume-pool-20260803 \
  bash measure-telemetry-volume.sh load-test.js

RUN_LOAD_TESTS=1 \
  K6_TEST_ID=volume-scan-no-index-20260803 \
  INDEX_STATE=without-index \
  bash measure-telemetry-volume.sh load-test-scan.js
```

For the scan workload, `INDEX_STATE` is required and must be `with-index` or `without-index`.
The runner checks that value against `IX_Orders_CustomerId` in SQL Server and stops before k6 if
they do not match. It does not change the index.

The safety flag is required because each invocation runs the full three-minute profile. The
default 15-second settling interval exceeds the Collector's five-second sampling decision wait
and normal one-second batch timeout. Increase `TELEMETRY_SETTLE_SECONDS` if the exporter queue has
not drained; do not silently compare runs with different settling intervals.

For each run, preserve these values in a dated result:

- k6 request count and duration from `k6-summary.json`;
- accepted and refused spans and log records at the Collector receiver;
- spans and log records sent or failed by the Seq exporter;
- the change in Seq's persisted `/data` size; and
- test ID, workload, index state, logging settings, software versions, and machine details.

Collector counters measure records, not encoded OTLP bytes, and a trace usually contains multiple
spans. Seq's directory growth includes indexes and storage-engine overhead and can be affected by
compaction. Report spans per request, exported log records per request, and persisted KiB per
request as operational volume estimates; do not present them as wire-format sizes. A negative Seq
size delta is possible during compaction and requires a repeated run or a longer observation
window. The runner rejects a Collector restart because cumulative counter deltas would be invalid.

Successful-request logging is disabled by default, so an error-free run may export few or no
application log records. Record `LOG_SUCCESSFUL_REQUESTS` and `LOG_SLOW_REQUESTS`; changing either
setting changes the experiment rather than merely improving measurement.
