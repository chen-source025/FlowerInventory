using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FlowerInventory.Models
{
    public class Flower
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "必須輸入花名")]
        [Display(Name = "花名")]
        public string Name { get; set; } = string.Empty;

        [Display(Name = "品種")]
        public string? Variety { get; set; }

        [Required(ErrorMessage = "必須輸入分類")]
        [Display(Name = "分類")]
        public string Category { get; set; } = "常駐型";

        [Required(ErrorMessage = "必須輸入ABC分類")]
        [Display(Name = "ABC分類")]
        public string ABCClass { get; set; } = "B";

        [Required(ErrorMessage = "必須輸入天數")]
        [Display(Name = "保存天數")]
        [Range(1, 365, ErrorMessage = "保存天數必須在 1-365 天之間")]
        public int? ShelfLifeDays { get; set; } = 30; // 可空(移除)

        [Required(ErrorMessage = "必須輸入單價")]
        [Display(Name = "單價")]
        [Range(0.01, 10000, ErrorMessage = "單價必須在 0.01-10000 之間")]
        [Column(TypeName = "decimal(10,2)")]
        public decimal? Price { get; set; } = 0;

        [Display(Name = "季節係數")]
        [Range(0.1, 3.0, ErrorMessage = "季節係數必須在 0.1-3.0 之間")]
        [Column(TypeName = "decimal(4,2)")]
        public decimal SeasonalFactor { get; set; } = 1.0m;

        [Display(Name = "品檢合格率")]
        [Range(0.1, 1.0, ErrorMessage = "合格率必須在 0.1-1.0 之間")]
        [Column(TypeName = "decimal(3,2)")]
        public decimal InspectionPassRate { get; set; } = 0.8m;

        [Display(Name = "補貨提前期(天)")]
        [Range(1, 90, ErrorMessage = "提前期必須在 1-90 天之間")]
        public int LeadTimeDays { get; set; } = 7;

        // UI 與計算使用
        [Display(Name = "補貨提前期(週)")]
        [Range(1, 12, ErrorMessage = "提前期(週)必須在 1-12 週之間")]
        public int LeadTimeWeeks { get; set; } = 1;

        // 供應頻率：預設每週 1 次，旺季/過年可能設為 2 或 3）
        [Display(Name = "供應次數/週")]
        [Range(1, 7, ErrorMessage = "每週供應次數必須在 1-7 之間")]
        public int SupplierDeliveriesPerWeek { get; set; } = 1;

        [Display(Name = "補貨週期(週)")]
        [Range(1, 4, ErrorMessage = "補貨週期必須在 1-4 週之間")]
        public int ReplenishCycleWeeks { get; set; } = 1;

        [Display(Name = "建立日期")]
        [DataType(DataType.DateTime)]
        public DateTime CreatedDate { get; set; } = DateTime.Now;

        public virtual ICollection<Batch> Batches { get; set; } = new List<Batch>();
        public virtual ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();

        public bool IsValidForCreation()
        {
            return  !string.IsNullOrWhiteSpace(Name) &&
                    !string.IsNullOrWhiteSpace(Category) &&
                    !string.IsNullOrWhiteSpace(ABCClass) &&
                    ShelfLifeDays > 0 &&
                    (Price ?? 0) > 0;
        }

        [NotMapped]
        public decimal TotalValue => (Price ?? 0) * (Batches?.Sum(b => b.QuantityPassed) ?? 0);
    }
}
