# Table Scan vs Index Load Test

This scenario demonstrates the performance impact of a missing index under concurrent load.
It uses a 500k-row `Orders` table and two k6 runs — one with a full table scan, one with a
non-clustered index — so you can directly compare latency and connection pool behavior in Grafana.

## Prerequisites

- Docker Desktop running
- k6 installed (`winget install grafana.k6`)
- The full stack started: `docker compose up --build -d`

The `--build` flag is required on first run (or after any code change) so the API image is
rebuilt with the seed script included. On startup, the API automatically creates `LoadTestDb`
and seeds the `Orders` table with 500,000 rows. This takes around 20–30 seconds — watch the
API container logs if you want to confirm it's done before running the test.

```bash
docker compose logs -f api-service
```

Look for: `Database seeding complete.`

## Run 1: Baseline (no index, full table scan)

The `Orders` table has no index on `CustomerId` at this point. Every request causes SQL Server
to scan all 500k rows to find the matching orders.

```bash
K6_TEST_ID=scan-without-index-20260802 bash run-k6.sh load-test-scan.js
```

While the test is running, open Grafana at http://localhost:3000 and sign in using the
credentials configured in `.env`. Watch:

- **Active Execution Latency (P95)** — expect this to climb significantly, likely well above 500ms
- **Connection Pool: Active vs Free vs Stasis** — Free connections will drop toward zero; Stasis
  will rise as requests queue waiting for a pool connection
- **Physical Connection Churn** — hard connects may spike if the pool is fully exhausted

You can also confirm the scan is happening in SQL Server while the test is running:

```bash
docker compose exec db-server /bin/bash -c '/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C -Q "SELECT session_id, wait_type, wait_time, status FROM sys.dm_exec_requests WHERE session_id > 50 AND wait_type IS NOT NULL ORDER BY wait_time DESC"'
```

Expect to see many sessions in `PAGEIOLATCH_SH` or `SOS_SCHEDULER_YIELD` — indicators of IO
pressure from reading data pages for each scan.

Record (or screenshot) the k6 summary and Grafana panels before moving to Run 2.

## Add the index

Between runs, add the non-clustered index via the management endpoint:

```bash
curl -X POST http://127.0.0.1:18080/v1/add-index
```

Expected response:
```json
{ "status": "Index created", "index": "IX_Orders_CustomerId" }
```

The index covers `CustomerId` and includes `OrderDate`, `Status`, and `Amount` so SQL Server
can satisfy the query entirely from the index without a key lookup back to the base table.

## Run 2: With index

```bash
K6_TEST_ID=scan-with-index-20260802 bash run-k6.sh load-test-scan.js
```

Both runs hit the same query — the only difference is the presence of the index. In Grafana:

- **Active Execution Latency (P95)** — should drop dramatically, typically by 10–50x
- **Connection Pool** — Free connections stay healthy; Stasis stays near zero because connections
  are returned quickly once queries complete in microseconds instead of milliseconds
- **Physical Connection Churn** — hard connects should be absent or minimal

The explicit IDs appear in k6 Prometheus metrics, API logs, and traces. Use them in Seq to keep
the two evidence windows distinct. A failed request also prints its request and trace identifiers
in the k6 terminal, with bounded output to avoid flooding the load generator.

## Resetting between runs

To drop the index and repeat the baseline:

```bash
curl -X POST http://127.0.0.1:18080/v1/drop-index
```

To fully reset the database (drop and re-seed), restart the stack:

```bash
docker compose down
docker compose up --build -d
```

## Endpoints reference

| Method | Endpoint              | Description                                      |
|--------|-----------------------|--------------------------------------------------|
| GET    | /v1/orders/by-customer | Query by CustomerId; execution depends on index state |
| POST   | /v1/add-index          | Creates IX_Orders_CustomerId                          |
| POST   | /v1/drop-index         | Drops IX_Orders_CustomerId                            |

## What to observe

The core insight this scenario illustrates is that **slow queries hold connections longer**.
With a table scan taking ~200ms per query and 200 virtual users hitting the endpoint
concurrently, the pool saturates quickly and requests start queuing. This compounds — queued
requests wait longer, which means they hold connections longer once they get one, which makes
the queue worse.

Adding the index collapses query time to sub-millisecond range. Connections are returned to the
pool almost immediately, the pool stays well-stocked with free connections, and throughput
increases dramatically without changing pool size or any application configuration.
