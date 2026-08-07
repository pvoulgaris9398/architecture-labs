# Long-polling transport

Status: **Runnable**.

This independent .NET 10 implementation holds `GET /api/events/poll` until events are available or
the bounded timeout expires. Clients pass a sequence cursor, receive every newer event in order,
and immediately issue the next request. A timeout returns `204 No Content`; disconnecting cancels
the outstanding wait.

```bash
cd labs/realtime-communication-lab/transports/long-polling
docker compose config --quiet
docker compose up --build -d
```

The server uses port `5002`. Start the WebSocket Compose project to serve the shared walkthrough
at <http://127.0.0.1:15173>, then select **Long polling**. Prometheus metrics are exposed at
<http://127.0.0.1:5002/metrics>; the shared Grafana stack provisions the **Long polling requests**
dashboard.

## Validate

```bash
(cd ../websocket && dotnet tool restore && \
  dotnet csharpier check ../long-polling/src/Server \
    ../long-polling/tests/Server.IntegrationTests)
dotnet build LongPollingDemo.slnx
dotnet run --project tests/Server.IntegrationTests
docker compose config --quiet
docker compose build
```
