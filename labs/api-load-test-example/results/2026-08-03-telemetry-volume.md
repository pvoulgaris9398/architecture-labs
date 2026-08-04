# Telemetry-volume comparison for the table-scan workload

## Question and status

This experiment asks how the table-scan workload's trace volume, Seq export volume, and observed
Seq disk growth differ with and without `IX_Orders_CustomerId`. The hypothesis was that the index
would reduce request latency and telemetry generated per completed request by avoiding the slow
database path.

- **Test date:** August 3, 2026
- **Status:** Exploratory single-pair measurement; repeat before treating the disk-growth result as
  stable
- **Endpoint:** `GET /v1/orders/by-customer`
- **Dataset:** 500,000 `Orders` rows
- **Load profile:** 30-second ramp to 200 VUs, 2-minute hold, and 30-second ramp down, with a 100 ms
  sleep after each iteration
- **Settling interval:** 15 seconds after k6 completed
- **Request logging:** successful-request and slow-request logging disabled

The runner verified the expected database index state before each run. Both variants executed the
same endpoint, SQL, load stages, and telemetry configuration. The index was the intended workload
difference. The unindexed run occurred first, followed by indexed attempts, so order and warm-cache
effects were not controlled through alternation.

## Preserved evidence

Raw Collector snapshots, k6 summaries, manifests, and volume summaries remain in the ignored local
results directories:

- `results/local/telemetry-volume-load-test-scan-1785809162/` — valid without-index run,
  `volume-scan-no-index-20260803`
- `results/local/telemetry-volume-load-test-scan-1785810360/` — valid with-index retry,
  `volume-scan-with-index-retry-20260803`

The intermediate `volume-scan-with-index-20260803` attempt completed its k6 profile but its parent
measurement process exited before capturing the post-run Collector and Seq measurements. It is
excluded from the telemetry-volume comparison.

## Raw observations

Both valid runs completed without failed HTTP checks. The Collector remained running throughout
each measurement, and neither the receiver nor Seq exporter reported refused or failed spans.

| Metric | Without index | With index |
| --- | ---: | ---: |
| HTTP requests | 49,973 | 168,999 |
| Successful checks | 49,972 | 168,998 |
| HTTP request failure rate | 0% | 0% |
| Median HTTP latency | 244.57 ms | 6.74 ms |
| p95 HTTP latency | 607.37 ms | 16.98 ms |
| Receiver accepted spans | 99,987 | 338,038 |
| Receiver refused spans | 0 | 0 |
| Seq exporter sent spans | 18,937 | 34,117 |
| Seq exporter failed spans | 0 | 0 |
| Receiver accepted log records | 0 | 0 |
| Seq exporter sent log records | 0 | 0 |
| Seq data-size change | 13,304 KiB | 7,644 KiB |

The Collector's lifetime counters included five accepted and exported log records before both
runs, but neither valid run added log records.

## Normalized comparison

The closed workload completed 3.38 times as many requests with the index, so raw telemetry totals
must be normalized by completed request.

| Metric | Without index | With index | Indexed change |
| --- | ---: | ---: | ---: |
| Receiver accepted spans/request | 2.0008 | 2.0002 | -0.03% |
| Seq exporter sent spans/request | 0.3789 | 0.2019 | -46.73% |
| Observed Seq growth/request | 272.61 bytes | 46.32 bytes | -83.01% |
| Median HTTP latency | 244.57 ms | 6.74 ms | -97.25% |
| p95 HTTP latency | 607.37 ms | 16.98 ms | -97.20% |

Receiver volume stayed at approximately two spans per request in both variants. The indexed run
sent fewer spans to Seq per request, consistent with tail sampling retaining a smaller fraction of
the faster traces. It nevertheless sent more spans in total because it completed substantially
more requests.

## Interpretation and limitations

For this pair of runs, the index removed the dominant database bottleneck: median and p95 latency
fell by about 97%, while the closed workload completed 3.38 times as many requests. Instrumentation
volume before sampling remained proportional to completed work rather than query duration.

The lower Seq-exported span count per request is evidence that sampling behavior depends on trace
latency and outcome. It should not be generalized into a fixed storage-saving percentage without
repetition and inspection of the configured sampling policies.

The Seq disk-size delta is the weakest measurement. Seq writes, compaction, indexing, and storage
allocation can make a short-window directory-size change differ from the logical event volume.
The observed 83% reduction per request is useful as an exploratory observation, not as a retention
or capacity-planning coefficient.

Additional limitations:

- This is one valid run per variant on one machine.
- The variants were not alternated, and the API, SQL Server cache, and Seq process were not reset
  between every attempt.
- An incomplete indexed attempt ran between the valid pair and added telemetry to Seq. Lifetime
  Collector counters remain comparable because each valid run uses before/after deltas, but the
  extra activity may influence Seq's later storage and compaction behavior.
- The workload is closed: faster responses cause each VU to issue more requests, so total request
  counts intentionally differ between variants.
- The experiment measured ingestion counters and directory growth, not retained event counts,
  compressed payload bytes, CPU, memory, or long-term retention.

## Follow-up

- Repeat at least three valid runs per index state and alternate execution order.
- Reset or deliberately warm the same application and database state before every run.
- Record Seq event counts or ingestion bytes in addition to directory size.
- Compare sampling decisions by policy and trace latency bucket.
- Report medians and ranges across repetitions before using the result for capacity planning.

