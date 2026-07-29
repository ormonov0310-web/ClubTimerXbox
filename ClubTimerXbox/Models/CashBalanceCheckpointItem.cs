using System;

namespace ClubTimerXbox.Models
{
    public class CashBalanceCheckpointItem
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime MonthStart { get; set; }
        public int CashAmount { get; set; }
        public string OperationId { get; set; } = "";
        public string Note { get; set; } = "";
    }
}
