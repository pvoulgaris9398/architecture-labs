SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @run_id uniqueidentifier = '$(RunId)';
DECLARE @scenario_id varchar(100) = 'ordered-build-quality-sweep';
DECLARE @repetitions int = 9;
DECLARE @executions_per_sample smallint = 500;
DECLARE @expected_samples int = 1800;
DECLARE @started_at datetime2(7);
DECLARE @partial_build_ms bigint;
DECLARE @full_build_ms bigint;

UPDATE dbo.ExperimentRun
SET expected_benchmark_samples = COALESCE(expected_benchmark_samples, 0) + @expected_samples
WHERE run_id = @run_id;

IF OBJECT_ID(N'dbo.OrderedBuildSegmentResult', N'U') IS NULL
BEGIN
    EXEC(N'
        CREATE TABLE dbo.OrderedBuildSegmentResult
        (
            run_id uniqueidentifier NOT NULL,
            storage_type varchar(20) NOT NULL,
            segment_id int NOT NULL,
            row_count int NOT NULL,
            minimum_asset_id bigint NOT NULL,
            maximum_asset_id bigint NOT NULL,
            minimum_date_id bigint NOT NULL,
            maximum_date_id bigint NOT NULL,
            on_disk_size bigint NOT NULL,
            CONSTRAINT PK_OrderedBuildSegmentResult
                PRIMARY KEY (run_id, storage_type, segment_id),
            CONSTRAINT FK_OrderedBuildSegmentResult_ExperimentRun
                FOREIGN KEY (run_id) REFERENCES dbo.ExperimentRun(run_id),
            CONSTRAINT CK_OrderedBuildSegmentResult_StorageType
                CHECK (storage_type IN (''partial-order'', ''full-order''))
        );
    ');
END;

DROP TABLE IF EXISTS dbo.ReturnsColumnstorePartialOrder;
DROP TABLE IF EXISTS dbo.ReturnsColumnstoreFullOrder;

SELECT asset_id, trading_date, simple_return, log_return
INTO dbo.ReturnsColumnstorePartialOrder
FROM dbo.ReturnsRowstore;

SELECT asset_id, trading_date, simple_return, log_return
INTO dbo.ReturnsColumnstoreFullOrder
FROM dbo.ReturnsRowstore;

SET @started_at = SYSDATETIME();
CREATE CLUSTERED COLUMNSTORE INDEX CCI_ReturnsColumnstorePartialOrder
ON dbo.ReturnsColumnstorePartialOrder
ORDER (asset_id, trading_date);
SET @partial_build_ms = DATEDIFF_BIG(millisecond, @started_at, SYSDATETIME());

SET @started_at = SYSDATETIME();
CREATE CLUSTERED COLUMNSTORE INDEX CCI_ReturnsColumnstoreFullOrder
ON dbo.ReturnsColumnstoreFullOrder
ORDER (asset_id, trading_date)
WITH (ONLINE = ON, MAXDOP = 1);
SET @full_build_ms = DATEDIFF_BIG(millisecond, @started_at, SYSDATETIME());

UPDATE STATISTICS dbo.ReturnsColumnstorePartialOrder WITH FULLSCAN;
UPDATE STATISTICS dbo.ReturnsColumnstoreFullOrder WITH FULLSCAN;

INSERT dbo.ScenarioResult
(
    run_id, scenario_id, result_type, storage_type, elapsed_ms, passed
)
VALUES
    (@run_id, 'ordered-build-quality-build', 'benchmark', 'partial-order',
        @partial_build_ms, 1),
    (@run_id, 'ordered-build-quality-build', 'benchmark', 'full-order',
        @full_build_ms, 1);

INSERT dbo.OrderedBuildSegmentResult
(
    run_id, storage_type, segment_id, row_count,
    minimum_asset_id, maximum_asset_id, minimum_date_id, maximum_date_id, on_disk_size
)
SELECT
    @run_id,
    design.storage_type,
    asset_segment.segment_id,
    asset_segment.row_count,
    asset_segment.min_data_id,
    asset_segment.max_data_id,
    date_segment.min_data_id,
    date_segment.max_data_id,
    asset_segment.on_disk_size + date_segment.on_disk_size
FROM
(
    VALUES
        ('partial-order', OBJECT_ID(N'dbo.ReturnsColumnstorePartialOrder')),
        ('full-order', OBJECT_ID(N'dbo.ReturnsColumnstoreFullOrder'))
) design(storage_type, object_id)
JOIN sys.partitions partition_definition
  ON partition_definition.object_id = design.object_id
 AND partition_definition.index_id = 1
JOIN sys.column_store_segments asset_segment
  ON asset_segment.hobt_id = partition_definition.hobt_id
 AND asset_segment.column_id = COLUMNPROPERTY(design.object_id, N'asset_id', N'ColumnId')
JOIN sys.column_store_segments date_segment
  ON date_segment.hobt_id = asset_segment.hobt_id
 AND date_segment.segment_id = asset_segment.segment_id
 AND date_segment.column_id = COLUMNPROPERTY(design.object_id, N'trading_date', N'ColumnId');

DECLARE @Assets TABLE (asset_id int NOT NULL PRIMARY KEY);
DECLARE @SamplePoints TABLE (sample_point int NOT NULL PRIMARY KEY);
DECLARE @asset_id int;
DECLARE @sample_point int;
DECLARE @repetition int;
DECLARE @execution int;
DECLARE @position tinyint;
DECLARE @partial_first bit;
DECLARE @start_date date;
DECLARE @end_date date;
DECLARE @observation_count bigint;
DECLARE @elapsed_microseconds bigint;
DECLARE @checksum float;
DECLARE @execution_checksum float;
DECLARE @warmup float;

INSERT @Assets (asset_id)
VALUES (1), (112), (223), (334), (445), (556), (667), (778), (889), (1000);

INSERT @SamplePoints (sample_point)
VALUES (21), (63), (126), (252), (504), (1260), (2520), (5000), (7500), (10000);

DECLARE warmup_cursor CURSOR LOCAL FAST_FORWARD FOR
    SELECT asset_id FROM @Assets ORDER BY asset_id;

OPEN warmup_cursor;
FETCH NEXT FROM warmup_cursor INTO @asset_id;

WHILE @@FETCH_STATUS = 0
BEGIN
    SELECT @warmup = SUM(log_return)
    FROM dbo.ReturnsColumnstorePartialOrder
    WHERE asset_id = @asset_id
    OPTION (MAXDOP 1);

    SELECT @warmup = SUM(log_return)
    FROM dbo.ReturnsColumnstoreFullOrder
    WHERE asset_id = @asset_id
    OPTION (MAXDOP 1);

    FETCH NEXT FROM warmup_cursor INTO @asset_id;
END;

CLOSE warmup_cursor;
DEALLOCATE warmup_cursor;

DECLARE sample_cursor CURSOR LOCAL FAST_FORWARD FOR
    SELECT asset.asset_id, point.sample_point
    FROM @Assets asset
    CROSS JOIN @SamplePoints point
    ORDER BY point.sample_point, asset.asset_id;

OPEN sample_cursor;
FETCH NEXT FROM sample_cursor INTO @asset_id, @sample_point;

WHILE @@FETCH_STATUS = 0
BEGIN
    SELECT @start_date = MIN(trading_date)
    FROM dbo.ReturnsRowstore
    WHERE asset_id = @asset_id;

    SELECT
        @end_date = DATEADD(day, 1, MAX(trading_date)),
        @observation_count = COUNT_BIG(*)
    FROM
    (
        SELECT TOP (@sample_point) trading_date
        FROM dbo.ReturnsRowstore
        WHERE asset_id = @asset_id
        ORDER BY trading_date
    ) observations;

    IF @observation_count <> @sample_point
        THROW 50040, 'An ordered-build sample point exceeds the available observations.', 1;

    SET @repetition = 1;
    WHILE @repetition <= @repetitions
    BEGIN
        SET @partial_first = IIF((@asset_id + @sample_point + @repetition) % 2 = 0, 1, 0);
        SET @position = 1;

        WHILE @position <= 2
        BEGIN
            SET @checksum = 0;
            SET @execution = 1;
            SET @started_at = SYSDATETIME();

            IF (@position = 1 AND @partial_first = 1)
                OR (@position = 2 AND @partial_first = 0)
            BEGIN
                WHILE @execution <= @executions_per_sample
                BEGIN
                    SELECT @execution_checksum = SUM(log_return)
                    FROM dbo.ReturnsColumnstorePartialOrder
                    WHERE asset_id = @asset_id
                      AND trading_date >= @start_date
                      AND trading_date < @end_date
                    OPTION (MAXDOP 1);
                    SET @checksum += @execution_checksum;
                    SET @execution += 1;
                END;

                SET @elapsed_microseconds =
                    DATEDIFF_BIG(microsecond, @started_at, SYSDATETIME());

                INSERT dbo.BenchmarkSample
                (
                    run_id, scenario_id, sample_point, repetition, storage_type,
                    execution_position, asset_id, start_date, end_date, observation_count,
                    executions_per_sample, elapsed_microseconds, checksum
                )
                VALUES
                (
                    @run_id, @scenario_id, @sample_point, @repetition, 'partial-order',
                    @position, @asset_id, @start_date, @end_date, @observation_count,
                    @executions_per_sample, @elapsed_microseconds, @checksum
                );
            END
            ELSE
            BEGIN
                WHILE @execution <= @executions_per_sample
                BEGIN
                    SELECT @execution_checksum = SUM(log_return)
                    FROM dbo.ReturnsColumnstoreFullOrder
                    WHERE asset_id = @asset_id
                      AND trading_date >= @start_date
                      AND trading_date < @end_date
                    OPTION (MAXDOP 1);
                    SET @checksum += @execution_checksum;
                    SET @execution += 1;
                END;

                SET @elapsed_microseconds =
                    DATEDIFF_BIG(microsecond, @started_at, SYSDATETIME());

                INSERT dbo.BenchmarkSample
                (
                    run_id, scenario_id, sample_point, repetition, storage_type,
                    execution_position, asset_id, start_date, end_date, observation_count,
                    executions_per_sample, elapsed_microseconds, checksum
                )
                VALUES
                (
                    @run_id, @scenario_id, @sample_point, @repetition, 'full-order',
                    @position, @asset_id, @start_date, @end_date, @observation_count,
                    @executions_per_sample, @elapsed_microseconds, @checksum
                );
            END;

            SET @position += 1;
        END;

        IF
        (
            SELECT ABS(MAX(checksum) - MIN(checksum))
            FROM dbo.BenchmarkSample
            WHERE run_id = @run_id
              AND scenario_id = @scenario_id
              AND asset_id = @asset_id
              AND sample_point = @sample_point
              AND repetition = @repetition
        ) > 1e-10
            THROW 50041, 'Partial-order and full-order checksums differ.', 1;

        SET @repetition += 1;
    END;

    FETCH NEXT FROM sample_cursor INTO @asset_id, @sample_point;
END;

CLOSE sample_cursor;
DEALLOCATE sample_cursor;

IF (SELECT COUNT(*) FROM dbo.BenchmarkSample
    WHERE run_id = @run_id AND scenario_id = @scenario_id) <> @expected_samples
    THROW 50042, 'The ordered-build sweep did not retain its expected sample count.', 1;

WITH Medians AS
(
    SELECT
        observation_count,
        storage_type,
        PERCENTILE_CONT(0.5) WITHIN GROUP
        (
            ORDER BY CAST(elapsed_microseconds AS decimal(18, 3))
                / executions_per_sample
        ) OVER (PARTITION BY observation_count, storage_type) AS median_microseconds
    FROM dbo.BenchmarkSample
    WHERE run_id = @run_id
      AND scenario_id = @scenario_id
)
SELECT DISTINCT observation_count, storage_type, median_microseconds
FROM Medians
ORDER BY observation_count, storage_type;

SELECT
    storage_type,
    COUNT(*) AS segments,
    SUM(row_count) AS rows,
    SUM(on_disk_size) AS ordered_columns_on_disk_size
FROM dbo.OrderedBuildSegmentResult
WHERE run_id = @run_id
GROUP BY storage_type
ORDER BY storage_type;
GO
