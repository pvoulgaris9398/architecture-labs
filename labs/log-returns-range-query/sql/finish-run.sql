SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @run_id uniqueidentifier = '$(RunId)';
DECLARE @status varchar(20) = '$(RunStatus)';

UPDATE dbo.ExperimentRun
SET completed_at = SYSDATETIME(), status = @status
WHERE run_id = @run_id;
GO
