using ClubTimerXbox.Models;
using ClubTimerXbox.Services;

// Frozen formulas from 08571354 (1.6.1); only data access is replaced with fixture queries.
internal sealed class SalaryAccrualLegacyOracle(SalaryAccrualFixture fixture)
{
    private const string GamesCategory = "Игры";
    private readonly SalaryAccrualFixture _fixture = fixture;
    internal sealed record Input(string EmployeeName, double WorkHours, List<PaidInterval> PaidIntervals);
    internal RatingAccrualBreakdown Time(
        Input input,
        DateTime monthStart,
        DateTime nextMonthStart)
    {
        var groups = new Dictionary<TimeAccrualGroupKey, double>();
        double intervalHours = 0;
        foreach (var interval in input.PaidIntervals)
        {
            foreach (var segment in SplitBySalaryRules(
                         input.EmployeeName,
                         interval.Start,
                         interval.End))
            {
                SalaryPolicyVersion policy =
                    _fixture.PolicyAt(segment.Start);
                AutoSalarySettings settings = policy.Settings;
                int rating = _fixture.Rating(
                    input.EmployeeName,
                    EmployeeRatingBranch.Time,
                    segment.Start);
                double hours = (segment.End - segment.Start).TotalHours;
                intervalHours += hours;
                var key = new TimeAccrualGroupKey(
                    policy.Id,
                    _fixture.Signature(
                        input.EmployeeName,
                        EmployeeRatingBranch.Time,
                        segment.Start),
                    settings.TimeMonthlyFundAmount,
                    settings.TimeMonthlyPlannedHours,
                    rating);
                groups[key] = groups.TryGetValue(key, out double total)
                    ? total + hours
                    : hours;
            }
        }

        double protectedExtraHours = Math.Max(0, input.WorkHours - intervalHours);
        if (protectedExtraHours > 0)
        {
            // Recovered hours represent an older protected balance. Pinning them to
            // the month opening prevents a later rating change from repricing them.
            DateTime at = monthStart;
            SalaryPolicyVersion policy = _fixture.PolicyAt(at);
            AutoSalarySettings settings = policy.Settings;
            int rating = _fixture.Rating(
                input.EmployeeName,
                EmployeeRatingBranch.Time,
                at);
            var key = new TimeAccrualGroupKey(
                policy.Id,
                _fixture.Signature(
                    input.EmployeeName,
                    EmployeeRatingBranch.Time,
                    at),
                settings.TimeMonthlyFundAmount,
                settings.TimeMonthlyPlannedHours,
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

    internal RatingAccrualBreakdown Games(
        string employeeName,
        DateTime fromInclusive,
        DateTime toExclusive)
    {
        var groups = _fixture.Cash(fromInclusive, toExclusive)
            .Where(record =>
                record.Category == GamesCategory &&
                record.IncomeEmployeeName.Equals(
                    employeeName,
                    StringComparison.OrdinalIgnoreCase))
            .Select(record =>
            {
                DateTime at = SalaryAccrualFixture.BusinessTime(record);
                SalaryPolicyVersion policy =
                    _fixture.PolicyAt(at);
                AutoSalarySettings settings = policy.Settings;
                int rating = _fixture.Rating(
                    employeeName,
                    EmployeeRatingBranch.Revenue,
                    at);
                return new
                {
                    record.Amount,
                    PolicyId = policy.Id,
                    RatingSignature = _fixture.Signature(
                        employeeName,
                        EmployeeRatingBranch.Revenue,
                        at),
                    settings.ExpenseReservePercent,
                    settings.SalaryFundPercent,
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

    internal int Products(
        string employeeName,
        DateTime fromInclusive,
        DateTime toExclusive)
    {
        var groups = _fixture
            .Products(fromInclusive, toExclusive, employeeName)
            .Select(entry =>
            {
                SalaryPolicyVersion policy =
                    _fixture.PolicyAt(entry.OccurredAt);
                return new
                {
                    entry.Amount,
                    PolicyId = policy.Id,
                    policy.Settings.ProductBonusPercent
                };
            })
            .GroupBy(item => new { item.PolicyId, item.ProductBonusPercent });
        int amount = groups.Sum(group => Percent(
            group.Sum(item => item.Amount),
            group.Key.ProductBonusPercent));
        return Math.Max(0, amount);
    }

    internal (int ExpenseReserve, int SalaryFund) GameFund(
        DateTime fromInclusive,
        DateTime toExclusive)
    {
        var groups = _fixture.Cash(fromInclusive, toExclusive)
            .Where(record => record.Category == GamesCategory)
            .Select(record =>
            {
                SalaryPolicyVersion policy = _fixture.PolicyAt(
                    SalaryAccrualFixture.BusinessTime(record));
                AutoSalarySettings settings = policy.Settings;
                return new
                {
                    record.Amount,
                    PolicyId = policy.Id,
                    settings.ExpenseReservePercent,
                    settings.SalaryFundPercent
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

    private IEnumerable<PaidInterval> SplitBySalaryRules(
        string employeeName,
        DateTime start,
        DateTime end)
    {
        var boundaries = _fixture
            .Versions(start, end)
            .Select(item => item.EffectiveFrom)
            .Concat(_fixture.Boundaries(employeeName, start, end))
            .Where(value => value > start && value < end)
            .Distinct()
            .OrderBy(value => value)
            .ToList();
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

    private static int Percent(int amount, int percent) => (int)Math.Round(amount * (percent / 100.0));
}
