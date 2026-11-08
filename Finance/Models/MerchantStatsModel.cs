namespace Finance.Models
{
    public class MerchantStatsModel
    {
        public string CategoryName { get; set; } = "";
        public string MerchantName { get; set; } = "";
        public int TransactionCount { get; set; }
        public decimal TotalAmount { get; set; }
    }
}