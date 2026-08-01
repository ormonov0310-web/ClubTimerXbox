using System;
using System.Collections.Generic;

namespace ClubTimerXbox.Models
{
    public class PaymentRecord
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public string BusinessDateKey { get; set; } = "";

        public string BusinessMonthKey { get; set; } = "";

        public string EmployeeName { get; set; } = "";

        public string OperationTitle { get; set; } = "";

        public string PlaceName { get; set; } = "";

        public Guid? GameSessionId { get; set; }

        public List<CheckoutItem> Items { get; set; } = new List<CheckoutItem>();

        public int TotalAmount { get; set; }

        public int CashAmount { get; set; }

        public int MBankAmount { get; set; }

        public string Comment { get; set; } = "";

        public bool IsMixedPayment
        {
            get
            {
                return CashAmount > 0 && MBankAmount > 0;
            }
        }
    }
}
