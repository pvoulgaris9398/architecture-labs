SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @run_id uniqueidentifier = '$(RunId)';

INSERT dbo.ExperimentRun (run_id, started_at, sql_server_version, status)
VALUES (@run_id, SYSDATETIME(), CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(256)), 'running');
GO
