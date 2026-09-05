# Long Asset History Diagnostics

Status: implemented; diagnostic observations are not benchmark conclusions.

## Question and controls

Hypothesis: differences in access paths, rows read and columnstore segment elimination
help explain the baseline timing differences. Inspect assets 112 and 778 at 21, 252,
2,520 and 10,000 observations. These are fixed representatives, not assumed winners.
The collector reads the exact date bounds from the latest successful timing run and
uses the same assignment-style `SUM(log_return)` predicate and `MAXDOP 1` as the baseline.
It warms each path once, captures one execution, and alternates the first layout by case.
It neither rebuilds indexes nor changes persistent database settings or benchmark rows.

## Run

Keep the lab container running and complete a timing baseline first. From this directory:

```bash
./run.sh
# Equivalent from PowerShell or Bash:
dotnet run capture.cs
# Bounded smoke check: asset 112, 21 observations, both layouts.
dotnet run capture.cs -- --smoke
```

Requires .NET 10 SDK and the existing lab `.env` (or `MSSQL_SA_PASSWORD`). Connects to
local SQL Server at `localhost,1435`. The local `sa` login has access; a restricted login
needs SELECT on lab tables, SHOWPLAN, and VIEW SERVER PERFORMANCE STATE for environment
metadata. `query.sql` is a typed-parameter template invoked by `capture.cs`, not a
standalone SSMS script. For interactive use open `compare-plans.sql` and enable Ctrl+M.
Run `diagnose.sql` in SSMS for current rowgroup/segment bounds and historical medians.

## Evidence and interpretation

Each invocation writes ignored files under `results/local/<baseline-run-id>/<capture-id>/`:

- `summary.md`: statement CPU/elapsed time, workspace grants, raw plan DOP, warning
  counts, table I/O/segment counters, and per-operator/thread rows, mode and reads.
- `measurements.json`: parameters, checksum, execution order, raw runtime attributes,
  memory/time attributes, reported waits and warning XML, including spill details.
- One `.sqlplan` and `.messages.txt` per case: actual plans for SSMS and raw SQL Server
  I/O/time output, including columnstore segments read/skipped when reported.
- `environment.json` and `query.sql`: current engine/compatibility, server-visible CPU
  and memory, capture time, baseline reference and executed query template.

Success means every selected case returns an actual plan and matches the saved baseline
checksum within 1e-10. Missing plan attributes are `n/a`, not zero. CPU/elapsed counters
can round to zero for short queries. Rows output by a scan can reflect aggregate pushdown;
do not confuse them with qualifying rows or sum row counts across operators. Workspace
memory excludes the buffer cache. Inspect warnings for spills; warning count is not spill
volume. Raw messages retain per-table and LOB I/O rather than combining unlike counters.
The table I/O summary parses English STATISTICS IO labels; the collector sets language
only for its own session. Operator counters can omit columnstore LOB reads even when
table I/O reports them. Raw plan DOP 0 represents a serial plan in these captures.

Instrumentation changes timing and the standalone diagnostic batch has a separate plan
cache context. Use the baseline report for timing comparisons. This captures the current
physical layout, not a historical reconstruction; checksum equality does not prove that
indexes, statistics or resource limits are unchanged. Record Docker limits, machine details
and host load separately when presenting conclusions. Do not run alongside another lab
workload. A small diagnostic subset cannot establish a crossover or explain every asset.

## Validate

From this directory:

```bash
dotnet build capture.cs
bash -n run.sh
dotnet run capture.cs -- --smoke
# From the lab directory:
docker compose config --quiet
# From the repository root:
git diff --check
```

Keep SQL and C# formatting consistent with adjacent files; no automatic SQL/C# formatter
is configured for these file-based tools. Review the smoke summary, matching JSON counters
and both plans in SSMS. There is no cleanup step required; outputs remain ignored. Deleting
saved captures or the database volume is destructive and must be an intentional action.
