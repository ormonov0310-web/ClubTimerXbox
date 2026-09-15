using System;

namespace ClubTimerXbox.Models
{
    public class CashExpectationAuditItem
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public string IdempotencyKey { get; set; } = "";

        public DateTime CreatedAt { get; set; }

        public string RootAcceptanceKey { get; set; } = "";

        public string AttemptKey { get; set; } = "";

        public string CheckedByEmployeeName { get; set; } = "";

        public string ResponsibleEmployeeName { get; set; } = "";

        public string Source { get; set; } = "";

        public Guid? SourceId { get; set; }

        public DateTime SourceObservedAt { get; set; }

        public DateTime SourceCommittedAt { get; set; }

        public int BaseAmount { get; set; }

        public int CashIncome { get; set; }

        public int CashExpenses { get; set; }

        public int ExpectedCashAmount { get; set; }

        public int ActualCashAmount { get; set; }

        public int Difference { get; set; }

        public int CalculationVersion { get; set; } = 1;
    }
}
