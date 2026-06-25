using System;

namespace ClubTimerXbox.Models
{
    public class CashlessBalanceCheckpointItem
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public DateTime MonthStart { get; set; }

        public int CashlessAmount { get; set; }

        public string Note { get; set; } = "";
    }
}
