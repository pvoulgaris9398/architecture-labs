# gRPC server-streaming test runbook

Use this runbook to build, start, and manually exercise the native gRPC transport. It verifies
HTTP/2 connectivity, unary publishing, live server-streaming delivery, multiple-subscriber fan-out,
ordered cursor replay, input validation, cancellation, bounded slow-consumer backpressure, and
Prometheus metrics.

This is a functional smoke test and a controlled mechanics exercise. It is not a performance
benchmark and produces no transport-comparison result.

## Prerequisites

- Docker with Docker Compose v2
- The .NET 10 SDK
- `grpcurl`
- `curl`
- Bash for the command examples
- Available host ports `5003` and `5004`

Run commands from the repository root unless a step says otherwise. Confirm the required tools:

```bash
docker compose version
dotnet --version
grpcurl --version
curl --version
```

### Install `grpcurl` on Windows

Chocolatey's community source does not currently provide a `grpcurl` package. On Windows, prefer
the WinGet package instead:

```powershell
winget install --id fullstorydev.grpcurl --exact --source winget
```

Open a new terminal and verify the installation:

```powershell
grpcurl --version
```

To install from an elevated Command Prompt:

1. Open the **Start** menu and search for **Command Prompt**.
2. Select **Run as administrator** and approve the User Account Control prompt.
3. Run this command in the elevated `cmd.exe` window:

   ```cmd
   winget install --id fullstorydev.grpcurl --exact --source winget --accept-source-agreements --accept-package-agreements
   ```

4. Close the elevated window, open a new Command Prompt, and verify that the updated `PATH` is in
   effect:

   ```cmd
   grpcurl --version
   where grpcurl
   ```

Expected result: `grpcurl --version` prints the installed version and `where grpcurl` prints the
resolved executable path. Running Command Prompt as administrator is useful when machine policy or
the selected install scope requires elevation; do not run the rest of the lab as administrator.

