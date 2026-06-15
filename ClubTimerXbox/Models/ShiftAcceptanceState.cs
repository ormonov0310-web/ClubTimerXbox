using System;

namespace ClubTimerXbox.Models
{
    public class ShiftAcceptanceStatus
    {
        public bool IsRequired { get; set; }

        public bool ProductsAccepted { get; set; }

        public bool CashAccepted { get; set; }

        public string AcceptanceKey { get; set; } = "";

        public string NewEmployeeName { get; set; } = "";

        public string ResponsibleEmployeeName { get; set; } = "";

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public DateTime? ProductsAcceptedAt { get; set; }

        public DateTime? CashAcceptedAt { get; set; }

        public DateTime? CompletedAt { get; set; }

        public bool IsCompleted
        {
            get
            {
                return !IsRequired || (ProductsAccepted && CashAccepted);
            }
        }
    }
}
