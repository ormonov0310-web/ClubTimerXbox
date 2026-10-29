using System;
using System.Collections.Generic;
using System.Linq;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services;

internal readonly record struct PaidInterval(DateTime Start, DateTime End);

internal readonly record struct RatingAccrualBreakdown(
    int Amount,
    int EarnedAmount,
    int LostAmount,
    IReadOnlyList<int> RatingPercents);

// Pure salary formulas over one owned input snapshot; no persistence or live service reads.
internal static class SalaryAccrualCalculator
{
    internal static RatingAccrualBreakdown Time(
        string employeeName, double workHours, IReadOnlyList<PaidInterval> intervals,
        DateTime monthStart, SalaryAccrualSource source)
    {
        using var measurement = SnapshotBuildMeasurements.Measure("salary.accrual");
        var groups = new Dictionary<TimeAccrualGroupKey, double>();
        double intervalHours = 0;
        foreach (var interval in intervals)
        {
            foreach (var segment in SplitBySalaryRules(
                         employeeName,
                         interval.Start,
                         interval.End, source))
            {
                SalaryAccrualPolicy policy =
                    source.Rules.PolicyAt(segment.Start);

                int rating = source.Rules.Employee(employeeName).At(EmployeeRatingBranch.Time, segment.Start).Percent;
                double hours = (segment.End - segment.Start).TotalHours;
                intervalHours += hours;
                var key = new TimeAccrualGroupKey(
                    policy.Id,
                    source.Rules.Employee(employeeName).At(EmployeeRatingBranch.Time, segment.Start).Signature,
                    policy.MonthlyFund,
                    policy.PlannedHours,
                    rating);
                groups[key] = groups.TryGetValue(key, out double total)
                    ? total + hours
                    : hours;
            }
        }

        double protectedExtraHours = Math.Max(0, workHours - intervalHours);
        if (protectedExtraHours > 0)
        {
            // Recovered hours represent an older protected balance. Pinning them to
            // the month opening prevents a later rating change from repricing them.
            DateTime at = monthStart;
            SalaryAccrualPolicy policy = source.Rules.PolicyAt(at);

            int rating = source.Rules.Employee(employeeName).At(EmployeeRatingBranch.Time, at).Percent;
            var key = new TimeAccrualGroupKey(
                policy.Id,
                source.Rules.Employee(employeeName).At(EmployeeRatingBranch.Time, at).Signature,
                policy.MonthlyFund,
                policy.PlannedHours,
                rating);
            groups[key] = groups.TryGetValue(key, out double total)
                ? total + protectedExtraHours
                : protectedExtraHours;
        }

        int amount = 0;
        int earnedAmount = 0;
        int lostAmount = 0;
        foreach (var group in groups)
        {
            var settings = new AutoSalarySettings
            {
                TimeMonthlyFundAmount = group.Key.MonthlyFund,
                TimeMonthlyPlannedHours = group.Key.PlannedHours
            };
            double baseline = EmployeeSalaryRuleEngine.CalculateTimeAccrual(
                group.Value,
                settings,
                100);
            RatingFinancialEffect effect =
                EmployeeSalaryRuleEngine.CalculateRatingFinancialEffect(
                    baseline,
                    group.Key.Rating);
            amount += effect.ActualAmount;
            earnedAmount += effect.EarnedAmount;
            lostAmount += effect.LostAmount;
        }

        return new RatingAccrualBreakdown(
            Math.Max(0, amount),
            earnedAmount,
            lostAmount,
            groups.Keys
                .Select(key => key.Rating)
                .Distinct()
                .OrderBy(value => value)
                .ToList());
    }

