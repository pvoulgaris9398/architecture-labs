-- Shared selection for timing reports and diagnostic context. Parameter: @scenario_id.
SELECT TOP (1)
    run.run_id,
    run.started_at,
    run.completed_at,
    run.sql_server_version
FROM dbo.ExperimentRun run
CROSS APPLY
(
    SELECT
        COUNT(*) AS sample_count,
        COUNT(DISTINCT sample.asset_id) AS asset_count,
        COUNT(DISTINCT sample.sample_point) AS point_count,
        MAX(sample.repetition) AS repetition_count
    FROM dbo.BenchmarkSample sample
    WHERE sample.run_id = run.run_id
      AND sample.scenario_id = @scenario_id
) shape
WHERE run.status = 'passed'
  AND shape.sample_count > 0
  AND shape.sample_count =
      shape.asset_count * shape.point_count * shape.repetition_count * 2
  AND NOT EXISTS
  (
      SELECT sample.asset_id, sample.sample_point, sample.repetition
      FROM dbo.BenchmarkSample sample
      WHERE sample.run_id = run.run_id
        AND sample.scenario_id = @scenario_id
      GROUP BY sample.asset_id, sample.sample_point, sample.repetition
      HAVING COUNT(*) <> 2
          OR ABS(MAX(sample.checksum) - MIN(sample.checksum)) > 1e-10
  )
ORDER BY run.completed_at DESC;
