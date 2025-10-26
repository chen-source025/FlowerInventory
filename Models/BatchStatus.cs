namespace FlowerInventory.Models
{
    public enum BatchStatus
    {
        Received = 1,    // 已收貨，待品檢
        Inspected = 2,   // 已品檢
        Active = 3,      // 有效庫存
        Expired = 4,     // 已過期
        Discarded = 5    // 已報廢
    }
}
