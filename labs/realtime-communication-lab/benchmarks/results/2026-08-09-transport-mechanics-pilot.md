# Transport mechanics pilot: WebSocket, SSE, and long polling

## Question and status

How do the three runnable transports behave under the same one-to-many, open-loop, steady-state
broadcast workload?

- **Status:** Exploratory pilot; repeat with longer windows and at least five repetitions before
  treating latency or resource differences as stable.
- **Test window:** August 9, 2026, 21:57-22:12 EDT (August 10, 01:57-02:12 UTC).
- **Benchmark contract:** [Realtime transport benchmarks](../README.md).

This pilot validates the benchmark workflow and identifies behaviors worth investigating. It does
not establish a universal transport ranking.

## Hypothesis

With subscribers already connected, WebSocket and SSE were expected to have lower delivery latency
and less request churn than long polling. WebSocket and SSE were expected to behave similarly for
this intentionally one-way workload. The harness also tested loss, duplication, delivery order,
disconnects, and whether its publisher sustained the requested cadence.

## Controlled workload

| Field | Value |
| --- | --- |
| Transports | Raw WebSocket, SSE, long polling |
| Subscribers | 10 and 100 |
| Publish rates | 10 and 100 messages/second |
| Payload | 256 UTF-8 bytes |
| Publisher | One open-loop scheduler using concurrent HTTP POSTs |
| Warm-up | 5 seconds |
| Measurement | 20 seconds |
| Delivery drain | 5 seconds |
| Repetitions | 2 per transport/profile |
| Total runs | 24 |
| Run order | Rotated by repetition |
| Server lifecycle | Fresh container for every run |
| Replay and slow-client mode | Disabled |

The load generator required achieved publish rate within one percent of target and p99 scheduling
lag within two publish intervals. All 24 runs passed this workload-validity check.

## Environment

| Field | Value |
| --- | --- |
| Git commit | `b014636ce5061add8cc0cbd658640c9d7626c271` |
| Worktree | Dirty; the new benchmark harness was uncommitted |
| Operating system | Microsoft Windows 10.0.26200, x64 |
| CPU | Intel64 Family 6 Model 170 Stepping 4, GenuineIntel |
| Logical processors | 22 |
| Runtime-available memory | 33,777,467,392 bytes |
| Docker memory | 16,483,557,376 bytes |
| .NET SDK/runtime | SDK 10.0.302; .NET 10.0.10 |
| Docker | 29.6.2, build dfc4efb |
| Docker Compose | v5.3.1 |

The dirty worktree means the commit alone does not identify the exact harness used. The server
implementations were unchanged by the benchmark work, but the harness must be committed before a
later run can be reproduced from a commit without additional context.

## Mechanical summary

Latency values are medians of each profile's two run-level percentiles. The p95 range shows the two
run-level p95 values. These short, two-run aggregates are descriptive only.

| Transport | Subscribers | Rate/s | Schedule pass | Reliability pass | Median p50 ms | Median p95 ms | Median p99 ms | p95 range ms | Missing | Duplicates | Out of order |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Long polling | 10 | 10 | 2/2 | 2/2 | 2.83 | 8.20 | 46.59 | 3.39-13.00 | 0 | 0 | 0 |
| SSE | 10 | 10 | 2/2 | 2/2 | 4.22 | 6.28 | 8.71 | 2.98-9.59 | 0 | 0 | 0 |
| WebSocket | 10 | 10 | 2/2 | 2/2 | 4.15 | 6.28 | 8.73 | 3.31-9.26 | 0 | 0 | 0 |
| Long polling | 10 | 100 | 2/2 | 2/2 | 1.99 | 2.82 | 3.60 | 2.75-2.89 | 0 | 0 | 0 |
| SSE | 10 | 100 | 2/2 | 0/2 | 2.07 | 2.83 | 3.35 | 2.77-2.88 | 0 | 0 | 4,185 |
| WebSocket | 10 | 100 | 2/2 | 0/2 | 1.99 | 2.63 | 3.09 | 2.60-2.67 | 0 | 0 | 5,124 |
| Long polling | 100 | 10 | 2/2 | 2/2 | 4.41 | 6.96 | 11.05 | 6.71-7.21 | 0 | 0 | 0 |
| SSE | 100 | 10 | 2/2 | 2/2 | 3.80 | 5.52 | 7.96 | 5.51-5.52 | 0 | 0 | 0 |
| WebSocket | 100 | 10 | 2/2 | 2/2 | 3.57 | 4.86 | 7.20 | 4.67-5.06 | 0 | 0 | 0 |
| Long polling | 100 | 100 | 2/2 | 2/2 | 3.91 | 7.11 | 11.26 | 6.74-7.47 | 0 | 0 | 0 |
| SSE | 100 | 100 | 2/2 | 0/2 | 3.28 | 5.12 | 8.67 | 5.02-5.23 | 0 | 0 | 56,450 |
| WebSocket | 100 | 100 | 2/2 | 0/2 | 3.13 | 4.66 | 6.88 | 4.44-4.89 | 0 | 0 | 56,072 |

