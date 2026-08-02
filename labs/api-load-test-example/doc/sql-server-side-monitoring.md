# SQL Server-Side Monitoring Requirements

## Context

Items (1) and (2) in `OpenTelemetryExtensions.cs` cover what the .NET driver can observe:
HTTP span enrichment, SQL exception classification, and all 16 `Microsoft.Data.SqlClient`
EventSource counters bridged into OpenTelemetry metrics. Those give you the **client-side** view.

This document captures the requirements for a complementary **server-side** monitoring layer
that queries SQL Server's Dynamic Management Views (DMVs) and publishes the results as
OpenTelemetry metrics. This is required because the driver cannot observe what is happening
inside SQL Server itself — queued requests, blocked sessions, long-running queries, wait stats,
and physical connection state all live exclusively in SQL Server memory.

---

## Goals

- Detect connection pool exhaustion **from the server's perspective** (not just the driver's).
- Identify long-running or blocking queries before they cascade into timeouts.
- Surface SQL Server wait statistics to explain _why_ queries are slow.
- Correlate server-side signals with the client-side metrics already emitted by the app.

---

## Approach: DMV-Polling Hosted Service

Implement a background `IHostedService` (`SqlServerDmvPoller`) that runs on a configurable
interval, executes a set of lightweight DMV queries against SQL Server using a dedicated
monitoring connection, and publishes the results as `ObservableGauge<T>` instruments through
a `System.Diagnostics.Metrics.Meter`. The OTel SDK collects these via `AddMeter()` and
exports them alongside the existing metrics.

The monitoring connection should use a separate, low-privilege SQL login with only
`VIEW SERVER STATE` permission. It must **not** share the application connection pool.

---

## Required Metrics

### 1. Active Sessions and Connections

| Metric Name                         | DMV Source                | Description                                                |
| ----------------------------------- | ------------------------- | ---------------------------------------------------------- |
| `sqlserver.sessions.active`         | `sys.dm_exec_sessions`    | Sessions with `status = 'running'`                         |
| `sqlserver.sessions.sleeping`       | `sys.dm_exec_sessions`    | Sessions with `status = 'sleeping'`                        |
| `sqlserver.connections.total`       | `sys.dm_exec_connections` | Total physical connections to the instance                 |
| `sqlserver.connections.by_database` | `sys.dm_exec_sessions`    | Connection count grouped by `database_name` (label per DB) |

**Pressure signal:** Compare server connection counts with the client-side connections-in-use and
connections-free gauges plus k6 failure and latency metrics. `sqlclient.pool.connections_stasis`
describes connections awaiting completion of an action; it is not a count of requests queued for
a pool slot and must not be used alone as an exhaustion alert.

---

### 2. Blocked and Waiting Sessions

| Metric Name                      | DMV Source             | Description                                      |
| -------------------------------- | ---------------------- | ------------------------------------------------ |
| `sqlserver.sessions.blocked`     | `sys.dm_exec_requests` | Sessions with `blocking_session_id != 0`         |
| `sqlserver.sessions.waiting`     | `sys.dm_exec_requests` | Sessions with `wait_time > 0`                    |
| `sqlserver.blocking.max_wait_ms` | `sys.dm_exec_requests` | Maximum `wait_time` (ms) across blocked sessions |

---

### 3. Long-Running Queries

| Metric Name                             | DMV Source             | Description                                        |
| --------------------------------------- | ---------------------- | -------------------------------------------------- |
| `sqlserver.requests.long_running_count` | `sys.dm_exec_requests` | Requests where `total_elapsed_time > threshold_ms` |
| `sqlserver.requests.max_elapsed_ms`     | `sys.dm_exec_requests` | Longest currently-running request duration         |

The threshold for "long-running" should be configurable (e.g., `SqlServerMonitorOptions.LongQueryThresholdMs`, default 5000 ms).

For observability, join `sys.dm_exec_requests` with `sys.dm_exec_sql_text` to capture the
SQL text of the longest-running query and emit it as a **span event** or structured log entry
rather than a metric label (to avoid high-cardinality metric explosion).

---

