IF DB_ID(N'LogReturnsLab') IS NULL
    CREATE DATABASE LogReturnsLab;
GO

USE LogReturnsLab;
GO

SET NOCOUNT ON;

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
