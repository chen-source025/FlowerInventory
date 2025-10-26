using FlowerInventory.Models;

namespace FlowerInventory.Services
{
    public interface IEnhancedInventoryService
    {
        // 庫存狀態查詢(使用非同步版本)
        Task<List<EnhancedInventoryViewModel>> GetEnhancedInventoryStatusAsync(CancellationToken cancellationToken = default);
        Task<ReplenishmentRecommendation> GetReplenishmentRecommendationAsync(int flowerId, CancellationToken cancellationToken = default);
        Task<List<FlowerDemandAnalysis>> GetDemandAnalysisAsync(CancellationToken cancellationToken = default);

        // 庫存操作(使用非同步版本)
        Task<InspectionResult> ProcessInspectionAsync(int batchId, int passedQuantity, string note, CancellationToken cancellationToken = default);
        Task<StockAdjustmentResult> AdjustStockAsync(int flowerId, int adjustmentQty, string reason, CancellationToken cancellationToken = default);

        // 報表生成(使用非同步版本)
        Task<InventoryReport> GenerateInventoryReportAsync(CancellationToken cancellationToken = default);
        Task<ABCReport> GenerateABCReportAsync(CancellationToken cancellationToken = default);

        // 計算方法(新增非同步版本)
        Task<double> CalculateEnhancedSafetyStockAsync(Flower flower, CancellationToken cancellationToken = default);
        Task<int> CalculateCurrentStockAsync(int flowerId, CancellationToken cancellationToken = default);
        Task<double> CalculateWeeklyDemandAsync(int flowerId, CancellationToken cancellationToken = default);

        // 同步版本(為向後兼容)
        [System.Obsolete("請使用非同步版本 CalculateEnhancedSafetyStockAsync")]
        double CalculateEnhancedSafetyStock(Flower flower);

        [System.Obsolete("請使用非同步版本 CalculateCurrentStockAsync")]
        int CalculateCurrentStock(Flower flower);

        [System.Obsolete("請使用非同步版本 CalculateWeeklyDemandAsync")]
        double CalculateWeeklyDemand(int flowerId);
    }

    // 庫存狀態視圖模型
    public class EnhancedInventoryViewModel
    {
        public int FlowerId { get; set; }
        public required string Name { get; set; }
        public string Variety { get; set; } = string.Empty;
        public required string Category { get; set; }
        public required string ABCClass { get; set; }

        // 庫存數據
        public int CurrentStock { get; set; }
        public double SafetyStock { get; set; }
        public double WeeklyDemand { get; set; }
        public required string StockStatus { get; set; }

        // 相關資料
        public ReplenishmentRecommendation? Recommendation { get; set; }
        public List<Batch> ExpiringBatches { get; set; } = new();
        public decimal InspectionPassRate { get; set; }
        public decimal? Price { get; set; }

        // 計算屬性
        public decimal TotalValue => (Price ?? 0) * CurrentStock;
        public double StockCoverageDays => WeeklyDemand > 0 ? (CurrentStock / WeeklyDemand) * 7 : 0;
        public bool IsLowStock => CurrentStock < SafetyStock;
        public bool IsOutOfStock => CurrentStock == 0;
        public bool NeedsReplenishment => IsLowStock || IsOutOfStock;
    }

    // 補貨建議
    public class ReplenishmentRecommendation
    {
        public int FlowerId { get; set; }
        public required string FlowerName { get; set; }

        // 庫存數據
        public int CurrentStock { get; set; }
        public double SafetyStock { get; set; }
        public double WeeklyDemand { get; set; }

        // 建議內容
        public bool NeedReplenishment { get; set; }
        public int SuggestedOrderQuantity { get; set; }
        public required string RecommendationLevel { get; set; }
        public required string Reason { get; set; }

        // 品檢相關
        public int ExpectedPassQuantity { get; set; }
        public DateTime RecommendedOrderDate { get; set; }

        // 狀態標記
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }

