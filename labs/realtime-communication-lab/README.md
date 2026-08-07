# Realtime communication lab

Status: **In progress**. WebSocket is complete; SSE and long polling are runnable; the other
comparisons are planned.

## Question

How do realtime transports, message brokers, and delivery controls differ in latency, throughput,
resource use, reliability, operational complexity, and developer experience under equivalent
workloads?

## Current scope

The migrated baseline is a .NET 10 server using ASP.NET Core's raw WebSocket support. It provides:

- a WebSocket endpoint at `/ws`;
- an HTTP endpoint at `/api/events` for publishing and retrieving events;
- ping/pong, acknowledgements, replay, heartbeat, ordering, and in-memory event storage;
- a bounded, single-writer outbound channel per connection with Prometheus queue metrics;
- a controlled slow-client and burst-publishing experiment for observing backpressure;
- fragmented text-message reassembly with a 64 KiB limit and protocol-specific close codes; and
- an interactive React walkthrough under `walkthrough-ui/`.

The in-memory event store is deliberately non-durable. Restarting the server loses its events.
This configuration is for local experimentation and is not production guidance.

The independent SSE implementation under `transports/sse` provides one-way event delivery,
`Last-Event-ID` replay, bounded per-client channels, a controlled slow-client experiment, and
OpenTelemetry Prometheus metrics. Start both transport Compose projects to use both walkthrough
tabs.

## Layout

```text
realtime-communication-lab/
├── walkthrough-ui/         # Interactive React and TypeScript lab interface
├── transports/
│   ├── websocket/          # Runnable migrated baseline
│   ├── signalr/            # Planned
│   ├── sse/                # Runnable SSE implementation
│   ├── long-polling/       # Runnable long-polling implementation
│   └── grpc-streaming/     # Planned
├── brokers/
│   ├── rabbitmq/           # Planned
│   ├── kafka/              # Planned
│   └── mqtt/               # Planned
├── reliability/
│   ├── replay/             # Planned
│   ├── acknowledgements/   # Planned
│   ├── ordering/           # Planned
│   └── backpressure/       # Planned
├── benchmarks/             # Planned workloads and results
├── observability/          # Planned lab-specific telemetry assets
└── docs/                   # Experiment design and decisions
```

Empty planned directories contain `.gitkeep` files so the intended structure remains visible in
Git without implying that those implementations exist.

## Run the WebSocket walkthrough

The WebSocket Compose project runs both the .NET server and the browser-based walkthrough. It
requires the external `architecture-labs-observability` Docker network created by the shared
observability project.

```bash
cd labs/realtime-communication-lab/transports/websocket
docker compose config --quiet
docker compose up --build
```

Open <http://127.0.0.1:15173>, select **WebSocket**, and follow the numbered steps. The UI proxies
its `/api` and `/ws` requests to the server within Docker, so the browser uses one origin.

The server exposes Prometheus metrics at <http://127.0.0.1:5000/metrics>. When the shared
observability stack is running, open Grafana and select **Realtime Communication / WebSocket
outbound queues**. The dashboard shows active connections, aggregate queue depth and capacity,
message rates, queue delay, backpressure waits, and send failures. Connection identifiers are
intentionally excluded from metric labels to avoid unbounded time-series cardinality.
The bounded `connection_mode` label distinguishes only `normal` and `slow` lab connections.

For UI development with hot reload, keep the server container running and start Vite separately:

```bash
cd labs/realtime-communication-lab/transports/websocket
docker compose up --build -d server

cd ../../walkthrough-ui
npx --yes pnpm@11.16.0 install --frozen-lockfile
npx --yes pnpm@11.16.0 dev
```

Vite proxies the same paths to the server at `http://127.0.0.1:5000`.

## Run the SSE walkthrough

Start the independent SSE server, then run the shared UI from the WebSocket Compose project:

```bash
cd labs/realtime-communication-lab/transports/sse
docker compose config --quiet
docker compose up --build -d

cd ../websocket
docker compose up --build
```

Open <http://127.0.0.1:15173> and select **SSE**. The UI proxies `/sse/*` through its container to
the SSE server's host port `5001`; the WebSocket and SSE application code remain independent.
Prometheus metrics are available directly at <http://127.0.0.1:5001/metrics>, and Grafana
provisions **Realtime Communication / SSE outbound queues**.

## Run the long-polling walkthrough

Start the independent long-polling server, then rebuild the shared UI so its proxy and new tab are
available:

```bash
cd labs/realtime-communication-lab/transports/long-polling
docker compose config --quiet
docker compose up --build -d

cd ../websocket
docker compose up --build
```

Open <http://127.0.0.1:15173> and select **Long polling**. Start polling, publish an event, and
watch the held HTTP request complete before the client immediately opens the next request. Stop
polling to observe cancellation through `AbortController`. The UI proxies `/long-polling/*` to
the independent server on host port `5002`.

Prometheus metrics are available at <http://127.0.0.1:5002/metrics>. Grafana provisions
**Realtime Communication / Long polling requests**, showing active held requests, request
outcomes, returned events, and wait duration.

## Run the WebSocket baseline

Requirements: Docker with Docker Compose v2, or the .NET 10 SDK for a local build.

```bash
cd labs/realtime-communication-lab/transports/websocket
docker compose config --quiet
docker compose up --build
```

The server remains directly available at `http://localhost:5000` for command-line testing. In
another terminal, connect with a WebSocket client:

```bash
npx wscat -c ws://localhost:5000/ws
```

Then send `{"type":"ping"}` or publish an event:

```bash
curl -X POST http://localhost:5000/api/events \
  -H 'Content-Type: application/json' \
  -d '{"message":"Hello"}'
```

The complete functional test procedure is in the
[`WebSocket demo test runbook`](transports/websocket/doc/runbook.md). The earlier
[`testing notes`](transports/websocket/doc/testing.md) retain implementation trade-off context.

Stop the baseline without deleting material state:

```bash
docker compose down
```

## Validation

```bash
cd labs/realtime-communication-lab/walkthrough-ui
pnpm install --frozen-lockfile
pnpm build

cd labs/realtime-communication-lab/transports/websocket
dotnet tool restore
dotnet build WebSocketDemo.slnx
dotnet csharpier check src/Server tests/Server.IntegrationTests
dotnet run --project tests/Server.IntegrationTests
docker compose config --quiet
docker compose build

dotnet csharpier check ../long-polling/src/Server \
  ../long-polling/tests/Server.IntegrationTests
cd ../long-polling
dotnet build LongPollingDemo.slnx
dotnet run --project tests/Server.IntegrationTests
docker compose config --quiet
docker compose build
```

No controlled benchmark has been run yet. Workload definition, controlled variables, success
criteria, machine characteristics, warm-up, measurement window, software versions, and limitations
must be recorded before drawing conclusions.
