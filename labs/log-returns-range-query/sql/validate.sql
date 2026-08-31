SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @expected_rows bigint = 10000000;
DECLARE @absolute_tolerance float = 1e-12;
DECLARE @relative_tolerance float = 1e-12;
DECLARE @run_id uniqueidentifier = '$(RunId)';
DECLARE @rowstore_rows bigint = (SELECT COUNT_BIG(*) FROM dbo.ReturnsRowstore);
DECLARE @columnstore_rows bigint = (SELECT COUNT_BIG(*) FROM dbo.ReturnsColumnstore);

IF @rowstore_rows <> @expected_rows OR @columnstore_rows <> @expected_rows
    THROW 50001, 'Validation failed: an unexpected number of generated rows was found.', 1;

-- 1900-01-01 was a Monday, so this check is independent of DATEFIRST and language settings.
IF EXISTS
(
    SELECT 1
    FROM dbo.ReturnsRowstore
    WHERE DATEDIFF(day, CONVERT(date, '1900-01-01'), trading_date) % 7 IN (5, 6)
)
OR EXISTS
(
    SELECT 1
    FROM dbo.ReturnsColumnstore
    WHERE DATEDIFF(day, CONVERT(date, '1900-01-01'), trading_date) % 7 IN (5, 6)
)
    THROW 50002, 'Validation failed: weekend observations were generated.', 1;

IF EXISTS
(
    SELECT 1
    FROM dbo.ReturnsRowstore
    WHERE simple_return <= -1.0
       OR ABS(log_return - LOG(1.0 + simple_return)) > @absolute_tolerance
)
OR EXISTS
(
    SELECT 1
    FROM dbo.ReturnsColumnstore
    WHERE simple_return <= -1.0
       OR ABS(log_return - LOG(1.0 + simple_return)) > @absolute_tolerance
)
    THROW 50003, 'Validation failed: generated return invariants were violated.', 1;

IF EXISTS
(
    SELECT asset_id, trading_date, simple_return, log_return FROM dbo.ReturnsRowstore
    EXCEPT
    SELECT asset_id, trading_date, simple_return, log_return FROM dbo.ReturnsColumnstore
)
OR EXISTS
(
    SELECT asset_id, trading_date, simple_return, log_return FROM dbo.ReturnsColumnstore
    EXCEPT
    SELECT asset_id, trading_date, simple_return, log_return FROM dbo.ReturnsRowstore
)
    THROW 50004, 'Validation failed: rowstore and columnstore data differ.', 1;

CREATE TABLE #Scenarios
(
    scenario_id varchar(100) NOT NULL UNIQUE,
    scenario varchar(40) NOT NULL PRIMARY KEY,
    asset_id int NOT NULL,
    start_date date NOT NULL,
    end_date date NOT NULL,
    expect_observations bit NOT NULL
);

INSERT #Scenarios (scenario_id, scenario, asset_id, start_date, end_date, expect_observations)
VALUES
    ('single-trading-day', 'one trading day', 42, '1990-01-01', '1990-01-02', 1),
    ('cross-weekend', 'range crossing a weekend', 42, '1990-01-05', '1990-01-09', 1),
    ('empty-weekend', 'empty weekend range', 42, '1990-01-06', '1990-01-07', 0);

INSERT #Scenarios (scenario_id, scenario, asset_id, start_date, end_date, expect_observations)
SELECT 'trading-year-252', '252 trading observations', 42,
    MIN(trading_date), DATEADD(day, 1, MAX(trading_date)), 1
FROM
(
    SELECT TOP (252) trading_date
    FROM dbo.ReturnsRowstore
    WHERE asset_id = 42
    ORDER BY trading_date
) trading_year;

INSERT #Scenarios (scenario_id, scenario, asset_id, start_date, end_date, expect_observations)
SELECT 'full-security-history', 'full security history', 42,
    MIN(trading_date), DATEADD(day, 1, MAX(trading_date)), 1
FROM dbo.ReturnsRowstore
WHERE asset_id = 42;

