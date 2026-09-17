# Tinybird as an Analytical Serving Layer

**Status:** Proposed architecture-lab experiment  
**Date:** 2026-09-16  
**Decision horizon:** Future evaluation; not a near-term migration dependency

## Executive summary

Tinybird is a managed ClickHouse platform that combines analytical storage with streaming and batch ingestion, SQL transformations, materialized views, authenticated application programming interface (API) endpoints, and a source-controlled development workflow.

It may be valuable to [firm] as a specialized **analytical serving layer** for operational telemetry, historical reporting, and low-latency analytical APIs. It should not own authoritative accounting data, workflow state, approvals, corrections, or operational reconciliation.

The recommendation is to retain Tinybird as a future experiment candidate. Evaluation should occur only after the SQL Server 2022 migration, estate inventory, and nightly-cycle stabilization establish a measured analytical problem that ordinary SQL Server improvements do not solve adequately.

## Proposed decision

> Consider Tinybird for reporting acceleration, operational analytics, and read-only analytical APIs. Explicitly exclude it from systems of record, workflow orchestration, and authoritative reconciliation.

Do not introduce Tinybird merely to modernize a product logo. It must demonstrate materially better delivery speed, performance, operability, or isolation than SQL Server 2022 and the existing C# platform at an acceptable level of cost and complexity.

## Workload classification

| Workload type | Fit | Assessment |
| --- | --- | --- |
| System of record | Poor | Do not use for accounting records, tax workflow truth, approvals, corrections, or authoritative business state. |
| Workflow state | Poor | Tinybird is not a durable workflow engine. SQL Server control tables and reusable C# operations remain the preferred boundary. |
| Transient cache | Conditional | Suitable for analytical query acceleration, but not as a general mutable application cache. |
| Reporting model | Strong | Best potential fit: historical facts, aggregations, dashboards, and low-latency analytical endpoints. |
| Integration staging | Conditional | Useful for append-only events, telemetry, and analytical history; less suitable for staging that requires transactional updates, approval, or operational recovery. |

## Candidate use cases

### Nightly-cycle operational analytics

Publish immutable lifecycle events such as:

- `CycleStarted`
- `StepStarted`
- `FileReceived`
- `RowsValidated`
- `RowsRejected`
- `ReconciliationFailed`
- `StepCompleted`
- `CyclePublished`

Tinybird could then serve dashboards and APIs showing cycle duration, current status, row counts, rejection trends, retries, reconciliation failures, and downstream freshness.

The existing control tables would remain the authoritative workflow state. Tinybird would contain an independently rebuildable analytical projection.

### Reporting isolation

If measurement shows that historical reporting creates material contention, latency, or maintenance cost in SQL Server, Tinybird could receive canonical reporting facts and provide a read-optimized model for Power BI or other consumers.

The intended flow is:

1. Operational systems remain authoritative.
2. SQL Server staging and controls validate and reconcile incoming data.
3. Stable, business-meaningful reporting facts or events are published.
4. Tinybird stores analytical projections and aggregations.
5. Dashboards and applications consume read-only APIs or query interfaces.

Avoid indiscriminate replication of operational tables. Published data contracts should express business meaning and preserve source identifiers, business dates, cycle identifiers, and lineage.

### Embedded analytical APIs

Tinybird Pipes can expose parameterized query results as authenticated REST endpoints. This may suit future internal applications needing fast historical or aggregate views without embedding unrestricted analytical SQL in each application.

Potential examples include account activity history, exception trends, tax-processing progress, cycle status, and data-quality drill-downs.

## Architectural strengths

- Managed ClickHouse avoids operating a separate analytical cluster.
- Data sources, transformations, endpoints, and connections can be defined as code.
- Local environments, fixtures, branches, and Continuous Integration/Continuous Delivery support repeatable change.
- Materialized views and columnar storage suit large, append-oriented analytical workloads.
- Published endpoints provide a stable, controlled boundary for applications.
- Quarantine data sources help identify records that do not match the ingestion schema.
- Analytical workloads can be isolated from transactional SQL Server workloads.

## Concerns and constraints

### SQL Server ingestion

Tinybird's documented first-class ingestion paths emphasize HTTP events, Kafka, files, Amazon S3, Google Cloud Storage, and selected connectors. A first-class SQL Server Change Data Capture connector was not identified during this assessment.

[firm] would therefore need to introduce and operate an ingestion path such as a C# publisher, controlled file export, or separate Change Data Capture and streaming layer. For a small team, that additional machinery could outweigh the benefits.

### Unproven need for ClickHouse-scale analytics

Before adopting another platform, evaluate whether SQL Server 2022 can meet the requirement through a separated reporting model, appropriate indexing, columnstore indexes, partitioning, incremental processing, and Power BI refresh strategies.

Tinybird should solve a demonstrated workload problem, not an anticipated scale problem.

### Vendor coupling

ClickHouse provides some portability, but Tinybird Pipes, endpoints, branches, tokens, deployment behavior, and operational tooling are platform-specific.

Mitigations should include:

- Keeping canonical schemas and contracts platform-independent.
- Retaining replayable source data.
- Restricting Tinybird-specific logic to analytical projection and serving concerns.
- Keeping authoritative business logic in portable C# or appropriately governed SQL.
- Placing [firm]-owned interfaces in front of Tinybird where the exit cost justifies them.

### Security, networking, and cost

Shared infrastructure may not satisfy [firm]'s security or private-networking requirements, while dedicated Enterprise infrastructure may be disproportionate to the workload. Enterprise pricing includes multiple consumption dimensions and contractual commitments.

Any production recommendation requires a security review, data-classification review, network design, support assessment, and representative cost model.

## Proposed experiment

### Hypothesis

Tinybird can provide a low-maintenance, source-controlled analytical projection of nightly-cycle telemetry with substantially faster query response and less implementation effort than building and operating an equivalent reporting service on the existing platform.

### Scope

- Use synthetic, sanitized, or otherwise approved cycle telemetry.
- Publish append-only lifecycle events through a small C# adapter.
- Build a limited set of Pipes, materialized views, and read-only endpoints.
- Create one operational dashboard or thin proof-of-concept client.
- Preserve the original event stream so the projection can be rebuilt elsewhere.
- Do not place business-critical workflow control or authoritative records in Tinybird.

### Comparison baseline

Implement or estimate the equivalent design using SQL Server 2022 with a reporting schema and the same canonical event contract. Compare the platforms against the same representative dataset and query set.

### Evaluation criteria

| Dimension | Evidence to collect |
| --- | --- |
| Delivery effort | Time to define schemas, ingest events, build queries, test, deploy, and expose endpoints |
| Query performance | Median and tail latency across representative time ranges and concurrency levels |
| Ingestion behavior | Throughput, end-to-end lag, batching behavior, throttling, and backpressure |
| Correctness | Source-to-projection counts, totals, business dates, duplicates, and reconciliation results |
| Failure recovery | Retry behavior, idempotency, replay, malformed-record handling, and rebuild procedure |
| Operability | Monitoring, alerting, deployment safety, incident diagnosis, and support burden |
| Security | Authentication, authorization, token scope, networking, auditability, and data residency |
| Cost | Steady-state, peak ingestion, storage, egress, support, and engineering effort |
| Portability | Ability to reproduce the projection and APIs using retained contracts and source data |

### Success conditions

- Demonstrably reduces analytical query latency or SQL Server reporting pressure.
- Requires little recurring operational attention from the [firm] team.
- Provides predictable reconciliation and replay behavior.
- Supports source-controlled, testable, and reviewable change.
- Meets security and networking requirements at a proportionate cost.
- Offers enough delivery advantage to justify an additional production platform.

### Rejection or exit conditions

- SQL Server 2022 meets the measured requirement with materially less complexity.
- SQL Server ingestion requires a disproportionate streaming or Change Data Capture platform.
- Reconciliation or replay cannot be made deterministic and observable.
- Enterprise networking, security, or contractual requirements make the economics unattractive.
- Tinybird-specific logic spreads into authoritative business rules or workflow control.
- The platform creates unacceptable dependency on specialist knowledge for a small team.

## Recommended sequence

1. Complete the SQL Server migration and workload inventory.
2. Establish cycle, step, correlation, and business-date identifiers.
3. Add structured telemetry, reconciliation evidence, and performance baselines.
4. Isolate reporting pressure using conventional SQL Server techniques.
5. Select a bounded problem only if a measurable gap remains.
6. Run the Tinybird experiment against an explicit SQL Server baseline.
7. Record the result in an Architecture Decision Record before any production adoption.

## Conclusion

Tinybird is technically credible and potentially valuable, particularly where append-oriented operational data must support fast historical queries or application-facing analytical APIs. Its managed ClickHouse foundation and development workflow are attractive for a small team that does not want to operate analytical infrastructure directly.

The principal risks are adding a platform before scale justifies it, creating a non-native SQL Server ingestion path, and allowing an analytical projection to drift into operational authority. A deliberately narrow telemetry experiment provides useful evidence while remaining reversible.

**Current recommendation:** watch, learn, and retain as a bounded lab candidate; do not make it part of the near-term [firm] modernization critical path.

## References

- [Tinybird core concepts](https://www.tinybird.co/docs/forward/core-concepts)
- [Tinybird quickstarts and platform comparison](https://www.tinybird.co/docs/forward/quickstarts)
- [API endpoints](https://www.tinybird.co/docs/forward/query-data/api-endpoints)
- [Local development](https://www.tinybird.co/docs/forward/development-workflow/local-development)
- [Kafka connector](https://www.tinybird.co/docs/forward/ingest-data/connectors/kafka)
- [Platform limits](https://www.tinybird.co/docs/forward/pricing/limits)
- [Pricing plans](https://www.tinybird.co/docs/forward/pricing)
- [Enterprise pricing](https://www.tinybird.co/docs/forward/pricing/enterprise)
- [Grafana integration](https://www.tinybird.co/docs/forward/guides/connect-grafana)