If WinGet is unavailable, download the Windows archive for the latest stable release from the
[official `grpcurl` releases](https://github.com/fullstorydev/grpcurl/releases/latest), extract
`grpcurl.exe`, and place its directory on your user `PATH`. Version `1.9.3` was the latest stable
release when this implementation was documented. Alternatively, if Go is already installed, use
the upstream-supported source installation and ensure Go's binary directory is on `PATH`:

```powershell
go install github.com/fullstorydev/grpcurl/cmd/grpcurl@v1.9.3
```

Do not use `choco install grpcurl` as a prerequisite for this runbook; it fails because that
package id is absent from the configured Chocolatey community feed.

The server joins the external `architecture-labs-observability` Docker network. The shared
observability stack normally creates it. If the stack is not running, create only the network:

```bash
docker network inspect architecture-labs-observability >/dev/null 2>&1 || \
  docker network create architecture-labs-observability
```

Creating the network does not start or modify any shared resource containers.

## Endpoint map

| Purpose | Host endpoint | Protocol |
| --- | --- | --- |
| Native gRPC | `127.0.0.1:5003` | Cleartext HTTP/2 (`h2c`) |
| Prometheus metrics | `http://127.0.0.1:5004/metrics` | HTTP/1.1 |

The ports are separated because ordinary HTTP/1.1 tools and Prometheus must not be forced through
the HTTP/2-only gRPC listener. This sample does not enable TLS, authentication, authorization, or
gRPC-Web and is suitable only for local experimentation.

## 1. Build and start the server

```bash
cd labs/realtime-communication-lab/transports/grpc-streaming
docker compose config --quiet
docker compose up --build -d
docker compose ps
```

Expected result: `server` is running and maps container ports `8080` and `8081` to host ports
`5003` and `5004` respectively.

Check the server logs:

```bash
docker compose logs server
```

Expected result: Kestrel reports that the application started. If the container exited, resolve
the error shown here before continuing.

Check the metrics listener:

```bash
curl -fsS http://127.0.0.1:5004/metrics | grep grpc_streaming
```

Expected result: at least the active-subscriber gauge is present. Counters and histograms appear
after the corresponding activity occurs.

## 2. Inspect the service contract

The server does not enable gRPC reflection. Supply the committed Protocol Buffer definition when
using `grpcurl`:

```bash
grpcurl -plaintext \
  -import-path Protos \
  -proto realtime.proto \
  list
```

Expected output includes:

```text
realtime.RealtimeTransport
```

Inspect its methods:

```bash
grpcurl -plaintext \
  -import-path Protos \
  -proto realtime.proto \
  describe realtime.RealtimeTransport
```

Expected result: `Publish` is unary and `Subscribe` returns a stream of `RealtimeEvent` messages.

## 3. Verify live server-streaming delivery

Open a second terminal in the gRPC implementation directory and start the included generated
.NET client:

```bash
dotnet run --project src/Client -- http://127.0.0.1:5003
```

Expected initial output:

```text
Streaming events after id 0 from http://127.0.0.1:5003. Press Ctrl+C to stop.
```

Leave the subscriber running. In another terminal in the same directory, publish an event:

```bash
grpcurl -plaintext \
  -import-path Protos \
  -proto realtime.proto \
  -d '{"message":"first event"}' \
  127.0.0.1:5003 realtime.RealtimeTransport/Publish
```

Expected unary response:

```json
{
  "id": "1",
  "message": "first event",
  "createdAtUtc": "<UTC timestamp>"
}
```

The subscriber should print the same id, timestamp, and message. Publish a second event:

```bash
grpcurl -plaintext \
  -import-path Protos \
  -proto realtime.proto \
  -d '{"message":"second event"}' \
  127.0.0.1:5003 realtime.RealtimeTransport/Publish
```

Expected result: the response and stream use id `2`. Event ids must increase in publication order.
Protocol Buffer JSON renders 64-bit integer fields as strings; the console client prints the id as
a number.

## 4. Verify multiple-subscriber fan-out

Leave the first subscriber connected and start the same client in another terminal:

```bash
dotnet run --project src/Client -- http://127.0.0.1:5003
```

Because its cursor is zero, the second client first receives the retained events. Publish another
event:

```bash
grpcurl -plaintext \
  -import-path Protos \
  -proto realtime.proto \
  -d '{"message":"fan-out event"}' \
  127.0.0.1:5003 realtime.RealtimeTransport/Publish
```

Expected result: both active subscribers receive the same event with the same id and timestamp.
This verifies fan-out to currently registered subscribers; it does not establish durable delivery
when a connection fails during publishing.

## 5. Verify replay after disconnection

1. Record the most recent id printed by one subscriber. The examples below assume it is `3`.
2. Stop that subscriber with `Ctrl+C`.
3. Publish two events while it is disconnected:

   ```bash
   grpcurl -plaintext \
     -import-path Protos \
     -proto realtime.proto \
     -d '{"message":"offline event one"}' \
     127.0.0.1:5003 realtime.RealtimeTransport/Publish

   grpcurl -plaintext \
     -import-path Protos \
     -proto realtime.proto \
     -d '{"message":"offline event two"}' \
     127.0.0.1:5003 realtime.RealtimeTransport/Publish
   ```

4. Reconnect using the recorded id as the second client argument:

   ```bash
   dotnet run --project src/Client -- http://127.0.0.1:5003 3
   ```

Expected result: the client replays only the two events whose ids are greater than `3`, in id
order, and then remains connected for live events.

Stop the replay client with `Ctrl+C` after verifying the output. Replay retains only the newest
500 events. Restarting or recreating the server erases the history and resets its id counter.

## 6. Exercise a subscription directly with `grpcurl`

The complete subscription request includes the replay cursor and controlled send delay:

```bash
grpcurl -plaintext \
  -import-path Protos \
  -proto realtime.proto \
  -d '{"afterId":0,"sendDelayMs":0}' \
  127.0.0.1:5003 realtime.RealtimeTransport/Subscribe
```

Expected result: retained events are printed first, followed by new events as they are published.
The command remains open because this is a server stream. Stop it with `Ctrl+C`.

Cancellation is normal for a long-lived subscription. The server may log the canceled call while
removing the subscriber; that log does not indicate a failed smoke test.

## 7. Verify invalid input handling

Publish a blank message:

```bash
grpcurl -plaintext \
  -import-path Protos \
  -proto realtime.proto \
  -d '{"message":"   "}' \
  127.0.0.1:5003 realtime.RealtimeTransport/Publish
```

Expected result: `grpcurl` exits nonzero with gRPC status `InvalidArgument` and detail
`message is required`.

Try an invalid replay cursor:

```bash
grpcurl -plaintext \
  -import-path Protos \
  -proto realtime.proto \
  -d '{"afterId":-1}' \
  127.0.0.1:5003 realtime.RealtimeTransport/Subscribe
```

Expected result: status `InvalidArgument` with detail `after_id cannot be negative`.

Try a delay above the bounded lab limit:

```bash
grpcurl -plaintext \
  -import-path Protos \
  -proto realtime.proto \
  -d '{"sendDelayMs":2001}' \
  127.0.0.1:5003 realtime.RealtimeTransport/Subscribe
```

Expected result: status `InvalidArgument` with detail
`send_delay_ms must be between 0 and 2000`. Publish messages are limited to 4096 characters.

## 8. Run the controlled slow-consumer experiment

Stop other subscribers so they do not complicate the observation. Open one deliberately slow
subscription in its own terminal:

```bash
grpcurl -plaintext \
  -import-path Protos \
  -proto realtime.proto \
  -d '{"afterId":0,"sendDelayMs":100}' \
  127.0.0.1:5003 realtime.RealtimeTransport/Subscribe
```

The server waits 100 ms before writing each event to this stream. In another Bash terminal, issue
550 publications with bounded concurrency and time the batch:

```bash
time seq 1 550 | xargs -I '{}' -P 32 grpcurl -plaintext \
  -import-path Protos \
  -proto realtime.proto \
  -d '{"message":"slow-client-event-{}"}' \
  127.0.0.1:5003 realtime.RealtimeTransport/Publish >/dev/null
```

Expected behavior:

- the subscriber's application channel can hold 500 events;
- publication initially proceeds quickly while that buffer fills;
- later unary calls wait for the slow stream to free capacity;
- all accepted events eventually arrive with strictly increasing ids; and
- `grpc_streaming_delivery_delay` observations increase while the backlog drains.

Watch the metrics from another terminal:

```bash
watch -n 1 'curl -fsS http://127.0.0.1:5004/metrics | grep grpc_streaming'
```

If `watch` is unavailable, rerun the `curl` command manually. Raw exporter names use underscores,
but Prometheus may store translated names differently. Query Prometheus for the authoritative
stored names before writing PromQL or Grafana panels.

This is a deterministic mechanics exercise, not a throughput measurement. Process startup from
hundreds of `grpcurl` calls, terminal rendering, HTTP/2 flow control, and the configured delay all
affect elapsed time. Do not compare this elapsed time with another transport's benchmark result.

Stop the slow subscription with `Ctrl+C` after its backlog drains.

## 9. Run the automated integration tests

From the gRPC implementation directory:

```bash
dotnet build GrpcStreamingDemo.slnx
dotnet run --project tests/Server.IntegrationTests
```

Expected result: the solution builds with no errors and all three tests pass. The suite verifies:

- ordered live delivery;
- replay of only events after the supplied cursor; and
- `InvalidArgument` for an empty publish message.

The tests run an in-process server and do not require the Compose server to be running.

Validate the container definition and image separately:

```bash
docker compose config --quiet
docker compose build
```

## 10. Stop the sample

Exit all subscriber processes with `Ctrl+C`, then stop the Compose project:

```bash
docker compose down
```

The sample defines no named volumes. This removes only its container and default Compose resources;
it does not remove the external `architecture-labs-observability` network or shared observability
containers. The in-memory event history is lost when the server stops.

## Troubleshooting

### External network not found

Create the prerequisite network with the command in the prerequisites section, or start the shared
observability project first.

### Host port 5003 or 5004 is already allocated

Find and stop the conflicting local process or container. The Compose file intentionally assigns
fixed, lab-specific host ports so the documented commands are reproducible.

### `grpcurl` reports a connection or HTTP/2 error

Confirm the server is running and use `-plaintext`. Port `5003` is h2c, not HTTPS and not an
HTTP/1.1 endpoint:

```bash
docker compose ps
docker compose logs server
```

### `grpcurl` reports that reflection is unsupported

Include all three contract flags from the examples: `-import-path Protos`,
`-proto realtime.proto`, and the fully qualified method name. Reflection is deliberately disabled.

### The first event id is greater than one

The running server already contains in-memory events. Recreate only this sample's server to reset
the history and id counter:

```bash
docker compose up -d --force-recreate server
```

This discards the sample's in-memory history. It does not delete volumes or shared resources.

### The metrics endpoint works but the gRPC endpoint does not work with `curl`

That is expected. Use a gRPC client against port `5003`; use ordinary `curl` only against the
HTTP/1.1 metrics endpoint on port `5004`.
