using System;

namespace ClubTimerXbox.Models
{
    public class PendingCashlessVerification
    {
        public string CommandId { get; set; } = "";

        public int ExpectedAmount { get; set; }

        public int ActualAmount { get; set; }

        public int ProgramExpectedAmount { get; set; }

        public string SuspectedEmployeeName { get; set; } = "";

        public string Note { get; set; } = "";

        public DateTime SuspectFrom { get; set; }

        public DateTime ObservedAt { get; set; }
    }
}
