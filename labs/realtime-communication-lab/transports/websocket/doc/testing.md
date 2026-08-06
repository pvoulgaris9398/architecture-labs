# Stage 1 Example

```bash
npm install -g wscat
```

```bash
wscat -c wss://websocket-echo.com
```

- This is the raw WebSocket endpoint; `wscat` will open a connection to it.

```bash
wscat -c ws://localhost:5000/ws
```

- If we post these messages to the `ws` endpoint, we may get a response
- Also, posting to `http://localhost:5000/api/events` will cause \
  us to get a notification via WebSocket on the `ws` endpoint \
  (assuming we are listening with `wscat`)

```json

{"type":"ping"}

{"type": "ack", "sequence": 0}

{"type": "replay", "lastSequence": 0}

{"type": "replay", "lastSequence": 2}

{"type": "blah"}

```

```bash
curl \
-X POST \
http://localhost:5000/api/events \
-H "Content-Type: application/json" \
-d "{\"message\":\"Hello\"}"
```

## EventStore Implementation & Trade-Offs

- **In-memory vs. Durable Storage**
  - **In-memory** (`ConcurrentQueue`, `List`) is extremely fast with low latency, but all events are lost if the process restarts.
  - **Durable storage** (PostgreSQL, EventStoreDB, Kafka) provides persistence, replay, and recovery, but introduces network and disk I/O latency.

- **Write Throughput vs. Read Efficiency**
  - An append-only queue provides **O(1)** writes and excellent throughput.
  - Replaying events by scanning the queue becomes **O(n)** as the event history grows, making long-running systems less efficient.

- **Memory Usage vs. Replay Window**
  - Keeping every event in memory allows unlimited replay but causes memory growth over time.
  - A ring buffer or fixed-size cache keeps memory bounded but limits how far back clients can replay events.

- **Concurrency vs. Simplicity**
  - Lock-free approaches (`ConcurrentQueue`, `Interlocked`) provide excellent scalability and avoid thread contention.
  - Simpler collections (`List<T>`) require explicit synchronization (`lock` or `ReaderWriterLockSlim`) but can offer better read performance and cache locality.

- **Ordering Guarantees vs. Scalability**
  - A single atomic sequence generator (`Interlocked.Increment`) guarantees a globally ordered event stream and simplifies replay.
  - In distributed or partitioned systems, maintaining global ordering becomes more difficult and often requires partitioning or accepting only per-partition ordering.

- **Replay Performance vs. Storage Complexity**
  - A simple append-only collection is easy to implement but requires scanning to find missed events.
  - Indexed databases or event stores can efficiently retrieve "events since sequence N" but add operational complexity.

- **Single Responsibility**
  - Keeping the `EventStore` focused solely on storing and retrieving events makes it easy to replace the implementation (e.g., swap an in-memory queue for PostgreSQL or Kafka) without affecting the rest of the application.

#### The key architectural trade-off

The central question is:

> **Are we optimizing for low-latency event ingestion, efficient replay, durability, or bounded resource usage?**

Most production systems can't maximize all four simultaneously, so the implementation of the `EventStore` is driven by which of those qualities is most important for the application's requirements.
