# Workflow and Batch Modernization Framework

## Status

- **Type:** Architecture framework
- **Scope:** Scheduled jobs, batch processes, file-driven integrations, and multi-step workflows
- **Horizon:** Incremental modernization
- **Disposition:** Use for discovery and proposal evaluation; do not presume a target orchestrator

## Executive Summary

[The firm] should modernize workflow and batch processing by separating business operations, durable workflow state, execution hosting, and operational telemetry.

The objective is not to replace SQL Server Agent reflexively. It is to make workflows observable, restartable, idempotent, source-controlled, and portable enough that an appropriate scheduler or orchestrator can execute them without owning their business logic.

The recommended direction is:

1. Inventory and classify existing workloads.
2. Extract reusable business operations from scheduler-specific definitions.
3. Represent workflow execution and dependencies explicitly.
4. Standardize idempotency, checkpointing, audit, and recovery.
5. Introduce alternative hosting only where it produces measurable value.

## Problem Context

Long-lived batch estates often accumulate logic across:

- SQL Server Agent job steps;
- stored procedures;
- command scripts;
- file polling and transfer utilities;
- application executables;
- linked servers;
- reporting processes;
- manually coordinated recovery steps.

Each mechanism may be reasonable individually. The architectural problem emerges when business logic, scheduling, dependency management, error handling, and audit behavior become inseparable.

Common symptoms include:

- dependencies encoded only through schedules or operator knowledge;
- jobs that must restart from the beginning after partial failure;
- duplicate processing after retries;
- insufficient evidence of what changed and why;
- inconsistent logging and notification;
- difficult local testing;
- production-only behavior;
- job definitions not managed with application and database source;
- scheduler replacement treated as the modernization rather than the execution model.

## Architectural Principles

### Scheduling is not business logic

A scheduler determines when work should be attempted. The underlying operation should remain callable and testable independently of SQL Server Agent, Hangfire, a Windows service, a command-line host, or another orchestration platform.

### Durable state is explicit

Workflow progress, attempts, checkpoints, dependencies, and outcomes should be stored as queryable state rather than inferred from log text or job history.

### Retry requires idempotency

A retry policy is unsafe unless the operation can detect previously completed effects or apply them repeatedly without producing an incorrect result.

### Recovery is designed with the happy path

Every material step should define how it resumes, compensates, or is safely rerun after interruption.

### Audit and diagnostic telemetry are distinct

Business audit records establish what business operation occurred, on whose authority, against which inputs, and with what outcome. Diagnostic logs, metrics, traces, and Extended Events explain system behavior. One should not be expected to substitute for the other.

### Source control is the system of record for definitions

Job definitions, stored procedures, schemas, configuration templates, deployment scripts, and reusable execution components should be versioned and promoted through controlled delivery processes.

### Prefer portable operations, not universal orchestration abstractions

Reusable components should expose stable operation boundaries and consistent execution contracts. [The firm] should avoid building a generalized orchestration framework unless multiple demonstrated use cases justify it.

## Workload Classification

Before changing a process, classify both its business role and execution characteristics.

### Business role

| Role | Primary concern |
| --- | --- |
| System of record | Transactional correctness, consistency, and auditability |
| Workflow state | Durable progress, dependencies, retry, and human intervention |
| Integration staging | Validation, reconciliation, replay, and source traceability |
| Reporting model | Reproducibility, freshness, lineage, and query performance |
| Transient cache | Freshness, replacement, and graceful degradation |

### Execution characteristics

- scheduled, event-driven, file-driven, or manually initiated;
- single-step or multi-step;
- short-running or long-running;
- transactional or eventually consistent;
- serial or parallel;
- internal or dependent on external systems;
- fully automated or requiring approval;
- safely repeatable or sensitive to duplication;
- bounded or dependent on an open-ended data stream.

The classification should drive the design. A nightly report refresh, securities master import, tax workflow, and cache rebuild should not inherit identical orchestration semantics merely because each currently runs as a job.

## Logical Architecture

### Operation layer

The operation layer contains reusable business capabilities such as:

- validate an inbound file;
- import a bounded data set;
- reconcile source and target records;
- generate a report;
- publish an outbound file;
- invoke an external interface;
- apply a business-effective update.

Operations should have typed inputs and outputs where practical, explicit error categories, and independently testable behavior.

### Workflow layer

The workflow layer coordinates operations and owns:

- dependencies;
- state transitions;
- checkpoints;
- retry and timeout policy;
- concurrency controls;
- approval and intervention points;
- compensation or recovery rules;
- final disposition.

### Hosting layer

The hosting layer executes workflow work. Possible hosts include:

- SQL Server Agent;
- a C# worker or Windows service;
- Hangfire;
- a cloud scheduler or managed workflow service;
- a command-line application invoked by an enterprise scheduler.

The host should be selected per workload. A single mandated host is unnecessary if operations and execution contracts are consistent.

### Persistence layer

