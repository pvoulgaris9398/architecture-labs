USE LogReturnsLab;
GO

SET NOCOUNT ON;

DECLARE @observation_count int = 5000;
DECLARE @start_date date = '1990-01-01';
DECLARE @late_crossover_asset int = 112;
DECLARE @early_crossover_asset int = 778;
DECLARE @late_end_date date;
DECLARE @early_end_date date;

SELECT @late_end_date = DATEADD(day, 1, MAX(trading_date))
FROM
(
    SELECT TOP (@observation_count) trading_date
    FROM dbo.ReturnsColumnstore
    WHERE asset_id = @late_crossover_asset
    ORDER BY trading_date
) observations;

SELECT @early_end_date = DATEADD(day, 1, MAX(trading_date))
FROM
(
    SELECT TOP (@observation_count) trading_date
    FROM dbo.ReturnsColumnstore
    WHERE asset_id = @early_crossover_asset
    ORDER BY trading_date
) observations;

SET STATISTICS IO ON;
SET STATISTICS TIME ON;

SELECT
    @late_crossover_asset AS asset_id,
    COUNT_BIG(*) AS observation_count,
    SUM(log_return) AS cumulative_log_return
FROM dbo.ReturnsColumnstore
WHERE asset_id = @late_crossover_asset
  AND trading_date >= @start_date
  AND trading_date < @late_end_date
OPTION (MAXDOP 1);

SELECT
    @early_crossover_asset AS asset_id,
    COUNT_BIG(*) AS observation_count,
    SUM(log_return) AS cumulative_log_return
FROM dbo.ReturnsColumnstore
WHERE asset_id = @early_crossover_asset
  AND trading_date >= @start_date
  AND trading_date < @early_end_date
OPTION (MAXDOP 1);

SET STATISTICS TIME OFF;
SET STATISTICS IO OFF;
GO
