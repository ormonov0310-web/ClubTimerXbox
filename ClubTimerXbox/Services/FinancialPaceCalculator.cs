using System;

namespace ClubTimerXbox.Services
{
    public static class FinancialPaceCalculator
    {
        public static int CalculateFixedExpenseAccrued(
            int dailyExpense,
            DateTime businessDayStart,
            DateTime asOf)
        {
            if (dailyExpense <= 0)
                return 0;

            DateTime accrualStart = businessDayStart.Date.AddHours(11);
            DateTime accrualEnd = businessDayStart.Date.AddDays(1).AddHours(1);
            if (asOf <= accrualStart)
                return 0;
            if (asOf >= accrualEnd)
                return dailyExpense;

            double progress = (asOf - accrualStart).TotalSeconds /
                              (accrualEnd - accrualStart).TotalSeconds;
            return (int)Math.Round(dailyExpense * progress);
        }

        public static int CalculatePercent(int gameRevenue, int totalExpense)
        {
            if (totalExpense <= 0)
                return 0;

            return (int)Math.Round(
                (gameRevenue - totalExpense) * 100.0 / totalExpense);
        }
    }
}
