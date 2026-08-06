# Seq logs and traces

## Role and license

Seq is this lab's persisted investigation backend for structured application logs and sampled
OpenTelemetry traces. Prometheus and Grafana remain the metrics path. The Collector is the routing
boundary, so the API is not coupled to Seq-specific instrumentation.

Seq does not publish an LTS channel. `datalust/seq:2026.1.17004-x64` was the latest stable production
image when this step was implemented on August 2, 2026, and is pinned deliberately. Seq is
proprietary software. Compose explicitly sets `ACCEPT_EULA=Y`; starting the service constitutes
acceptance of the [Seq EULA](https://datalust.co/doc/eula-current.pdf). The free Individual license
permits one person to access the web interface and currently has a 50 GB license storage limit.
This lab's tighter operational target is 5 GiB.

## First start and retention

Configure and start the shared stack from the repository root:

```bash
cd shared/observability
cp .env.example .env
docker compose up -d --build
curl --fail http://127.0.0.1:5341/health
```

Open `http://127.0.0.1:5341` and sign in using `SEQ_ADMIN_USER` and `SEQ_ADMIN_PASSWORD` from
`shared/observability/.env`. The
`SEQ_FIRSTRUN_*` values are applied only when `seq_data` is initialized; changing them later does
not change an existing account password.

Seq retention policies are stored application objects, not Docker environment settings. On the
first start, open **Data > Storage > Retention** and create an **All events** policy that deletes
events after **14 days**. This is the hard time cap in the lab's retention convention and is the
least expensive policy for Seq to process. Confirm the policy appears before running a load test.
The more selective 24-hour, three-day, and seven-day tiers in `observability-conventions.md` are a
Step 9 refinement; the all-events policy supplies the initial hard bound.

The `seq_data` volume persists events, traces, the admin account, and the retention policy across
ordinary `docker compose down` and container recreation. The Compose volume is not a byte quota.
Stop generating load and inspect storage if it approaches the 5 GiB lab target.

## Signal path and failure behavior

```text
.NET API -- OTLP/HTTP --> Collector -- OTLP/HTTP --> Seq
    |
    +-- /metrics --> Prometheus --> Grafana
```

The Collector sends logs and sampled traces to `http://seq:5341/ingest/otlp`. Its queue holds at
most 1,024 batches and retries network failures with exponential backoff for at most five minutes.
When Seq remains unavailable, telemetry can be dropped instead of blocking API requests or growing
memory without limit. Prometheus scraping and API request handling remain independent.

The Seq host mapping binds the UI/API only on `127.0.0.1:5341`; the Collector uses Seq's internal
ingestion-only port 5341. This unauthenticated internal ingestion path is acceptable only on this
isolated local observability network. It is not a production security design.

## Verify logs, traces, and correlation

Generate a deterministic 404, which creates an error log and a trace retained by tail sampling:

```bash
curl -i \
  -H 'X-Request-ID: seq-check-1' \
  -H 'X-Test-ID: correlation-check' \
  -H 'X-Test-Scenario: connection-pool' \
  http://127.0.0.1:18080/v1/not-found
```

Copy `X-Trace-ID` from the response. In Seq, search events with:

```sql
@TraceId = 'paste-the-32-character-trace-id-here'
```

The request log should expose structured request, test, route, status, and duration properties.
Open its **Trace** menu to inspect the server span and any child SQL client span. For a database
endpoint, verify the HTTP span is the parent of the SQL operation and that the trace shows route,
HTTP status, SQL operation, durations, and error status without credentials, connection strings,
request bodies, SQL parameter values, or customer data.

Useful searches include:

```sql
@Resource['service.name'] = 'PoolMonitoringApi'
test_id = 'correlation-check'
request_id = 'seq-check-1'
@Level in ['Error', 'Fatal']
```

Combine filters with `and` and constrain the time picker to the test window. OTLP-native trace and
span identifiers are `@TraceId` and `@SpanId`; application scope fields remain snake_case.

## Safe shutdown and reset

Routine shutdown preserves Seq data:

```bash
docker compose -f ../../shared/observability/docker-compose.yaml \
  --env-file ../../shared/observability/.env down
```

Resetting Seq is destructive. Follow the exact volume inspection and removal procedure in
`observability-conventions.md`; never use `docker compose down --volumes` as routine cleanup.

## References

- [Seq Docker deployment](https://docs.datalust.co/docs/docker-deployment-overview)
- [Seq OpenTelemetry ingestion](https://docs.datalust.co/docs/ingestion-with-opentelemetry)
- [Seq retention policies](https://docs.datalust.co/docs/retention-policies)
- [Seq pricing and Individual license](https://datalust.co/Pricing)
