# Restartable ETL Pipeline Architecture Lab --- Requirements

## 1. Purpose

This architecture lab will explore the requirements and behavior of a
restartable, observable ETL pipeline composed of discrete business-level
processing steps.

The lab is intentionally small. Its purpose is to exercise and evaluate
concepts such as:

-   discrete and independently observable business processing steps;
-   partial data acceptance with step-level failure;
-   explicit restart and resume behavior;
-   record-level remediation without modifying source history;
-   immutable audit and execution history;
-   end-to-end data lineage;
-   dataset/batch identity independent of processing execution;
-   correlation of business execution with detailed SQL Server activity;
-   durable logging that survives transactional rollback; and
-   collection of both business-processing metrics and SQL Server
    resource metrics.

The initial implementation will use SQL Server stored procedures and SQL
queries only. External orchestration, user interfaces, external
telemetry exporters, and production source integrations are outside the
initial scope.

------------------------------------------------------------------------

## 2. Goals

The lab shall demonstrate that a multi-step ETL process can:

1.  Process a defined input dataset as a distinct batch.
2.  Execute a sequence of discrete business-level steps.
3.  Commit valid records even when other records in the same step are
    rejected.
4.  Prevent downstream steps from executing while unresolved errors
    remain.
5.  Preserve the exact data originally received.
6.  Permit explicit, audited remediation of supported data-quality
    problems.
7.  Resume a failed step without unnecessarily reprocessing records that
    previously succeeded.
8.  Preserve every execution and remediation attempt as immutable
    history.
9.  Trace every record through its complete processing history across
    pipeline runs and re-runs.
10. Correlate pipeline activity with detailed SQL Server execution and
    resource information.
11. Preserve critical execution and error information even when
    business-data transactions roll back.

------------------------------------------------------------------------

## 3. Non-Goals

The initial lab does not require:

-   production file ingestion;
-   production API integration;
-   scheduled or event-driven triggering;
-   a graphical operational or remediation interface;
-   distributed orchestration;
-   concurrent execution of multiple instances of the same pipeline;
-   automatic restart after failure;
-   automatic application of prior overrides to future batches;
-   a generalized data-quality rules engine;
-   downstream reconciliation workflow implementation;
-   OpenTelemetry exporters or external observability platforms; or
-   a positions or valuation-processing workflow.

These capabilities may be considered in later experiments.

------------------------------------------------------------------------

## 4. Initial Business Scenario

The lab will model a simple data-loading pipeline containing the
following business-level steps:

1.  **Load Security Master**
2.  **Load Prices**

The source data will initially be represented by SQL queries returning
mock data rows.

The mock inputs represent datasets that could eventually originate from
files, APIs, database queries, or other external systems. The pipeline
requirements shall not assume that SQL queries are the permanent source
mechanism.

The Price step depends on successful completion of the Security Master
step. A price referencing an unknown security is therefore an example of
data that cannot be successfully processed.

------------------------------------------------------------------------

## 5. Terminology

### 5.1 Pipeline

A **Pipeline** is the definition of a business processing sequence,
including its steps and dependencies.

A Pipeline shall have a stable **Pipeline ID**.

### 5.2 Pipeline Run

A **Pipeline Run** is one execution of a Pipeline.

Each Pipeline Run shall have a unique **Pipeline Run ID**.

A Pipeline Run represents processing activity and shall remain
conceptually distinct from the dataset being processed.

### 5.3 Batch

A **Batch** represents an identifiable input dataset supplied to
processing.

Each Batch shall have a unique **Batch ID**.

Batch identity shall be independent of Pipeline Run identity. This
distinction must make it possible to determine whether:

-   a new dataset is being processed; or
-   the same dataset is being processed again in another execution.

The requirements shall not assume that Batch and Pipeline Run always
have a one-to-one relationship.

### 5.4 Step

A **Step** is a meaningful business-level processing operation rather
than an individual SQL statement.