CREATE TABLE #ValidationResults
(
    scenario_id varchar(100) NOT NULL,
    scenario varchar(40) NOT NULL,
    storage_type varchar(20) NOT NULL,
    asset_id int NOT NULL,
    start_date date NOT NULL,
    end_date date NOT NULL,
    observation_count bigint NOT NULL,
    compounded_simple_return float NULL,
    compounded_log_return float NULL,
    absolute_delta float NULL,
    relative_delta float NULL,
    passed bit NOT NULL
);

INSERT #ValidationResults
SELECT
    scenario.scenario_id,
    scenario.scenario,
    storage.storage_type,
    scenario.asset_id,
    scenario.start_date,
    scenario.end_date,
    aggregates.observation_count,
    calculated.compounded_simple_return,
    calculated.compounded_log_return,
    delta.absolute_delta,
    CASE
        WHEN delta.absolute_delta IS NULL THEN NULL
        WHEN scale.denominator = 0.0 THEN delta.absolute_delta
        ELSE delta.absolute_delta / scale.denominator
    END,
    CASE
        WHEN scenario.expect_observations = 0
            THEN IIF(aggregates.observation_count = 0
                AND calculated.compounded_simple_return IS NULL
                AND calculated.compounded_log_return IS NULL, 1, 0)
        WHEN aggregates.observation_count > 0
            AND calculated.compounded_simple_return IS NOT NULL
            AND calculated.compounded_log_return IS NOT NULL
            AND (delta.absolute_delta <= @absolute_tolerance
                OR delta.absolute_delta / NULLIF(scale.denominator, 0.0) <= @relative_tolerance)
            THEN 1
        ELSE 0
    END
FROM #Scenarios scenario
CROSS JOIN (VALUES ('rowstore'), ('columnstore')) storage(storage_type)
OUTER APPLY
(
    SELECT
        COUNT_BIG(returns.simple_return) AS observation_count,
        PRODUCT(1.0 + returns.simple_return) - 1.0 AS compounded_simple_return,
        EXP(SUM(returns.log_return)) - 1.0 AS compounded_log_return
    FROM
    (
        SELECT simple_return, log_return
        FROM dbo.ReturnsRowstore
        WHERE storage.storage_type = 'rowstore'
          AND asset_id = scenario.asset_id
          AND trading_date >= scenario.start_date
          AND trading_date < scenario.end_date
        UNION ALL
        SELECT simple_return, log_return
        FROM dbo.ReturnsColumnstore
        WHERE storage.storage_type = 'columnstore'
          AND asset_id = scenario.asset_id
          AND trading_date >= scenario.start_date
          AND trading_date < scenario.end_date
    ) returns
) aggregates
CROSS APPLY
(
    VALUES (aggregates.compounded_simple_return, aggregates.compounded_log_return)
) calculated(compounded_simple_return, compounded_log_return)
CROSS APPLY
(
    VALUES (ABS(calculated.compounded_simple_return - calculated.compounded_log_return))
) delta(absolute_delta)
CROSS APPLY
(
    VALUES
    (
        CASE
            WHEN ABS(calculated.compounded_simple_return) >= ABS(calculated.compounded_log_return)
                THEN ABS(calculated.compounded_simple_return)
            ELSE ABS(calculated.compounded_log_return)
        END
    )
) scale(denominator);

INSERT dbo.ScenarioResult
(
    run_id, scenario_id, result_type, storage_type, asset_id, start_date, end_date,
    observation_count, simple_return_result, log_return_result, absolute_delta,
    relative_delta, passed
)
SELECT
    @run_id, scenario_id, 'correctness', storage_type, asset_id, start_date, end_date,
    observation_count, compounded_simple_return, compounded_log_return, absolute_delta,
    relative_delta, passed
FROM #ValidationResults;

SELECT
    scenario,
    storage_type,
    asset_id,
    start_date,
    end_date,
    observation_count,
    compounded_simple_return,
    compounded_log_return,
    absolute_delta,
    relative_delta,
    passed
FROM #ValidationResults
ORDER BY scenario, storage_type;

IF EXISTS (SELECT 1 FROM #ValidationResults WHERE passed = 0)
    THROW 50005, 'Validation failed: canonical return calculations disagree.', 1;

PRINT 'Correctness validation passed.';
GO
