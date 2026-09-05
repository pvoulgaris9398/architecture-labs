/*

VIEW SERVER PERFORMANCE STATE required for DMV's

Purpose: Compare CPU, elapsed time, reads, memory grants, spills, parallelism,
and columnstore segment elimination for completed rowstore/columnstore queries.

Usage: Connect to localhost,1435 in SSMS and run after the lab workload.
SQL Server 2025; requires VIEW SERVER PERFORMANCE STATE (sa already has access).

Each row represents a cached statement. Last values describe its latest completed
execution; averages can mix parameter values. Statistics disappear with eviction
or recompilation and are not persistent benchmark history.
Times are milliseconds; grants are KB; spills are pages. Memory grants exclude
buffer-cache memory. Rows returned are not rows scanned (SUM can return one row).
Click cached_plan in SSMS for the compiled plan, without actual runtime counters.

Options: Change TOP (50) or ORDER BY to explore expensive queries. For last actual
plans, use LastActualExecutionPlans.sql. For live waits, use CurrentActivity.sql.

*/
USE LogReturnsLab;
GO

SELECT TOP (50)
    qs.last_execution_time,
    qs.execution_count,
    qs.query_hash,
    qs.query_plan_hash,

    -- Most recent execution; convert microseconds to milliseconds.
    qs.last_elapsed_time / 1000.0 AS last_elapsed_ms,
    qs.last_worker_time  / 1000.0 AS last_cpu_ms,

    -- Averages across executions of this cached statement.
    qs.total_elapsed_time / 1000.0
        / NULLIF(qs.execution_count, 0) AS avg_elapsed_ms,
    qs.total_worker_time / 1000.0
        / NULLIF(qs.execution_count, 0) AS avg_cpu_ms,

    qs.last_logical_reads,
    qs.last_physical_reads,
    qs.last_logical_writes,

    qs.last_grant_kb      AS granted_memory_kb,
    qs.last_used_grant_kb AS used_grant_memory_kb,
    qs.last_ideal_grant_kb AS ideal_grant_memory_kb,
    qs.last_spills        AS spilled_pages,

    qs.last_rows AS rows_returned,
    qs.last_dop  AS degree_of_parallelism,
    qs.last_columnstore_segment_reads AS segments_read,
    qs.last_columnstore_segment_skips AS segments_skipped,

    stmt.statement_text,
    qp.query_plan AS cached_plan
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
OUTER APPLY sys.dm_exec_query_plan(qs.plan_handle) AS qp
WHERE attr.attribute = 'dbid'
  AND CONVERT(int, attr.value) = DB_ID()
  AND (
      stmt.statement_text LIKE N'%ReturnsRowstore%'
      OR stmt.statement_text LIKE N'%ReturnsColumnstore%'
  )
  AND stmt.statement_text NOT LIKE N'%sys.dm_exec_query_stats%'
ORDER BY qs.last_execution_time DESC;