Examples in the initial lab are:

-   Load Security Master
-   Load Prices

A step owns the business logic necessary to determine what work remains
when it is resumed.

### 5.5 Step Execution

A **Step Execution** is an individual attempt to execute or resume a
Step.

Each execution attempt shall be preserved independently so that a failed
attempt is not overwritten when a later attempt succeeds.

### 5.6 Correlation ID

A **Correlation ID** is an explicitly supplied unique identifier used to
correlate technical execution activity and telemetry belonging to an
overall processing operation.

Correlation ID and Pipeline Run ID are distinct concepts even when an
initial implementation uses a one-to-one relationship between them.

------------------------------------------------------------------------

## 6. Batch Requirements

### 6.1 Batch Identity and Immutability

Every input dataset shall be represented as a distinct immutable dataset instance and shall be identifiable by Batch ID.

A Batch represents one specific acquisition or production of a dataset at a particular point in time. Once established, the Batch identity, dataset contents, and metadata describing that acquisition or production shall be immutable.

Re-acquiring or regenerating a dataset shall create a new Batch with a new Batch ID, even when the resulting business data is identical to a previously created Batch.

Content equality shall not imply Batch identity. Two Batches may contain identical dataset contents while remaining distinct historical dataset instances.

### 6.2 Batch Content Fingerprint

A Batch shall support an integrity fingerprint or checksum calculated over its immutable dataset contents.

The fingerprint shall support uses such as:

- verifying dataset integrity;
- detecting identical dataset contents across distinct Batches; and
- supporting audit, lineage, and diagnostic investigation.

A matching fingerprint shall indicate content equivalence according to the defined fingerprinting rules; it shall not cause two independently created Batches to be treated as the same Batch.

The fingerprint algorithm, canonicalization rules, and physical representation are design concerns and are not specified by these requirements. The design should preserve the conceptual distinction between Batch identity and dataset content identity.

### 6.3 Batch Metadata

The system shall support capture of metadata sufficient to describe the
dataset and its origin, including, where applicable:

-   source type, such as file, API, database query, or another source;
-   source identifier, such as filename, API dataset or endpoint name,
    or query source;
-   acquisition or receipt timestamp;
-   effective or as-of date;
-   source record count;
-   acquisition timing or duration;
-   source-provided identifiers or version information; and
-   integrity metadata such as source size or checksum when applicable.

The precise persistence model for this metadata is a design concern and
is not specified by these requirements.

### 6.4 Batch Versus Processing Metrics

Metadata describing the input Batch shall remain conceptually separate
from metrics describing what a Pipeline Run did while processing that
Batch.

------------------------------------------------------------------------

## 7. Source and Staging Requirements

### 7.1 Preservation of Received Data

The system shall preserve the data received for a Batch exactly as it
was received.

Staged source data shall serve as a historical record of:

-   what data was received;
-   which Batch supplied it; and
-   when it was received.

### 7.2 Immutable Source History

Data-quality remediation shall not modify the original staged source
data.

A correction or override shall be represented separately from the
original source record.

------------------------------------------------------------------------

## 8. Step Processing Requirements

### 8.1 Discrete Business Steps

Each business-level Step shall be independently executable and
observable.

The orchestration mechanism shall determine which Step should execute.

The Step itself shall determine what records or work remain to be
processed.

### 8.2 Partial Record Acceptance

When a Step encounters invalid data:

-   valid records shall be allowed to complete successfully;
-   invalid records shall be captured as errors;
-   successful records shall not be rolled back solely because another
    source record is invalid; and
-   the overall Step shall not be considered successful while unresolved
    data errors remain.

### 8.3 Downstream Blocking

A downstream Step shall not begin until all required predecessor Steps
have completed successfully.

For example, Load Prices shall not proceed while Load Security Master
has unresolved errors.

### 8.4 Explicit Restart

Restart or resume after a failure shall require an explicit operational
action.

