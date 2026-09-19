using System;
using System.Collections.Generic;

namespace ClubTimerXbox.Models
{
    public enum EmployeeDebtStatus
    {
        Active = 0,
        Paused = 1,
        Closed = 2
    }

    public enum EmployeeDebtEventKind
    {
        Created = 0,
        Increased = 1,
        AutomaticRepayment = 2,
        SalaryRepayment = 3,
        HandToHandRepayment = 4,
        Forgiven = 5,
        FreezeRequested = 6,
        FreezeApproved = 7,
        FreezeRejected = 8,
        Resumed = 9,
        RateChanged = 10,
        CreditWithdrawal = 11,
        PausedByOwner = 12,
        FreezeExpired = 13
    }

    public sealed class EmployeeDebtContract
    {
        public string Id { get; set; } = "";

        public string DebtorEmployeeId { get; set; } = "";

        public string DebtorNameSnapshot { get; set; } = "";

        public string CreditorEmployeeId { get; set; } = "";

        public string CreditorNameSnapshot { get; set; } = "";

        public DateTime CreatedAt { get; set; }

        public DateTime UpdatedAt { get; set; }

        public DateTime? ClosedAt { get; set; }

        public int DeductionPercent { get; set; }

        public EmployeeDebtStatus Status { get; set; } = EmployeeDebtStatus.Active;

        public long Revision { get; set; } = 1;

        public string FreezeRequestId { get; set; } = "";

        public DateTime? FreezeRequestedAt { get; set; }

        public int FreezeRequestedDurationDays { get; set; }

        public DateTime? FreezeRejectedAt { get; set; }

        public DateTime? FreezeRequestAvailableAt { get; set; }

        public DateTime? PauseStartedAt { get; set; }

        public DateTime? PauseUntil { get; set; }
    }

    public sealed class EmployeeDebtEvent
    {
        public string Id { get; set; } = "";

        public string OperationId { get; set; } = "";

        public string ContractId { get; set; } = "";

        public EmployeeDebtEventKind Kind { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime UpdatedAt { get; set; }

        public DateTime? SourceBusinessDate { get; set; }

        public string SalarySourceMonthKey { get; set; } = "";

        public int Amount { get; set; }

        public int PreviousPercent { get; set; }

        public int NewPercent { get; set; }

        public string ActorEmployeeId { get; set; } = "";

        public string ActorName { get; set; } = "";

        public string PaymentMethod { get; set; } = "";

        public string Note { get; set; } = "";

        public int FreezeDurationDays { get; set; }

        public DateTime? FreezeUntil { get; set; }
    }

    public sealed class EmployeeDebtEarningCursor
    {
        public string DebtorEmployeeId { get; set; } = "";

        public DateTime BusinessDate { get; set; }

        public string ActiveContractId { get; set; } = "";

        public int GrossHighWater { get; set; }

        public int FractionRemainder { get; set; }

        public DateTime UpdatedAt { get; set; }
    }

    public sealed class EmployeeDebtState
    {
        public int SchemaVersion { get; set; } = 1;

        public List<EmployeeDebtContract> Contracts { get; set; } = new();

        public List<EmployeeDebtEvent> Events { get; set; } = new();

        public Dictionary<string, EmployeeDebtEarningCursor> EarningCursors { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class EmployeeDebtSalaryAllocation
    {
        public string SourceMonthKey { get; set; } = "";

        public DateTime? SourceBusinessDate { get; set; }

        public int Amount { get; set; }
    }

    public sealed class EmployeeDebtManualRepaymentQuote
    {
        public string ContractId { get; set; } = "";

        public long Revision { get; set; }

        public int DebtRemainingAmount { get; set; }

        public int AvailableSalaryAmount { get; set; }

        public int CurrentPeriodAvailableSalaryAmount { get; set; }

        public int AutomaticReserveAmount { get; set; }

        public int MaximumManualPaymentAmount { get; set; }
    }

    public sealed class EmployeeDebtContractSnapshot
    {
        public string Id { get; set; } = "";

        public string DebtorEmployeeId { get; set; } = "";

        public string DebtorName { get; set; } = "";

        public string CreditorEmployeeId { get; set; } = "";

        public string CreditorName { get; set; } = "";

        public DateTime CreatedAt { get; set; }

        public int PrincipalAmount { get; set; }

        public int RepaidAmount { get; set; }

        public int ForgivenAmount { get; set; }

        public int RemainingAmount { get; set; }

        public int DeductionPercent { get; set; }

        public EmployeeDebtStatus Status { get; set; }

        public bool IsAutomaticQueueHead { get; set; }

        public bool HasFreezeRequest { get; set; }

        public string FreezeRequestId { get; set; } = "";

        public DateTime? FreezeRequestedAt { get; set; }

        public int FreezeRequestedDurationDays { get; set; }

        public DateTime? FreezeRejectedAt { get; set; }

        public DateTime? FreezeRequestAvailableAt { get; set; }

        public DateTime? PauseStartedAt { get; set; }

        public DateTime? PauseUntil { get; set; }

        public long Revision { get; set; }

        public List<EmployeeDebtEvent> Events { get; set; } = new();
    }

    public sealed class EmployeeDebtDaySnapshot
    {
        public DateTime BusinessDate { get; set; }

        public int RepaidAmount { get; set; }

        public int ReceivedAmount { get; set; }

        public int WithdrawnAmount { get; set; }

        public int ForgivenAmount { get; set; }

        public List<EmployeeDebtEvent> Events { get; set; } = new();
    }

    public sealed class EmployeeDebtEmployeeSnapshot
    {
        public string EmployeeId { get; set; } = "";

        public string MonthKey { get; set; } = "";

        public int OpeningDebtAmount { get; set; }

        public int AddedDebtAmount { get; set; }

        public int RepaidDebtAmount { get; set; }

        public int ForgivenDebtAmount { get; set; }

        public int ClosingDebtAmount { get; set; }

        public int OpeningCreditAmount { get; set; }

        public int ReceivedCreditAmount { get; set; }

        public int WithdrawnCreditAmount { get; set; }

        public int ClosingCreditAmount { get; set; }

        public bool HasPausedDebt { get; set; }

        public List<EmployeeDebtContractSnapshot> Contracts { get; set; } = new();

        public List<EmployeeDebtDaySnapshot> Days { get; set; } = new();
    }
}
