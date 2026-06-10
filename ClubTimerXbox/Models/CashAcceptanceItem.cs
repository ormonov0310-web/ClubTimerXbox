using System;

namespace ClubTimerXbox.Models
{
    public class CashAcceptanceItem
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public string CheckedByEmployeeName { get; set; } = "";

        public string ResponsibleEmployeeName { get; set; } = "";

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
