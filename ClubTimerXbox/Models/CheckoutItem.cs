using System;

namespace ClubTimerXbox.Models
{
    public class CheckoutItem
    {
        public string Name { get; set; } = "";

        public int Quantity { get; set; } = 1;

        public int UnitPrice { get; set; }

        public int PurchasePrice { get; set; }

        public int TotalAmount
        {
            get
            {
                return Quantity * UnitPrice;
            }
        }

        public string Category { get; set; } = "";

        public string ItemType { get; set; } = "";
    }
}
