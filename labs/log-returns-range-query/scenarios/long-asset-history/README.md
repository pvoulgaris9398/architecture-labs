# Long Asset History Sweep

## Question

At what range size, if any, does summing one asset's log returns become faster on the ordered
clustered columnstore than on the clustered rowstore?

The sweep samples 10 individual assets distributed across the table: 1, 112, 223, 334, 445, 556,
667, 778, 889, and 1,000. For each asset, it measures 10 cumulative ranges from 21 through 10,000
trading observations beginning at that asset's first observation. Each retained sample times 500
executions, and each layout produces 9 samples per asset at every point with a warm cache and
`MAXDOP 1`. Execution order alternates between layouts.

From this scenario directory, run `./run.sh` to rebuild and validate the deterministic dataset and
then run only this sweep. Every measurement is retained in `dbo.BenchmarkSample`. Timing a batch
keeps short rowstore lookups above the container clock's resolution. Divide `elapsed_microseconds`
by `executions_per_sample` to obtain the per-execution value. A complete run retains 1,800 samples
and takes roughly ten times as long as the earlier single-asset sweep.

## Analyze

This query returns the pooled graph-ready median across all sampled assets for each layout and
observation count in the latest completed run:

```sql
WITH LatestRun AS
(
    SELECT TOP (1) run_id
    FROM dbo.ExperimentRun
    WHERE status = 'passed'
    ORDER BY completed_at DESC
),
Medians AS
(
    SELECT
        sample.observation_count,
        sample.storage_type,
        PERCENTILE_CONT(0.5) WITHIN GROUP
            (ORDER BY CAST(sample.elapsed_microseconds AS decimal(18, 3))
                / sample.executions_per_sample)
            OVER (PARTITION BY sample.observation_count, sample.storage_type) AS median_microseconds
    FROM dbo.BenchmarkSample sample
    JOIN LatestRun run ON run.run_id = sample.run_id
    WHERE sample.scenario_id = 'long-asset-history-sweep'
)
SELECT DISTINCT observation_count, storage_type, median_microseconds
FROM Medians
ORDER BY observation_count, storage_type;
```

Plot `observation_count` on the x-axis and `median_microseconds` on the y-axis, with one series per
`storage_type`. Before treating an apparent crossover as stable, calculate the same median with
`asset_id` added to both `SELECT` and `PARTITION BY` to inspect each asset's curve. Also inspect raw
repetitions and `execution_position`, and run the experiment several times under comparable host
conditions.

## Limitations

This measures individual-asset queries, not one query spanning multiple assets. It does not yet
establish whether a crossover changes with date position, cache state, parallelism, concurrency,
rowgroup quality, or host load.
