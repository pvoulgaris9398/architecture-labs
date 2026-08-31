# Narrow Lookup

## Question

How do the rowstore and ordered columnstore layouts behave when one security's returns are aggregated
over one calendar year?

This is the selective, index-friendly baseline. The working hypothesis is that the clustered
rowstore key `(asset_id, trading_date)` will support this access pattern well. Treat that as a
hypothesis until the measurements and actual execution plans support it.

## Run in SSMS

1. Connect to `localhost,1435` and open `query.sql`.
2. Enable **Include Actual Execution Plan** with `Ctrl+M`.
3. Run the entire script. It warms both access paths before enabling measured statistics.
4. Review the result grids, the **Messages** tab, and the **Execution Plan** tab.
5. Run the entire script several times to observe warm-cache variation.

The script is read-only and re-runnable. Change the three parameters at the top to explore another
security or half-open date range. Keep the same parameters for both layouts.

## Review

Confirm first that both result rows have the same observation count and that `return_from_logs` and
`compounded_simple_return` agree within floating-point tolerance. Then compare:

- elapsed and CPU time in the **Messages** tab;
- logical reads in the **Messages** tab;
- seek versus scan behavior;
- row mode versus batch mode;
- estimated versus actual row counts;
- columnstore rowgroups and segments read or skipped;
- plan warnings.

The script fixes `MAXDOP 1` and uses a warm cache. It does not alternate execution order, collect
repetitions, or calculate a median, so interactive timings are exploratory rather than benchmark
conclusions.