The initial lab shall not automatically restart failed Steps.

### 8.5 Resume Semantics

When a failed Step is resumed:

-   the Step shall determine which work remains;
-   previously successful records shall not be unnecessarily
    reprocessed;
-   unresolved failed records shall be identified;
-   applicable remediation shall be considered;
-   affected records shall be reprocessed;
-   the retry activity shall be observable and logged;
-   the Step shall remain unsuccessful if unresolved errors remain; and
-   downstream processing may continue only after the Step succeeds.

The orchestration layer shall be able to request that a Step resume
without requiring knowledge of the Step's record-level retry logic.

### 8.6 Successful Step Re-Execution

The initial lab shall not support arbitrary re-execution of an already
successful Step.

Reprocessing a completed Step is considered a distinct operational
concern because of its potential downstream consequences.

------------------------------------------------------------------------

## 9. Data Validation Requirements

### 9.1 Supported Validation Errors

The lab shall explicitly support multiple recognized categories of
data-quality failure.

Initial examples shall include:

-   invalid or unrecognized currency;
-   invalid or unrecognized security type code; and
-   invalid CUSIP.

The architecture shall allow additional supported error and remediation
types to be introduced later.

### 9.2 Technical Validity Versus Business Correctness

The ingestion pipeline shall distinguish between data that is
structurally or reference-data invalid and data that is technically
valid but potentially incorrect from a business perspective.

For example:

-   an unrecognized currency code such as `XYZ` may be rejected during
    ingestion; while
-   a recognized currency such as `USD` may pass ingestion validation
    even if `USD` is ultimately incorrect for that particular security.

The latter class of issue shall be documented as requiring downstream
business reconciliation rather than being treated as an
ingestion-validation failure.

Implementation of that downstream reconciliation workflow is outside the
initial lab scope.

------------------------------------------------------------------------

## 10. Error and Remediation Requirements

### 10.1 Error Capture

Invalid records shall be recorded with sufficient information to
determine:

-   the affected source/staging record;
-   the Batch;
-   the Pipeline Run;
-   the Step and Step Execution;
-   the error category;
-   the rejected value or condition;
-   the time the error occurred; and
-   its remediation status.

### 10.2 Typed Remediation

The system shall support explicit remediation types rather than treating
all corrections as unrestricted arbitrary field replacement.

Initial remediation examples shall include:

-   currency correction;
-   security type correction; and
-   CUSIP correction.

### 10.3 Original Value Preservation

A remediation shall not replace or obscure the original value.

It shall be possible to determine both:

-   the value originally supplied by the source; and
-   the correction applied for processing.

### 10.4 Required Reason

Every user-entered remediation shall require a reason.

The system shall support a controlled reason value, such as a selected
reason code.

The requirements shall permit an optional explanatory comment and shall
permit the design to support an `OTHER`-style reason requiring free-text
explanation.

### 10.5 Remediation Audit Information

A remediation shall capture sufficient audit information to identify:

-   the affected record;
-   the remediation type;
-   the original condition or value;
-   the corrective action or replacement value;
-   the user responsible for the remediation;
-   the timestamp; and
-   the reason.

User identity may be mocked in the initial lab.

### 10.6 Immutable Remediation History

Remediation history shall be append-only.

An incorrect remediation shall not be updated or deleted in place.

Instead, a subsequent correcting or offsetting remediation record shall
be added so that the complete historical sequence remains visible.

### 10.7 Batch-Scoped Remediation

A remediation made for one Batch shall not automatically modify or
establish a correction rule for future Batches.

If a future Batch contains the same invalid source value, it shall be
treated as a new data-quality event unless a future design explicitly
introduces persistent remediation rules.

------------------------------------------------------------------------

## 11. Execution State Requirements

### 11.1 Pipeline Run State

At minimum, the lab shall represent the following Pipeline Run states:

-   Pending
-   Running
-   Failed
-   Succeeded

