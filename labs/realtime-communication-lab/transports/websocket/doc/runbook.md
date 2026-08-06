# WebSocket demo test runbook

Use this runbook to verify the raw WebSocket baseline manually. It tests connection establishment,
application-level ping/pong, live event broadcast, ordered retrieval, acknowledgement handling,
replay after disconnection, invalid input handling, and idle-connection cleanup.

This is a functional smoke test, not a controlled performance benchmark.

## Prerequisites

- Docker with Docker Compose v2
- `curl`
- Node.js and npm for running `wscat` through `npx`
- An available host port `5000`

Run commands from the repository root unless a step says otherwise.

The demo joins the external `architecture-labs-observability` Docker network. The shared
observability stack normally creates this network. If that stack is not running, create only the
network before starting the demo:

```bash
docker network inspect architecture-labs-observability >/dev/null 2>&1 || \
  docker network create architecture-labs-observability
```

Creating the network does not start or modify any shared resource containers.

## 1. Build and start the server

```bash
cd labs/realtime-communication-lab/transports/websocket
docker compose config --quiet
docker compose up --build -d
docker compose ps
```

Expected result: `server` is running and publishes container port `8080` on host port `5000`.

The Compose project also starts the interactive walkthrough at <http://127.0.0.1:15173>. Select the
**WebSocket** tab to perform the same core connection, ping, publish, acknowledgement, and replay
checks from the browser. Continue below when testing the protocol directly with command-line tools.

Check the HTTP endpoint:

```bash
curl -fsS http://127.0.0.1:5000/
```

Expected response:

```text
WebSocket Demo

GET  /ws
POST /api/events
GET  /api/events?since=0
```

Check that the Prometheus endpoint is available:

```bash
curl -fsS http://127.0.0.1:5000/metrics | grep 'websocket_connections_active'
```

Expected result: the metric is present. Its value increases after opening a WebSocket connection.
The shared Grafana dashboard **Realtime Communication / WebSocket outbound queues** visualizes the
same metrics when the shared observability stack is running.

If startup fails, inspect the logs before continuing:

```bash
docker compose logs server
```

## 2. Connect and test ping/pong

Keep the Compose terminal available. Open a second terminal from the WebSocket implementation
directory and connect:

```bash
npx wscat -c ws://127.0.0.1:5000/ws
```

At the `>` prompt, send:

```json
{"type":"ping"}
```

Expected response:

```json
{"Type":"pong","TimestampUtc":"<UTC timestamp>"}
```

The message-property casing is not significant for incoming messages. Outgoing WebSocket messages
currently use the .NET property names shown above.

## 3. Verify live event broadcast

Leave `wscat` connected. In a third terminal, publish an event:

```bash
curl -fsS -X POST http://127.0.0.1:5000/api/events \
  -H 'Content-Type: application/json' \
  -d '{"message":"first event"}'
```

Expected HTTP response: an event record with sequence `1`, a UTC timestamp, and the message
`first event`.

Expected `wscat` message:

```json
{"Type":"event","Sequence":1,"Timestamp":"<UTC timestamp>","Message":"first event"}
```

Publish a second event:

```bash
curl -fsS -X POST http://127.0.0.1:5000/api/events \
  -H 'Content-Type: application/json' \
  -d '{"message":"second event"}'
```

Expected result: the HTTP response and WebSocket message use sequence `2`. Sequences should be
strictly increasing in publication order.

## 4. Verify HTTP retrieval

Retrieve all events after sequence zero:

```bash
curl -fsS 'http://127.0.0.1:5000/api/events?since=0'
```

Expected result: the response contains `first event` and `second event`, ordered by sequence.

Retrieve only events after sequence one:

```bash
curl -fsS 'http://127.0.0.1:5000/api/events?since=1'
```

Expected result: the response contains only `second event`.

## 5. Verify acknowledgement handling

At the active `wscat` prompt, acknowledge the latest sequence:

```json
{"type":"ack","sequence":2}
```

Acknowledgements intentionally produce no WebSocket response. Verify that the server processed the
message:

```bash
docker compose logs --since 1m server
```

Expected log entry: a client connection identifier followed by `acknowledged 2`.

The current acknowledgement is connection-local and in-memory. It is not persisted and does not
yet affect replay or delivery behavior.

## 6. Verify replay after disconnection

1. Exit `wscat` with `Ctrl+C`.
2. Publish an event while no client is connected:

   ```bash
   curl -fsS -X POST http://127.0.0.1:5000/api/events \
     -H 'Content-Type: application/json' \
     -d '{"message":"offline event"}'
   ```

3. Reconnect:

   ```bash
   npx wscat -c ws://127.0.0.1:5000/ws
   ```

4. Request everything after the last sequence received before disconnecting:

   ```json
   {"type":"replay","lastSequence":2}
   ```

Expected result: `wscat` receives the `offline event` with sequence `3`. Sending a replay request
with `lastSequence` equal to the current latest sequence returns no messages.

Replay data is held only in process memory. Restarting or recreating the server resets the event
store and sequence counter.

## 7. Verify multiple-client broadcast

Open two separate `wscat` sessions, then publish another event through the HTTP endpoint. Both
sessions should receive the same event with the same sequence and timestamp.

This confirms fan-out to the currently connected clients; it does not establish delivery
guarantees when a connection fails during a broadcast.

## 8. Verify invalid-message handling

Send each of these values from `wscat`:

```text
not-json
{}
{"type":"unknown"}
```

Expected result: the server keeps the connection open and sends no response. Its logs report
invalid JSON, a missing message type, and an unknown message type respectively:

```bash
docker compose logs --since 1m server
```

## 9. Optional heartbeat check

Connect a client and do not send any application messages. The server considers a connection idle
after two minutes and checks for idle connections every 30 seconds. The connection should
therefore close after approximately two to two-and-a-half minutes with the reason
`Heartbeat timeout`.

Sending an application message such as `{"type":"ping"}` updates the connection's last-seen time.

## 10. Stop and clean up

Exit all `wscat` sessions, then stop the demo:

```bash
docker compose down
```

The demo defines no named volumes, so this removes only its containers. It does not remove the
external `architecture-labs-observability` network or any shared observability resources.

## Troubleshooting

### External network not found

Create the prerequisite network using the command in the prerequisites section, or start the
shared observability project first.

### Host port 5000 is already allocated

Stop the conflicting local process or container. The demo currently fixes host port `5000` in its
Compose configuration.

### `npx` cannot find or download `wscat`

Install it once with `npm install --global wscat`, then run the same `wscat -c ...` command without
`npx`.

### Events begin at a sequence greater than one

The existing server process already contains events. Recreate only this demo's server to reset its
in-memory state:

```bash
docker compose up -d --force-recreate server
```