The persistence layer contains business data, workflow state, audit records, and idempotency evidence. These concerns may share a database but should remain conceptually distinct.

### Observability layer

The observability layer provides structured logs, metrics, traces, alerts, dashboards, and diagnostic data across the entire execution path.

## Control Tables as a Workflow State Machine

Control tables can provide an effective durable coordination mechanism when their responsibilities and transitions are explicit.

Representative entities include:

| Entity | Purpose |
| --- | --- |
| Workflow definition | Identifies a versioned workflow type |
| Workflow run | Records one end-to-end execution |
| Step run | Records an operation attempt and outcome |
| Dependency | Describes prerequisite relationships |
| Checkpoint | Records durable progress within a large operation |
| Work item | Represents a bounded unit eligible for processing |
| Audit event | Records a material business or operator action |

### Suggested workflow states

- `Pending`
- `Ready`
- `Running`
- `Waiting`
- `Succeeded`
- `FailedRetryable`
- `FailedTerminal`
- `Cancelled`
- `RequiresIntervention`

Transitions should be validated rather than inferred from arbitrary status text. Each transition should record its timestamp, actor or component, attempt number, and reason.

### Concurrency control

Workers should claim eligible work atomically. Depending on the platform, this may use:

- row-version checks;
- transactional updates with locking hints;
- leases with expiration and renewal;
- queue delivery and visibility timeouts;
- unique constraints protecting idempotency keys.

The implementation must account for worker termination after claiming work and before recording completion.

## Idempotency and Restartability

### Idempotency key

Every externally initiated unit of work should have a stable identity derived from an appropriate business key, source message identifier, file identity, workflow run, or operation request.

The key should prevent an accidental retry from creating a second business effect while still allowing a deliberate correction or new version to be processed.

### Checkpointing

Large workloads should record progress at a meaningful boundary rather than holding one transaction for the entire run or restarting from zero.

Checkpoint design should consider:

- source ordering and stability;
- the smallest safely repeatable unit;
- reconciliation after partial completion;
- whether source data can change during execution;
- retention and cleanup of checkpoint state.

### Side effects

Database changes, files, messages, email, and external service calls have different retry behavior. Workflows should explicitly identify side effects and use patterns such as:

- database uniqueness constraints;
- inbox and outbox records;
- compare-and-set updates;
- presence-based add or remove operations;
- business-effective timestamps;
- reconciliation before retry;
- compensating actions where true idempotency is impossible.

## Logging, Audit, and Observability

### Structured application logging

Every workflow and step should emit structured events containing, as applicable:

- workflow name and version;
- workflow-run and step-run identifiers;
- correlation and causation identifiers;
- operation name;
- attempt number;
- start and completion timestamps;
- duration;
- input reference rather than sensitive payload;
- rows read, inserted, updated, rejected, and unchanged;
- outcome and error category;
- checkpoint or recovery position.

Sensitive financial, tax, client, authentication, and personally identifiable information should not be written to logs by default.

### Business audit

Audit records should answer:

- What business action occurred?
- Which source or instruction authorized it?
- Who or what initiated and approved it?
- Which records or effective dates were affected?
- What was the result?
- Was a correction, override, or reprocessing action performed?

Audit data should have defined retention, access, and immutability requirements appropriate to the workflow.

### Metrics

Useful metrics include:

- executions and outcomes by workflow;
- duration and queue age;
- throughput;
- retry counts;
- failure categories;
- records processed and rejected;
- dependency wait time;
- manual interventions;
- recovery time;
- service-level objective attainment.

### Distributed tracing

Correlation identifiers should flow through job execution, application services, database operations, messages, files, and external calls where feasible. Traces are especially valuable when an apparent batch process crosses multiple services or integration boundaries.

### SQL Server Extended Events

Extended Events are appropriate for targeted database diagnostics, including:

- long-running or failed statements;
- blocking and deadlocks;
- execution anomalies;
- investigation of database-level behavior.

They should be enabled and retained deliberately to control overhead and data volume. Extended Events should complement structured application telemetry and durable audit records rather than replace them.

## SQL Server Agent Guidance

SQL Server Agent remains appropriate when:

- the work is predominantly database-local;
- scheduling and dependency needs are modest;
- operational ownership resides with the database platform;
- retry and recovery behavior is simple and explicit;
- the definition is source-controlled and deployed consistently.

Warning signs include:

- extensive business logic embedded in job steps;
- cross-system workflows coordinated through time offsets;
- large command scripts stored only in production;
- dependencies inferred from another job's schedule;
- manual restart instructions requiring tribal knowledge;
- limited visibility outside SQL Server tooling;
- credential or environment configuration embedded in definitions.

Agent jobs should ideally invoke versioned stored procedures or executables with explicit parameters and return well-defined outcomes.

## Reusable C# Execution Components

A small set of reusable C# building blocks may improve consistency across different hosts. Candidate capabilities include:

