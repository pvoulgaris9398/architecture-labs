/* Interactive companion to capture.cs. Run in SSMS with Ctrl+M enabled.
   Choose asset 112 or 778 and 21, 252, 2520, or 10000 observations.
   Uses current tables without rebuilding. Timings include plan instrumentation;
   use timing-baseline for benchmark conclusions. */
USE LogReturnsLab;
GO
SET NOCOUNT ON;
DECLARE @asset_id int = 112;
DECLARE @observations int = 2520;
DECLARE @start_date date;
DECLARE @end_date date;
DECLARE @actual_count bigint;
DECLARE @rowstore float;
DECLARE @columnstore float;

SELECT @start_date = MIN(trading_date),
       @end_date = DATEADD(day, 1, MAX(trading_date)),
       @actual_count = COUNT_BIG(*)
FROM (
    SELECT TOP (@observations) trading_date
    FROM dbo.ReturnsRowstore
    WHERE asset_id = @asset_id
    ORDER BY trading_date
) observations;
IF @actual_count <> @observations
    THROW 50031, 'Requested range is not available in the current dataset.', 1;

-- Warm both paths. When Ctrl+M is on SSMS also displays these warm-up plans;
-- inspect the repeated SELECTs after STATISTICS IO/TIME are enabled below.
SELECT @rowstore = SUM(log_return)
FROM dbo.ReturnsRowstore
WHERE asset_id = @asset_id AND trading_date >= @start_date AND trading_date < @end_date
OPTION (MAXDOP 1);
SELECT @columnstore = SUM(log_return)
FROM dbo.ReturnsColumnstore
WHERE asset_id = @asset_id AND trading_date >= @start_date AND trading_date < @end_date
OPTION (MAXDOP 1);

SET STATISTICS IO ON;
SET STATISTICS TIME ON;
SELECT @rowstore = SUM(log_return)
FROM dbo.ReturnsRowstore
WHERE asset_id = @asset_id AND trading_date >= @start_date AND trading_date < @end_date
OPTION (MAXDOP 1);
SELECT @columnstore = SUM(log_return)
FROM dbo.ReturnsColumnstore
WHERE asset_id = @asset_id AND trading_date >= @start_date AND trading_date < @end_date
OPTION (MAXDOP 1);
SET STATISTICS TIME OFF;
SET STATISTICS IO OFF;

IF @rowstore IS NULL OR @columnstore IS NULL OR ABS(@rowstore - @columnstore) > 1e-10
    THROW 50032, 'Rowstore and columnstore checksums differ.', 1;
SELECT @asset_id AS asset_id, @observations AS observations,
       @start_date AS start_date, @end_date AS end_date,
       @rowstore AS rowstore_sum, @columnstore AS columnstore_sum;
GO
