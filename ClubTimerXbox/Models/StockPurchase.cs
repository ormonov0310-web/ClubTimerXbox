using System;
using System.Collections.Generic;

namespace ClubTimerXbox.Models
{
    public class StockPurchase
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public string AddedBy { get; set; } = "Владелец";

        public string Note { get; set; } = "";

        public List<StockPurchaseItem> Items { get; set; } = new List<StockPurchaseItem>();

        public int TotalAmount
        {
            get
            {
                int total = 0;

                foreach (var item in Items)
                {
                    total += item.TotalAmount;
                }

                return total;
            }
        }
    }
}