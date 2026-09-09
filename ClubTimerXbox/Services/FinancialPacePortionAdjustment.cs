using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services;

internal static class FinancialPacePortionAdjustment
{
    public static bool Apply(FinancialPaceDaySnapshot snapshot, OverNormPortionDay day,
        int salary, DateTime now, bool monthClosed)
    {
        if (monthClosed || !snapshot.IsClosed || snapshot.StartInclusive != day.Start ||
            (snapshot.OverNormPortionCount == day.Portions.Count && snapshot.GameRevenue == day.Revenue))
            return false;
        int revenue = checked((int)day.Revenue);
        salary = Math.Max(0, salary);
        snapshot.RecognitionAdjustments ??= new();
        snapshot.RecognitionAdjustments.Add(new FinancialPaceRecognitionAdjustment
        {
            RecordedAt = now, PreviousGameRevenue = snapshot.GameRevenue, GameRevenue = revenue,
            PreviousSalary = snapshot.SalaryAccrued, Salary = salary, PortionCount = day.Portions.Count
        });
        snapshot.GameRevenue = revenue;
        snapshot.SalaryAccrued = salary;
        snapshot.OverNormPortionCount = day.Portions.Count;
        snapshot.TotalExpense = checked(snapshot.FixedExpenseAccrued + salary);
        snapshot.Difference = checked(revenue - snapshot.TotalExpense);
        snapshot.Percent = snapshot.HasExpenseBaseline
            ? FinancialPaceCalculator.CalculatePercent(revenue, snapshot.TotalExpense) : 0;
        // The original baseline/time and previous values remain in the closed day's audit.
        return true;
    }
}