### 11.2 Step State

Steps shall support equivalent execution state sufficient to determine
whether downstream dependencies are satisfied.

### 11.3 Attempt History

Failure history shall be preserved.

If a Step Execution fails and a later resume succeeds:

-   the original execution shall remain recorded as Failed;
-   a new Step Execution shall represent the retry;
-   the successful retry shall not overwrite the failed attempt; and
-   the current Step state shall be determinable from the complete
    execution history.

------------------------------------------------------------------------

## 12. Transaction Requirements

### 12.1 Independent Business-Step Transactions

Business Steps shall establish transactional boundaries appropriate to
their processing responsibilities.

The entire Pipeline Run shall not require one encompassing transaction.

### 12.2 Valid and Invalid Record Persistence

The system shall permit successful records and data-quality error
information to persist independently as required to support partial
acceptance and remediation.

### 12.3 Durable Operational Logging

Critical execution, audit, and error information required to diagnose or
resume processing shall survive rollback of the business-data
transaction that generated the failure.

The design used to achieve this behavior is intentionally unspecified
and is a subject of the architecture lab.

------------------------------------------------------------------------

## 13. Lineage Requirements

### 13.1 End-to-End Record Lineage

The system shall provide immutable historical lineage sufficient to
trace every processed record through its complete lifecycle.

Lineage shall support tracing a record to:

-   its originating Pipeline;
-   its originating Batch;
-   its original staged source record;
-   its originating Pipeline Run;
-   subsequent Pipeline Runs that processed or affected it;
-   relevant Step Executions;
-   errors encountered;
-   remediations or overrides applied; and
-   subsequent processing that affected the record.

### 13.2 Preservation Across Re-Runs

Later processing shall not overwrite historical lineage from earlier
Pipeline Runs.

A record affected by multiple executions shall retain traceability to
all relevant executions.

### 13.3 Bidirectional Traceability

The system shall support both forward and reverse investigation.

Given a record, it shall be possible to determine its source and
processing history.

Given a Batch ID, Pipeline Run ID, or other supported execution
identity, it shall be possible to determine the records and processing
activity associated with it.

### 13.4 Implementation Independence

These requirements specify lineage capability, not a particular storage
design.

The requirements do not prescribe mutable "last processed" identifiers,
lineage tables, temporal tables, or any other particular implementation
mechanism.

------------------------------------------------------------------------

## 14. Correlation Requirements

### 14.1 Explicit Correlation ID

A Correlation ID shall be explicitly supplied to pipeline execution.

### 14.2 Correlation Propagation

The Correlation ID shall be propagated throughout processing so that
execution activity associated with the overall operation can be
correlated.

The lab shall investigate propagation of correlation context into SQL
Server execution context, including mechanisms such as session context
where appropriate.

### 14.3 Record Correlation

Individual records shall retain sufficient correlation information to
associate their processing with the appropriate execution context.

### 14.4 SQL Server Telemetry Correlation

The lab shall investigate whether the same correlation context can be
made available to SQL Server Extended Events or related diagnostic
mechanisms so that detailed SQL execution activity can be correlated
with Pipeline Run, Step Execution, and record-processing information.

The exact SQL Server mechanism is a design and experimental concern
rather than a predetermined requirement implementation.

------------------------------------------------------------------------

## 15. Logging Requirements

### 15.1 Logging Granularity

The lab shall target detailed execution observability, including
individual SQL statements executed as part of pipeline processing.

Logging and diagnostic information shall be sufficient to reconstruct
the sequence of meaningful execution events within a Step Execution.

### 15.2 Business-Level Events

At minimum, observable events shall include:

-   Pipeline Run start and completion;
-   Step start and completion;
-   Step failure;
-   Step resume/retry;
-   number of records considered;
-   number successfully processed;
-   number rejected;
-   remediation activity;
-   transition to downstream processing; and
-   terminal Pipeline Run status.