Every run completed all publisher POSTs without message loss, duplication, publish failure, or
subscriber disconnect. The reliability failures in the table are ordering violations only.

## Container CPU observations

The harness requested `docker stats` snapshots once per second during each measurement window. The
table reports the arithmetic mean and maximum of the raw Docker CPU percentages across both runs.
Docker CPU percentage can exceed 100% when a container uses more than one logical processor.

| Transport | Subscribers | Rate/s | Samples | Mean CPU % | Maximum CPU % |
| --- | ---: | ---: | ---: | ---: | ---: |
| Long polling | 10 | 10 | 42 | 11.57 | 88.32 |
| SSE | 10 | 10 | 42 | 6.73 | 42.56 |
| WebSocket | 10 | 10 | 42 | 2.90 | 23.49 |
| Long polling | 10 | 100 | 42 | 33.80 | 128.70 |
| SSE | 10 | 100 | 42 | 16.27 | 74.91 |
| WebSocket | 10 | 100 | 42 | 11.87 | 55.10 |
| Long polling | 100 | 10 | 42 | 24.68 | 56.04 |
| SSE | 100 | 10 | 42 | 5.88 | 35.96 |
| WebSocket | 100 | 10 | 42 | 5.07 | 29.85 |
| Long polling | 100 | 100 | 42 | 212.60 | 365.08 |
| SSE | 100 | 100 | 42 | 28.48 | 89.93 |
| WebSocket | 100 | 100 | 42 | 21.94 | 63.01 |

This pilot suggests that long polling's repeated HTTP request lifecycle becomes materially more
CPU-intensive as subscriber count and publish rate rise. Longer, repeated runs are required before
estimating the size or stability of that difference. The overlapping Docker CLI sampling calls add
fixed observer overhead to every transport and are an explicit limitation.

## Ordering observation

SSE and WebSocket preserved order at 10 messages per second but reported ordering violations in all
100-message-per-second runs. Long polling preserved order in every pilot run.

This pattern is consistent with concurrent publish requests assigning server sequences and then
entering independent asynchronous broadcast operations. A later sequence can be enqueued to a
persistent subscriber before an earlier broadcast reaches that subscriber. Long polling reads the
event store by sequence after a poll wakes, which can avoid that particular broadcast race. This is
an inference from the observed results and current implementations, not yet an isolated causal
experiment.

The next focused experiment should vary only publisher concurrency while holding rate, subscribers,
payload, and duration constant. That would distinguish a transport property from the current
server-side broadcast coordination policy.

## Limitations

- Two repetitions and 20-second measurement windows do not characterize run-to-run variability or
  steady thermal behavior.
- The load generator, Docker Desktop, servers, and observation processes shared one machine.
- HTTP POST ingress is part of publish-to-receive latency; this is not server-only queue latency.
- Open-loop concurrent POSTs do not provide a client-side total request order. The server-assigned
  event sequence is the ordering authority used by the harness.
- Docker resource samples are short and include observer overhead.
- Raw JSON, resource samples, and Prometheus snapshots remain ignored local artifacts rather than
  tracked evidence.
- TLS, proxies, internet latency, reconnection storms, multi-node fan-out, and bidirectional traffic
  were outside scope.

## Evidence and reproduction

The ignored local session was `benchmarks/results/local/pilot-20260810T015500Z`. It contains 24
canonical JSON results, raw before/after Prometheus snapshots embedded in each result, resource
samples, `session.txt`, `summary.json`, and `summary.md`.

Run the current pilot from Git Bash:

```bash
cd labs/realtime-communication-lab/benchmarks
./run-benchmarks.sh
```

Do not treat a rerun as comparable unless its environment, workload, schedule-validity checks, and
implementation state are recorded and reviewed.
