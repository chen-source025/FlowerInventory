namespace FlowerInventory.Utilities
{
    public static class InventoryConstants
    {
        // 安全庫存計算相關常數
        public static class SafetyStock
        {
            // 服務水準設定
            public const double DEFAULT_SERVICE_LEVEL = 0.95;
            public const int DEFAULT_LEAD_TIME_DAYS = 7;

            // Z值對照表 (服務水準 -> Z值)
            public static readonly Dictionary<double, double> Z_VALUES = new()
            {
                { 0.80, 0.84 }, { 0.85, 1.04 }, { 0.90, 1.28 },
                { 0.95, 1.65 }, { 0.99, 2.33 }, { 0.975, 1.96 }
            };

            // 需求分類閾值
            public const double LOW_DEMAND_THRESHOLD = 15.0;
            public const double MEDIUM_DEMAND_THRESHOLD = 30.0;

            // 變異性分類閾值
            public const double LOW_VARIABILITY_THRESHOLD = 0.3;
            public const double MEDIUM_VARIABILITY_THRESHOLD = 0.6;

            // 需求分析設定
            public const int DEMAND_LOOKBACK_DAYS = 84; // 12週歷史資料
            public const int MIN_WEEKS_FOR_ANALYSIS = 4; // 最少需要4週資料
            public const int MIN_WEEKS_FOR_STD_DEV = 2; // 計算標準差最少需要2週資料
            public const double DEFAULT_CV = 0.4; // 預設變異係數
            public const double BUFFER_WEEKS = 1.0; // 補貨緩衝週數

            // 計算參數邊界
            public const double MIN_CV = 0.1;
            public const double MAX_CV = 2.0;
            public const double MIN_SERVICE_LEVEL = 0.5;
            public const double MAX_SERVICE_LEVEL = 0.999;
        }

        // ABC 分類相關常數
        public static class ABCClassification
        {
            // 分類閾值 (百分比)
            public const decimal CLASS_A_THRESHOLD = 80.0m;
            public const decimal CLASS_B_THRESHOLD = 95.0m;
            public const decimal CLASS_C_THRESHOLD = 100.0m;

            // 預設需求設定
            public const double CLASS_A_DEFAULT_DEMAND = 25.0;
            public const double CLASS_B_DEFAULT_DEMAND = 15.0;
            public const double CLASS_C_DEFAULT_DEMAND = 8.0;

            // 變異係數設定
            public const double CLASS_A_CV = 0.4;
            public const double CLASS_B_CV = 0.3;
            public const double CLASS_C_CV = 0.2;

            // 分類顏色標識
            public static readonly Dictionary<string, string> CLASS_COLORS = new()
            {
                { ABCClasses.CLASS_A, "#FF4444" }, // 紅色
                { ABCClasses.CLASS_B, "#FFAA00" }, // 橙色
                { ABCClasses.CLASS_C, "#44AA44" }  // 綠色
            };
        }

        // 補貨相關常數
        public static class Replenishment
        {
            public const double BUFFER_WEEKS = 1.0;
            public const int MIN_REORDER_POINT = 1;
            public const int MAX_REORDER_POINT = 10000;
            public const int DEFAULT_REORDER_QUANTITY = 50;

            // 補貨週期設定
            public const int CLASS_A_REVIEW_DAYS = 7;    // A類每週檢視
            public const int CLASS_B_REVIEW_DAYS = 14;   // B類每兩週檢視
            public const int CLASS_C_REVIEW_DAYS = 30;   // C類每月檢視
        }

        // 補貨建議等級
        public static class ReplenishmentLevels
        {
            public const string NONE = "無需補貨";
            public const string SUGGESTED = "建議補貨";
            public const string URGENT = "緊急補貨";
            public const string CRITICAL = "嚴重缺貨";
            public const string ERROR = "計算錯誤";

            public static readonly Dictionary<string, string> CSS_CLASSES = new()
            {
                { NONE, "bg-success" },
                { SUGGESTED, "bg-warning" },
                { URGENT, "bg-danger" },
                { CRITICAL, "bg-dark" },
                { ERROR, "bg-secondary" }
            };

            public static readonly Dictionary<string, int> PRIORITY_LEVELS = new()
            {
                { NONE, 0 },
                { SUGGESTED, 1 },
                { URGENT, 2 },
                { CRITICAL, 3 },
                { ERROR, -1 }
            };
        }

        // 業務規則相關常數
        public static class BusinessRules
        {
            // 數值邊界
            public const int MIN_SHELF_LIFE_DAYS = 1;
            public const int MAX_SHELF_LIFE_DAYS = 365;
            public const decimal MIN_PRICE = 0m;
            public const decimal MAX_PRICE = 10000m;
            public const decimal MIN_SEASONAL_FACTOR = 0.1m;
            public const decimal MAX_SEASONAL_FACTOR = 3.0m;
            public const decimal MIN_PASS_RATE = 0.1m;
            public const decimal MAX_PASS_RATE = 1.0m;
            public const int MIN_LEAD_TIME_DAYS = 1;
            public const int MAX_LEAD_TIME_DAYS = 30;

            // 批次相關
            public const int MIN_QUANTITY = 1;
            public const int MAX_QUANTITY = 10000;

            // 到期警示天數
            public const int EXPIRING_SOON_DAYS = 3;
            public const int EXPIRY_CHECK_DAYS = 7;

            // 品質檢查
            public const double MIN_QUALITY_SCORE = 0.0;
            public const double MAX_QUALITY_SCORE = 100.0;
            public const double QUALITY_PASS_THRESHOLD = 80.0;
        }

        // 花卉分類常數
        public static class FlowerCategories
        {
            public const string RESIDENTIAL = "常駐型";
            public const string RELIGIOUS = "宗教型";
            public const string SEASONAL = "季節性";
            public const string POPULAR = "流行性";
            public const string SPECIAL = "特殊型";

            public static readonly string[] ALL_CATEGORIES =
            {
                RESIDENTIAL, RELIGIOUS, SEASONAL, POPULAR, SPECIAL
            };

            public static readonly Dictionary<string, string> DESCRIPTIONS = new()
            {
                { RESIDENTIAL, "常年穩定需求" },
                { RELIGIOUS, "宗教節日相關" },
                { SEASONAL, "季節性需求" },
                { POPULAR, "流行趨勢影響" },
                { SPECIAL, "特殊場合使用" }
            };
        }

        // ABC 分類常數
        public static class ABCClasses
        {
            public const string CLASS_A = "A";
            public const string CLASS_B = "B";
            public const string CLASS_C = "C";

            public static readonly string[] ALL_CLASSES =
            {
                CLASS_A, CLASS_B, CLASS_C
            };

            public static readonly Dictionary<string, string> DESCRIPTIONS = new()
            {
                { CLASS_A, "高價值 - 嚴格控制" },
                { CLASS_B, "中價值 - 一般控制" },
                { CLASS_C, "低價值 - 簡化控制" }
            };
        }

        // 交易類型常數
        public static class TransactionTypes
        {
            public const string INBOUND = "In";
            public const string OUTBOUND = "Out";
            public const string ADJUSTMENT = "Adjust";
            public const string TRANSFER = "Transfer";

            public static readonly Dictionary<string, string> DISPLAY_NAMES = new()
            {
                { INBOUND, "進貨" },
                { OUTBOUND, "出貨" },
                { ADJUSTMENT, "調整" },
                { TRANSFER, "調撥" }
            };

            public static readonly Dictionary<string, string> CSS_CLASSES = new()
            {
                { INBOUND, "text-success" },
                { OUTBOUND, "text-danger" },
                { ADJUSTMENT, "text-warning" },
                { TRANSFER, "text-info" }
            };
        }

        // 庫存狀態常數
        public static class StockStatus
        {
            public const string NORMAL = "正常";
            public const string LOW_STOCK = "需補貨";
            public const string OUT_OF_STOCK = "缺貨";
            public const string OVERSTOCK = "過量庫存";
            public const string EXPIRING = "即將到期";

            public static readonly Dictionary<string, string> CSS_CLASSES = new()
            {
                { NORMAL, "text-success" },
                { LOW_STOCK, "text-warning" },
                { OUT_OF_STOCK, "text-danger" },
                { OVERSTOCK, "text-info" },
                { EXPIRING, "text-warning" }
            };

            public static readonly Dictionary<string, int> SEVERITY_LEVELS = new()
            {
                { NORMAL, 0 },
                { EXPIRING, 1 },
                { LOW_STOCK, 2 },
                { OVERSTOCK, 3 },
                { OUT_OF_STOCK, 4 }
            };
        }

        // 需求模式常數
        public static class DemandPatterns
        {
            public const string LOW = "低需求";
            public const string MEDIUM = "中需求";
            public const string HIGH = "高需求";
            public const string SEASONAL = "季節性";
            public const string ERRATIC = "不穩定";

            public static readonly Dictionary<string, (double Min, double Max)> THRESHOLDS = new()
            {
                { LOW, (0, 15) },
                { MEDIUM, (15, 30) },
                { HIGH, (30, double.MaxValue) }
            };

            public static readonly Dictionary<string, string> DESCRIPTIONS = new()
            {
                { LOW, "需求穩定且量少" },
                { MEDIUM, "需求中等波動" },
                { HIGH, "需求高且可能波動" },
                { SEASONAL, "季節性波動" },
                { ERRATIC, "難以預測" }
            };
        }

        // 變異性描述擴充方法
        public static string GetVariabilityDescription(this string variability)
        {
            return variability switch
            {
                "穩定" => "需求波動較小，預測準確度高",
                "中等" => "需求有一定波動，需要適度安全庫存",
                "高度變動" => "需求波動大，需要較高安全庫存",
                _ => "需求模式分析中"
            };
        }
            

        // 變異性分類擴充方法
        public static string GetVariability(this double cv)
        {
            return cv switch
            {
                < 0.3 => "穩定",
                < 0.6 => "中等",
                _ => "高度變動"
            };
        }

        // 變異性分類
        public static class Variability
        {
            public const string STABLE = "穩定";
            public const string MEDIUM = "中等";
            public const string HIGH = "高度變動";

            public static readonly Dictionary<string, (double Min, double Max)> CV_THRESHOLDS = new()
            {
                { STABLE, (0, 0.3) },
                { MEDIUM, (0.3, 0.6) },
                { HIGH, (0.6, double.MaxValue) }
            };
        }

        // 管理策略
        public static class ManagementStrategies
        {
            public const string CLASS_A_STRATEGY = "嚴格控制，高服務水準，頻繁檢視";
            public const string CLASS_B_STRATEGY = "一般控制，中等服務水準，定期檢視";
            public const string CLASS_C_STRATEGY = "簡化控制，基本服務水準，偶爾檢視";
            public const string SEASONAL_STRATEGY = "季節性預測，提前備貨";
            public const string ERRATIC_STRATEGY = "安全庫存較高，彈性調整";

            public static readonly Dictionary<string, string> BY_CLASS = new()
            {
                { ABCClasses.CLASS_A, CLASS_A_STRATEGY },
                { ABCClasses.CLASS_B, CLASS_B_STRATEGY },
                { ABCClasses.CLASS_C, CLASS_C_STRATEGY }
            };

            public static readonly Dictionary<string, string> BY_DEMAND_PATTERN = new()
            {
                { DemandPatterns.SEASONAL, SEASONAL_STRATEGY },
                { DemandPatterns.ERRATIC, ERRATIC_STRATEGY }
            };
        }

        // 系統設定
        public static class SystemSettings
        {
            public const int DEFAULT_PAGE_SIZE = 20;
            public const int RECENT_ITEMS_COUNT = 10;
            public const int DASHBOARD_ITEMS_COUNT = 5;
            public const int BATCH_SIZE = 100;
            public const string DATE_FORMAT = "yyyy/MM/dd";
            public const string DATETIME_FORMAT = "yyyy/MM/dd HH:mm";
            public const string TIME_FORMAT = "HH:mm";

            // 快取設定
            public const int CACHE_DURATION_MINUTES = 30;
            public const int REPORT_CACHE_HOURS = 4;
        }

        // 錯誤訊息
        public static class ErrorMessages
        {
            public const string FLOWER_NOT_FOUND = "花卉不存在";
            public const string BATCH_NOT_FOUND = "批次不存在";
            public const string INSUFFICIENT_STOCK = "庫存不足";
            public const string INVALID_QUANTITY = "無效的數量";
            public const string INSPECTION_QUANTITY_EXCEEDED = "合格數量不能大於進貨數量";
            public const string DATABASE_ERROR = "資料庫操作失敗";
            public const string CALCULATION_ERROR = "計算過程中發生錯誤";
            public const string INVALID_PARAMETERS = "參數無效";
            public const string DATA_VALIDATION_FAILED = "資料驗證失敗";
        }

        // 成功訊息
        public static class SuccessMessages
        {
            public const string FLOWER_CREATED = "花卉新增成功";
            public const string FLOWER_UPDATED = "花卉更新成功";
            public const string FLOWER_DELETED = "花卉刪除成功";
            public const string BATCH_CREATED = "進貨批次建立成功";
            public const string INSPECTION_COMPLETED = "品檢作業完成";
            public const string SHIPMENT_COMPLETED = "出貨作業完成";
            public const string STOCK_ADJUSTED = "庫存調整完成";
            public const string CALCULATION_COMPLETED = "計算完成";
            public const string DATA_SAVED = "資料儲存成功";
        }

        // 警告訊息
        public static class WarningMessages
        {
            public const string LOW_STOCK_WARNING = "庫存偏低，建議補貨";
            public const string EXPIRING_WARNING = "商品即將到期";
            public const string OVERSTOCK_WARNING = "庫存過量，建議促銷";
            public const string DATA_INCONSISTENCY = "資料不一致，請檢查";
        }
    }

    // 擴充方法 for Constants
    public static class ConstantsExtensions
    {
        // 交易類型相關
        public static string GetTransactionTypeDisplayName(this string transactionType)
        {
            return InventoryConstants.TransactionTypes.DISPLAY_NAMES
                .GetValueOrDefault(transactionType, transactionType);
        }

        public static string GetTransactionTypeCssClass(this string transactionType)
        {
            return InventoryConstants.TransactionTypes.CSS_CLASSES
                .GetValueOrDefault(transactionType, "text-secondary");
        }

        // 庫存狀態相關
        public static string GetStockStatusCssClass(this string stockStatus)
        {
            return InventoryConstants.StockStatus.CSS_CLASSES
                .GetValueOrDefault(stockStatus, "text-secondary");
        }

        public static int GetStockStatusSeverity(this string stockStatus)
        {
            return InventoryConstants.StockStatus.SEVERITY_LEVELS
                .GetValueOrDefault(stockStatus, 0);
        }

        // 補貨等級相關
        public static string GetReplenishmentLevelCssClass(this string level)
        {
            return InventoryConstants.ReplenishmentLevels.CSS_CLASSES
                .GetValueOrDefault(level, "bg-secondary");
        }

        public static int GetReplenishmentPriority(this string level)
        {
            return InventoryConstants.ReplenishmentLevels.PRIORITY_LEVELS
                .GetValueOrDefault(level, 0);
        }

        // 管理策略相關
        public static string GetManagementStrategy(this string abcClass)
        {
            return InventoryConstants.ManagementStrategies.BY_CLASS
                .GetValueOrDefault(abcClass, "未定義策略");
        }

        public static string GetManagementStrategyByDemandPattern(this string demandPattern)
        {
            return InventoryConstants.ManagementStrategies.BY_DEMAND_PATTERN
                .GetValueOrDefault(demandPattern, "標準管理策略");
        }

        // ABC 分類相關
        public static double GetDefaultDemandByABCClass(this string abcClass)
        {
            return abcClass switch
            {
                InventoryConstants.ABCClasses.CLASS_A => InventoryConstants.ABCClassification.CLASS_A_DEFAULT_DEMAND,
                InventoryConstants.ABCClasses.CLASS_B => InventoryConstants.ABCClassification.CLASS_B_DEFAULT_DEMAND,
                InventoryConstants.ABCClasses.CLASS_C => InventoryConstants.ABCClassification.CLASS_C_DEFAULT_DEMAND,
                _ => 10.0
            };
        }

        public static double GetCVByABCClass(this string abcClass)
        {
            return abcClass switch
            {
                InventoryConstants.ABCClasses.CLASS_A => InventoryConstants.ABCClassification.CLASS_A_CV,
                InventoryConstants.ABCClasses.CLASS_B => InventoryConstants.ABCClassification.CLASS_B_CV,
                InventoryConstants.ABCClasses.CLASS_C => InventoryConstants.ABCClassification.CLASS_C_CV,
                _ => 0.3
            };
        }

        public static string GetABCClassColor(this string abcClass)
        {
            return InventoryConstants.ABCClassification.CLASS_COLORS
                .GetValueOrDefault(abcClass, "#666666");
        }

        // 需求模式分析
        public static string GetDemandPattern(this double weeklyDemand)
        {
            foreach (var pattern in InventoryConstants.DemandPatterns.THRESHOLDS)
            {
                if (weeklyDemand >= pattern.Value.Min && weeklyDemand < pattern.Value.Max)
                    return pattern.Key;
            }
            return InventoryConstants.DemandPatterns.LOW;
        }

        public static string GetDemandPatternDescription(this string demandPattern)
        {
            return InventoryConstants.DemandPatterns.DESCRIPTIONS
                .GetValueOrDefault(demandPattern, "需求模式");
        }

        // 變異性分析
        public static string GetVariability(this double cv)
        {
            foreach (var variability in InventoryConstants.Variability.CV_THRESHOLDS)
            {
                if (cv >= variability.Value.Min && cv < variability.Value.Max)
                    return variability.Key;
            }
            return InventoryConstants.Variability.MEDIUM;
        }

        // Z值計算
        public static double GetZValue(this double serviceLevel)
        {
            var zValues = InventoryConstants.SafetyStock.Z_VALUES;

            // 精確匹配
            if (zValues.TryGetValue(serviceLevel, out var z))
                return z;

            // 線性插值計算
            var sortedLevels = zValues.Keys.OrderBy(x => x).ToList();
            var lower = sortedLevels.LastOrDefault(x => x <= serviceLevel);
            var upper = sortedLevels.FirstOrDefault(x => x >= serviceLevel);

            if (lower == default && upper != default) return zValues[upper];
            if (upper == default && lower != default) return zValues[lower];
            if (lower == upper) return zValues[lower];

            // 線性插值
            var zLower = zValues[lower];
            var zUpper = zValues[upper];
            return zLower + (zUpper - zLower) * (serviceLevel - lower) / (upper - lower);
        }

        // 補貨週期計算
        public static int GetReviewDaysByABCClass(this string abcClass)
        {
            return abcClass switch
            {
                InventoryConstants.ABCClasses.CLASS_A => InventoryConstants.Replenishment.CLASS_A_REVIEW_DAYS,
                InventoryConstants.ABCClasses.CLASS_B => InventoryConstants.Replenishment.CLASS_B_REVIEW_DAYS,
                InventoryConstants.ABCClasses.CLASS_C => InventoryConstants.Replenishment.CLASS_C_REVIEW_DAYS,
                _ => 14
            };
        }

        // 資料驗證
        public static bool IsValidQuantity(this int quantity)
        {
            return  quantity >= InventoryConstants.BusinessRules.MIN_QUANTITY &&
                    quantity <= InventoryConstants.BusinessRules.MAX_QUANTITY;
        }

        public static bool IsValidPrice(this decimal price)
        {
            return  price >= InventoryConstants.BusinessRules.MIN_PRICE &&
                    price <= InventoryConstants.BusinessRules.MAX_PRICE;
        }

        public static bool IsValidServiceLevel(this double serviceLevel)
        {
            return  serviceLevel >= InventoryConstants.SafetyStock.MIN_SERVICE_LEVEL &&
                    serviceLevel <= InventoryConstants.SafetyStock.MAX_SERVICE_LEVEL;
        }
    }
}
