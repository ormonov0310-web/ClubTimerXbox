using System;
using System.Collections.Generic;

namespace ClubTimerXbox.Models
{
    public readonly record struct BusinessPeriodRange(
        DateTime StartInclusive,
        DateTime EndExclusive,
        string Key)
    {
        public bool Contains(DateTime value)
        {
            return value >= StartInclusive && value < EndExclusive;
        }
    }

    public sealed class EmployeePayrollObligation
    {
        public string EmployeeId { get; set; } = "";

        public string EmployeeName { get; set; } = "";

        public string MonthKey { get; set; } = "";

        public int AccruedAmount { get; set; }

        public int BonusAmount { get; set; }

        public int PenaltyAmount { get; set; }

        public int PaidAmount { get; set; }

        public int TimeAmount { get; set; }

        public int GameRevenueAmount { get; set; }

        public int ProductBonusAmount { get; set; }

        public int TimeRatingPercent { get; set; } = 100;

        public int RevenueRatingPercent { get; set; } = 100;

        public int OverallRatingPercent { get; set; } = 100;

        public int RemainingAmount =>
            AccruedAmount + BonusAmount - PenaltyAmount - PaidAmount;
    }

    public sealed class PayrollPaymentAllocation
    {
        public string EmployeeName { get; set; } = "";

        public string SourceMonthKey { get; set; } = "";

        public int Amount { get; set; }
    }

    public sealed class PayrollPaymentTransaction
    {
        public string OperationId { get; set; } = "";

        public DateTime CreatedAt { get; set; }

        public string PaymentMethod { get; set; } = "";

        public string Description { get; set; } = "";

        public int PostedAllocationCount { get; set; }

        public bool IsCompleted { get; set; }

        public List<PayrollPaymentAllocation> Allocations { get; set; } = new();
    }

    public sealed class OwnerIncomeWithdrawalTransaction
    {
        public string OperationId { get; set; } = "";

        public DateTime CreatedAt { get; set; }

        public int Amount { get; set; }

        public string PaymentMethod { get; set; } = "";

        public string AccountingMonthKey { get; set; } = "";

        public string Title { get; set; } = "";

        public string Description { get; set; } = "";

        public bool IsCashRecordPosted { get; set; }

        public bool IsIncomeDeducted { get; set; }

        public bool IsCompleted { get; set; }
    }

    public enum BusinessMonthCloseStep
    {
        None = 0,
        Prepared = 1,
        CashInvestigationsFinalized = 2,
        SalarySnapshotCreated = 3,
        ProfitSnapshotCreated = 4,
        CarryForwardCreated = 5,
        Completed = 6
    }

    public sealed class BusinessMonthCloseJournal
    {
        public string OperationId { get; set; } = "";

        public string MonthKey { get; set; } = "";

        public BusinessMonthCloseStep LastCompletedStep { get; set; }

        public DateTime StartedAt { get; set; }

        public DateTime? CompletedAt { get; set; }

        public bool IsDeferred { get; set; }

        public string DeferredReason { get; set; } = "";
    }

    public sealed class BusinessMonthLedger
    {
        public string MonthKey { get; set; } = "";

        public int GameRevenue { get; set; }

        public int ProductRevenue { get; set; }

        public int ProductCostOfGoodsSold { get; set; }

        public int ServiceRevenue { get; set; }

        public int OtherRevenue { get; set; }

        public int ClubExpenses { get; set; }

        public int UnknownCashShortage { get; set; }

        public int ExtraReserve { get; set; }

        public int ArchivedExtra { get; set; }

        public int ClosedNetProfit { get; set; }

        public int ProfitIncludedAtActivation { get; set; }

        public bool IsClosed { get; set; }

        public DateTime? ArchivedAt { get; set; }

        public string ArchiveChecksum { get; set; } = "";

        public bool IsArchiveVerified { get; set; }

        public Dictionary<string, double> WorkedHours { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);

        public List<EmployeePayrollObligation> Payroll { get; set; } = new();

        public List<SalaryPolicyVersion> SalaryPolicyVersions { get; set; } = new();

        public List<EmployeeRatingArchiveItem> EmployeeRatings { get; set; } = new();
    }

    public sealed class EmployeeRatingArchiveItem
    {
        public string EmployeeId { get; set; } = "";

        public string EmployeeName { get; set; } = "";

        public int TimePercent { get; set; } = 100;

        public int RevenuePercent { get; set; } = 100;

        public int OverallPercent { get; set; } = 100;

        public List<EmployeeRatingEvent> Events { get; set; } = new();
    }

    public sealed class BusinessLedgerState
    {
        public int SchemaVersion { get; set; } = 1;

        public string ActivatedMonthKey { get; set; } = "";

        public DateTime ActivatedAt { get; set; }

        public int CashBalance { get; set; }

        public int CashlessBalance { get; set; }

        public int StockValue { get; set; }

        public int RetainedOwnerIncome { get; set; }

        public Dictionary<string, BusinessMonthLedger> Months { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, BusinessMonthCloseJournal> CloseJournal { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, PayrollPaymentTransaction> PayrollPayments { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, OwnerIncomeWithdrawalTransaction> OwnerWithdrawals { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class BusinessDaySnapshot
    {
        public string DayKey { get; set; } = "";

        public DateTime ClosedAt { get; set; }

        public int GameRevenue { get; set; }

        public int ProductsAndServicesRevenue { get; set; }

        public int Expenses { get; set; }

        public int CashMovement { get; set; }

        public int CashlessMovement { get; set; }
    }

    public sealed class BusinessDayTransitionState
    {
        public string ActivatedDayKey { get; set; } = "";

        public Dictionary<string, BusinessDaySnapshot> ClosedDays { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);
    }
}
