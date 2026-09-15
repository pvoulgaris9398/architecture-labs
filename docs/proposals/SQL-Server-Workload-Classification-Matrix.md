# SQL Server Workload Classification and Modernization Matrix

## Purpose

Use this matrix to analyze SQL Server workloads individually before recommending modernization. A single SQL Server estate may support several workload types with materially different requirements. Classification helps distinguish where SQL Server remains a strong fit, where incremental improvement is sufficient, and where another platform may deserve evaluation.

The classification is a starting point, not a migration decision. A workload should move only when measured constraints and business value justify the additional technology, operational burden, and migration risk.

## Workload classification matrix

| Workload type | Primary purpose | Important qualities | SQL Server strengths | Common warning signs | Potential options to evaluate |
| --- | --- | --- | --- | --- | --- |
| **System of record** | Maintain authoritative business state | Correctness, durability, transactional consistency, constraints, recovery, security, and auditability | Mature ACID transactions, relational integrity, backup and recovery, security controls, operational tooling, and broad organizational familiarity | Unclear ownership; business rules embedded inconsistently across applications, triggers, and stored procedures; update-all behavior; weak audit history; tightly coupled integrations | Retain and improve SQL Server; clarify ownership boundaries; introduce domain-oriented services; use targeted updates and optimistic concurrency; strengthen auditing, temporal history, backup testing, and recovery objectives; separate non-authoritative workloads |
| **Transient cache** | Provide fast access to data that can be recomputed or reloaded | Low latency, high throughput, bounded staleness, expiration, eviction behavior, and affordable failure | Familiar operations, rich querying, and acceptable performance at modest scale | Large volumes of disposable data; cleanup jobs for expiration; excessive polling; application-managed sharding; SQL contention caused by cache traffic; strict tail-latency requirements | Improve indexes and cache policy first; add in-process caching; evaluate Redis or DragonflyDB; define time-to-live and invalidation semantics; test cache-aside or write-through patterns; confirm behavior during cache loss and warming |
| **Workflow state** | Track durable progress through multi-step business or technical processes | Restartability, idempotency, explicit transitions, retries, leases, timeouts, approvals, and operational visibility | Durable transactional state, control tables, set-based claiming, and straightforward inspection and reporting | Status flags with undocumented meanings; hidden state in SQL Agent history; duplicate processing; manual restart procedures; unbounded retries; jobs coupled through schedules; no correlation identifiers | Formalize control tables as a state machine; define allowed transitions and invariants; build reusable C# worker components; evaluate Hangfire or a workflow engine only when requirements justify it; add retry policy, dead-letter handling, correlation, and replay tooling |
| **Reporting model** | Support operational reporting, historical analysis, and business intelligence | Query performance, freshness, lineage, reproducibility, semantic consistency, and historical retention | Strong SQL ecosystem, views, stored procedures, SQL Server Reporting Services, indexing, columnstore, and familiar reporting tools | Reports contend with transactions; sprawling stored procedures; duplicated calculations; unclear definitions; direct access to operational schemas; nightly windows continually expand | Tune queries and indexes; establish governed views or semantic models; use read replicas or a separate reporting database; evaluate dimensional models, columnstore, Power BI, or a warehouse; introduce explicit freshness and lineage expectations |
| **Integration staging** | Receive, validate, reconcile, transform, and hand off external data | Isolation, schema validation, traceability, replay, restartability, quarantine, reconciliation, and retention policy | Excellent set-based validation and transformation, bulk loading, transactional promotion, and inspection of rejected data | Staging becomes an accidental system of record; files are overwritten; no batch identity or lineage; destructive reloads; linked-server coupling; manual reconciliation; unclear cleanup rules | Preserve immutable source files in object storage; add batch and source identifiers; separate landing, validation, and promotion stages; introduce queues for event-driven handoff where useful; automate reconciliation; make every stage restartable and idempotent |

## Cross-cutting assessment axes

Apply the following questions to every identified workload. Record current evidence, the required target state, and any uncertainty rather than assigning a technology prematurely.

| Assessment axis | Questions to answer | Why it affects the decision |
| --- | --- | --- |
| **Business role and ownership** | Is the data authoritative, derived, cached, staged, or workflow metadata? Which business capability owns it? | Determines boundaries, permissible data loss, and which component is allowed to change state. |
| **Criticality** | What business process stops if it fails? What are the recovery time and recovery point objectives? | Establishes the required resilience, support model, and migration safeguards. |
| **Consistency and transactions** | Must changes be immediately consistent? Which fields or records must change atomically? | Separates transactional requirements from workloads that can accept eventual consistency. |
| **Read and write shape** | What are the volumes, ratios, key access patterns, query complexity, hot spots, batch sizes, and concurrency levels? | Provides evidence for indexing, partitioning, caching, batching, or a different storage model. |
| **Latency and freshness** | Which percentiles matter? How stale may the data be? Are requirements different for users, reports, and integrations? | Prevents vague goals such as "real time" or "faster" from driving architecture. |
| **Retention and expiration** | How long must data remain available? Is deletion regulatory, operational, or merely cache eviction? | Influences storage layout, archival, time-to-live support, and cleanup design. |
| **Failure and recovery** | What happens after partial completion, restart, dependency failure, timeout, or duplicate delivery? | Drives idempotency, checkpoints, retries, compensation, replay, and operator tooling. |
| **Audit and security** | Who changed what and why? Which regulatory, privacy, entitlement, and segregation-of-duty controls apply? | May outweigh performance or convenience and constrain platform choices. |
| **Batch and real-time interaction** | Can scheduled and event-driven updates overlap? Which source wins when they conflict? | Exposes stale overwrites, race conditions, collision windows, and reconciliation needs. |
| **Coupling and dependencies** | Which applications, jobs, stored procedures, reports, linked servers, files, and vendors depend on the workload? | Determines blast radius, sequencing, contract boundaries, and feasible strangler points. |
| **Observability** | Can a transaction or batch be traced end to end? Are metrics, structured logs, audit events, and alerts actionable? | Makes current behavior measurable and permits safe comparison during change. |
| **Operations and skills** | Who deploys, patches, monitors, restores, and troubleshoots it? Is the expertise sustainable? | A technically attractive platform can still be a net loss if it adds unsupported operational complexity. |
| **Cost and licensing** | What are the software, infrastructure, support, engineering, and opportunity costs? | Enables comparison of total cost rather than focusing only on licenses or hardware. |
| **Migration safety** | Can old and new paths run together? How will parity be measured? What is the rollback path? | Favors reversible migration over high-risk cutovers or speculative rewrites. |

