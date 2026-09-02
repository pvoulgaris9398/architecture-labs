# Ordered Columnstore Build Quality

## Question

How does a documented full-order columnstore build compare with the default best-effort ordered
build in construction cost, segment overlap, and single-asset range-query performance?

The scenario changes one intentional variable:

| Design | Index build options |
| --- | --- |
| `partial-order` | Default build behavior |
| `full-order` | `ONLINE = ON, MAXDOP = 1` |

Both designs contain identical copies of the deterministic 10-million-row dataset and use
`ORDER (asset_id, trading_date)`. The full-order settings follow Microsoft's SQL Server 2025
guidance for producing a fully ordered clustered columnstore without overlapping segments.

## Run

From this scenario directory:

```bash
./run.sh
```

The scenario recreates its two comparison tables, records each index-build duration in
`dbo.ScenarioResult`, stores their segment metadata in `dbo.OrderedBuildSegmentResult`, and retains
1,800 raw query samples in `dbo.BenchmarkSample`. Queries use the same 10 assets, 10 history lengths,
9 repetitions, 500 executions per retained sample, warm cache, and `MAXDOP 1` as the long-history
scenario.

Expect this run to take longer than the long-history scenario because it copies the dataset twice,
builds two additional columnstore indexes, and deliberately builds one of them serially. The two
scenario tables are disposable and are replaced on every run; retained results are preserved.

## Inspect

Recent build durations:

```sql
SELECT run.started_at, result.storage_type, result.elapsed_ms
FROM dbo.ScenarioResult result
JOIN dbo.ExperimentRun run ON run.run_id = result.run_id
WHERE result.scenario_id = 'ordered-build-quality-build'
ORDER BY run.started_at DESC, result.storage_type;
```

Segment ranges for a run:

```sql
SELECT storage_type, segment_id, row_count, minimum_asset_id, maximum_asset_id
FROM dbo.OrderedBuildSegmentResult
WHERE run_id = '<run-id>'
ORDER BY storage_type, segment_id;
```

## Limitations

This compares initial index construction on one SQL Server version and local host. It does not yet
measure incremental loads, overlap introduced by later DML, rebuild maintenance, concurrency,
parallel query execution, or `tempdb` resource consumption during the online full-order build.
