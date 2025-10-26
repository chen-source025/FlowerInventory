using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FlowerInventory.Models
{
    public class Transaction
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "請選擇花卉")]
        [Display(Name = "花卉")]
        public int FlowerId { get; set; }

        [Display(Name = "批次")]
        public int? BatchId { get; set; }

        [Required(ErrorMessage = "數量變更是必填欄位")]
        [Display(Name = "數量變更")]
        public int ChangeQty { get; set; }

        [Required(ErrorMessage = "交易類型是必填欄位")]
        [Display(Name = "交易類型")]
        public TransactionType TransactionType { get; set; } = TransactionType.In;   // In, Out, Adjust

        [Required(ErrorMessage = "交易日期是必填欄位")]
        [Display(Name = "交易日期")]
        [DataType(DataType.DateTime)]
        public DateTime TransactionDate { get; set; } = DateTime.Now;

        [Display(Name = "備註")]
        [StringLength(400)]
        public string? Note { get; set; }

        // 導航屬性
        [ForeignKey("FlowerId")]
        public virtual Flower Flower { get; set; } = null!;

        [ForeignKey("BatchId")]
        public virtual Batch? Batch { get; set; }

        // 計算屬性
        [NotMapped]
        public bool IsInbound => TransactionType == TransactionType.In;

        [NotMapped]
        public bool IsOutbound => TransactionType == TransactionType.Out;

        [NotMapped]
        public bool IsAdjustment => TransactionType == TransactionType.Adjust;

        [NotMapped]
        public string TransactionTypeText => TransactionType switch
        {
            TransactionType.In => "進貨",
            TransactionType.Out => "出貨",
            TransactionType.Adjust => "調整",
            _ => TransactionType.ToString()
        };

        [NotMapped]
        public string ChangeQtyDisplay => TransactionType switch
        {
            TransactionType.In => $"+{ChangeQty}",
            TransactionType.Out => $"-{ChangeQty}",
            TransactionType.Adjust => $"{(ChangeQty >= 0 ? "+" : "")}{ChangeQty}",
            _ => ChangeQty.ToString()
        };

        public bool IsValidTransaction()
        {
            if (ChangeQty == 0)
                return false;

            return TransactionType switch
            {
                TransactionType.In => ChangeQty > 0,
                TransactionType.Out => ChangeQty < 0,
                TransactionType.Adjust => true,        // 調整可正可負
                _ => false
            };
        }
    }
}