### 4. Wait Statistics

| Metric Name                           | DMV Source             | Description                                   |
| ------------------------------------- | ---------------------- | --------------------------------------------- |
| `sqlserver.waits.waiting_tasks_count` | `sys.dm_os_wait_stats` | Cumulative waiting task count per `wait_type` |
| `sqlserver.waits.wait_time_ms`        | `sys.dm_os_wait_stats` | Cumulative wait time (ms) per `wait_type`     |

Emit these as delta counters (snapshot the previous value each poll, emit the diff).
Filter to the most actionable wait types by default:

```
CXPACKET, PAGEIOLATCH_SH, PAGEIOLATCH_EX, LCK_M_X, LCK_M_S,
ASYNC_NETWORK_IO, WRITELOG, SOS_SCHEDULER_YIELD, RESOURCE_SEMAPHORE,
THREADPOOL
```

The filter list should be configurable via `SqlServerMonitorOptions.WaitTypesToTrack`.

---

### 5. Connection Pool Configuration Drift

| Metric Name                        | DMV Source           | Description                                                      |
| ---------------------------------- | -------------------- | ---------------------------------------------------------------- |
| `sqlserver.config.max_connections` | `sys.configurations` | Configured max connections on the instance                       |
| `sqlserver.pool.utilization_ratio` | Computed             | `sqlserver.connections.total / sqlserver.config.max_connections` |

---

## Implementation Requirements

### Configuration

All options should be bound from `appsettings.json` under a `SqlServerMonitor` key:

```json
{
  "SqlServerMonitor": {
    "Enabled": true,
    "PollingIntervalSeconds": 15,
    "LongQueryThresholdMs": 5000,
    "MonitoringConnectionString": "Server=...;User Id=monitor_user;Password=...;Pooling=false;",
    "WaitTypesToTrack": [
      "CXPACKET",
      "PAGEIOLATCH_SH",
      "LCK_M_X",
      "ASYNC_NETWORK_IO",
      "RESOURCE_SEMAPHORE"
    ]
  }
}
```

`MonitoringConnectionString` must be distinct from the application connection string and must
have `Pooling=false` to avoid consuming application pool slots.

### Permissions

The monitoring SQL login requires only:

```sql
GRANT VIEW SERVER STATE TO [monitor_user];
```

No data-read permissions are needed for DMV polling.

### Error Handling

- If the monitoring connection fails, log a warning and skip that poll cycle — do not throw.
- Emit a `sqlserver.monitor.poll_errors_total` counter that increments on each failed poll.
- After three consecutive failures, log an error and back off to `PollingIntervalSeconds * 5`
  until a successful poll resumes normal cadence.

### Lifecycle

- The service must stop gracefully on `CancellationToken` cancellation (i.e., `StopAsync`).
- Dispose the monitoring `SqlConnection` in `StopAsync`.
- Do not hold a persistent open connection between polls — open, query, close each cycle to
  avoid the monitoring connection itself showing as a stale session.

### Meter Registration

Register the meter in `AddAppTelemetryV2`:

```csharp
.AddMeter("SqlServer.DmvPoller")
```

The `SqlServerDmvPoller` hosted service should create its `Meter` with this name.

---

## Out of Scope for this Service

The following require Extended Events or SQL Server Agent and are intentionally excluded:

- Deadlock graphs — use Extended Events session `system_health` for these.
- Historical query plan capture — use Query Store.
- Replication or Always On health — separate monitoring concern.

---

## Acceptance Criteria

1. All metrics in the tables above are visible in Grafana within one polling interval of the
   condition occurring.
2. `sqlserver.sessions.blocked` > 0 generates a visible spike that can be correlated with client
   connection utilization, k6 latency, and k6 request failures.
3. `sqlserver.requests.long_running_count` reflects queries exceeding the configured threshold
   within one poll cycle.
4. The monitoring service does not consume any slots from the application connection pool
   (verify via `sqlclient.pool.pooled_connections` remaining stable while the poller runs).
5. A monitoring connection failure increments `sqlserver.monitor.poll_errors_total` and does
   not crash the application.
