# Realtime transport benchmarks

Status: **Pilot validated**. An exploratory short-window pilot is recorded; the longer controlled
comparison still needs at least five measured repetitions per profile.

## Recorded results

- [August 9, 2026 transport-mechanics pilot](results/2026-08-09-transport-mechanics-pilot.md) —
  exploratory, two repetitions with 20-second measurement windows.

## Initial experiment: steady-state broadcast delivery

### Question

How do raw WebSocket, Server-Sent Events (SSE), and long polling compare when delivering the same
server-published messages to the same number of already-connected subscribers on one local
machine?

This first experiment isolates transport mechanics. It does not attempt to model every transport's
most natural application workflow, and it does not establish a universal winner.

### Hypothesis

With connections already established, WebSocket and SSE should have lower delivery latency and
less request churn than long polling. WebSocket and SSE may differ less materially for this
one-way workload because the bidirectional capability of WebSocket is intentionally unused.

### Workload contract

Each measured run must use:

- one load-generator process and one publisher;
- an equal number of subscribers for each transport;
- connections established and subscribers confirmed ready before warm-up;
- the same UTF-8 message body, unique message identifier, publish rate, message count, and cadence;
- the transport's existing HTTP `POST /api/events` endpoint as the publishing path;
- one-to-many delivery, where every published event is expected by every subscriber;
- replay disabled and normal-client mode selected;
- a fixed warm-up followed by a fixed measurement window; and
- a fresh server process for each run so prior in-memory events cannot affect the result.

The initial local tiers are 10, 100, and 500 subscribers at 10 and 100 messages per second, using a
256-byte UTF-8 message. These values remain configurable. Maximum-throughput saturation is a
separate future experiment rather than an implicit goal of this fixed-rate comparison.

The publisher is open-loop: it starts POST requests at the configured cadence without waiting for
the previous response. This preserves the intended arrival rate when requests overlap. Publishing
stops at the measurement boundary; all outstanding POSTs must finish within the configured drain
period or the run fails. The result records attempted and successful POSTs so achieved throughput
can be checked against the target rather than assumed. It also records achieved publish rate and
p50, p95, p99, and maximum lag between each intended and actual POST launch time. Schedule lag is a
load-generator integrity signal and is not delivery latency.

A valid schedule must achieve a rate within one percent of target and keep p99 launch lag within two
publish intervals: 200 ms at 10 messages per second and 20 ms at 100 messages per second. The JSON
records the calculated limit and pass/fail status. An invalid schedule is preserved, then exits with
code 3 so the full runner stops rather than collecting a non-comparable matrix.

### Primary experiment boundary

Connection establishment is excluded from steady-state timing. The harness starts measurement only
after all subscribers are ready and the warm-up has completed. Connection-establishment latency is
a separate secondary experiment so handshake cost does not obscure sustained delivery behavior.

For long polling, a subscriber is ready when it has an outstanding poll. After receiving an event
or timeout, it must immediately issue the next poll. Poll timeout responses during the measurement
window count as request churn but not as delivered events.

### Measurements

The load generator must report, per transport and run:

- publish-to-receive latency at p50, p95, p99, maximum, and as a distribution;
- delivered, missing, duplicate, and out-of-order event counts;
- successful and failed publish counts;
- subscriber disconnects and reconnects;
- HTTP request count, including long-poll timeouts;
- achieved publish and delivery rates; and
- connection-establishment latency in the separate setup experiment.

Publish-to-receive latency begins immediately before the publisher sends the HTTP request and ends
when a subscriber receives the matching event. Keeping the publisher and subscribers in one load
generator avoids cross-machine clock skew. This measurement includes HTTP publish ingress, server
processing, fan-out, transport delivery, and client parsing; it is not server-only queue latency.

Capture server CPU, memory, network I/O, and the existing transport-specific Prometheus metrics over
the same measurement window. Do not infer comparable semantics from differently named metrics;
record metric definitions with the results.

### Controlled variables

Keep these values identical across transports within a comparison set:

