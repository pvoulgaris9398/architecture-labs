# OTLP export overhead comparison

## Question and hypothesis

This experiment asks whether exporting the API's traces, structured logs, and duplicate metric
stream over OTLP causes a measurable throughput or latency penalty during the connection-pool load
scenario. The hypothesis was that enabled export would have a small cost, but that the cost might
be indistinguishable from ordinary run-to-run variation on this machine.

`TELEMETRY_OTLP_ENABLED=false` disabled the three OTLP exporters while preserving the API's
OpenTelemetry instrumentation and Prometheus scrape endpoint. This isolates exporter overhead; it
does not compare a fully uninstrumented application with an instrumented one.

## Environment

- Test date: August 2, 2026
- CPU: Intel Core Ultra 7 155H, 16 cores and 22 logical processors
- Host memory: 33,777,467,392 bytes, approximately 31.46 GiB
- Docker Desktop: 4.84.0
- Docker Engine: 29.6.2, Linux/AMD64
- Docker Compose: 5.3.1
- Docker-visible resources: 22 CPUs and 16,097,220 KiB, approximately 15.35 GiB memory
- Docker resource policy: WSL 2 dynamic defaults with no user `.wslconfig`
- k6: 2.1.0, Windows/AMD64
- .NET SDK: 10.0.101
- Host activity: mostly idle, with light web browsing and no known heavyweight competing workload

The Alpine image used to inspect Docker-visible resources was downloaded after the measurements
and therefore did not compete with the test workload.

## Method

The comparison used the unchanged three-minute `load-test.js` profile: a 30-second ramp to 200
virtual users, a two-minute ramp to 1,000 virtual users, and a 30-second ramp down. Each mode ran
three times. Execution order alternated to reduce warm-up and thermal bias:

1. Round 1: disabled, then enabled
2. Round 2: enabled, then disabled
3. Round 3: disabled, then enabled

The runner recreated only the API container between modes, waited for `/health`, and passed
`--no-thresholds` so the deliberately strict lab threshold could not abort data collection. The
workload, database, other services, and k6 stages remained unchanged. Raw k6 JSON summaries were
kept locally under the ignored `results/local/connection-pool-full/` directory.

## Observations

All six runs lasted approximately 180 seconds and completed without failed requests.

| Mode | Round | Requests | Requests/s | Average ms | Median ms | p90 ms | p95 ms | Maximum ms |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Disabled | 1 | 218,387 | 1,211.78 | 308.56 | 290.03 | 547.91 | 617.87 | 813.50 |
| Enabled | 1 | 219,146 | 1,216.82 | 307.41 | 307.04 | 536.62 | 567.29 | 683.26 |
| Enabled | 2 | 219,546 | 1,218.48 | 307.75 | 291.62 | 544.87 | 572.04 | 665.91 |
| Disabled | 2 | 219,440 | 1,218.12 | 306.84 | 302.64 | 534.39 | 566.46 | 689.05 |
| Disabled | 3 | 219,352 | 1,217.49 | 306.86 | 303.29 | 535.91 | 571.98 | 690.80 |
| Enabled | 3 | 225,064 | 1,249.37 | 299.37 | 279.73 | 530.83 | 604.50 | 721.55 |

Median values across the three runs in each mode were:

| Metric | OTLP disabled | OTLP enabled | Enabled change |
| --- | ---: | ---: | ---: |
| Throughput | 1,217.49 requests/s | 1,218.48 requests/s | +0.08% |
| Average latency | 306.86 ms | 307.41 ms | +0.18% |
| Median latency | 302.64 ms | 291.62 ms | -3.64% |
| p90 latency | 535.91 ms | 536.62 ms | +0.13% |
| p95 latency | 571.98 ms | 572.04 ms | +0.01% |
| Maximum latency | 690.80 ms | 683.26 ms | -1.09% |

Paired enabled-versus-disabled throughput changes were +0.42%, +0.03%, and +2.62%. Paired p95
changes were -8.19%, +0.98%, and +5.69%. The direction changed between rounds rather than showing
a consistent exporter penalty.

## Interpretation

OTLP export did not produce a measurable throughput or latency penalty in this connection-pool
workload. Median throughput differed by only +0.08%, median p95 latency differed by +0.01%, and the
paired changes varied in both directions. The evidence supports treating exporter overhead as
smaller than the observed run-to-run variation, not claiming that telemetry improves performance.

Both modes exceeded the ordinary 150 ms p95 threshold because this saturation profile intentionally
pushes the API far beyond its artificial 100 ms database delay. That threshold result does not
invalidate the comparison because the controlled workload and failure rate were equivalent.

## Limitations

- The conclusion applies to this machine, software set, and connection-pool workload.
- The host was mostly idle but not isolated; light web browsing may have added small variation.
- Only OTLP exporter overhead was disabled. Instrumentation and Prometheus scraping remained active.
- Three runs per mode characterize obvious effects but do not provide a high-powered statistical
  estimate of very small overhead.
- This experiment did not measure telemetry volume, sampling survival, outage behavior, retention,
  CPU consumption, memory consumption, or disk growth. Those Step 9 checks remain open.
