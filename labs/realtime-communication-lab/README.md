# Realtime communication lab

Status: **In progress**. The raw WebSocket baseline is runnable; the other comparisons are planned.

## Question

How do realtime transports, message brokers, and delivery controls differ in latency, throughput,
resource use, reliability, operational complexity, and developer experience under equivalent
workloads?

## Current scope

The migrated baseline is a .NET 10 server using ASP.NET Core's raw WebSocket support. It provides:

- a WebSocket endpoint at `/ws`;
- an HTTP endpoint at `/api/events` for publishing and retrieving events;
- ping/pong, acknowledgements, replay, heartbeat, ordering, and in-memory event storage; and
- a small browser client under `transports/websocket/src/Client`.

The in-memory event store is deliberately non-durable. Restarting the server loses its events.
This configuration is for local experimentation and is not production guidance.

## Layout

```text
realtime-communication-lab/
├── transports/
│   ├── websocket/          # Runnable migrated baseline
│   ├── signalr/            # Planned
│   ├── sse/                # Planned
│   ├── long-polling/       # Planned
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

## Run the WebSocket baseline

Requirements: Docker with Docker Compose v2, or the .NET 10 SDK for a local build.

```bash
cd labs/realtime-communication-lab/transports/websocket
docker compose config --quiet
docker compose up --build
```

The server listens on `http://localhost:5000`. In another terminal, connect with a WebSocket client:

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
cd labs/realtime-communication-lab/transports/websocket
dotnet tool restore
dotnet build WebSocketDemo.sln
dotnet csharpier check src/Server
docker compose config --quiet
```

No controlled benchmark has been run yet. Workload definition, controlled variables, success
criteria, machine characteristics, warm-up, measurement window, software versions, and limitations
must be recorded before drawing conclusions.