- server image build and .NET runtime version;
- container CPU and memory allocation;
- load-generator host and process version;
- subscriber count and publish rate;
- payload bytes and serialization content;
- warm-up and measurement durations;
- run order randomization and repetition count;
- normal-client behavior with no artificial send delay;
- observability configuration; and
- other active applications and labs.

Run only one transport under test at a time. The shared observability services may remain active only
if they are used identically in every run.

### Semantic differences that remain

- WebSocket uses a persistent upgraded TCP connection and supports bidirectional messages, though
  this workload measures server-to-client delivery only.
- SSE uses a persistent HTTP response with text event framing and browser-style reconnection
  semantics.
- Long polling creates a new HTTP request after each delivery or timeout and may return multiple
  events in one response. Report both per-event latency and events per response.
- Wire framing and protocol overhead are intentionally not normalized away; they are properties of
  the transports being compared.

### Run discipline

Each measured run uses a 30-second warm-up, a 120-second measurement window, and a five-second drain
period for already-published messages. Use five measured repetitions per transport and profile.
Randomize or rotate transport order to reduce thermal and run-order bias. A failed run is
preserved and labeled rather than silently discarded. Define exclusion criteria before collecting
results.

Do not run the workload automatically as part of routine validation or CI. It is an opt-in local
experiment that can consume significant CPU, memory, and time.

### Success criteria

The initial benchmark is ready to support conclusions only when:

- all transports execute the same documented workload contract;
- the harness detects missing, duplicate, and out-of-order deliveries;
- at least five valid repetitions exist for every compared transport and profile;
- machine, software, workload, warm-up, measurement-window, and run-order metadata are recorded;
- raw output or a lossless practical summary is preserved without sensitive or oversized data; and
- conclusions describe observed tradeoffs and limitations rather than declaring a universal winner.

### Known limitations

- A single-machine run introduces resource contention between the load generator, containers, and
  observability stack.
- The current implementations are independent demonstrations and may require small benchmark-only
  controls to expose equivalent readiness and workload behavior.
- The initial experiment covers one-way broadcast delivery, not client-to-server messaging,
  internet latency, proxies, TLS termination, reconnection storms, or multi-node fan-out.
- Results apply to the recorded versions and machine configuration and require revalidation after
  material runtime, container, or implementation changes.

## Harness

Build the independent .NET 10 load generator:

```bash
cd labs/realtime-communication-lab/benchmarks
dotnet build RealtimeBenchmarks.slnx
```

For a correctness smoke check, start one transport server and override the measured defaults:

```bash
dotnet run --project src/LoadGenerator -- \
  --transport websocket \
  --subscribers 10 \
  --rate 10 \
  --payload-bytes 256 \
  --warmup-seconds 2 \
  --duration-seconds 5 \
  --drain-seconds 2 \
  --container-name architecture-labs-realtime-websocket-server \
  --fail-on-reliability-issue true \
  --output results/local/websocket-smoke.json
```

Valid transports are `websocket`, `sse`, and `long-polling`. Their default base URLs are ports
5000, 5001, and 5002 respectively; override one with `--base-url` when necessary. A smoke result is
only a correctness check and must not be presented as benchmark evidence.

`--payload-bytes` counts the UTF-8 bytes in the application message, excluding JSON and transport
framing. The generated identifier and padding are ASCII, so character count and UTF-8 byte count
are equal. The harness rejects a requested size too small to hold its unique identifier.

`--drain-seconds` defaults to five. Publishing and resource sampling stop before the drain begins;
outstanding publisher requests must complete within it, and the harness continues accepting
already-published deliveries with their full latency before finalizing missing-event counts.

Automatic subscriber reconnection is disabled in the baseline. Any subscriber task that completes
before teardown is recorded with its status and error, if present. The result records reconnects as
zero so a future reconnection experiment cannot be confused with this controlled steady-state
comparison.

