# Realtime transport benchmark results: <date and profile>

Status: **Template; no results recorded**

## Question and hypothesis

Link to the benchmark contract and state the specific comparison made in this run set.

## Environment

| Field | Value |
| --- | --- |
| Test date and timezone | |
| Commit | |
| Operating system | |
| CPU | |
| Logical processors | |
| Memory | |
| Docker version | |
| Docker Compose version | |
| .NET runtime/image | |
| Load-generator version | |
| Container CPU/memory limits | |
| Observability enabled | |

## Workload

| Field | Value |
| --- | --- |
| Subscriber count | |
| Payload size | 256 UTF-8 bytes (unless overridden) |
| Publish rate | |
| Warm-up duration | |
| Measurement duration | |
| Delivery drain | 5 seconds (unless overridden) |
| Measured repetitions | |
| Poll timeout | |
| Run order | |

Record achieved publish rate and publisher schedule-lag p50, p95, p99, and maximum for every run.
The rate must remain within one percent of target and p99 lag within two publish intervals. Do not
accept a run whose load generator did not sustain those limits.

## Results

| Transport | Run | p50 | p95 | p99 | Max | Missing | Duplicates | Out of order | Publish failures | Disconnects | Requests |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| WebSocket | | | | | | | | | | | |
| SSE | | | | | | | | | | | |
| Long polling | | | | | | | | | | | |

Record latency units, aggregate method, variability across runs, achieved rates, events per
long-poll response, and server CPU, memory, and network I/O alongside this table.

Report publisher POSTs separately from WebSocket upgrade attempts, SSE stream requests, and
long-poll requests and timeout responses. Do not combine setup and measurement-window counts into a
single request total.

Calculate Prometheus counter and histogram-bucket deltas from the raw before/after snapshots stored
in each JSON result. Preserve the stored metric names exactly when documenting queries.

## Correctness checks

- Expected versus received deliveries:
- Missing or duplicate identifiers:
- Ordering violations:
- Reconnects or premature disconnects:
- Invalid or excluded runs and the predeclared reason:

## Observations

Describe measured behavior without attributing causality that the evidence cannot establish.

## Limitations

Record machine contention, instrumentation effects, implementation differences, and anything that
limits generalization.

## Evidence

List the tracked raw or summarized artifacts and the exact commands used. Do not commit oversized
captures or machine-sensitive data.
