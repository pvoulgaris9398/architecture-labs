# Distributed app exercise runbook

Use this runbook to verify the application's synchronous, asynchronous, and telemetry paths without
running a heavy load test. Run commands from `labs/distributed-app-example` unless a step changes
directories.

## Prerequisites

- Docker Engine and Docker Compose v2 are running.
- `shared/observability/.env` and this lab's `.env` exist and contain local values.
- `POSTGRES_HOST=postgres` in the lab `.env`.
- `OTEL_EXPORTER_OTLP_ENDPOINT=http://distributed-app-collector:4317` in the lab `.env`.
- The configured PostgreSQL username and password match the credentials stored in the existing
  volume. Changing initialization variables does not update roles in an initialized database.

## 1. Start the shared observability stack

```bash
cd ../../shared/observability
docker compose config --quiet
docker compose up -d
docker compose ps
cd ../../labs/distributed-app-example
```

Confirm `distributed-app-collector`, Prometheus, Grafana, and Jaeger are running before starting
the application. Seq and `api-load-test-collector` are also part of the shared platform even when
this lab is the only active workload.

## 2. Start the application

```bash
docker compose config --quiet
docker compose up --build -d
docker compose ps
```

The following services should be running, and services with health checks should become healthy:

- `postgres`
- `redis`
- `rabbitmq`
- `python-grpc-inventory`
- `python-rabbitmq-worker`
- `dotnet-api-gateway`
- `typescript-dashboard`

If the gateway exits, inspect its logs before continuing:

```bash
docker compose logs --tail 100 dotnet-api-gateway
```

## 3. Seed PostgreSQL

```bash
curl --fail-with-body -X POST http://127.0.0.1:5242/products/seed
```

The endpoint is idempotent. It either inserts the demonstration products or reports that data
already exists.

## 4. Exercise cache-aside reads and gRPC

Request the same product twice:

```bash
curl --fail-with-body http://127.0.0.1:5242/products/1
curl --fail-with-body http://127.0.0.1:5242/products/1
```

Expected behavior:

1. The first uncached response reports `"dataSource":"PostgreSQL Database"`.
2. The second response reports `"dataSource":"Redis Cache"`.
3. Both responses include stock information returned by the Python gRPC inventory service.

Redis data can survive ordinary container recreation because this lab mounts a named volume. If a
previous run cached product 1, both responses may report a cache hit. Use a product not previously
requested or inspect Redis before interpreting that result; do not delete the Redis volume merely
to force a cache miss without explicit approval.

## 5. Exercise asynchronous checkout

Publish an order through the gateway:

```bash
curl --fail-with-body -X POST http://127.0.0.1:5242/checkout \
  -H 'Content-Type: application/json' \
  -d '{"productId":1,"quantity":2}'
```

The API should return HTTP `202 Accepted` with an order ID. Confirm that the Python worker receives
and acknowledges the message:

```bash
docker compose logs --since 2m python-rabbitmq-worker
```

## 6. Inspect telemetry

With the example ports, use these interfaces:

| Interface | URL | What to verify |
| --- | --- | --- |
| Application dashboard | <http://127.0.0.1:5173> | Product queries and checkout work from the browser |
| Grafana | <http://127.0.0.1:3000> | The Distributed App dashboard reports telemetry traffic |
| Prometheus targets | <http://127.0.0.1:9090/targets> | Both distributed-app Collector targets are up |
| Jaeger | <http://127.0.0.1:16686> | Cross-service traces are searchable |
| RabbitMQ management | <http://127.0.0.1:15672> | The order queue and consumer are visible |

Grafana credentials come from `shared/observability/.env`; RabbitMQ credentials come from the lab
`.env`.

In Jaeger, inspect these services:

- `DotnetApiGateway`
- `PythonInventoryService`
- `PythonOrderWorker`

The product-read trace should include the .NET request and Python gRPC inventory work. Checkout
publishing and worker processing currently create separate spans; the message does not yet carry
trace context across RabbitMQ.

## 7. Generate a small telemetry sample

This loop generates enough requests to make charts easier to inspect without constituting a load
test:

```bash
for request in {1..25}; do
  curl --silent --show-error --fail --output /dev/null \
    http://127.0.0.1:5242/products/1
  sleep 0.2
done
```

Allow at least one metrics export and Prometheus scrape interval before interpreting Grafana.

## 8. Exercise queue recovery safely

Stop only the worker, publish an order, and restart the worker:

```bash
docker compose stop python-rabbitmq-worker

curl --fail-with-body -X POST http://127.0.0.1:5242/checkout \
  -H 'Content-Type: application/json' \
  -d '{"productId":1,"quantity":2}'

docker compose start python-rabbitmq-worker
docker compose logs --since 2m -f python-rabbitmq-worker
```

The checkout should be accepted while the worker is stopped, remain queued, and be processed after
the worker restarts. Press `Ctrl+C` to stop following logs; it does not stop the container.

## 9. Optional dependency-failure observations

These checks are reversible but intentionally cause request failures:

```bash
# Observe synchronous request behavior when inventory is unavailable.
docker compose stop python-grpc-inventory
curl -i http://127.0.0.1:5242/products/1
docker compose start python-grpc-inventory

# Observe cache dependency behavior.
docker compose stop redis
curl -i http://127.0.0.1:5242/products/1
docker compose start redis
```

Inspect the gateway logs, Jaeger traces, and Grafana telemetry around each failure window. Confirm
all stopped services recover before proceeding:

```bash
docker compose ps
```

## 10. Stop safely

Stop the application while preserving PostgreSQL and Redis data:

```bash
docker compose down
```

Stop the shared observability containers while preserving Prometheus, Grafana, and Seq data:

```bash
cd ../../shared/observability
docker compose down
```

Do not add `--volumes` to either command during routine use. Removing the lab volumes deletes local
application state; removing the shared volumes deletes retained telemetry for every lab.
