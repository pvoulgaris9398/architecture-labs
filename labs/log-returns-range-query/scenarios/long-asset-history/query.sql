SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @run_id uniqueidentifier = '$(RunId)';
DECLARE @scenario_id varchar(100) = 'long-asset-history-sweep';
DECLARE @asset_id int;
DECLARE @repetitions int = 9;
DECLARE @executions_per_sample smallint = 500;
DECLARE @execution int;
DECLARE @sample_point int;
DECLARE @repetition int;
DECLARE @start_date date;
DECLARE @end_date date;
DECLARE @observation_count bigint;
DECLARE @rowstore_first bit;
DECLARE @position tinyint;
DECLARE @started_at datetime2(7);
DECLARE @elapsed_microseconds bigint;
DECLARE @checksum float;
DECLARE @execution_checksum float;
DECLARE @warmup float;

DECLARE @Assets TABLE (asset_id int NOT NULL PRIMARY KEY);
DECLARE @SamplePoints TABLE (sample_point int NOT NULL PRIMARY KEY);

-- Ten evenly distributed positions across the generated asset-id range.
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
    -- Warm both complete access paths once for every sampled asset.
    SELECT @warmup = SUM(log_return)
    FROM dbo.ReturnsRowstore
    WHERE asset_id = @asset_id
    OPTION (MAXDOP 1);

    SELECT @warmup = SUM(log_return)
    FROM dbo.ReturnsColumnstore
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
    ORDER BY asset.asset_id, point.sample_point;

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
        THROW 50020, 'A long-history sample point exceeds the available observations.', 1;

    SET @repetition = 1;
    WHILE @repetition <= @repetitions
    BEGIN
        -- Alternate which layout executes first at every repetition and sample point.
        SET @rowstore_first = IIF((@sample_point + @repetition) % 2 = 0, 1, 0);
        SET @position = 1;

        WHILE @position <= 2
        BEGIN
            IF (@position = 1 AND @rowstore_first = 1)
                OR (@position = 2 AND @rowstore_first = 0)
            BEGIN
                SET @started_at = SYSDATETIME();
                SET @checksum = 0;
                SET @execution = 1;
                WHILE @execution <= @executions_per_sample
                BEGIN
                    SELECT @execution_checksum = SUM(log_return)
                    FROM dbo.ReturnsRowstore
                    WHERE asset_id = @asset_id
                      AND trading_date >= @start_date
                      AND trading_date < @end_date
                    OPTION (MAXDOP 1);
                    SET @checksum += @execution_checksum;
                    SET @execution += 1;
                END;
                SET @elapsed_microseconds = DATEDIFF_BIG(microsecond, @started_at, SYSDATETIME());

                INSERT dbo.BenchmarkSample
                (
                    run_id, scenario_id, sample_point, repetition, storage_type,
                    execution_position, asset_id, start_date, end_date,
                    observation_count, executions_per_sample, elapsed_microseconds, checksum
                )
                VALUES
                (
                    @run_id, @scenario_id, @sample_point, @repetition, 'rowstore',
                    @position, @asset_id, @start_date, @end_date,
                    @observation_count, @executions_per_sample, @elapsed_microseconds, @checksum
                );
            END
            ELSE
            BEGIN
                SET @started_at = SYSDATETIME();
                SET @checksum = 0;
                SET @execution = 1;
                WHILE @execution <= @executions_per_sample
                BEGIN
                    SELECT @execution_checksum = SUM(log_return)
                    FROM dbo.ReturnsColumnstore
                    WHERE asset_id = @asset_id
                      AND trading_date >= @start_date
                      AND trading_date < @end_date
                    OPTION (MAXDOP 1);
                    SET @checksum += @execution_checksum;
                    SET @execution += 1;
                END;
                SET @elapsed_microseconds = DATEDIFF_BIG(microsecond, @started_at, SYSDATETIME());

                INSERT dbo.BenchmarkSample
                (
                    run_id, scenario_id, sample_point, repetition, storage_type,
                    execution_position, asset_id, start_date, end_date,
                    observation_count, executions_per_sample, elapsed_microseconds, checksum
                )
                VALUES
                (
                    @run_id, @scenario_id, @sample_point, @repetition, 'columnstore',
                    @position, @asset_id, @start_date, @end_date,
                    @observation_count, @executions_per_sample, @elapsed_microseconds, @checksum
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
            THROW 50021, 'Rowstore and columnstore checksums differ in the long-history sweep.', 1;

        SET @repetition += 1;
    END;

    FETCH NEXT FROM sample_cursor INTO @asset_id, @sample_point;
END;

CLOSE sample_cursor;
DEALLOCATE sample_cursor;

WITH Summary AS
(
    SELECT
        observation_count,
        asset_id,
        storage_type,
        CAST(elapsed_microseconds AS decimal(18, 3)) / executions_per_sample
            AS microseconds_per_execution,
        COUNT(*) OVER (PARTITION BY observation_count, storage_type) AS samples,
        executions_per_sample
    FROM dbo.BenchmarkSample
    WHERE run_id = @run_id
      AND scenario_id = @scenario_id
)
SELECT DISTINCT
    observation_count,
    storage_type,
    COUNT(DISTINCT asset_id) OVER (PARTITION BY observation_count, storage_type) AS assets,
    samples,
    executions_per_sample,
    MIN(microseconds_per_execution) OVER
        (PARTITION BY observation_count, storage_type) AS minimum_microseconds,
    PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY microseconds_per_execution)
        OVER (PARTITION BY observation_count, storage_type) AS median_microseconds,
    MAX(microseconds_per_execution) OVER
        (PARTITION BY observation_count, storage_type) AS maximum_microseconds
FROM Summary
ORDER BY observation_count, storage_type;
GO
