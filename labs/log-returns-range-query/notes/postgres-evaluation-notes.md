# PostgreSQL Evaluation Notes

These notes describe how a future PostgreSQL evaluation would differ from the SQL Server
rowstore-versus-columnstore lab. They are planning material, not an implemented phase.

## What carries over

- The same narrow, long-history, time-slice, and full-aggregation query scenarios.
- Identical data and equivalent queries across comparison targets.
- Warm- and cold-cache controls, repeated runs, alternating execution order, and checksums.
- Elapsed time, CPU, I/O, storage, concurrency, load time, and modification cost.

## What changes

Core PostgreSQL does not provide a SQL Server-style clustered columnstore index. A core PostgreSQL
evaluation would instead compare physical and index designs over heap tables:

- Heap plus a multicolumn B-tree on `(asset_id, trading_date)`.
- A covering B-tree that can support index-only scans.
- Physically correlated data plus a BRIN index.
- Partitioned versus non-partitioned tables.
- Sequential scans versus indexed access at different selectivity levels.

BRIN is relevant because it summarizes physical block ranges and can avoid ranges that cannot match
a predicate. This resembles columnstore segment elimination as a pruning technique, but BRIN is not
columnar storage.

## PostgreSQL-specific axes

- `VACUUM`, autovacuum, MVCC bloat, and visibility-map coverage.
- Index-only scans and the remaining heap fetches reported by the plan.
- Physical correlation, insertion order, and the effect of `CLUSTER`.
- BRIN `pages_per_range` and summarization state.
- `work_mem`, parallel workers, planner estimates, and statistics quality.
- Evidence from `EXPLAIN (ANALYZE, BUFFERS, WAL)`.
- Bulk loading with `COPY` and write-ahead-log volume.
- Table and index sizes measured with PostgreSQL functions.

An actual PostgreSQL rowstore-versus-columnstore comparison requires choosing a specific extension
or PostgreSQL-derived product. That choice adds implementation, version, deployment, maintenance,
and licensing differences that must be treated as experimental variables.

## References

- [PostgreSQL indexes](https://www.postgresql.org/docs/current/indexes.html)
- [BRIN indexes](https://www.postgresql.org/docs/current/brin.html)
- [Index-only scans and covering indexes](https://www.postgresql.org/docs/current/indexes-index-only-scans.html)
