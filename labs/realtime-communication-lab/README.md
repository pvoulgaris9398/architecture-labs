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

For UI development with hot reload, keep the server container running and start Vite separately:

```bash
cd labs/realtime-communication-lab/transports/websocket
docker compose up --build -d server

cd ../..
npx --yes pnpm@11.16.0 install --frozen-lockfile
npx --yes pnpm@11.16.0 dev
```

Vite proxies the same paths to the server at `http://127.0.0.1:5000`.

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
cd labs/realtime-communication-lab
pnpm install --frozen-lockfile
pnpm build

cd labs/realtime-communication-lab/transports/websocket
dotnet tool restore
dotnet build WebSocketDemo.sln
dotnet csharpier check src/Server
docker compose config --quiet
docker compose build
```

No controlled benchmark has been run yet. Workload definition, controlled variables, success
criteria, machine characteristics, warm-up, measurement window, software versions, and limitations
must be recorded before drawing conclusions.
