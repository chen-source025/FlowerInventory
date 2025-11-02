using FlowerInventory.Models;
using FlowerInventory.Services;
using System.ComponentModel.DataAnnotations;

namespace FlowerInventory.ViewModels
{
    // 工作流程儀表板 ViewModel
    public class WorkflowDashboardViewModel
    {
        public List<EnhancedInventoryViewModel> InventoryStatus { get; set; } = new();
        public int TotalItems { get; set; }
        public int NeedReplenishmentCount { get; set; }
        public int UrgentReplenishmentCount { get; set; }
        public List<Batch> RecentBatches { get; set; } = new();
        public List<Transaction> RecentTransactions { get; set; } = new();
        public decimal TotalInventoryValue { get; set; }

        // 計算屬性
        public int NormalItems => TotalItems - NeedReplenishmentCount;
        public decimal ServiceLevel => TotalItems > 0 ?
            (decimal)(TotalItems - UrgentReplenishmentCount) / TotalItems : 0;
        public string ServiceLevelDisplay => ServiceLevel.ToString("P1");

        // 計算屬性 (基於庫存狀態)
        public int LowStockItems => InventoryStatus.Count(i => i.IsLowStock && !i.IsOutOfStock);
        public int OutOfStockItems => InventoryStatus.Count(i => i.IsOutOfStock);
        public int NormalStockItems => InventoryStatus.Count(i => !i.IsLowStock && !i.IsOutOfStock);

        // 基於實際庫存狀態的服務水準
        public string ActualServiceLevelDisplay
        {
            get
            {
                var inStockItems = InventoryStatus.Count(i => !i.IsOutOfStock);
                var totalItems = InventoryStatus.Count;
                return totalItems > 0 ? $"{((decimal)inStockItems / totalItems * 100):F1}%" : "100%";
            }
        }
    }

    // 品檢作業 ViewModel
    public class InspectionViewModel
    {
        public Batch Batch { get; set; } = new();
        public decimal ExpectedPassRate { get; set; }
        public int ExpectedPassQuantity { get; set; }
        public int CurrentStock { get; set; }
        public double SafetyStock { get; set; }
        public string StockStatus { get; set; } = string.Empty;

        // 計算屬性
        public int FailedQuantity => Batch.QuantityReceived - ExpectedPassQuantity;
        public bool WillCauseShortage => (CurrentStock + ExpectedPassQuantity) < SafetyStock;
    }

    // 出貨作業 ViewModel
    public class ShipmentViewModel
    {
        [Required(ErrorMessage = "請選擇花卉")]
        [Display(Name = "花卉")]
        public int FlowerId { get; set; }

        [Required(ErrorMessage = "請輸入出貨數量")]
        [Range(1, 1000, ErrorMessage = "數量必須在1-1000之間")]
        [Display(Name = "出貨數量")]
        public int Quantity { get; set; }

        [Required(ErrorMessage = "請輸入出貨原因")]
        [Display(Name = "出貨原因")]
        public string Reason { get; set; } = "銷售";

        [Display(Name = "客戶名稱")]
        [StringLength(100)]
        public string? CustomerName { get; set; }

        // 導航屬性
        public List<Flower> AvailableFlowers { get; set; } = new();
    }

    // 庫存調整 ViewModel
    public class StockAdjustmentViewModel
    {
        [Required(ErrorMessage = "請選擇花卉")]
        [Display(Name = "花卉")]
        public int FlowerId { get; set; }

        [Required(ErrorMessage = "請輸入調整數量")]
        [Range(-1000, 1000, ErrorMessage = "調整數量必須在-1000到1000之間")]
        [Display(Name = "調整數量")]
        public int AdjustmentQuantity { get; set; }

        [Required(ErrorMessage = "請輸入調整原因")]
        [Display(Name = "調整原因")]
        [StringLength(200)]
        public string Reason { get; set; } = string.Empty;

        [Display(Name = "備註")]
        [StringLength(500)]
        public string? Note { get; set; }

        // 導航屬性
        public List<Flower> AvailableFlowers { get; set; } = new();

        // 計算屬性
        public string AdjustmentType => AdjustmentQuantity >= 0 ? "增加" : "減少";
        public int AbsoluteQuantity => Math.Abs(AdjustmentQuantity);
    }

    // 行動裝置庫存項目 ViewModel
    public class MobileInventoryItemViewModel
    {
        public int FlowerId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string ABCClass { get; set; } = string.Empty;
        public int CurrentStock { get; set; }
        public double SafetyStock { get; set; }
        public double WeeklyDemand { get; set; }
        public string StockStatus { get; set; } = string.Empty;
        public decimal? Price { get; set; }

        // 計算屬性
        public decimal TotalValue => (Price ?? 0) * CurrentStock;
        public double CoverageDays => WeeklyDemand > 0 ? (CurrentStock / WeeklyDemand) * 7 : 0;
        public bool IsLowStock => CurrentStock < SafetyStock;
        public string StatusColor => StockStatus switch
        {
            "正常" => "text-success",
            "需補貨" => "text-warning",
            "缺貨" => "text-danger",
            _ => "text-secondary"
        };
    }
}
