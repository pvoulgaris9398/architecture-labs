# Initial table-scan versus index comparison

- **Date:** Sunday, August 2, 2026
- **Status:** Exploratory; needs controlled repetition
- **Endpoint:** `GET /v1/orders/by-customer`
- **Comparison:** No `IX_Orders_CustomerId` index, followed by the covering index
- **Dataset:** 500,000 `Orders` rows
- **Connection pool maximum:** 150 connections
- **Load profile:** 30 seconds to 50 VUs, 2 minutes at 200 VUs, 30 seconds to zero, with a
  100 ms sleep after each iteration

No raw Prometheus export or screenshots were preserved with this initial observation. Values below
were transcribed from Grafana and should be treated as approximate.

## Test machine

- Windows 11 Version 25H2, OS Build 26200.8875
- Dell Inspiron 365
- Intel Core i7-10700 at 2.90 GHz
- 8 physical cores and 16 logical processors
- 48 GB DDR4 memory

Future runs should also record Docker Desktop version, Docker or WSL backend, container resource
allocation, Windows power mode, relevant image and tool versions, Git revision, background
workloads, warm-up procedure, and whether the API connection pool was reset.

## Raw observations

| Signal | Without index | With index | Initial observation |
| --- | ---: | ---: | --- |
| HTTP request p95 latency | 706 ms | About 5 ms | Large reduction after adding the index |
| Average SQL client operation duration | 421 ms | About 1.5 ms | Unindexed duration climbed steadily; indexed duration stayed flat |
| Connection-pool utilization | Peak about 96% | About 7–9% maximum | The unindexed run approached the 150-connection limit |
| Hard connects | Peak shown as 15/sec | Peak shown as 8/sec | Rate display needs correction; indexed run may have inherited a warm pool |
| Hard disconnects | Flat | Flat | Physical connections were retained for reuse |
| Pool checkouts | Peak shown around 9,000/sec | Peak shown around 9,000/sec | Rate display needs correction |
| Pool inventory | Rose through roughly 50, 100, and 150, then declined in steps | Noted as much less pressured | Pool grew on demand and later pruned idle connections |
| Server active HTTP requests | Peak about 122 | Peak about 158 | Concurrent requests, not requests per second |
| k6 request rate | Peak about 1,600/sec | Peak about 1,760/sec | Indexed peak was about 10% higher |
| k6 virtual users | Peak about 200 | Peak about 200 | Expected: VUs are a controlled workload input |

## Summary

Adding the covering index changed the system from database-bound and close to connection-pool
saturation to a low-latency workload with substantial pool headroom:

- HTTP p95 latency was approximately 141 times lower (`706 / 5`).
- Average SQL operation duration was approximately 281 times lower (`421 / 1.5`).
- Pool utilization fell from roughly 144 of 150 connections in use at the observed peak to roughly
  11–14 connections.
- Peak k6 request rate increased by approximately 10%, although independently observed peaks may
  have occurred during different phases and are not a steady-state throughput comparison.

The central mechanism is connection hold time. Without the index, every request scans the table
and holds a pooled connection for hundreds of milliseconds. Concurrent requests accumulate, the
pool approaches its configured maximum, and later requests wait longer. With the covering index,
SQL completes quickly and returns connections to the pool before demand accumulates.

## Would the unindexed test continue degrading until the server crashed?

Not necessarily. This k6 script uses a closed workload model: each of the 200 VUs waits for its
response, sleeps for 100 ms, and only then sends another request. As latency rises, each VU
completes fewer iterations, so the load generator partially self-throttles.

Under continued load, the more likely progression is:

1. Table scans consume increasing SQL Server CPU, memory bandwidth, and data-page activity.
2. Queries hold connections longer.
3. The 150-connection pool approaches saturation.
4. Additional requests wait for a connection.
5. Tail latency rises and throughput plateaus or falls.
6. If waits become long enough, connection-acquisition or HTTP timeouts produce errors.

The system may settle into a high-latency saturated state rather than crash. A crash would normally
require exhaustion of another finite resource or a software failure. An open-arrival-rate test
would reveal overload more aggressively because it would continue attempting the configured
arrival rate instead of allowing slow responses to reduce generated traffic.

## Connection pool active, free, and stasis

The utilization result is strong evidence of improved connection efficiency:

- Without the index, approximately 96% utilization means about 144 of the 150 configured
  connections were in use at the observed peak.
- With the index, 7–9% utilization means roughly 11–14 connections were sufficient for the
  observed workload.

The apparent inverse relationship between active and stasis connections should not be used as
evidence of index efficiency. `connections_in_use` describes connections actively consumed by the
application. Stasis describes connections temporarily unavailable while awaiting completion of an
action, commonly transaction-related cleanup. It is not a count of requests queued for a pool
slot. This application does not explicitly manage transactions, so stasis is a secondary signal.

