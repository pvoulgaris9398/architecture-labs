# Long Asset History Sweep

## Question

At what range size, if any, does summing one asset's log returns become faster on the ordered
clustered columnstore than on the clustered rowstore?

The sweep uses asset 42 and cumulative ranges beginning at its first observation. It measures 10
range sizes from 21 through 10,000 trading observations. Each retained sample times 500 executions,
and each layout produces 9 samples at every point with a warm cache and `MAXDOP 1`. Execution order
alternates between layouts.

From this scenario directory, run `./run.sh` to rebuild and validate the deterministic dataset and
then run only this sweep. Every measurement is retained in `dbo.BenchmarkSample`. Timing a batch
keeps short rowstore lookups above the container clock's resolution. Divide `elapsed_microseconds`
by `executions_per_sample` to obtain the per-execution value.

## Analyze

This query returns one graph-ready median per layout and observation count for the latest completed
run:

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
`storage_type`. Inspect raw repetitions and `execution_position` before treating an apparent
crossover as stable. Run the experiment several times under comparable host conditions.

## Limitations

This isolates range size for one asset and one cumulative start date. It does not yet establish
whether a crossover changes with asset, date position, cache state, parallelism, concurrency,
rowgroup quality, or host load.
