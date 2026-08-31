# SQL Server Log Returns Storage

Status: in progress

## Question

How does a clustered rowstore index compare with an ordered clustered columnstore index when
summing precomputed log returns over asset and date ranges?

The initial experiment changes only the storage/index type. Both tables contain the same generated
data and receive the same single-threaded queries. The benchmark reports timings, not a conclusion.

The deterministic dataset contains 1,000 securities with 10,000 Monday-through-Friday observations
each. Exchange holidays are deliberately out of scope. Each log return is derived from its simple
return with `LOG(1 + simple_return)`.

## Run

Copy `.env.example` to `.env`, then run:

```bash
docker compose up -d
./run.sh
```

The setup creates 10,000,000 rows in each table. Before benchmarking, `run.sh` verifies dataset
invariants and compares `PRODUCT(1 + simple_return) - 1` with `EXP(SUM(log_return)) - 1` for fixed
canonical ranges. A validation failure stops the run. It then runs warm-cache queries against both
tables. Stop the container with `docker compose down`; add `--volumes` only if you intentionally
want to delete the lab database.

## Connect with sqlcmd

With the container running and `sqlcmd` installed on your host, connect through port `1435`:

```bash
sqlcmd -S localhost,1435 -U sa -d LogReturnsLab -C
```

Enter the password from `.env` when prompted. At the `sqlcmd` prompt, try:

```sql
SELECT COUNT(*) FROM dbo.ReturnsRowstore;
GO
EXIT
```

The earlier Python application-code comparison is retained in `notes/python-baseline/`. ClickHouse,
Snowflake, and dbt are possible later phases but are not implemented yet.
