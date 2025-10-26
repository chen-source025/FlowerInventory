-- cSpell:ignore FULLSCAN
-- FlowerInventory 資料庫優化腳本
-- 在 SQL Server Management Studio 中執行

USE [FlowerInventoryDb];
GO

-- 1. 複合索引提升查詢效能
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Transactions_FlowerId_Date')
BEGIN
    CREATE NONCLUSTERED INDEX IX_Transactions_FlowerId_Date 
    ON [dbo].[Transactions] ([FlowerId], [TransactionDate] DESC)
    INCLUDE ([ChangeQty], [TransactionType]);
    PRINT '建立索引 IX_Transactions_FlowerId_Date';
END
ELSE
    PRINT '索引 IX_Transactions_FlowerId_Date 已存在';

-- 2. 包含性索引
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Batches_Status_Expiry')
BEGIN
    CREATE NONCLUSTERED INDEX IX_Batches_Status_Expiry 
    ON [dbo].[Batches] ([Status]) 
    INCLUDE ([ExpiryDate], [QuantityPassed], [FlowerId]);
    PRINT '建立索引 IX_Batches_Status_Expiry';
END
ELSE
    PRINT '索引 IX_Batches_Status_Expiry 已存在';

-- 3. 花卉查詢優化索引
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Flowers_Category_ABC')
BEGIN
    CREATE NONCLUSTERED INDEX IX_Flowers_Category_ABC 
    ON [dbo].[Flowers] ([Category], [ABCClass])
    INCLUDE ([Name], [Price], [ShelfLifeDays]);
    PRINT '建立索引 IX_Flowers_Category_ABC';
END
ELSE
    PRINT '索引 IX_Flowers_Category_ABC 已存在';

-- 4. 建立庫存預警預存程序
IF OBJECT_ID('sp_GetInventoryAlerts', 'P') IS NOT NULL
    DROP PROCEDURE sp_GetInventoryAlerts;
GO

CREATE PROCEDURE sp_GetInventoryAlerts
AS
BEGIN
    SET NOCOUNT ON;
    
    SELECT 
        f.Id as FlowerId,
        f.Name as FlowerName,
        f.ABCClass,
        f.Category,
        ISNULL(SUM(b.QuantityPassed), 0) as CurrentStock,
        f.Price,
        ISNULL(SUM(b.QuantityPassed), 0) * f.Price as TotalValue,
        CASE 
            WHEN ISNULL(SUM(b.QuantityPassed), 0) = 0 THEN '缺貨'
            WHEN ISNULL(SUM(b.QuantityPassed), 0) < f.LeadTimeDays * 2 THEN '緊急補貨'
            WHEN ISNULL(SUM(b.QuantityPassed), 0) < f.LeadTimeDays * 4 THEN '建議補貨'
            ELSE '正常'
        END as AlertLevel
    FROM Flowers f
    LEFT JOIN Batches b ON f.Id = b.FlowerId AND b.Status = 3 -- Active status
    GROUP BY f.Id, f.Name, f.ABCClass, f.Category, f.Price, f.LeadTimeDays
    HAVING ISNULL(SUM(b.QuantityPassed), 0) < f.LeadTimeDays * 4  -- 庫存少於4天需求
    ORDER BY AlertLevel, f.ABCClass;
END
GO
PRINT '建立預存程序 sp_GetInventoryAlerts';

-- 5. 建立交易統計預存程序
IF OBJECT_ID('sp_GetTransactionSummary', 'P') IS NOT NULL
    DROP PROCEDURE sp_GetTransactionSummary;
GO

CREATE PROCEDURE sp_GetTransactionSummary
    @StartDate DATE,
    @EndDate DATE
AS
BEGIN
    SET NOCOUNT ON;
    
    SELECT 
        f.Name as FlowerName,
        f.Category,
        COUNT(*) as TransactionCount,
        SUM(CASE WHEN t.TransactionType = 1 THEN t.ChangeQty ELSE 0 END) as TotalInbound,
        SUM(CASE WHEN t.TransactionType = 2 THEN ABS(t.ChangeQty) ELSE 0 END) as TotalOutbound,
        AVG(CASE WHEN t.TransactionType = 2 THEN ABS(t.ChangeQty) ELSE NULL END) as AvgDailyOutbound
    FROM Transactions t
    INNER JOIN Flowers f ON t.FlowerId = f.Id
    WHERE t.TransactionDate BETWEEN @StartDate AND @EndDate
    GROUP BY f.Name, f.Category
    ORDER BY TotalOutbound DESC;
END
GO
PRINT '建立預存程序 sp_GetTransactionSummary';

-- 6. 建立批次到期預警預存程序
IF OBJECT_ID('sp_GetExpiringBatches', 'P') IS NOT NULL
    DROP PROCEDURE sp_GetExpiringBatches;
GO

CREATE PROCEDURE sp_GetExpiringBatches
    @DaysThreshold INT = 7
AS
BEGIN
    SET NOCOUNT ON;
    
    SELECT 
        b.BatchNo,
        f.Name as FlowerName,
        b.QuantityPassed,
        b.ExpiryDate,
        DATEDIFF(DAY, GETDATE(), b.ExpiryDate) as DaysUntilExpiry,
        CASE 
            WHEN DATEDIFF(DAY, GETDATE(), b.ExpiryDate) <= 3 THEN '緊急'
            WHEN DATEDIFF(DAY, GETDATE(), b.ExpiryDate) <= 7 THEN '注意'
            ELSE '正常'
        END as AlertLevel
    FROM Batches b
    INNER JOIN Flowers f ON b.FlowerId = f.Id
    WHERE b.ExpiryDate IS NOT NULL 
        AND b.ExpiryDate <= DATEADD(DAY, @DaysThreshold, GETDATE())
        AND b.Status = 3 -- Active batches only
        AND b.QuantityPassed > 0
    ORDER BY b.ExpiryDate ASC;
END
GO
PRINT '建立預存程序 sp_GetExpiringBatches';

-- 7. 更新統計資訊
PRINT '開始更新統計資訊...';
UPDATE STATISTICS Flowers WITH FULLSCAN;
UPDATE STATISTICS Batches WITH FULLSCAN;
UPDATE STATISTICS Transactions WITH FULLSCAN;
PRINT '統計資訊更新完成';

-- 8. 顯示優化結果
PRINT '=== 資料庫優化完成 ===';
PRINT '已建立的索引:';
PRINT '- IX_Transactions_FlowerId_Date (交易查詢優化)';
PRINT '- IX_Batches_Status_Expiry (批次狀態查詢優化)';
PRINT '- IX_Flowers_Category_ABC (花卉分類查詢優化)';
PRINT '';
PRINT '已建立的預存程序:';
PRINT '- sp_GetInventoryAlerts (庫存預警)';
PRINT '- sp_GetTransactionSummary (交易統計)';
PRINT '- sp_GetExpiringBatches (到期批次預警)';
GO