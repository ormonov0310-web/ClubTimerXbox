namespace ClubTimerXbox.Models
{
    public class StockPurchaseItem
    {
        public string ProductName { get; set; } = "";

        public int Quantity { get; set; }

        public int PurchasePrice { get; set; }

        public int SalePrice { get; set; }

        public int MinimumQuantity { get; set; }

        public int TotalAmount
        {
            get
            {
                return Quantity * PurchasePrice;
            }
        }
    }
}
