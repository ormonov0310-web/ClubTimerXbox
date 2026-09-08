using ClubTimerXbox.Models;
using ClubTimerXbox.Services;
using System.Text.Json;

internal sealed class FinancialPaceTestSuite
{
    private static readonly DateTime Start = new(2026, 9, 1, 6, 0, 0);
    private int _passed;

    public void Run()
    {
        Test("no fixed costs before noon", () => Equal(0, Accrued(11, 59)));
        Test("noon begins at zero", () => Equal(0, Accrued(12)));
        Test("six pm catches up to half", () => Equal(600, Accrued(18)));
        Test("midnight has the full fixed cost", () => Equal(1200, Accrued(24)));
        Test("fixed costs stay full until business close", () => Equal(1200, Accrued(29, 59)));
        Test("fixed cost ramp is bounded and monotone", RampIsMonotone);
        Test("zero expense is safe", () => Equal(0,
            FinancialPaceCalculator.CalculateFixedExpenseAccrued(0, Start, Start.AddHours(20))));
        Test("activity clock is unchanged", ActivityClockIsUnchanged);
        Test("one pm forecast does not invent five hundred", OnePmForecast);
        Test("forecast does not extrapolate accrued fixed expense", AccruedExpenseDoesNotChangeProjection);
        Test("six pm forecast subtracts full cost once", SixPmForecast);
        Test("after one am forecast equals open day fact", AfterHoursForecast);
        Test("early day uses closed evidence only", EarlyDayUsesClosedEvidence);
        Test("first morning has insufficient forecast data", FirstMorningHasNoForecast);
        Test("missing baseline has no forecast", MissingBaseline);
        Test("midmonth coverage stays explicit", MidmonthCoverage);
        Test("closed leap month is immutable fact", ClosedLeapMonth);
        Test("owner withdrawals follow accounting month", OwnerWithdrawalsFollowMonth);
        Test("owner withdrawals combine payment methods", OwnerWithdrawalsCombineMethods);
        Test("owner totals exclude payroll purchases and games", OwnerTotalsExcludeOtherRows);
        Test("legacy withdrawal follows six am business boundary", LegacyWithdrawalBoundary);
        Console.WriteLine($"Financial pace: {_passed} scenarios passed.");
    }

    private static int Accrued(int hour, int minute = 0) =>
        FinancialPaceCalculator.CalculateFixedExpenseAccrued(
            1200, Start, Start.Date.AddHours(hour).AddMinutes(minute));

    private static void RampIsMonotone()
    {
        int previous = 0;
        for (int minute = 0; minute <= 24 * 60; minute++)
        {
            int value = FinancialPaceCalculator.CalculateFixedExpenseAccrued(
                1237, Start, Start.AddMinutes(minute));
            True(value >= previous && value <= 1237);
            previous = value;
        }
    }

    private static void ActivityClockIsUnchanged()
    {
        Equal(0d, FinancialPaceCalculator.CalculateOperatingProgress(Start, Start.Date.AddHours(11)));
        Equal(0.5d, FinancialPaceCalculator.CalculateOperatingProgress(Start, Start.Date.AddHours(18)));
        Equal(1d, FinancialPaceCalculator.CalculateOperatingProgress(Start, Start.Date.AddHours(25)));
    }

    private static void OnePmForecast()
    {
        var day = Day(Start, 300, 100, 13);
        Equal(100, day.Difference);
        var result = Forecast(new[] { day }, Start.Date.AddHours(13));
        Equal(200, result.CurrentDayProjectedDifference);
        Equal(6000, result.ProjectedDifference);
    }

    private static void AccruedExpenseDoesNotChangeProjection()
    {
        var day = Day(Start, 300, 100, 13);
        var first = Forecast(new[] { day }, Start.Date.AddHours(13));
        day.FixedExpenseAccrued = 171;
        day.TotalExpense = 271;
        day.Difference = 29;
        var second = Forecast(new[] { day }, Start.Date.AddHours(13));
        Equal(first.ProjectedDifference, second.ProjectedDifference);
    }

    private static void SixPmForecast()
    {
        var day = Day(Start, 1200, 300, 18);
        Equal(300, day.Difference);
        Equal(600, Forecast(new[] { day }, Start.Date.AddHours(18)).CurrentDayProjectedDifference);
    }

    private static void AfterHoursForecast()
    {
        var day = Day(Start, 3000, 500, 28);
        Equal(day.Difference, Forecast(new[] { day }, Start.Date.AddHours(28)).CurrentDayProjectedDifference);
    }

    private static void EarlyDayUsesClosedEvidence()
    {
        var yesterday = Day(Start, 2500, 300, 30, closed: true);
        var today = Day(Start.AddDays(1), 80, 30, 10);
        var result = Forecast(new[] { yesterday, today }, Start.AddDays(1).Date.AddHours(10));
        True(result.IsAvailable && !result.IncludesCurrentDayProjection);
        Equal(30000, result.ProjectedDifference);
    }

