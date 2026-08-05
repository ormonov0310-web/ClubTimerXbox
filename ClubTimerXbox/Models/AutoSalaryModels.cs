using System;
using System.Collections.Generic;

namespace ClubTimerXbox.Models
{
    public class AutoSalarySettings
    {
        public int ExpenseReservePercent { get; set; } = 30;

        public int SalaryFundPercent { get; set; } = 25;

        public int TimeSharePercent { get; set; } = 45;

        public int GameRevenueSharePercent { get; set; } = 55;

        public int TimeMonthlyFundAmount { get; set; } = 20000;

        public int TimeMonthlyPlannedHours { get; set; } = 420;

        public int ProductRevenueSharePercent { get; set; } = 0;

        public int ProductBonusPercent { get; set; } = 2;

        public int WorkDayStartHour { get; set; } = 11;

        public int WorkDayEndHour { get; set; } = 1;

        public int DailyGameRevenueNorm { get; set; } = 5000;

        public int OverNormBonusPercent { get; set; } = 10;

        public int PunctualityBonusAmount { get; set; } = 50;

        public int LateActiveSessionBonusAmount { get; set; } = 50;

        public string OpeningResponsibleEmployeeName { get; set; } = "";

        public int LateOpeningGraceMinutes { get; set; } = 30;

        public int LateOpeningPenaltyStepMinutes { get; set; } = 30;

        public int LateOpeningPenaltyStepAmount { get; set; } = 50;

        public int LateOpeningMaxAutoMinutes { get; set; } = 150;
    }

    public class AutoSalaryBonusItem
    {
        public DateTime CreatedAt { get; set; }

        public string Type { get; set; } = "";

        public string Title { get; set; } = "";

        public string Description { get; set; } = "";

        public int Amount { get; set; }
    }

    public class AutoSalaryEmployeeResult
    {
        public string EmployeeId { get; set; } = "";

        public string EmployeeName { get; set; } = "";

        public double WorkHours { get; set; }

        public int GameRevenue { get; set; }

        public int ProductRevenue { get; set; }

        public int TimeAmount { get; set; }

        public int GameRevenueAmount { get; set; }

        public int ProductShareAmount { get; set; }

        public int ProductBonusAmount { get; set; }

        public int BonusAmount { get; set; }

        public List<AutoSalaryBonusItem> Bonuses { get; set; } =
            new List<AutoSalaryBonusItem>();

        public int GrossAmount { get; set; }

        public int LossesAmount { get; set; }

        public int MoneyLossesAmount { get; set; }

        public int RawMoneyLossesAmount { get; set; }

        public int ProductLossesAmount { get; set; }

        public int ViolationLossesAmount { get; set; }

        public int PaidAmount { get; set; }

        public int CarryInAmount { get; set; }

        public int CurrentPeriodRemainingAmount { get; set; }

        public int RemainingAmount { get; set; }

        public int TimeRatingPercent { get; set; } = 100;

        public int RevenueRatingPercent { get; set; } = 100;

        public int OverallRatingPercent { get; set; } = 100;

        public bool RatingHasWarning { get; set; }

        public List<EmployeeRatingEvent> RatingEvents { get; set; } = new();
    }

    public class AutoSalaryReport
    {
        public string MonthKey { get; set; } = "";

        public AutoSalarySettings Settings { get; set; } = new AutoSalarySettings();

        public DateTime SettingsEffectiveFrom { get; set; }

        public bool HasPendingSettings { get; set; }

        public int GameRevenue { get; set; }

        public int ProductRevenue { get; set; }

        public int ExpenseReserveAmount { get; set; }

        public int SalaryBaseAmount { get; set; }

        public int SalaryFundAmount { get; set; }

        public int TimeFundAmount { get; set; }

        public int GameRevenueFundAmount { get; set; }

        public int ProductShareFundAmount { get; set; }

        public int ProductBonusTotalAmount { get; set; }

        public int BonusTotalAmount { get; set; }

        public List<AutoSalaryEmployeeResult> Employees { get; set; } =
            new List<AutoSalaryEmployeeResult>();
    }
}
