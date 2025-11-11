-- FlowerInventory PostgreSQL 資料庫優化腳本
-- 在 PostgreSQL 中執行

-- 1. 複合索引提升查詢效能
DO $$ 
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_indexes WHERE indexname = 'ix_transactions_flowerid_date') THEN
        CREATE INDEX ix_transactions_flowerid_date 
        ON "Transactions" ("FlowerId", "TransactionDate" DESC, "ChangeQty", "TransactionType");
        RAISE NOTICE '建立索引 ix_transactions_flowerid_date';
    ELSE
        RAISE NOTICE '索引 ix_transactions_flowerid_date 已存在';
    END IF;
END $$;

-- 2. 包含性索引（使用複合索引替代 INCLUDE）
DO $$ 
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_indexes WHERE indexname = 'ix_batches_status_expiry') THEN
        CREATE INDEX ix_batches_status_expiry 
        ON "Batches" ("Status", "ExpiryDate", "QuantityPassed", "FlowerId");
        RAISE NOTICE '建立索引 ix_batches_status_expiry';
    ELSE
        RAISE NOTICE '索引 ix_batches_status_expiry 已存在';
    END IF;
END $$;

-- 3. 花卉查詢優化索引
DO $$ 
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_indexes WHERE indexname = 'ix_flowers_category_abc') THEN
        CREATE INDEX ix_flowers_category_abc 
        ON "Flowers" ("Category", "ABCClass", "Name", "Price", "ShelfLifeDays");
        RAISE NOTICE '建立索引 ix_flowers_category_abc';
    ELSE
        RAISE NOTICE '索引 ix_flowers_category_abc 已存在';
    END IF;
END $$;

-- 4. 建立庫存預警函數
CREATE OR REPLACE FUNCTION get_inventory_alerts()
RETURNS TABLE (
    flower_id integer,
    flower_name text,
    abc_class text,
    category text,
    current_stock bigint,
    price numeric,
    total_value numeric,
    alert_level text
) AS $$
BEGIN
    RETURN QUERY
    SELECT 
        f."Id" as flower_id,
        f."Name" as flower_name,
        f."ABCClass" as abc_class,
        f."Category" as category,
        COALESCE(SUM(b."QuantityPassed"), 0) as current_stock,
        f."Price" as price,
        COALESCE(SUM(b."QuantityPassed"), 0) * f."Price" as total_value,
        CASE 
            WHEN COALESCE(SUM(b."QuantityPassed"), 0) = 0 THEN '缺貨'
            WHEN COALESCE(SUM(b."QuantityPassed"), 0) < f."LeadTimeDays" * 2 THEN '緊急補貨'
            WHEN COALESCE(SUM(b."QuantityPassed"), 0) < f."LeadTimeDays" * 4 THEN '建議補貨'
            ELSE '正常'
        END as alert_level
    FROM "Flowers" f
    LEFT JOIN "Batches" b ON f."Id" = b."FlowerId" AND b."Status" = 3 -- Active status
    GROUP BY f."Id", f."Name", f."ABCClass", f."Category", f."Price", f."LeadTimeDays"
    HAVING COALESCE(SUM(b."QuantityPassed"), 0) < f."LeadTimeDays" * 4  -- 庫存少於4天需求
    ORDER BY 
        CASE 
            WHEN COALESCE(SUM(b."QuantityPassed"), 0) = 0 THEN 1
            WHEN COALESCE(SUM(b."QuantityPassed"), 0) < f."LeadTimeDays" * 2 THEN 2
            WHEN COALESCE(SUM(b."QuantityPassed"), 0) < f."LeadTimeDays" * 4 THEN 3
            ELSE 4
        END,
        f."ABCClass";
END;
$$ LANGUAGE plpgsql;

-- 5. 建立交易統計函數
CREATE OR REPLACE FUNCTION get_transaction_summary(
    start_date date,
    end_date date
)
RETURNS TABLE (
    flower_name text,
    category text,
    transaction_count bigint,
    total_inbound numeric,
    total_outbound numeric,
    avg_daily_outbound numeric
) AS $$
BEGIN
    RETURN QUERY
    SELECT 
        f."Name" as flower_name,
        f."Category" as category,
        COUNT(*) as transaction_count,
        SUM(CASE WHEN t."TransactionType" = 1 THEN t."ChangeQty" ELSE 0 END) as total_inbound,
        SUM(CASE WHEN t."TransactionType" = 2 THEN ABS(t."ChangeQty") ELSE 0 END) as total_outbound,
        AVG(CASE WHEN t."TransactionType" = 2 THEN ABS(t."ChangeQty") ELSE NULL END) as avg_daily_outbound
    FROM "Transactions" t
    INNER JOIN "Flowers" f ON t."FlowerId" = f."Id"
    WHERE t."TransactionDate" BETWEEN start_date AND end_date
    GROUP BY f."Name", f."Category"
    ORDER BY total_outbound DESC;
END;
$$ LANGUAGE plpgsql;

-- 6. 建立批次到期預警函數
CREATE OR REPLACE FUNCTION get_expiring_batches(
    days_threshold integer DEFAULT 7
)
RETURNS TABLE (
    batch_no text,
    flower_name text,
    quantity_passed integer,
    expiry_date timestamp with time zone,
    days_until_expiry integer,
    alert_level text
) AS $$
BEGIN
    RETURN QUERY
    SELECT 
        b."BatchNo" as batch_no,
        f."Name" as flower_name,
        b."QuantityPassed" as quantity_passed,
        b."ExpiryDate" as expiry_date,
        EXTRACT(DAY FROM (b."ExpiryDate" - NOW()))::integer as days_until_expiry,
        CASE 
            WHEN EXTRACT(DAY FROM (b."ExpiryDate" - NOW())) <= 3 THEN '緊急'
            WHEN EXTRACT(DAY FROM (b."ExpiryDate" - NOW())) <= 7 THEN '注意'
            ELSE '正常'
        END as alert_level
    FROM "Batches" b
    INNER JOIN "Flowers" f ON b."FlowerId" = f."Id"
    WHERE b."ExpiryDate" IS NOT NULL 
        AND b."ExpiryDate" <= (NOW() + (days_threshold || ' days')::interval)
        AND b."Status" = 3 -- Active batches only
        AND b."QuantityPassed" > 0
    ORDER BY b."ExpiryDate" ASC;
END;
$$ LANGUAGE plpgsql;

-- 7. 更新統計資訊
ANALYZE "Flowers";
ANALYZE "Batches";
ANALYZE "Transactions";

-- 8. 顯示優化結果
DO $$
BEGIN
    RAISE NOTICE '=== 資料庫優化完成 ===';
    RAISE NOTICE '已建立的索引:';
    RAISE NOTICE '- ix_transactions_flowerid_date (交易查詢優化)';
    RAISE NOTICE '- ix_batches_status_expiry (批次狀態查詢優化)';
    RAISE NOTICE '- ix_flowers_category_abc (花卉分類查詢優化)';
    RAISE NOTICE '';
    RAISE NOTICE '已建立的函數:';
    RAISE NOTICE '- get_inventory_alerts() (庫存預警)';
    RAISE NOTICE '- get_transaction_summary() (交易統計)';
    RAISE NOTICE '- get_expiring_batches() (到期批次預警)';
END $$;