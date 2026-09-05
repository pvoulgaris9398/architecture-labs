/*

Purpose: Inspect the last known actual plans for cached lab statements and check
whether last-plan capture and persistent Query Store history are enabled.

Usage: Connect to localhost,1435 in SSMS. Run the status checks below. If capture
is off, optionally select and execute the commented ALTER statement, rerun the
lab workload, then run the plan query. Click last_actual_plan in the results.
SQL Server 2025; DMV access requires VIEW SERVER PERFORMANCE STATE. Changing
the scoped setting requires ALTER ANY DATABASE SCOPED CONFIGURATION (sa has both).

The script is read-only by default. Enabling capture changes database configuration
and adds diagnostic overhead; keep the setting consistent across benchmark runs.
Plans depend on cache residency and eligibility; NULL or reduced detail is possible.
The plan can cover the whole batch. Multiple statement rows can share that plan;
its last execution need not match every statement's last execution time.

Alternative: In SSMS press Ctrl+M before running a scenario to collect its actual
plan directly. ResourceUsage.sql returns compiled plans and resource counters.
Query Store retains interval runtime/wait aggregates and compiled plans, rather
than a per-execution actual-plan history. This script does not enable Query Store.

*/
USE LogReturnsLab;
GO

SELECT @@VERSION AS engine_version;

SELECT name, value
FROM sys.database_scoped_configurations
WHERE name = N'LAST_QUERY_PLAN_STATS';

SELECT actual_state_desc, desired_state_desc, wait_stats_capture_mode_desc
FROM sys.database_query_store_options;
GO

-- Optional: enable capture BEFORE rerunning the workload.
-- ALTER DATABASE SCOPED CONFIGURATION SET LAST_QUERY_PLAN_STATS = ON;
-- Restore OFF afterward only if that was the original setting:
-- ALTER DATABASE SCOPED CONFIGURATION SET LAST_QUERY_PLAN_STATS = OFF;

SELECT TOP (50)
    qs.last_execution_time,
    qs.query_hash,
    qs.query_plan_hash,
    stmt.statement_text,
    qp.query_plan AS last_actual_plan
FROM sys.dm_exec_query_stats AS qs
CROSS APPLY sys.dm_exec_sql_text(qs.sql_handle) AS txt
CROSS APPLY sys.dm_exec_plan_attributes(qs.plan_handle) AS attr
CROSS APPLY
(
    VALUES
    (
        SUBSTRING(
            txt.text,
            qs.statement_start_offset / 2 + 1,
            (
                CASE qs.statement_end_offset
                    WHEN -1 THEN DATALENGTH(txt.text)
                    ELSE qs.statement_end_offset
                END - qs.statement_start_offset
            ) / 2 + 1
        )
    )
) AS stmt(statement_text)
OUTER APPLY sys.dm_exec_query_plan_stats(qs.plan_handle) AS qp
WHERE attr.attribute = 'dbid'
  AND CONVERT(int, attr.value) = DB_ID()
  AND (
      stmt.statement_text LIKE N'%ReturnsRowstore%'
      OR stmt.statement_text LIKE N'%ReturnsColumnstore%'
  )
  AND stmt.statement_text NOT LIKE N'%sys.dm_exec_query_stats%'
ORDER BY qs.last_execution_time DESC;