## Suggested discovery record

Create one record for each material workload rather than one record per server or database.

| Field | Example content |
| --- | --- |
| Workload name | Nightly portfolio position import |
| Business capability and owner | Investment accounting; Operations |
| Classification | Integration staging plus workflow state |
| Authoritative source and destination | Vendor file to internal accounting system |
| Trigger and frequency | File arrival; nightly |
| Inputs and outputs | File format, tables, reports, downstream messages |
| Volume and timing | Rows, bytes, normal duration, peak duration, processing window |
| Dependencies | SQL Agent jobs, stored procedures, shares, linked servers, vendors, applications |
| Required guarantees | No silent loss, restartable, idempotent, fully auditable |
| Current failure and recovery behavior | Manual rerun from step three; duplicate handling uncertain |
| Observability | Batch ID and job history exist; no end-to-end correlation |
| Known pain or risk | Window growth and manual reconciliation |
| Evidence still needed | Three months of timings, failure history, row counts, support effort |
| Candidate next experiment | Instrument one run and document state transitions before redesigning |

## Potential next steps to evaluate

### Near term: understand and stabilize

1. Inventory material workloads by business capability, not merely by server, database, or SQL Agent job.
2. Classify each workload; allow multiple classifications when a pipeline crosses boundaries.
3. Map dependencies from trigger through stored procedures, tables, files, reports, applications, and downstream consumers.
4. Capture baseline evidence: duration, throughput, row counts, latency percentiles where relevant, failure rates, retry behavior, support effort, and resource contention.
5. Identify authoritative data ownership and document collision rules between batch and real-time updates.
6. Put database objects, SQL Agent definitions, configuration, and deployment procedures under source control where practical.
7. Add stable batch, workflow, and correlation identifiers so processing can be traced end to end.

### Medium term: create seams and reduce risk

1. Separate orchestration from business and data-manipulation logic. Keep reusable operations callable from SQL Agent, a C# worker, Hangfire, or another host.
2. Formalize workflow control tables with explicit states, permitted transitions, leases, retry limits, checkpoints, and terminal failure handling.
3. Make file and batch pipelines restartable and idempotent; preserve immutable inputs and quarantined failures.
4. Isolate reporting pressure using governed views, indexes, columnstore, replicas, or a reporting database before selecting a larger analytics platform.
5. Identify transient data masquerading as durable relational state and define its expiration, invalidation, recovery, and warming requirements.
6. Replace linked-server and schedule coupling incrementally with explicit contracts where the operational benefit is measurable.
7. Standardize structured logging, metrics, audit events, and correlation across the execution hosts.

### Longer term: run targeted modernization experiments

1. Select one bounded, reversible workload with clear pain and measurable success criteria.
2. Test the candidate architecture against a production-shaped workload rather than vendor benchmarks.
3. Use shadow execution, dual reads, or parallel calculations to measure semantic parity without changing the authoritative path.
4. Roll out gradually with feature flags or controlled cohorts and retain an explicit rollback path.
5. Consider specialized platforms only for demonstrated needs: Redis or DragonflyDB for high-throughput transient caching; queues or streams for decoupled delivery; object storage for immutable landing data; workflow engines for genuinely complex long-running coordination; analytical stores for reporting at scale.
6. Retire the old path only after correctness, performance, recovery, operational ownership, and business outcomes have been demonstrated.
7. Record consequential platform decisions and rejected alternatives in Architecture Decision Records.

## Decision principles

- Modernize workloads, not product logos.
- Classification does not imply extraction.
- Keep SQL Server where it satisfies the required guarantees at acceptable cost and complexity.
- Prefer the smallest intervention that resolves a measured constraint.
- Treat operational ownership and recoverability as architectural requirements.
- Design migrations to be observable, comparable, reversible, and incremental.
- Introduce a new platform only when its benefits outweigh the permanent cost of another moving part.

## Initial outcome categories

Each workload assessment should conclude with one provisional outcome:

| Outcome | Meaning |
| --- | --- |
| **Retain** | SQL Server is an appropriate fit and no material change is justified. |
| **Improve in place** | Schema, indexing, queries, deployment, observability, recovery, or ownership should be strengthened. |
| **Isolate** | Keep the workload on SQL Server but reduce contention or coupling through schemas, databases, replicas, contracts, or execution boundaries. |
| **Experiment** | Evidence suggests another pattern or platform may help; conduct a bounded production-shaped test. |
| **Migrate incrementally** | The alternative has demonstrated sufficient value; use shadowing, parity checks, staged traffic, and rollback. |
| **Retire** | The workload, duplication, or legacy path no longer provides business value and can be safely removed. |

