SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @iterations int = 1000;
DECLARE @iteration int;
DECLARE @asset_id int;
DECLARE @start_date date;
DECLARE @end_date date;
DECLARE @value float;
DECLARE @rowstore_checksum float = 0;
DECLARE @columnstore_checksum float = 0;
DECLARE @started_at datetime2;
DECLARE @rowstore_ms bigint;
DECLARE @columnstore_ms bigint;
DECLARE @run_id uniqueidentifier = '$(RunId)';

-- Warm both access paths before measurement.
SELECT @value = SUM(log_return) FROM dbo.ReturnsRowstore
WHERE asset_id = 42 AND trading_date >= '2000-01-01' AND trading_date < '2010-01-01'
OPTION (MAXDOP 1);

SELECT @value = SUM(log_return) FROM dbo.ReturnsColumnstore
WHERE asset_id = 42 AND trading_date >= '2000-01-01' AND trading_date < '2010-01-01'
OPTION (MAXDOP 1);

SET @iteration = 0;
SET @started_at = SYSDATETIME();
WHILE @iteration < @iterations
BEGIN
    SET @asset_id = (@iteration * 37) % 1000 + 1;
    SET @start_date = DATEADD(day, (@iteration * 97) % 9000, CONVERT(date, '1990-01-01'));
    SET @end_date = DATEADD(day, 365, @start_date);
    SELECT @value = SUM(log_return) FROM dbo.ReturnsRowstore
    WHERE asset_id = @asset_id AND trading_date >= @start_date AND trading_date < @end_date
    OPTION (MAXDOP 1);
    SET @rowstore_checksum += COALESCE(@value, 0);
    SET @iteration += 1;
END;
SET @rowstore_ms = DATEDIFF_BIG(millisecond, @started_at, SYSDATETIME());

SET @iteration = 0;
SET @started_at = SYSDATETIME();
WHILE @iteration < @iterations
BEGIN
    SET @asset_id = (@iteration * 37) % 1000 + 1;
    SET @start_date = DATEADD(day, (@iteration * 97) % 9000, CONVERT(date, '1990-01-01'));
    SET @end_date = DATEADD(day, 365, @start_date);
    SELECT @value = SUM(log_return) FROM dbo.ReturnsColumnstore
    WHERE asset_id = @asset_id AND trading_date >= @start_date AND trading_date < @end_date
    OPTION (MAXDOP 1);
    SET @columnstore_checksum += COALESCE(@value, 0);
    SET @iteration += 1;
END;
SET @columnstore_ms = DATEDIFF_BIG(millisecond, @started_at, SYSDATETIME());

INSERT dbo.ScenarioResult
(
    run_id, scenario_id, result_type, storage_type, elapsed_ms, checksum, passed
)
VALUES
    (@run_id, 'one-year-ranges-1000', 'benchmark', 'rowstore', @rowstore_ms,
        @rowstore_checksum, IIF(ABS(@rowstore_checksum - @columnstore_checksum) <= 1e-12, 1, 0)),
    (@run_id, 'one-year-ranges-1000', 'benchmark', 'columnstore', @columnstore_ms,
        @columnstore_checksum, IIF(ABS(@rowstore_checksum - @columnstore_checksum) <= 1e-12, 1, 0));

SELECT @iterations AS iterations, @rowstore_ms AS rowstore_ms,
    @columnstore_ms AS columnstore_ms, @rowstore_checksum AS rowstore_checksum,
    @columnstore_checksum AS columnstore_checksum,
    ABS(@rowstore_checksum - @columnstore_checksum) AS checksum_delta;
GO
