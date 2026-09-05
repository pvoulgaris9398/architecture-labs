/*

Purpose: Inspect currently executing lab requests, resource counters, waits,
and blocking sessions.

Usage: Connect to localhost,1435 in a second SSMS window; run while a workload
executes in the first window. Rerun to refresh. Short queries may be missed.
SQL Server 2025; requires VIEW SERVER PERFORMANCE STATE (sa already has access).

CPU, elapsed, and current wait times are milliseconds. Wait columns describe
the current request wait, not accumulated wait history. Parallel requests have
coordinator-thread reporting limitations, including read counters in row mode.
Use ResourceUsage.sql after completion for comparisons. batch_text is the entire
batch, which may contain more than the currently executing statement.

*/
USE LogReturnsLab;
GO

SELECT
    r.session_id,
    r.status,
    r.command,
    r.cpu_time AS cpu_ms,
    r.total_elapsed_time AS elapsed_ms,
    r.logical_reads,
    r.reads,
    r.writes,
    r.dop,
    r.wait_type,
    r.wait_time AS current_wait_ms,
    r.wait_resource,
    r.blocking_session_id,
    txt.text AS batch_text
FROM sys.dm_exec_requests AS r
OUTER APPLY sys.dm_exec_sql_text(r.sql_handle) AS txt
WHERE r.database_id = DB_ID()
  AND r.session_id <> @@SPID
ORDER BY r.total_elapsed_time DESC;
