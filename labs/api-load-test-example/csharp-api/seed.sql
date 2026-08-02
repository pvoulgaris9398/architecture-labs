-- Creates the load test database and a table with 500k rows.
-- Designed to be run once on first container start.
-- The Orders table has no index on CustomerId initially, forcing a full table
-- scan on the /v1/orders/by-customer endpoint. Use POST /v1/add-index to add the index
-- between load test runs to observe the performance difference.

USE master;
GO

IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = 'LoadTestDb')
BEGIN
    CREATE DATABASE LoadTestDb;
END
GO

USE LoadTestDb;
GO

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Orders')
BEGIN
    CREATE TABLE Orders (
        OrderId     INT IDENTITY(1,1) PRIMARY KEY,
        CustomerId  INT           NOT NULL,
        OrderDate   DATETIME2     NOT NULL,
        Status      NVARCHAR(20)  NOT NULL,
        Amount      DECIMAL(10,2) NOT NULL
    );

    -- Seed 500,000 rows. CustomerId is spread across 10,000 distinct customers
    -- so each customer has ~50 orders — enough data to make a scan hurt.
    WITH Nums AS (
        SELECT TOP (500000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n
        FROM sys.all_columns a CROSS JOIN sys.all_columns b
    )
    INSERT INTO Orders (CustomerId, OrderDate, Status, Amount)
    SELECT
        (n % 10000) + 1,
        DATEADD(second, -n, GETUTCDATE()),
        CASE (n % 4)
            WHEN 0 THEN 'Pending'
            WHEN 1 THEN 'Shipped'
            WHEN 2 THEN 'Delivered'
            ELSE 'Cancelled'
        END,
        CAST((n % 500) + 1 AS DECIMAL(10,2))
    FROM Nums;
END
GO

