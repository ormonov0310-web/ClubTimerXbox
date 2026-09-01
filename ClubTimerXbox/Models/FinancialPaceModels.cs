using System;
using System.Collections.Generic;

namespace ClubTimerXbox.Models
{
    public sealed class FinancialPaceManualExpenseVersion
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public DateTime CreatedAt { get; set; }
        public DateTime EffectiveFrom { get; set; }
        public int MonthlyExpenseAmount { get; set; }
    }

    public sealed class FinancialPacePoint
    {
        public DateTime CreatedAt { get; set; }
        public int GameRevenue { get; set; }
        public int FixedExpenseAccrued { get; set; }
        public int SalaryAccrued { get; set; }
        public int TotalExpense { get; set; }
        public int Difference { get; set; }
        public int Percent { get; set; }
    }

    public sealed class FinancialPaceDaySnapshot
    {
        public string BusinessDateKey { get; set; } = "";
        public string BusinessMonthKey { get; set; } = "";
        public DateTime StartInclusive { get; set; }
        public DateTime EndExclusive { get; set; }
        public DateTime CalculatedAt { get; set; }
        public bool IsClosed { get; set; }
        public bool HasExpenseBaseline { get; set; }
        public string ExpenseSourceType { get; set; } = "Missing";
        public string ExpenseSourceMonthKey { get; set; } = "";
        public int MonthlyFixedExpense { get; set; }
        public int DailyFixedExpense { get; set; }
        public int FixedExpenseAccrued { get; set; }
        public int SalaryAccrued { get; set; }
        public int TotalExpense { get; set; }
        public int GameRevenue { get; set; }
        public int Difference { get; set; }
        public int Percent { get; set; }
        public List<FinancialPacePoint> Timeline { get; set; } = new();
    }

    public sealed class FinancialPaceMonthSnapshot
    {
        public string MonthKey { get; set; } = "";
        public int GameRevenue { get; set; }
        public int TotalExpense { get; set; }
        public int Difference { get; set; }
        public int Percent { get; set; }
        public int ProfitableDays { get; set; }
        public int LossDays { get; set; }
        public int NeutralDays { get; set; }
        public bool HasExpenseBaseline { get; set; }
        public string ExpenseSourceType { get; set; } = "Missing";
        public string ExpenseSourceMonthKey { get; set; } = "";
        public int MonthlyFixedExpense { get; set; }
        public int DailyFixedExpense { get; set; }
        public int? ManualMonthlyExpense { get; set; }
        public DateTime? ManualExpenseEffectiveFrom { get; set; }
        public List<FinancialPaceDaySnapshot> Days { get; set; } = new();
    }

    public sealed class FinancialPaceState
    {
        public List<FinancialPaceManualExpenseVersion> ManualExpenseVersions { get; set; } = new();
        public Dictionary<string, FinancialPaceDaySnapshot> ClosedDays { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, List<FinancialPacePoint>> OpenDayTimelines { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);
    }
}
