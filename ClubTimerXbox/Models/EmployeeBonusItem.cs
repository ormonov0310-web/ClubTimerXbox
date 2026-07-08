using System;

namespace ClubTimerXbox.Models
{
    public class EmployeeBonusItem
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public string EmployeeName { get; set; } = "";

        public string CreatedBy { get; set; } = "";

        public string BonusType { get; set; } = "";

        public string Title { get; set; } = "";

        public string Description { get; set; } = "";

        public int Amount { get; set; }

        public string SalaryMonthKey { get; set; } = "";
    }
}
