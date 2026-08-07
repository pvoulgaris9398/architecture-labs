# Server-Sent Events transport

Status: **Runnable**.

This independent .NET 10 implementation exposes `GET /events/stream`, `POST /api/events`, event
retrieval, and a bounded burst endpoint. Each SSE record contains `id`, `event: message`, and JSON
`data`. Reconnection uses the standard `Last-Event-ID` header; the walkthrough also accepts a
`lastEventId` query value to demonstrate manual replay.

The event store and per-client 500-message channels are in-memory and intentionally local-only.
The controlled `sendDelayMs` query value is capped at 2000 ms and the burst endpoint at 1000
events.

## Run

Start the shared observability stack, then:

```bash
cd labs/realtime-communication-lab/transports/sse
docker compose config --quiet
docker compose up --build -d
```

The server is available at <http://127.0.0.1:5001>. Start the WebSocket Compose project as well to
serve the shared walkthrough at <http://127.0.0.1:15173>, then select **SSE**. Grafana provisions
the **Realtime Communication / SSE outbound queues** dashboard.

## Validate

```bash
dotnet build SseDemo.slnx
dotnet run --project tests/Server.IntegrationTests
docker compose config --quiet
```

The integration tests verify the SSE field format, live delivery, ordered replay, and
`Last-Event-ID` semantics.
