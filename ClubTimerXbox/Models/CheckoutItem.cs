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

        // Необязательная трассировка исходной позиции. Старые платежи этих полей не имеют.
        public Guid? SourceSaleLineId { get; set; }

        public Guid? SourceGameSessionId { get; set; }

        public string SourcePlaceName { get; set; } = "";

        public string CreatedByEmployeeName { get; set; } = "";

        public DateTime? SourceCreatedAt { get; set; }

        public string DebtResponsibleEmployeeName { get; set; } = "";

        public Guid? DebtResponsibleShiftId { get; set; }

        public DateTime? DebtAcceptedAt { get; set; }
    }
}
