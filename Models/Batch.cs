using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FlowerInventory.Models;

public class Batch
{
    public int Id { get; set; }

    [Required(ErrorMessage = "請選擇花卉")]
    [Display(Name = "花卉")]
    public int FlowerId { get; set; }

    [Display(Name = "批號")]
    public string? BatchNo { get; set; }

    [Required(ErrorMessage = "必須輸入進貨數量")]
    [Range(1, 10000, ErrorMessage = "數量必須在 1-10000 之間")]
    [Display(Name = "進貨數量")]
    public int QuantityReceived { get; set; }

    [Display(Name = "品檢合格數量")]
    public int QuantityPassed { get; set; }

    [Required(ErrorMessage = "必須輸入收貨日期")]
    [Display(Name = "收貨日期")]
    [DataType(DataType.Date)]
    public DateTime ReceivedDate { get; set; } = DateTime.UtcNow;

    [Display(Name = "到期日")]
    [DataType(DataType.Date)]
    public DateTime? ExpiryDate { get; set; }

    [Display(Name = "品檢備註")]
    [StringLength(500)]
    public string? InspectionNote { get; set; }

    // 新增屬性
    [Display(Name = "狀態")]
    public BatchStatus Status { get; set; } = BatchStatus.Active;

    [Display(Name = "建立日期")]
    [DataType(DataType.DateTime)]
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

    // 導航屬性（非 null，使用 null-forgiving operator）
    [ForeignKey("FlowerId")]
    public virtual Flower Flower { get; set; } = null!;

    // 計算屬性
    [NotMapped]
    [Display(Name = "是否已過期")]

    public bool IsExpired =>
        ExpiryDate.HasValue &&
        ExpiryDate.Value < DateTime.Today &&
        ExpiryDate.Value <= DateTime.Today.AddDays(3);

    [NotMapped]
    [Display(Name = "即將過期")]
    public bool IsExpiringSoon =>
        ExpiryDate.HasValue &&
        ExpiryDate.Value >= DateTime.Today &&
        ExpiryDate.Value <= DateTime.Today.AddDays(3);

    [NotMapped]
    [Display(Name = "合格率")]
    [DisplayFormat(DataFormatString = "{0:P2}")]
    public decimal PassRate =>
        QuantityReceived > 0 ?
        (decimal)QuantityPassed / QuantityReceived : 0m;

    [NotMapped]
    [Display(Name = "距離過期天數")]
    public int DaysUntilExpiry =>
        ExpiryDate.HasValue ? (ExpiryDate.Value - DateTime.Today).Days : int.MaxValue;

    [NotMapped]
    [Display(Name = "是否有效")]
    public bool IsValid =>
        QuantityPassed > 0 && !IsExpired;

    // 新增計算屬性
    [NotMapped]
    [Display(Name = "不合格數量")]
    public int QuantityFailed => QuantityReceived - QuantityPassed;

    [NotMapped]
    [Display(Name = "庫存天數")]
    public int DaysInStock => (DateTime.Today - ReceivedDate).Days;

    public bool CanBeDeleted()
    {
        return Status == BatchStatus.Received &&
                QuantityPassed > 0 &&
                ReceivedDate <= DateTime.UtcNow;
    }

    public void MarkAsInspected()
    {
        if (ExpiryDate.HasValue && ExpiryDate.Value < DateTime.Today)
        {
            Status = BatchStatus.Expired;
        }
    }
}
