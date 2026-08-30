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

For each scenario, record elapsed time, CPU time, logical reads, rows selected, execution plan,
result checksum, and columnstore segments read/skipped. Use several repetitions, alternate execution
order, and report the median.

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
2. Add storage size, logical reads, CPU time, plans, and segment-elimination evidence.
3. Compare ordered and unordered columnstore layouts.
4. Evaluate load and modification behavior.
5. Add parallelism and concurrency only after the single-session baseline is stable.

## References

- [Columnstore query performance](https://learn.microsoft.com/en-us/sql/relational-databases/indexes/columnstore-indexes-query-performance)
- [Ordered columnstore indexes](https://learn.microsoft.com/en-us/sql/relational-databases/indexes/ordered-columnstore-indexes)
- [Columnstore data-loading guidance](https://learn.microsoft.com/en-us/sql/relational-databases/indexes/columnstore-indexes-data-loading-guidance)
- [Intelligent Query Processing details](https://learn.microsoft.com/en-us/sql/relational-databases/performance/intelligent-query-processing-details)
