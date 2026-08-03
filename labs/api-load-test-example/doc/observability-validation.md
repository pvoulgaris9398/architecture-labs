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
