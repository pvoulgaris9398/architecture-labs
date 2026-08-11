# gRPC server-streaming transport

Status: **Runnable**.

This independent .NET 10 implementation compares native gRPC server streaming with the lab's
browser-oriented transports. `Subscribe` opens one HTTP/2 response stream, `Publish` is unary,
and `after_id` replays retained events after a reconnect. Each subscriber has a bounded 500-event
channel; publishing waits when a subscriber falls behind. `send_delay_ms` (0-2000 ms) deliberately
slows one stream so backpressure can be observed.

The event history and subscriber channels are in-memory and intentionally local-only. History is
limited to the newest 500 events and is lost on restart. Native gRPC is not directly consumable by
the existing browser walkthrough, so this sample includes a .NET console subscriber instead of
introducing a gRPC-Web proxy as another experimental variable.

## Run

Start the shared observability network, then:

```bash
cd labs/realtime-communication-lab/transports/grpc-streaming
docker compose config --quiet
docker compose up --build -d
```

In one terminal, start the subscriber:

```bash
dotnet run --project src/Client -- http://127.0.0.1:5003
```

In another terminal, publish with `grpcurl`:

```bash
grpcurl -plaintext \
  -import-path Protos \
  -proto realtime.proto \
  -d '{"message":"Hello over HTTP/2"}' \
  127.0.0.1:5003 realtime.RealtimeTransport/Publish
```

Reconnect from a known cursor by passing it as the client's second argument:

```bash
dotnet run --project src/Client -- http://127.0.0.1:5003 12
```

Prometheus metrics are served over HTTP/1.1 at <http://127.0.0.1:5004/metrics>, separately from
the cleartext HTTP/2 (`h2c`) gRPC endpoint. The metrics cover active subscribers, published and
delivered events, and delivery delay. Verify the stored metric names in Prometheus before adding
Grafana queries.

## Validate

```bash
dotnet build GrpcStreamingDemo.slnx
dotnet run --project tests/Server.IntegrationTests
docker compose config --quiet
docker compose build
```

The integration tests verify live ordered delivery, cursor replay, and invalid publish handling.
The [test runbook](doc/runbook.md) documents the complete manual exercise, expected results,
metrics checks, slow-consumer experiment, troubleshooting, and non-destructive shutdown.

## Controlled comparison notes

- The payload contract carries the same logical event id, message, and creation time used by the
  other transport experiments.
- gRPC uses Protocol Buffers rather than JSON, and native clients require generated stubs. Those
  are intentional protocol differences, not workload improvements.
- HTTP/2 multiplexing and flow control affect backpressure behavior. The per-subscriber bounded
  channel makes application-level pressure visible without claiming it models every proxy or
  production deployment.
- No benchmark result is claimed until the shared load generator gains a gRPC adapter and the
  documented controlled workload is run.