    private static void FirstMorningHasNoForecast() =>
        True(!Forecast(new[] { Day(Start, 80, 30, 10) }, Start.Date.AddHours(10)).IsAvailable);

    private static void MissingBaseline()
    {
        var day = Day(Start, 1200, 100, 18);
        day.HasExpenseBaseline = false;
        True(!Forecast(new[] { day }, Start.Date.AddHours(18)).IsAvailable);
    }

    private static void MidmonthCoverage()
    {
        var day = Day(Start.AddDays(16), 1200, 300, 18);
        var result = Forecast(new[] { day }, day.CalculatedAt);
        Equal("2026-09-17", result.PeriodStartKey);
        Equal(14, result.TotalDays);
        Equal(8400, result.ProjectedDifference);
    }

    private static void ClosedLeapMonth()
    {
        DateTime start = new(2028, 2, 1, 6, 0, 0);
        var days = Enumerable.Range(0, 29)
            .Select(index => Day(start.AddDays(index), 1600 + index, 200, 30, closed: true))
            .ToArray();
        // A saved legacy snapshot is authoritative even if its components differ.
        days[0].Difference = 137;
        string before = JsonSerializer.Serialize(days);
        var result = FinancialPaceCalculator.CalculateMonthForecast(
            days, start, start.AddMonths(1), start.AddMonths(1));
        Equal(days.Sum(day => day.Difference), result.ProjectedDifference);
        Equal(29, result.TotalDays);
        True(result.IsFinal && result.CoveragePercent == 100);
        Equal(before, JsonSerializer.Serialize(days));
    }

    private static void OwnerWithdrawalsFollowMonth()
    {
        var rows = new[] { Withdrawal(500, "2026-09"), Withdrawal(1500, "2026-08") };
        Equal(500, Total(rows, Start));
        Equal(1500, Total(rows, Start.AddMonths(-1)));
        Equal(rows.Sum(row => row.Amount), Total(rows, Start) + Total(rows, Start.AddMonths(-1)));
    }

    private static void OwnerWithdrawalsCombineMethods()
    {
        var cash = Withdrawal(400, "2026-09");
        var bank = Withdrawal(600, "2026-09");
        bank.PaymentMethod = "Безнал";
        Equal(1000, Total(new[] { cash, bank }, Start));
    }

    private static void OwnerTotalsExcludeOtherRows()
    {
        var salary = Withdrawal(800, "2026-09");
        salary.ExpenseCategory = "Зарплата";
        var purchase = Withdrawal(200, "2026-09");
        purchase.ExpenseCategory = "Закупка";
        var game = Withdrawal(500, "2026-09");
        game.Category = "Игры";
        Equal(50, Total(new[] { salary, purchase, game, Withdrawal(50, "2026-09") }, Start));
    }

    private static void LegacyWithdrawalBoundary()
    {
        var row = Withdrawal(77, "");
        row.CreatedAt = Start.AddSeconds(-1);
        Equal(0, Total(new[] { row }, Start));
        Equal(77, Total(new[] { row }, Start.AddMonths(-1)));
        row.CreatedAt = Start;
        Equal(77, Total(new[] { row }, Start));
    }

    private static CashRecord Withdrawal(int amount, string month) => new()
    {
        Category = "Расходы", ExpenseCategory = "Владелец",
        PaymentMethod = "Наличные", Amount = amount,
        AccountingMonthKey = month, CreatedAt = Start.AddDays(4)
    };

    private static int Total(IEnumerable<CashRecord> rows, DateTime start) =>
        CashService.GetOwnerWithdrawRecordsByPeriod(rows, start, start.AddMonths(1))
            .Sum(row => row.Amount);

    private static FinancialPaceDaySnapshot Day(DateTime start, int games, int salary, int hour, bool closed = false)
    {
        DateTime asOf = start.Date.AddHours(hour);
        int fixedExpense = FinancialPaceCalculator.CalculateFixedExpenseAccrued(1200, start, asOf);
        return new FinancialPaceDaySnapshot
        {
            BusinessDateKey = start.ToString("yyyy-MM-dd"), StartInclusive = start,
            EndExclusive = start.AddDays(1), CalculatedAt = asOf, IsClosed = closed,
            HasExpenseBaseline = true, DailyFixedExpense = 1200,
            FixedExpenseAccrued = fixedExpense, SalaryAccrued = salary, GameRevenue = games,
            TotalExpense = fixedExpense + salary, Difference = games - fixedExpense - salary
        };
    }

    private static FinancialPaceForecastSnapshot Forecast(FinancialPaceDaySnapshot[] days, DateTime asOf) =>
        FinancialPaceCalculator.CalculateMonthForecast(days, Start, Start.AddMonths(1), asOf);

    private void Test(string name, Action action)
    {
        action();
        _passed++;
        Console.WriteLine($"PASS pace: {name}");
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }

    private static void True(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Expected true.");
    }
}
