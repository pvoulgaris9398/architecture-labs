SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @run_id uniqueidentifier = '$(RunId)';
DECLARE @status varchar(20) = '$(RunStatus)';

IF @status = 'passed'
AND EXISTS
(
    SELECT 1
    FROM dbo.ExperimentRun run
    WHERE run.run_id = @run_id
      AND run.expected_benchmark_samples IS NOT NULL
      AND run.expected_benchmark_samples <>
          (SELECT COUNT(*) FROM dbo.BenchmarkSample sample WHERE sample.run_id = @run_id)
)
    SET @status = 'failed';

UPDATE dbo.ExperimentRun
SET completed_at = SYSDATETIME(), status = @status
WHERE run_id = @run_id;
GO