Loss, duplication, ordering violations, publish failures, and disconnects are transport reliability
outcomes. A completed measured run records them, sets `ReliabilityPassed` accordingly, and exits
successfully so the matrix preserves and continues after an adverse result. Harness, environment,
and evidence-collection exceptions still fail the process and abort the runner. Use
`--fail-on-reliability-issue true` for smoke validation when any adverse reliability outcome should
produce exit code 2.

When `--container-name` is supplied, the harness samples `docker stats` once per second during the
measurement window only. Each JSON result preserves the raw Docker CPU percentage, memory usage and
percentage, network I/O, block I/O, and process count strings with UTC capture timestamps. The
runner supplies the correct server container name automatically. Prometheus transport metrics are
captured as raw text immediately before and after the measurement window and embedded in the JSON
result. Counter and histogram-bucket deltas therefore exclude warm-up traffic. Interpret each
transport's metrics according to its own definitions rather than assuming matching names imply
matching semantics.

On Docker Desktop, an individual `docker stats --no-stream` call may take longer than one second.
The sampler launches snapshots on a one-second schedule and allows overlapping calls to finish
normally; timestamps record when each snapshot was requested. This fixed observer overhead is
applied identically to all transports and must be listed as a limitation with measured results.

Every result also embeds the Git commit and dirty-worktree flag, OS and process architectures, CPU
description, logical processor count, runtime-available memory, Docker Desktop memory allocation,
.NET SDK and runtime versions, Docker and Compose versions, and UTC run timestamps. A dirty flag is
allowed during development but must be explained in preserved benchmark results.

The opt-in runner defaults to a pilot matrix of 10 and 100 subscribers, 10 and 100 messages per
second, all three transports, and two repetitions. Each run uses a five-second warm-up, 20-second
measurement, and five-second drain. It builds the harness, rotates transport order between
repetitions, starts a fresh server for every run, and stores ignored JSON under a timestamped
`results/local/<UTC-session>/` directory:

```bash
cd labs/realtime-communication-lab/benchmarks
./run-benchmarks.sh
```

Result files are written through a temporary file and atomically renamed when complete. To resume
an interrupted session, supply the original directory name; the runner skips completed result
files:

```bash
BENCHMARK_RUN_ID=20260810T120000Z ./run-benchmarks.sh
```

Each session includes `session.txt` with the workload matrix. A run that violates the publisher
schedule threshold is renamed with `schedule-invalid` and stops the session. Its evidence is
preserved, while a later resume retries the missing canonical result. Reliability failures remain
in their canonical files and do not stop or repeat the matrix.

After the matrix completes, the runner writes atomic `summary.json` and `summary.md` files. The
summarizer groups the five repetitions by transport, subscriber tier, publish rate, and payload,
reports schedule and reliability pass counts, median run-level p50/p95/p99 latency, p95 range,
maximum latency, and total correctness failures. The Markdown is explicitly a mechanical
aggregation, not an experimental conclusion. Regenerate it independently with:

```bash
dotnet run --project src/LoadGenerator -- \
  --summarize results/local/20260810T120000Z
```

The pilot still consumes meaningful local resources and takes roughly 20-30 minutes. It requires
Docker, Docker Compose v2, curl, Bash, .NET 10, and the external
`architecture-labs-observability` network. The script stops each transport with
`docker compose down` but does not delete volumes.

Override the pilot through environment variables. For example, the larger 10/100/500 subscriber,
five-repetition, 30-second warm-up, and 120-second measurement matrix is:

```bash
BENCHMARK_SUBSCRIBERS="10 100 500" \
BENCHMARK_REPETITIONS=5 \
BENCHMARK_WARMUP_SECONDS=30 \
BENCHMARK_DURATION_SECONDS=120 \
./run-benchmarks.sh
```

The harness currently reports delivery latency, correctness totals, unexpected disconnects,
reconnects, server container resource samples, and Prometheus snapshots. Collection of request
counts is separated by lifecycle: publisher POSTs, WebSocket upgrade attempts, SSE stream requests,
and long-poll requests and timeout responses. Upgrade and stream counts describe setup; publisher
and long-poll counts cover the measurement window. The success criteria above remain authoritative.