        // 計算屬性
        public double StockCoverageDays => WeeklyDemand > 0 ? (CurrentStock / WeeklyDemand) * 7 : 0;
        public double ShortageAmount => Math.Max(0, SafetyStock - CurrentStock);
    }

    // 品檢結果
    public class InspectionResult
    {
        public int BatchId { get; set; }
        public int ReceivedQty { get; set; }
        public int PassedQty { get; set; }
        public decimal PassRate { get; set; }
        public string Message { get; set; } = string.Empty;
        public DateTime InspectionDate { get; set; }
        public bool Success { get; set; }

        public int FailedQty => ReceivedQty - PassedQty;
        public string BatchNumber { get; set; } = string.Empty;
        public string FlowerName { get; set; } = string.Empty;
    }

    // 庫存調整結果
    public class StockAdjustmentResult
    {
        public int FlowerId { get; set; }
        public string FlowerName { get; set; } = string.Empty;
        public int OldStock { get; set; }
        public int NewStock { get; set; }
        public int AdjustmentQty { get; set; }
        public string Reason { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public bool Success { get; set; }
        public DateTime AdjustmentDate { get; set; }

        public string AdjustmentType => AdjustmentQty >= 0 ? "增加" : "減少";
        public int AbsoluteAdjustmentQty => Math.Abs(AdjustmentQty);
    }

    // 需求分析
    public class FlowerDemandAnalysis
    {
        public int FlowerId { get; set; }
        public string FlowerName { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string ABCClass { get; set; } = string.Empty;

        // 需求統計
        public double AvgWeeklyDemand { get; set; }
        public double DemandStdDev { get; set; }
        public double SafetyStock { get; set; }
        public double ReorderPoint { get; set; }

        // 分析結果
        public string DemandPattern { get; set; } = string.Empty;
        public string VariabilityLevel { get; set; } = string.Empty;
        public double ServiceLevel { get; set; } = 0.95;
        public int ReviewFrequencyDays { get; set; } = 7;

        // 計算屬性
        public double CoefficientOfVariation => AvgWeeklyDemand > 0 ? DemandStdDev / AvgWeeklyDemand : 0;
    }

    // 庫存報表
    public class InventoryReport
    {
        public DateTime ReportDate { get; set; }
        public int TotalFlowers { get; set; }
        public int TotalItems { get; set; }
        public decimal TotalValue { get; set; }
        public int LowStockItems { get; set; }
        public int OutOfStockItems { get; set; }
        public int ExpiringItems { get; set; }
        public List<EnhancedInventoryViewModel> InventoryDetails { get; set; } = new();
        public List<ReplenishmentRecommendation> ReplenishmentList { get; set; } = new();

        // 計算屬性
        public decimal AverageItemValue => TotalItems > 0 ? TotalValue / TotalItems : 0;
        public double ServiceLevel => TotalFlowers > 0 ? (double)(TotalFlowers - OutOfStockItems) / TotalFlowers : 0;
        public decimal InventoryTurnoverRatio { get; set; }

        public int CriticalItems { get; set; }
        public decimal InventoryInvestment { get; set; }
        public string ReportPeriod { get; set; } = "即時";
    }

    // ABC 分析項目
    public class ABCAnalysisItem
    {
        public int FlowerId { get; set; }
        public string FlowerName { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public int CurrentStock { get; set; }
        public decimal? UnitPrice { get; set; }
        public decimal TotalValue { get; set; }
        public decimal Percentage { get; set; }
        public decimal CumulativePercentage { get; set; }
        public string ABCClass { get; set; } = string.Empty;
        public string ManagementStrategy { get; set; } = string.Empty;

        public double WeeklyDemand { get; set; }
        public double SafetyStock { get; set; }
        public int ReviewFrequencyDays { get; set; }
    }

    // ABC 分析報表
    public class ABCReport
    {
        public DateTime ReportDate { get; set; }
        public decimal TotalValue { get; set; }
        public int ClassACount { get; set; }
        public int ClassBCount { get; set; }
        public int ClassCCount { get; set; }
        public decimal ClassAValue { get; set; }
        public decimal ClassBValue { get; set; }
        public decimal ClassCValue { get; set; }
        public List<ABCAnalysisItem> Items { get; set; } = new();

        // 計算屬性
        public decimal ClassAPercentage => TotalValue > 0 ? (ClassAValue / TotalValue) * 100 : 0;
        public decimal ClassBPercentage => TotalValue > 0 ? (ClassBValue / TotalValue) * 100 : 0;
        public decimal ClassCPercentage => TotalValue > 0 ? (ClassCValue / TotalValue) * 100 : 0;

        public int TotalItems => ClassACount + ClassBCount + ClassCCount;
        public string AnalysisDate => ReportDate.ToString("yyyy/MM/dd");
        public string Summary => 
            $"A類({ClassACount}項, {ClassAPercentage:F1}%) | " +
            $"B類({ClassBCount}項, {ClassBPercentage:F1}%) | " +
            $"C類({ClassCCount}項, {ClassCPercentage:F1}%)";
    }
}
