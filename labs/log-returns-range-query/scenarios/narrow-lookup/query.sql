USE LogReturnsLab;
GO

SET NOCOUNT ON;

DECLARE @asset_id int = 42;
DECLARE @start_date date = '2000-01-01';
DECLARE @end_date date = '2001-01-01';

IF @start_date >= @end_date
    THROW 50010, 'The start date must be earlier than the end date.', 1;

-- Warm both access paths before collecting interactive statistics.
DECLARE @warmup float;

SELECT @warmup = SUM(log_return)
FROM dbo.ReturnsRowstore
WHERE asset_id = @asset_id
  AND trading_date >= @start_date
  AND trading_date < @end_date
OPTION (MAXDOP 1);

SELECT @warmup = SUM(log_return)
FROM dbo.ReturnsColumnstore
WHERE asset_id = @asset_id
  AND trading_date >= @start_date
  AND trading_date < @end_date
OPTION (MAXDOP 1);

SET STATISTICS IO ON;
SET STATISTICS TIME ON;

SELECT
    'narrow-lookup' AS scenario_id,
    'rowstore' AS storage_type,
    @asset_id AS asset_id,
    @start_date AS start_date,
    @end_date AS end_date,
    COUNT_BIG(*) AS observation_count,
    SUM(log_return) AS cumulative_log_return,
    EXP(SUM(log_return)) - 1.0 AS return_from_logs,
    PRODUCT(1.0 + simple_return) - 1.0 AS compounded_simple_return,
    ABS(
        (EXP(SUM(log_return)) - 1.0)
        - (PRODUCT(1.0 + simple_return) - 1.0)
    ) AS absolute_delta
FROM dbo.ReturnsRowstore
WHERE asset_id = @asset_id
  AND trading_date >= @start_date
  AND trading_date < @end_date
OPTION (MAXDOP 1);

SELECT
    'narrow-lookup' AS scenario_id,
    'columnstore' AS storage_type,
    @asset_id AS asset_id,
    @start_date AS start_date,
    @end_date AS end_date,
    COUNT_BIG(*) AS observation_count,
    SUM(log_return) AS cumulative_log_return,
    EXP(SUM(log_return)) - 1.0 AS return_from_logs,
    PRODUCT(1.0 + simple_return) - 1.0 AS compounded_simple_return,
    ABS(
        (EXP(SUM(log_return)) - 1.0)
        - (PRODUCT(1.0 + simple_return) - 1.0)
    ) AS absolute_delta
FROM dbo.ReturnsColumnstore
WHERE asset_id = @asset_id
  AND trading_date >= @start_date
  AND trading_date < @end_date
OPTION (MAXDOP 1);

SET STATISTICS TIME OFF;
SET STATISTICS IO OFF;
GO

