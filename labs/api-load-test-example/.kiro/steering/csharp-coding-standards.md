---
inclusion: always
---

# C# Coding Standards

## General Style

- Top-level statements in `Program.cs` — no explicit `Program` class or `Main` method
- Implicit usings enabled — standard BCL namespaces do not need explicit `using` statements
- Nullable reference types enabled (`<Nullable>enable</Nullable>`) — no suppression of nullability warnings without justification
- `record` types for simple data carriers (e.g. `record Order(...)`) — prefer over classes for immutable query result shapes
- Verbatim string literals (`"""..."""`) for multi-line SQL strings — do not concatenate SQL across multiple string variables
- `var` for local variables where the type is obvious from the right-hand side

## Async

- All database calls are `async` / `await` — no `.Result` or `.Wait()` blocking calls
- `IHostedService` background work uses `CancellationToken` throughout and stops gracefully in `StopAsync`
- `Task.Delay` with a `CancellationToken` where applicable in retry loops

## Resource Management

- `SqlConnection` is always `using var` or `using (var ...)` — never stored as a field or reused across requests
- Connections are opened implicitly by Dapper (`QueryAsync` opens if closed) — no explicit `connection.Open()` needed
- `Meter` and `EventListener` are disposed in `Dispose()` overrides

## Dependency Injection and Hosted Services

- Telemetry bootstrapping goes in `WebApplicationBuilder` extension methods, not inline in `Program.cs`
- Background services that also need to be resolved as singletons are registered both ways:

  ```csharp
  builder.Services.AddSingleton<SqlClientEventBridge>();
  builder.Services.AddHostedService(sp => sp.GetRequiredService<SqlClientEventBridge>());
  ```

  This avoids the DI container creating two instances.

## Error Handling and Logging

- Use structured logging with message templates — never string interpolation in log calls:

  ```csharp
  // Correct
  logger.LogWarning("Seeding attempt {Attempt}/{Max} failed: {Message}", attempt, max, ex.Message);

  // Wrong
  logger.LogWarning($"Seeding attempt {attempt}/{max} failed: {ex.Message}");
  ```

- Retry loops use `catch (Exception ex) when (attempt < maxRetries)` exception filters to
  let the final attempt throw naturally rather than swallowing it
- Infrastructure startup failures (DB not ready) are retried with bounded attempts and logged
  at `Warning` per attempt, `Error` on final failure — never silently ignored

## SQL Conventions

- Parameterised queries via Dapper anonymous objects — never string-concatenated SQL:

  ```csharp
  // Correct
  connection.QueryAsync<Order>("SELECT ... WHERE CustomerId = @CustomerId", new { CustomerId = id });

  // Wrong
  connection.QueryAsync<Order>($"SELECT ... WHERE CustomerId = {id}");
  ```

- DDL statements (CREATE TABLE, CREATE INDEX, DROP INDEX) are always guarded with
  `IF NOT EXISTS` / `IF EXISTS` checks so they are safe to run multiple times
- `GO` batch separators in `.sql` files are split manually before execution because
  `SqlConnection` does not understand `GO` — see `SeedDatabaseAsync` in `Program.cs`
- SQL scripts use `USE <database>;` + `GO` to target the correct database explicitly

## Thread Safety in EventListener Bridges

When bridging `EventSource` counters to OTel metrics the callback and collection threads are
different. Always protect the shared value dictionary with a `Lock`:

```csharp
private readonly Lock _valuesLock = new();

// Writer (EventListener callback thread)
lock (_valuesLock) { _latestValues[name] = value; }

// Reader (OTel collection callback)
lock (_valuesLock) { return _latestValues.TryGetValue(...); }
```

Use `Lock` (the new .NET 9+ type) rather than `object` + `lock` statement.

## Extension Method Versioning

When iterating on a complex configuration extension method, keep the previous version as a
named artifact (`V1`, `V2`, etc.) rather than overwriting it. This preserves the teaching
progression and lets readers see what changed between versions. The current production version
is always the highest-numbered one (`AddAppTelemetryV2`).

## Connection String Management

Connection strings are constants at the top of `Program.cs`. Key settings for this project:

- `TrustServerCertificate=True` — required for the self-signed cert on the SQL Server container
- `Max Pool Size=150` — explicitly set; default is 100
- `Pooling=false` — used only on monitoring/diagnostic connections that must not consume app pool slots
- Separate connection strings per logical database (`master` vs `LoadTestDb`) — do not reuse
  a connection string by substituting the database name at runtime

