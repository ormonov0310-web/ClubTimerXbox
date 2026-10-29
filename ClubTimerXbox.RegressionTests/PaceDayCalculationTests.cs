using System.Text.Json;
using ClubTimerXbox.Models;
using ClubTimerXbox.Services;

internal static class PaceDayCalculationTests
{
    internal static void Run()
    {
        DateTime start = new(2026, 10, 1, 6, 0, 0);
        var timeline = new List<FinancialPacePoint> { new() { CreatedAt = start, GameRevenue = 1, TotalExpense = 2, Difference = -1 } };
        int compared = 0;
        foreach (int monthOffset in new[] { -1, 0, 1 })
        foreach (int minutes in new[] { -1, 0, 359, 360, 720, 1079, 1080, 1200, 1439, 1440, 1441 })
        foreach (int monthly in new[] { 0, 1, 1200, 36000 })
        foreach (int salary in new[] { -10, 0, 300, 1491 })
        foreach (int games in new[] { -120, 0, 120, 5500 })
        {
            DateTime day = start.AddMonths(monthOffset), end = day.AddDays(1), now = day.AddMinutes(minutes);
            var actual = FinancialPaceCalculator.CalculateDay(day, end, now, salary, games, monthly,
                "OwnerForecast", "", timeline);
            DateTime calculation = now <= end ? now : end;
            bool hasBaseline = monthly > 0;
            int daily = hasBaseline ? (int)Math.Round(monthly / 30.0) : 0;
            int fixedCost = FinancialPaceCalculator.CalculateFixedExpenseAccrued(daily, day, calculation);
            int expense = fixedCost + Math.Max(0, salary);
            var expected = new FinancialPaceDaySnapshot
            {
                BusinessDateKey = day.ToString("yyyy-MM-dd"), BusinessMonthKey = BusinessCalendarService.GetBusinessMonth(day).Key,
                StartInclusive = day, EndExclusive = end, CalculatedAt = calculation, IsClosed = now >= end,
                HasExpenseBaseline = hasBaseline, ExpenseSourceType = "OwnerForecast", ExpenseSourceMonthKey = "",
                MonthlyFixedExpense = monthly, DailyFixedExpense = daily, FixedExpenseAccrued = fixedCost,
                SalaryAccrued = Math.Max(0, salary), TotalExpense = expense, GameRevenue = games,
                Difference = games - expense, Percent = hasBaseline ? FinancialPaceCalculator.CalculatePercent(games, expense) : 0,
                Timeline = timeline
            };
            if (JsonSerializer.Serialize(expected) != JsonSerializer.Serialize(actual))
                throw new Exception("Pace pure day differs from original calculation");
            actual.Timeline[0].GameRevenue = 999;
            if (timeline[0].GameRevenue != 1) throw new Exception("Pace timeline is shared");
            compared++;
        }
        Console.WriteLine($"Pace day: {compared} full-field comparisons across month/time/expense/signed revenue/salary inputs and timeline isolation passed; pure inputs only.");
    }
}
