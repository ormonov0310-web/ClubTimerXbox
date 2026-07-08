using System;

namespace ClubTimerXbox.Models
{
    public enum CashReconciliationKind
    {
        CashExtra,
        CashShortage,
        CashlessExtra,
        CashlessShortage,
        TransferCashToCashless,
        TransferCashlessToCash,
        OwnerWithdrawal,
        OwnerDeposit,
        Other
    }

    public enum CashReconciliationStatus
    {
        Open,
        Resolved
    }

    public class CashReconciliationItem
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public CashReconciliationKind Kind { get; set; } = CashReconciliationKind.Other;

        public CashReconciliationStatus Status { get; set; } = CashReconciliationStatus.Open;

        public int Amount { get; set; }

        public int OriginalAmount { get; set; }

        public int ResolvedAmount { get; set; }

        public int FormalizedAmount { get; set; }

        public int ExpectedAmount { get; set; }

        public int ActualAmount { get; set; }

        public string CheckedByEmployeeName { get; set; } = "";

        public string ResponsibleEmployeeName { get; set; } = "";

        public string SuspectedEmployeeName { get; set; } = "";

        public string Title { get; set; } = "";

        public string Note { get; set; } = "";

        public DateTime? ResolvedAt { get; set; }

        public string ResolvedBy { get; set; } = "";

        public string ResolutionNote { get; set; } = "";
    }
}