- operation contracts and typed results;
- workflow and correlation context;
- structured logging;
- metrics and tracing;
- retry classification;
- idempotency checks;
- checkpoint persistence;
- database transaction helpers;
- file identity and validation;
- standardized exit codes;
- secure configuration and secret access.

These components should remain libraries or thin infrastructure adapters. They should not evolve into a custom enterprise scheduler without a separately justified proposal.

## Source Control and Delivery

The following should be versioned where applicable:

- SQL Server Agent definitions;
- stored procedures and schemas;
- workflow definitions;
- executable source;
- configuration templates;
- observability and alert definitions;
- database migration scripts;
- operational runbooks;
- rollback procedures.

Deployment should support environment-specific configuration without modifying the versioned definition. Credentials and secrets should remain outside source control.

Changes should be reviewable, reproducible, and attributable. Production edits should be reconciled promptly back to source or, preferably, prevented through controlled deployment.

## Modernization Sequence

### Phase 1: Inventory

- Catalog jobs, schedules, owners, dependencies, inputs, outputs, and service expectations.
- Map stored procedures, scripts, executables, linked servers, file locations, and external systems.
- Identify manual interventions and recovery procedures.
- Record current duration, failure frequency, and operational pain.

### Phase 2: Stabilize

- Place definitions and code under source control.
- Standardize execution identifiers, status recording, and structured logging.
- Document dependencies and recovery steps.
- Address unsafe retry behavior and missing business audit.

### Phase 3: Extract operations

- Separate business operations from scheduler configuration.
- Introduce explicit inputs, outputs, and error categories.
- Add characterization and integration tests around current behavior.

### Phase 4: Make workflow state durable

- Introduce explicit run, step, dependency, and checkpoint records where justified.
- Implement safe claiming, timeout, and recovery semantics.
- Add idempotency protections around material side effects.

### Phase 5: Evaluate hosting

- Retain SQL Server Agent for suitable database-local work.
- Pilot an application host or orchestrator for workflows that need richer coordination, visibility, or recovery.
- Compare operational complexity and outcomes before broader migration.

### Phase 6: Incrementally migrate

- Select a bounded workflow with representative characteristics.
- Operate old and new paths in a controlled comparison where feasible.
- Preserve rollback and reconciliation capabilities.
- Expand only after demonstrated operational benefit.

## Evaluation Matrix

| Criterion | SQL Server Agent | Application worker | Hangfire | Managed orchestrator |
| --- | --- | --- | --- | --- |
| Database-local work | Strong | Good | Good | Variable |
| Multi-system coordination | Limited | Strong with implementation | Strong | Strong |
| Durable workflow state | Basic job history | Explicitly designed | Built-in job state | Usually built in |
| Long-running workflows | Limited | Strong with durable design | Moderate to strong | Strong |
| Human approval | Custom | Custom | Custom | Platform dependent |
| Local testing | Limited | Strong | Strong | Variable |
| Operational overhead | Low when already operated | Moderate | Moderate | Platform dependent |
| Vendor portability | SQL Server dependent | Potentially strong | Framework dependent | Often platform dependent |
| Best fit | Database administration and bounded database jobs | Domain-rich or integration-heavy workflows | Background application jobs | Complex managed workflows |

This matrix is directional. A proposal should assess the actual version, operating model, security constraints, workload, and team skills.

## Anti-Patterns

- Replacing the scheduler without changing unsafe workflow semantics.
- Using time offsets as the only dependency mechanism.
- Retrying non-idempotent operations blindly.
- Treating log text as durable workflow state.
- Recording success before all external side effects complete.
- Updating all loaded records when only a targeted subset changed.
- Hiding manual intervention outside the workflow record.
- Building a universal orchestration abstraction before multiple needs exist.
- Adopting a platform because it is modern rather than because it addresses measured constraints.
- Allowing production job definitions to diverge from source control.

## Architecture Lab Questions

For each candidate workflow, determine:

- What business outcome does the workflow produce?
- What is the authoritative source for its inputs and state?
- What constitutes one independently retryable unit?
- Which steps have irreversible or externally visible effects?
- How are duplicates detected?
- What happens if execution stops after each step or side effect?
- Which dependencies are essential and which are historical accidents?
- What evidence is required for audit and operations?
- Which recovery actions require human judgment?
- Does the current scheduler create a material limitation?
- What is the smallest change that improves reliability or supportability?

## Initial Recommendation

Begin with discovery and stabilization rather than platform replacement. Select one operationally important but bounded workflow, place all of its definitions under source control, document its dependency and recovery model, introduce consistent run and step identifiers, and verify idempotent restart behavior.

Use the results to determine whether SQL Server Agent remains sufficient, whether a reusable C# operation host would help, or whether a richer orchestrator is justified. The first success criterion should be safer and more understandable operation—not migration to a particular product.

## Conclusion

Workflow modernization is primarily the process of making state, effects, dependencies, and recovery explicit. Once those qualities exist, [the firm] can choose execution hosts based on workload fit rather than allowing scheduler limitations to define the architecture.
