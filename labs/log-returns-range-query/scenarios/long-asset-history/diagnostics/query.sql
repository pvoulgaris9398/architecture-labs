-- Invoked by capture.cs with typed parameters. Local variables and the assignment
-- aggregate mirror timing-baseline/query.sql; only the table token is substituted.
-- Instrumentation is enabled by the caller after one uninstrumented warm-up.
SET NOCOUNT ON;
DECLARE @asset_id int = @input_asset;
DECLARE @start_date date = @input_start;
DECLARE @end_date date = @input_end;
DECLARE @execution_checksum float;

SELECT @execution_checksum = SUM(log_return)
FROM dbo.$(TableName)
WHERE asset_id = @asset_id
  AND trading_date >= @start_date
  AND trading_date < @end_date
OPTION (MAXDOP 1);

SET @checksum_output = @execution_checksum;
