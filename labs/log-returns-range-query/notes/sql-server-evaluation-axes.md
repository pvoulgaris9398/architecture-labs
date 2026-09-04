# SQL Server Rowstore vs. Columnstore Evaluation Axes

This note defines potential comparison dimensions for the lab. It is a test plan, not a conclusion.
Change one intentional variable at a time and run equivalent queries against identical data.

## Core query scenarios

Start with four selectivity levels to find where the preferred storage layout changes:

| Scenario | Assets | Date range | Purpose |
| --- | ---: | --- | --- |
| Narrow lookup | 1 | 1 year | Small, index-friendly range |
| Long asset history | 1 | All dates | Larger contiguous range |
| Market time slice | All | 1 year | Broad analytical aggregation |
| Full aggregation | All | All dates | Maximum scan |

Elapsed time is one outcome, not the complete explanation. For each scenario, record elapsed and CPU
time, logical and physical reads, rows read and returned, memory grant and actual memory use, spills,
waits, degree of parallelism, the actual execution plan, result checksum, and columnstore segments
read/skipped. Use several repetitions, alternate execution order, and report the median.

## Measurement model

Use three layers so collection details do not leak into the analysis:

1. The benchmark harness defines the run, controls cache state and execution order, captures wall-clock
   timing, and assigns stable run, dataset, query, and storage-design identifiers.
2. SQL Server telemetry captures engine evidence from execution plans, runtime statistics, and DMVs.
   Treat DMV values as transient and often cumulative: isolate the target execution with before/after
   snapshots where necessary. Use Query Store for persisted query, plan, runtime, and wait history, but
   do not assume it replaces per-run harness measurements or every plan-level diagnostic.
3. Normalized experiment results join both sources into one row (or related rows) per measured
   execution, with consistent units and explicit missing values. Preserve the raw plan and enough
   source detail to recalculate summaries.

Memory grant and actual memory use are different measurements. Likewise, distinguish rows read from
rows returned, logical from physical reads, and requested from observed parallelism. These differences
often explain why similar elapsed times arise from different resource costs.

## Dataset and query characteristics

Record the dimensions that define each observation rather than relying only on a scenario name:

- Dataset: total row count, distinct securities, observations per security, date span,
  ordering/distribution, skew, and physical build quality.
- Query: securities selected, date range, qualifying rows, selectivity, range width, projection and
  aggregation shape, and warm- or cold-cache state.
- Physical design: rowstore or columnstore layout, keys and indexes, columnstore ordering and rowgroup
  quality, plus ClickHouse table ordering and partitioning when that later phase is implemented.
- Environment: engine and compatibility versions, resource limits, host characteristics, statistics
  state, concurrency, and requested and observed degree of parallelism.

Vary one characteristic at a time where a controlled comparison is claimed. Use a dataset matrix when
testing scale, density, ordering, or skew so results are not accidentally attributed to storage design.

## Analysis relationships

Plot relationships that expose engine behavior, not only storage design versus elapsed time:

- selectivity to elapsed time and logical reads;
- rows read or returned to CPU time and memory use;
- logical and physical reads to elapsed time;
- memory grant, memory used, spills, and waits to elapsed time;
- execution-plan shape and degree of parallelism to elapsed and CPU time;
- range width and dataset scale to plan choice and resource use.

Look for crossover points: the selectivity, range width, or dataset scale where clustered rowstore and
ordered clustered columnstore exchange advantage, and later where ClickHouse crosses either SQL Server
layout. Report the conditions and resource tradeoffs at each crossover rather than declaring a universal
winner.

## Evaluation axes

### Query behavior

- Selectivity: narrow ranges through full-table scans.
- Projection width: only `log_return` versus additional columns.
- Aggregation shape: one total, grouped by asset, and grouped by time period.
- Parallelism: fixed `MAXDOP 1` baseline, then a controlled parallel comparison.
- Cache state: warm cache first; cold cache only as a separately documented experiment.
- Concurrency: single session first, then a fixed number of concurrent readers.

### Physical design

- Clustered rowstore key and covering-index choices.
- Ordered versus unordered clustered columnstore.
- Column order and observed rowgroup/segment elimination.
- Optional rowstore indexes on a clustered columnstore for selective access.
- Compression ratio and total storage, measured from SQL Server rather than assumed.
- Rowgroup quality: compressed, delta-store, deleted rows, size, and overlap.

### Data lifecycle

- Initial load and index-build time.
- Batch inserts versus small inserts.
- Update and delete cost.
- Maintenance cost after data changes, including reorganize or rebuild operations.

### Operational behavior

- CPU, memory, and I/O consumption.
- Statistics and plan stability.
- Locking and blocking under concurrent reads and writes.
- Recovery, backup size, and restore time if the lab later expands beyond query performance.

## Controls to preserve

- Same rows, data types, queries, parameters, and result validation for both layouts.
- Same SQL Server version, compatibility level, container resources, and host conditions.
- Record row count, data distribution, software versions, machine characteristics, test date,
  warm-up procedure, repetitions, and measurement window with retained results.
- Do not infer a universal winner from one query shape or table size.
- Treat batch execution mode as an observed plan property, not a synonym for columnstore; modern SQL
  Server can also use batch mode on rowstore.

## Suggested order

1. Implement the four core query scenarios with warm cache and `MAXDOP 1`.
2. Add normalized SQL Server telemetry, including reads, CPU, rows, memory, spills, waits, plans,
   parallelism, storage size, and segment-elimination evidence.
3. Compare ordered and unordered columnstore layouts.
4. Evaluate load and modification behavior.
5. Add parallelism and concurrency only after the single-session baseline is stable.

## References

- [Columnstore query performance](https://learn.microsoft.com/en-us/sql/relational-databases/indexes/columnstore-indexes-query-performance)
- [Ordered columnstore indexes](https://learn.microsoft.com/en-us/sql/relational-databases/indexes/ordered-columnstore-indexes)
- [Columnstore data-loading guidance](https://learn.microsoft.com/en-us/sql/relational-databases/indexes/columnstore-indexes-data-loading-guidance)
- [Intelligent Query Processing details](https://learn.microsoft.com/en-us/sql/relational-databases/performance/intelligent-query-processing-details)
