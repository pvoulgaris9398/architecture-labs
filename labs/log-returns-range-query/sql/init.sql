IF DB_ID(N'LogReturnsLab') IS NULL
    CREATE DATABASE LogReturnsLab;
GO

USE LogReturnsLab;
GO

SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.ExperimentRun', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ExperimentRun
    (
        run_id uniqueidentifier NOT NULL PRIMARY KEY,
        started_at datetime2 NOT NULL,
        completed_at datetime2 NULL,
        sql_server_version nvarchar(256) NOT NULL,
        status varchar(20) NOT NULL,
        CONSTRAINT CK_ExperimentRun_Status CHECK (status IN ('running', 'passed', 'failed'))
    );
END;

IF OBJECT_ID(N'dbo.ScenarioResult', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ScenarioResult
    (
        run_id uniqueidentifier NOT NULL,
        scenario_id varchar(100) NOT NULL,
        result_type varchar(20) NOT NULL,
        storage_type varchar(20) NOT NULL,
        asset_id int NULL,
        start_date date NULL,
        end_date date NULL,
        observation_count bigint NULL,
        simple_return_result float NULL,
        log_return_result float NULL,
        absolute_delta float NULL,
        relative_delta float NULL,
        elapsed_ms bigint NULL,
        checksum float NULL,
        passed bit NOT NULL,
        CONSTRAINT PK_ScenarioResult
            PRIMARY KEY (run_id, scenario_id, result_type, storage_type),
        CONSTRAINT FK_ScenarioResult_ExperimentRun
            FOREIGN KEY (run_id) REFERENCES dbo.ExperimentRun(run_id),
        CONSTRAINT CK_ScenarioResult_Type CHECK (result_type IN ('correctness', 'benchmark'))
    );
END;
GO

DROP TABLE IF EXISTS dbo.ReturnsRowstore;
DROP TABLE IF EXISTS dbo.ReturnsColumnstore;

CREATE TABLE dbo.ReturnsRowstore
(
    asset_id int NOT NULL,
    trading_date date NOT NULL,
    simple_return float NOT NULL,
    log_return float NOT NULL,
    CONSTRAINT PK_ReturnsRowstore PRIMARY KEY CLUSTERED (asset_id, trading_date)
);

CREATE TABLE dbo.ReturnsColumnstore
(
    asset_id int NOT NULL,
    trading_date date NOT NULL,
    simple_return float NOT NULL,
    log_return float NOT NULL
);
GO

WITH Digits AS
(
    SELECT digit FROM (VALUES (0),(1),(2),(3),(4),(5),(6),(7),(8),(9)) value(digit)
),
Numbers AS
(
    SELECT TOP (10000000)
        ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) - 1 AS number
    FROM Digits a CROSS JOIN Digits b CROSS JOIN Digits c
    CROSS JOIN Digits d CROSS JOIN Digits e CROSS JOIN Digits f
    CROSS JOIN Digits g
)
INSERT dbo.ReturnsRowstore WITH (TABLOCK) (asset_id, trading_date, simple_return, log_return)
SELECT
    CAST(number / 10000 AS int) + 1,
    DATEADD(day, observation_number + 2 * (observation_number / 5), CONVERT(date, '1990-01-01')),
    simple_return,
    LOG(1.0 + simple_return)
FROM Numbers
CROSS APPLY (VALUES (CAST(number % 10000 AS int))) observation(observation_number)
CROSS APPLY (VALUES (SIN(CAST(number AS float) * 0.017) * 0.02)) generated(simple_return);

INSERT dbo.ReturnsColumnstore WITH (TABLOCK) (asset_id, trading_date, simple_return, log_return)
SELECT asset_id, trading_date, simple_return, log_return
FROM dbo.ReturnsRowstore
ORDER BY asset_id, trading_date;

CREATE CLUSTERED COLUMNSTORE INDEX CCI_ReturnsColumnstore
ON dbo.ReturnsColumnstore
ORDER (asset_id, trading_date);

UPDATE STATISTICS dbo.ReturnsRowstore WITH FULLSCAN;
UPDATE STATISTICS dbo.ReturnsColumnstore WITH FULLSCAN;
GO