    internal static RatingAccrualBreakdown Games(
        string employeeName,
        DateTime fromInclusive,
        DateTime toExclusive, SalaryAccrualSource source)
    {
        using var measurement = SnapshotBuildMeasurements.Measure("salary.accrual");
        var groups = source.Games(fromInclusive, toExclusive, employeeName)
            .Select(record =>
            {
                DateTime at = record.OccurredAt;
                SalaryAccrualPolicy policy =
                    source.Rules.PolicyAt(at);

                int rating = source.Rules.Employee(employeeName).At(EmployeeRatingBranch.Revenue, at).Percent;
                return new
                {
                    record.Amount,
                    PolicyId = policy.Id,
                    RatingSignature = source.Rules.Employee(employeeName).At(EmployeeRatingBranch.Revenue, at).Signature,
                    ExpenseReservePercent = policy.ReservePercent,
                    SalaryFundPercent = policy.SalaryPercent,
                    Rating = rating
                };
            })
            .GroupBy(item => new
            {
                item.PolicyId,
                item.RatingSignature,
                item.ExpenseReservePercent,
                item.SalaryFundPercent,
                item.Rating
            });

        double amount = 0;
        double baselineAmount = 0;
        double earnedAmount = 0;
        double lostAmount = 0;
        var ratingPercents = new HashSet<int>();
        foreach (var group in groups)
        {
            ratingPercents.Add(group.Key.Rating);
            int revenue = group.Sum(item => item.Amount);
            int reserve = Percent(revenue, group.Key.ExpenseReservePercent);
            int salaryBeforeRating = Percent(
                Math.Max(0, revenue - reserve),
                group.Key.SalaryFundPercent);
            double ratedAmount = salaryBeforeRating * group.Key.Rating / 100.0;
            amount += ratedAmount;
            baselineAmount += salaryBeforeRating;
            double difference = ratedAmount - salaryBeforeRating;
            if (difference >= 0)
                earnedAmount += difference;
            else
                lostAmount += -difference;
        }

        int actual = Math.Max(0, (int)Math.Round(amount));
        int baseline = Math.Max(0, (int)Math.Round(baselineAmount));
        int earned = Math.Max(0, (int)Math.Round(earnedAmount));
        int lost = Math.Max(0, (int)Math.Round(lostAmount));

        // Separate rounding of gains and losses may differ by one som from
        // the already fixed salary amount. Keep the displayed net exact.
        int reconciliation = (actual - baseline) - (earned - lost);
        if (reconciliation > 0)
            earned += reconciliation;
        else if (reconciliation < 0)
            lost += -reconciliation;

        return new RatingAccrualBreakdown(
            actual,
            earned,
            lost,
            ratingPercents.OrderBy(value => value).ToList());
    }

    internal static int Products(
        string employeeName,
        DateTime fromInclusive,
        DateTime toExclusive, SalaryAccrualSource source)
    {
        using var measurement = SnapshotBuildMeasurements.Measure("salary.accrual");
        var groups = source.Products(fromInclusive, toExclusive, employeeName)
            .Select(entry =>
            {
                SalaryAccrualPolicy policy =
                    source.Rules.PolicyAt(entry.OccurredAt);
                return new
                {
                    entry.Amount,
                    PolicyId = policy.Id,
                    ProductBonusPercent = policy.ProductPercent
                };
            })
            .GroupBy(item => new { item.PolicyId, item.ProductBonusPercent });
        int amount = groups.Sum(group => Percent(
            group.Sum(item => item.Amount),
            group.Key.ProductBonusPercent));
        return Math.Max(0, amount);
    }

    internal static (int ExpenseReserve, int SalaryFund) GameFund(
        DateTime fromInclusive,
        DateTime toExclusive, SalaryAccrualSource source)
    {
        using var measurement = SnapshotBuildMeasurements.Measure("salary.accrual");
        var groups = source.Games(fromInclusive, toExclusive)
            .Select(record =>
            {
                SalaryAccrualPolicy policy = source.Rules.PolicyAt(
                    record.OccurredAt);

                return new
                {
                    record.Amount,
                    PolicyId = policy.Id,
                    ExpenseReservePercent = policy.ReservePercent,
                    SalaryFundPercent = policy.SalaryPercent
                };
            })
            .GroupBy(item => new
            {
                item.PolicyId,
                item.ExpenseReservePercent,
                item.SalaryFundPercent
            });
        int reserve = 0;
        int salary = 0;
        foreach (var group in groups)
        {
            int revenue = group.Sum(item => item.Amount);
            int groupReserve = Percent(revenue, group.Key.ExpenseReservePercent);
            reserve += groupReserve;
            salary += Percent(
                Math.Max(0, revenue - groupReserve),
                group.Key.SalaryFundPercent);
        }

        return (
            Math.Max(0, reserve),
            Math.Max(0, salary));
    }

    private static IEnumerable<PaidInterval> SplitBySalaryRules(
        string employeeName,
        DateTime start,
        DateTime end, SalaryAccrualSource source)
    {
        var boundaries = source.Rules.Boundaries(employeeName, start, end);
        DateTime cursor = start;
        foreach (DateTime boundary in boundaries)
        {
            yield return new PaidInterval(cursor, boundary);
            cursor = boundary;
        }

        if (end > cursor)
            yield return new PaidInterval(cursor, end);
    }

    private readonly record struct TimeAccrualGroupKey(
        Guid PolicyId,
        string RatingSignature,
        int MonthlyFund,
        int PlannedHours,
        int Rating);

    private static int Percent(int amount, int percent)
        => (int)Math.Round(amount * (percent / 100.0));
}
