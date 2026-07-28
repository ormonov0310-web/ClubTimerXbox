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

    public enum CashReconciliationOrigin
    {
        Unknown,
        CashAcceptance,
        CashlessVerification,
        BalanceRawDifference
    }

    public enum CashReconciliationStage
    {
        Ready,
        AwaitingCashAcceptance,
        AwaitingCashlessVerification
    }

    public enum CashResponsibilityLevel
    {
        Unknown,
        Suspected,
        Confirmed
    }

    public enum CashReconciliationResolution
    {
        None,
        PairedTender,
        ExtraSettlement,
        FormalizedLoss,
        OwnerBaseline,
        MonthClosed,
        InputCorrection,
        Legacy
    }

    public class CashExtraContribution
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid InvestigationId { get; set; } = Guid.NewGuid();

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public CashReconciliationKind Kind { get; set; } =
            CashReconciliationKind.CashExtra;

        public CashReconciliationOrigin Origin { get; set; } =
            CashReconciliationOrigin.Unknown;

        public CashReconciliationStage Stage { get; set; } =
            CashReconciliationStage.Ready;

        public int OriginalAmount { get; set; }

        public int Amount { get; set; }

        public int ResolvedAmount { get; set; }
    }

    public class CashReconciliationItem
    {
        public int AccountingSchemaVersion { get; set; }

        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid InvestigationId { get; set; } = Guid.NewGuid();

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public CashReconciliationKind Kind { get; set; } = CashReconciliationKind.Other;

        public CashReconciliationStatus Status { get; set; } = CashReconciliationStatus.Open;

        public CashReconciliationOrigin Origin { get; set; } =
            CashReconciliationOrigin.Unknown;

        public CashReconciliationStage Stage { get; set; } =
            CashReconciliationStage.Ready;

        public CashResponsibilityLevel ResponsibilityLevel { get; set; } =
            CashResponsibilityLevel.Unknown;

        public CashReconciliationResolution Resolution { get; set; } =
            CashReconciliationResolution.None;

        public long CheckpointNumber { get; set; }

        public long? ClosedAtCheckpointNumber { get; set; }

        public List<CashExtraContribution> ExtraContributions { get; set; } = new();

        public int Amount { get; set; }

        public int OriginalAmount { get; set; }

        public int ResolvedAmount { get; set; }

        public int FormalizedAmount { get; set; }

        public int PostedFormalizedAmount { get; set; }

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
