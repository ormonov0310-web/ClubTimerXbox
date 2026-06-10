using System;

namespace ClubTimerXbox.Models
{
    public class ActionLogItem
    {
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public string EmployeeName { get; set; } = "";

        public string ActionType { get; set; } = "";

        public string PlaceName { get; set; } = "";

        public string Description { get; set; } = "";

        public int Amount { get; set; }

        public string IncomeEmployeeName { get; set; } = "";
    }
}