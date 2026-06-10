using System;

namespace ClubTimerXbox.Models
{
    public class ShiftLogItem
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public string EmployeeName { get; set; } = "";

        public DateTime StartedAt { get; set; } = DateTime.Now;

        public DateTime? ClosedAt { get; set; }

        public bool IsClosed { get; set; }
    }
}