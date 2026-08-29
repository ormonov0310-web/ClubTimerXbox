using System;

namespace ClubTimerXbox.Models
{
    public class CashAcceptanceItem
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public string CheckedByEmployeeName { get; set; } = "";

        public string ResponsibleEmployeeName { get; set; } = "";

        public string AcceptanceKey { get; set; } = "";

        public string RootAcceptanceKey { get; set; } = "";

        public System.Collections.Generic.List<string> AttemptKeys { get; set; } =
            new System.Collections.Generic.List<string>();

        public bool IsProvisional { get; set; }

        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        public DateTime? FinalizeAt { get; set; }

        public DateTime? FinalizedAt { get; set; }

        public PendingCashlessVerification? PendingCashlessVerification { get; set; }

        public int ExpectedCashAmount { get; set; }

        public int ActualCashAmount { get; set; }

        public int Difference { get; set; }

        public int ShortageAmount
        {
            get
            {
                if (Difference < 0)
                    return Math.Abs(Difference);

                return 0;
            }
        }

        public int ExtraAmount
        {
            get
            {
                if (Difference > 0)
                    return Difference;

                return 0;
            }
        }

        public string Note { get; set; } = "";
    }
}
