USE LogReturnsLab;
GO

SET NOCOUNT ON;

DECLARE @scenario_id varchar(100) = 'long-asset-history-sweep';
DECLARE @run_id uniqueidentifier;

SELECT TOP (1) @run_id = run.run_id
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
  AND shape.sample_count = shape.asset_count * shape.point_count * shape.repetition_count * 2
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

IF @run_id IS NULL
    THROW 50030, 'No structurally complete long-history run was found.', 1;

CREATE TABLE #Assets (asset_id int NOT NULL PRIMARY KEY);

INSERT #Assets (asset_id)
SELECT DISTINCT asset_id
FROM dbo.BenchmarkSample
WHERE run_id = @run_id
  AND scenario_id = @scenario_id;

SELECT
    row_group.row_group_id,
    row_group.state_desc,
    row_group.total_rows,
    row_group.deleted_rows,
    row_group.size_in_bytes,
    row_group.trim_reason_desc
FROM sys.dm_db_column_store_row_group_physical_stats row_group
WHERE row_group.object_id = OBJECT_ID(N'dbo.ReturnsColumnstore')
ORDER BY row_group.row_group_id;

SELECT
    segment.segment_id,
    segment.row_count,
    segment.min_data_id AS minimum_asset_id,
    segment.max_data_id AS maximum_asset_id,
    segment.on_disk_size
FROM sys.column_store_segments segment
JOIN sys.partitions partition_definition
  ON partition_definition.hobt_id = segment.hobt_id
WHERE partition_definition.object_id = OBJECT_ID(N'dbo.ReturnsColumnstore')
  AND segment.column_id = COLUMNPROPERTY(
      OBJECT_ID(N'dbo.ReturnsColumnstore'), N'asset_id', N'ColumnId')
ORDER BY segment.segment_id;

SELECT
    asset.asset_id,
    COUNT(*) AS candidate_segments,
    SUM(segment.row_count) AS candidate_segment_rows,
    MIN(segment.min_data_id) AS covering_minimum_asset_id,
    MAX(segment.max_data_id) AS covering_maximum_asset_id
FROM #Assets asset
JOIN sys.column_store_segments segment
  ON asset.asset_id BETWEEN segment.min_data_id AND segment.max_data_id
JOIN sys.partitions partition_definition
  ON partition_definition.hobt_id = segment.hobt_id
 AND partition_definition.object_id = OBJECT_ID(N'dbo.ReturnsColumnstore')
WHERE segment.column_id = COLUMNPROPERTY(
    OBJECT_ID(N'dbo.ReturnsColumnstore'), N'asset_id', N'ColumnId')
GROUP BY asset.asset_id
ORDER BY asset.asset_id;

-- Identical bounds across every segment mean the date predicate cannot eliminate a whole segment.
SELECT
    segment.segment_id,
    segment.row_count,
    segment.min_data_id AS minimum_date_id,
    segment.max_data_id AS maximum_date_id
FROM sys.column_store_segments segment
JOIN sys.partitions partition_definition
  ON partition_definition.hobt_id = segment.hobt_id
WHERE partition_definition.object_id = OBJECT_ID(N'dbo.ReturnsColumnstore')
  AND segment.column_id = COLUMNPROPERTY(
      OBJECT_ID(N'dbo.ReturnsColumnstore'), N'trading_date', N'ColumnId')
ORDER BY segment.segment_id;

WITH Medians AS
(
    SELECT
        sample.asset_id,
        sample.observation_count,
        sample.storage_type,
        PERCENTILE_CONT(0.5) WITHIN GROUP
        (
            ORDER BY CAST(sample.elapsed_microseconds AS decimal(18, 3))
                / sample.executions_per_sample
        ) OVER
        (
            PARTITION BY sample.asset_id, sample.observation_count, sample.storage_type
        ) AS median_microseconds
    FROM dbo.BenchmarkSample sample
    WHERE sample.run_id = @run_id
      AND sample.scenario_id = @scenario_id
)
SELECT DISTINCT
    asset_id,
    observation_count,
    storage_type,
    median_microseconds
FROM Medians
ORDER BY asset_id, observation_count, storage_type;
GO