The direct evidence is the combination of SQL duration, HTTP latency, active/free connections, and
pool utilization. The dashboard description that presents stasis as a general pool-pressure signal
should be revised.

## Physical connection churn

Flat hard disconnects indicate that pooling retained physical connections for reuse. The observed
hard-connect difference is not yet a controlled index comparison because the indexed run followed
the unindexed run and may have inherited an already-warmed client pool. The index does not directly
make physical connection creation cheaper.

The current SqlClient EventCounter bridge also requests updates every five seconds but publishes a
raw interval `Increment` as a gauge. Incrementing EventCounters report the amount accumulated over
the listener interval; a per-second display must normalize that amount by the interval. Therefore,
values read through the current rate panels are likely overstated by a factor of five:

- A displayed 9,000 pool checkouts likely represents about 9,000 checkouts per five-second
  reporting interval, or approximately 1,800/sec.
- A displayed 15 hard connects likely represents approximately 3/sec.
- A displayed 8 hard connects likely represents approximately 1.6/sec.

The corrected checkout estimate aligns closely with the k6 request rate because each request
checks out approximately one database connection. These rate values must be corrected in the
instrumentation and rerun before they are published as authoritative measurements.

References:

- [Microsoft: EventCounters in .NET](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/event-counters)
- [Microsoft: SqlClient event counters](https://learn.microsoft.com/en-us/sql/connect/ado-net/event-counters?view=sql-server-ver17)

## Pool inventory behavior

The stepped rise toward 150 is primarily ADO.NET client-pool behavior, not SQL Server allocating
connections in fixed groups of 50. The pool creates physical connections as concurrent demand
requires them and retains idle connections for later reuse. Reaching 150 corresponds to the
configured `Max Pool Size`.

The apparent steps may be exaggerated by the five-second EventCounter reporting interval and
Prometheus scrape timing. Downward steps after load are consistent with idle connection pruning.

For a controlled comparison, restart only the API before each measured run so the client pool is
reset without recreating SQL Server or the seeded database:

```bash
docker compose restart api-service
curl --fail http://127.0.0.1:18080/health
```

Both variants then need the same deliberate warm-up. SQL Server's buffer cache can still make later
runs warmer, so repeat and alternate the indexed/unindexed ordering rather than relying on one
sequence.

## Active HTTP requests

The dashboard panel is an instantaneous gauge of concurrent in-flight requests, not a rate.
Therefore, the observations are peaks of about 122 and 158 active requests, not 122/sec and
158/sec.

A larger active-request peak does not by itself mean that the server handled requests more
efficiently. The indexed peak of 158 is also not consistent with combining 1,760 requests/sec and
5 ms p95 as if all three values occurred simultaneously. Separately observed maxima often occur at
different points in the ramp or measurement window.

Future comparisons should use an aligned steady-state window and report mean and maximum active
requests alongside sustained throughput and latency from that same window.

## k6 request rate and virtual users

The 200-VU peak is expected in both runs because the script configures it. It confirms workload
shape, not application capacity.

The indexed peak request rate was about 10% higher:

```text
(1760 - 1600) / 1600 = 10%
```

Peak throughput is insufficient for comparison. The unindexed peak may have occurred before pool
saturation and its later throughput may have fallen as latency increased. Future results should
report average or median throughput during the same two-minute 200-VU plateau and include total
iterations and error ratio.

The indexed test also approached the client-side pacing ceiling created by 200 VUs and a 100 ms
sleep:

```text
200 / 0.1 seconds = approximately 2,000 requests/sec
```

This indicates that the index removed the obvious database bottleneck, but it does not establish
the indexed API's maximum capacity. A separate capacity experiment would need a different workload
model or shorter pacing delay.

## Preliminary conclusion

In this initial single-machine run, the covering index reduced both SQL and HTTP latency by more
than two orders of magnitude and prevented connection-pool saturation under the configured
200-VU workload. The evidence supports the hypothesis that the unindexed scan held connections
long enough to amplify database latency into pool pressure and HTTP latency.

This is a preliminary conclusion. It should not be presented as a general hardware-independent
throughput claim until the experiment is repeated with corrected rate telemetry, fresh API pools,
identical warm-up, aligned steady-state windows, repeated runs, and preserved evidence.

## Follow-up checklist

- [x] Normalize incrementing SqlClient EventCounters to per-second rates in the bridge. The
  correction was implemented after this exploratory run; the displayed rates above remain the
  original uncorrected observations.
- [ ] Revise dashboard titles and descriptions for stasis and active HTTP requests.
- [ ] Add a repeatable pre-run API restart and warm-up procedure.
- [ ] Run at least three repetitions per variant and alternate variant order.
- [ ] Record aligned steady-state statistics rather than independent panel peaks.
- [ ] Preserve k6 summaries and safe Grafana screenshots or metric exports for each run.
- [ ] Record Docker allocation, software versions, Git revision, power mode, and background load.
- [ ] Report median and range across repetitions.
