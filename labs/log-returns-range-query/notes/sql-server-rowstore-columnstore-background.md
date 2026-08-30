# SQL Server Architecture Background: Columnstore vs. Rowstore

This background note describes common behavioral and performance differences between rowstore and
columnstore architectures in SQL Server. The statements are design tendencies, not benchmark
conclusions; actual behavior depends on the schema, indexes, data distribution, and query plan.

## Direct architectural overview

The choice between rowstore and columnstore represents a trade-off between transactional access and
analytical throughput.

- **Rowstore** stores the values of each row together. It is commonly used for transactional
  workloads and excels at selective lookups and modifications when supported by appropriate indexes.
- **Columnstore** stores each column in separate compressed segments grouped into rowgroups. It is
  designed for analytical workloads that scan and aggregate large numbers of rows.

## Detailed evaluation axes

### 1. Workload type

- **Rowstore:** Well suited to point lookups and small ranges. A B-tree index can navigate directly
  to a matching key or range.
- **Columnstore:** Well suited to scans and aggregations over large datasets. Compression, column
  elimination, rowgroup elimination, and batch processing can reduce the work required.

These are tendencies rather than strict OLTP/OLAP boundaries. SQL Server supports hybrid designs,
including rowstore indexes on columnstore tables and nonclustered columnstore indexes on rowstore
tables.

### 2. Data layout and storage footprint

- **Rowstore:** Stores complete rows together on data pages. Row and page compression can reduce its
  footprint.
- **Columnstore:** Divides rows into rowgroups and stores each column in a separate compressed
  segment. Similar values within a column often compress effectively.

Columnstore commonly uses less space for suitable analytical data, but the compression ratio is
data-dependent and should be measured rather than assumed.

### 3. I/O efficiency

- **Rowstore:** Reading a heap or clustered rowstore page brings complete stored rows into memory.
  Covering indexes and index-only access can avoid reading unused table columns in some plans.
- **Columnstore:** Reads only referenced column segments and can skip rowgroups whose segment
  metadata cannot satisfy the query predicate.

Columnstore pruning depends on data distribution, load order, column order, and segment overlap.
Ordered columnstore indexes can improve elimination for predicates aligned with their order.

### 4. Data modification performance

- **Rowstore:** Generally well suited to frequent small inserts, updates, and deletes.
- **Columnstore:** Supports modification, but small inserts may enter delta rowgroups before being
  compressed. Deletes are tracked until maintenance removes deleted rows from compressed rowgroups.

Large batch loads can go directly into compressed rowgroups and perform very differently from
single-row or small-batch writes. Load pattern and rowgroup quality should therefore be measured.

### 5. Memory and CPU utilization

- **Rowstore:** Plans may use row mode or batch mode depending on SQL Server version, compatibility
  level, query, and optimizer decisions.
- **Columnstore:** Analytical plans commonly use batch mode, processing groups of rows per operator
  and benefiting from compressed column access.

Batch mode is not exclusive to columnstore, so the actual execution plan is authoritative.

## Comparative summary

| Evaluation axis | Rowstore | Columnstore |
| --- | --- | --- |
| Typical workload | Selective reads and transactional changes | Broad scans and analytical aggregation |
| Data layout | Values stored together by row | Values stored in compressed column segments |
| Selective access | B-tree seeks and small range scans | Segment elimination; optional B-tree indexes |
| Broad-query I/O | Reads complete stored rows or covering indexes | Reads referenced columns and eligible segments |
| Compression | Optional row or page compression | Column-oriented compression; result varies by data |
| Modifications | Usually efficient for frequent small changes | Sensitive to batch size, delta rowgroups, and maintenance |
| Execution mode | Row or batch mode | Commonly batch mode for analytical plans |

## Bookstore analogy

Rowstore resembles a bookstore in which each complete book is stored together. If you know the title
and have a good catalog, you can go directly to that book. Columnstore resembles an inventory sheet
organized by field: calculating the total price can read the price values without reading every
description and publication date.

The analogy is deliberately simplified. Real SQL Server designs can combine both access patterns,
and measured query plans determine which mechanics are actually used.

## References

- [Columnstore indexes overview](https://learn.microsoft.com/en-us/sql/relational-databases/indexes/columnstore-indexes-overview)
- [Columnstore query performance](https://learn.microsoft.com/en-us/sql/relational-databases/indexes/columnstore-indexes-query-performance)
- [Ordered columnstore indexes](https://learn.microsoft.com/en-us/sql/relational-databases/indexes/ordered-columnstore-indexes)
- [Columnstore data-loading guidance](https://learn.microsoft.com/en-us/sql/relational-databases/indexes/columnstore-indexes-data-loading-guidance)
