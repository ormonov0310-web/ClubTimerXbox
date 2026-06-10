using System;

namespace ClubTimerXbox.Models
{
    public class CashlessDayRecord
    {
        public DateTime Date { get; set; }

        public int Amount { get; set; }

        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        public string Note { get; set; } = "";
    }
}