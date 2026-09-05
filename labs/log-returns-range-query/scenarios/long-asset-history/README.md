# Long Asset History

Status: in progress; timing baseline implemented, diagnostic companion available.

Compare rowstore and ordered columnstore for single-asset cumulative ranges under
warm-cache, `MAXDOP 1` conditions. Timing answers which layout is faster for a run;
diagnostics investigates the execution behavior behind it.

- [timing-baseline](timing-baseline/README.md): the existing 10-asset, 10-range sweep,
  with 9 samples of 500 executions per layout. Owns its runner and timing report.
- [diagnostics](diagnostics/README.md): a bounded subset of the same workload, using
  the current tables and saved baseline ranges to collect actual plans and resources.
- `shared/latest-timing-run.sql`: the successful-run selection used by both C# tools.
  Experiment-specific query execution stays within each sub-scenario.

From this directory, using Bash and .NET 10 SDK:

```bash
# Rebuilds the deterministic dataset and runs the full timing sweep; can take minutes.
(cd timing-baseline && ./run.sh)
# Reads stored measurements; no benchmark execution.
(cd timing-baseline && dotnet run report.cs)
# Executes 16 instrumented queries plus warm-ups against the existing tables.
(cd diagnostics && ./run.sh)
```

The timing scenario ID remains `long-asset-history-sweep`, so existing database
measurements remain usable. New timing reports live in `timing-baseline/results/local/`.
Previously generated `results/local/` files remain untouched; regenerate from the new
location when needed. Diagnostics keeps each capture in its own output directory.

Run diagnostics before rebuilding or running `ordered-build-quality`: saved checksums
can validate results but cannot prove that index layout matches a historical timing run.
`ordered-build-quality` remains a separate scenario that changes the columnstore build.
Do not run these workloads concurrently; they share tables and server resources.