### 15.3 Retry Logging

Resume activity shall be explicitly observable.

For example, the system shall be capable of recording information
equivalent to:

> Retrying failed Security Master records.

The exact message format is not prescribed.

### 15.4 Detailed SQL Activity

The lab shall investigate SQL Server-native mechanisms for capturing
detailed SQL execution information.

Candidate mechanisms may include:

-   Extended Events;
-   Query Store;
-   SQL Server execution and resource statistics; and
-   correlation through SQL Server session context or equivalent
    facilities.

These mechanisms shall supplement rather than replace the authoritative
business execution and audit history.

------------------------------------------------------------------------

## 16. Metrics Requirements

### 16.1 Basic Execution Metrics

The system shall capture sufficient metrics to determine, at minimum:

-   start time;
-   end time;
-   duration;
-   records examined;
-   records accepted;
-   records rejected;
-   Step status;
-   Pipeline Run status; and
-   execution/retry counts.

### 16.2 Detailed SQL Resource Metrics

The lab shall investigate collection and correlation of detailed SQL
Server resource usage associated with pipeline execution.

Relevant metrics may include, where SQL Server makes them available and
useful:

-   execution duration;
-   CPU consumption;
-   logical reads;
-   physical reads;
-   writes;
-   row counts;
-   query execution counts;
-   waits or blocking information; and
-   query plan or Query Store identifiers.

The requirements do not mandate a single collection mechanism.

------------------------------------------------------------------------

## 17. Operational Requirements

### 17.1 Initial Interface

The initial lab shall be operable entirely through:

-   SQL Server stored procedures; and
-   SQL queries.

No graphical user interface is required.

### 17.2 Manual Triggering

Pipeline Runs shall initially be started manually.

Scheduling, file watchers, API triggers, and other automated initiation
mechanisms are outside the initial scope.

### 17.3 Manual Resume

Failed processing shall be explicitly resumed by an operator after
remediation.

### 17.4 Concurrency

The initial lab shall permit only one active instance of the pipeline at
a time.

An attempt to start another active instance shall fail cleanly and shall
be observable.

Concurrent pipeline execution may be evaluated as a future architecture
experiment.

------------------------------------------------------------------------

## 18. Observability Model

The lab shall distinguish among three categories of observability
information.

### 18.1 Business Execution History

Authoritative information describing:

-   Pipeline Runs;
-   Steps;
-   Step Executions;
-   processing status;
-   record counts;
-   errors;
-   retries; and
-   remediation.

### 18.2 SQL Server Diagnostic Telemetry

Detailed technical information describing SQL execution and resource
behavior.

Extended Events, Query Store, and other SQL Server-native diagnostic
facilities may be evaluated for this purpose.

### 18.3 Correlation

The system shall provide sufficient identifiers and context to correlate
business execution history with SQL Server diagnostic telemetry without
requiring either source to replace the other.

------------------------------------------------------------------------

## 19. Acceptance Scenarios

### 19.1 Successful Pipeline

Given a valid Security Master Batch:

1.  A new Pipeline Run is manually started.
2.  Load Security Master processes all records successfully.
3.  The Step is marked successful.
4.  Load Prices is permitted to execute.
5.  The Pipeline Run eventually reaches Succeeded.
6.  Records can be traced to their Batch and Pipeline Run.
7.  Business logs and relevant SQL telemetry can be correlated.

### 19.2 Data-Quality Failure

Given a Security Master Batch containing 1,000 records where one record
contains an unrecognized currency:

1.  999 valid records are processed successfully.
2.  The invalid record is captured as an error.
3.  The original staged value remains unchanged.
4.  Load Security Master is considered failed.
5.  Load Prices does not begin.
6.  The Pipeline Run reflects the failure.
7.  Diagnostic and execution history remains available.

### 19.3 Remediation and Resume

Given the preceding failed Pipeline Run:

1.  A user enters a typed currency remediation.
2.  The remediation references the affected staged record.
3.  The original invalid value remains preserved.
4.  User identity, timestamp, correction, and reason are recorded.
5.  An operator explicitly resumes Load Security Master.
6.  A new Step Execution is created.
7.  The Step identifies the unresolved record rather than unnecessarily
    reprocessing the 999 successful records.
8.  The remediation is applied for processing.
9.  The corrected record succeeds.
10. The prior failed Step Execution remains in history.
11. The new Step Execution succeeds.
12. Load Prices is now permitted to execute.

### 19.4 Incorrect Remediation

Given a previously entered remediation that is itself incorrect:

1.  The original remediation remains immutable.
2.  A new correcting or offsetting remediation is entered.
3.  The complete remediation history remains visible.
4.  Subsequent resume processing uses the applicable correction without
    destroying prior history.

### 19.5 New Batch with Repeated Source Error

Given a later Batch containing the same invalid value previously
remediated:

1.  The new Batch receives its own Batch ID.
2.  The new processing receives its own Pipeline Run ID.
3.  The previous Batch's remediation is not silently applied.
4.  The repeated invalid value is treated as a new data-quality issue.
5.  Historical lineage distinguishes the two occurrences.

### 19.6 Technically Valid but Incorrect Data

Given a security containing `USD`, where `USD` is a recognized currency
but is later determined to be incorrect for that security:

1.  The ingestion pipeline may accept the record because the value is
    technically valid.
2.  The condition is considered a downstream reconciliation concern.
3.  The lab documents the limitation without attempting to infer
    business truth during ingestion.

### 19.7 Business Transaction Rollback

Given a SQL failure requiring rollback of business-data changes:

1.  Business-data changes within the failed transactional boundary are
    rolled back.
2.  Critical execution and diagnostic information required to identify
    the failure remains available.
3.  The failed execution attempt remains part of immutable execution
    history.
4.  The system retains sufficient state to support later diagnosis and
    resume.

------------------------------------------------------------------------

## 20. Architecture Questions to Be Explored by the Lab

The requirements intentionally leave several implementation questions
unresolved.

The lab should provide evidence for subsequent design decisions
concerning:

-   how Pipeline, Batch, Run, Step, Execution, and record lineage are
    persisted;
-   how immutable lineage is represented across multiple Pipeline Runs;
-   how remediation records are associated and superseded;
-   how a Step efficiently identifies work remaining after failure;
-   how durable logging survives rollback of business transactions;
-   how SQL Server session context can propagate Correlation ID;
-   how Extended Events can consume or expose that correlation context;
-   how Query Store complements Extended Events and business execution
    logs;
-   how statement-level telemetry can be associated with Step
    Executions;
-   how much detailed telemetry is useful before observability overhead
    becomes excessive; and
-   whether SQL Server alone is sufficient for the desired orchestration
    behavior or an external orchestrator becomes justified.

These are design and experimental questions rather than requirements
assumptions.

------------------------------------------------------------------------

## 21. Success Criteria

The architecture lab will be considered successful if it demonstrates,
using the Security Master and Price workflow, that:

1.  an identifiable Batch can be processed by an identifiable Pipeline
    Run;
2.  discrete business Steps can execute with independent state;
3.  valid records can persist while invalid records block Step
    completion;
4.  invalid records can be remediated without modifying original source
    history;
5.  a failed Step can be explicitly resumed and intelligently process
    only necessary work;
6.  immutable execution, error, and remediation history is retained;
7.  every processed record can be traced through its source, Batch,
    Pipeline Runs, retries, errors, and remediations;
8.  critical execution information survives relevant transactional
    rollbacks;
9.  business execution activity can be correlated with detailed SQL
    Server execution telemetry; and
10. the resulting evidence is sufficient to make informed architecture
    decisions about orchestration, persistence, lineage, and
    observability without those decisions having been predetermined by
    the requirements.
