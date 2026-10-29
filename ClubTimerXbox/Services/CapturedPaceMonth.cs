using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services;

// A detached calculation produces a mutation plan; only its owner may persist it.
internal sealed class CapturedPaceMonth
{
    internal sealed record Change(string DayKey, FinancialPaceDaySnapshot? ClosedDay,
        List<FinancialPacePoint>? Timeline);
    internal sealed record Calculation(FinancialPaceMonthSnapshot Snapshot, IReadOnlyList<Change> Changes);
    internal sealed record DaySource(bool HasActual, int MonthlyExpense, string MonthKey,
        IReadOnlyDictionary<string, int> GamesByDay);
    private static readonly DateTime FeatureEffectiveFrom = new(2026, 9, 1, 6, 0, 0);
    private readonly FinancialPaceState _state;
    private readonly Dictionary<string, OverNormPortionDay?> _portions;
    private readonly DaySource _source;
    private readonly DateTime _now;
    private readonly DateTime _month;
    private readonly bool _monthClosed;
    private readonly int _ownerWithdrawn;
    private readonly List<Change> _changes = new();

    internal CapturedPaceMonth(DateTime month, DateTime now, FinancialPaceState state,
        IReadOnlyDictionary<string, OverNormPortionDay?> portions, DaySource source,
        bool monthClosed, int ownerWithdrawn)
    {
        _month = month; _now = now;
        _state = Copy(state);
        _portions = portions.ToDictionary(pair => pair.Key, pair => Copy(pair.Value));
        _source = source with { GamesByDay = source.GamesByDay.ToDictionary(pair => pair.Key, pair => pair.Value) };
        _monthClosed = monthClosed; _ownerWithdrawn = ownerWithdrawn;
    }

    internal Calculation Calculate(IReadOnlyDictionary<string, List<AutoSalaryDayEarning>> dailyEarningsByEmployee)
    {
        DateTime monthAnchor = _month;
        var month = BusinessCalendarService.GetBusinessMonthByAnchor(monthAnchor);
        DateTime now = _now;
        DateTime lastIncludedStart = now < month.StartInclusive
            ? month.StartInclusive.AddDays(-1)
            : Min(
                BusinessCalendarService.GetBusinessDay(now).StartInclusive,
                month.EndExclusive.AddDays(-1));
        var salaryByDay = BuildSalaryByDay(dailyEarningsByEmployee);
        var days = new List<FinancialPaceDaySnapshot>();
        var source = new Lazy<DaySource>(() => _source);
        var monthClosed = new Lazy<bool>(() => _monthClosed);
        for (DateTime dayStart = month.StartInclusive;
             dayStart <= lastIncludedStart && dayStart < month.EndExclusive;
             dayStart = dayStart.AddDays(1))
        {
            DateTime dayEnd = dayStart.AddDays(1);
            string dayKey = dayStart.ToString("yyyy-MM-dd");
            bool isClosed = now >= dayEnd;
            var portions = _portions.GetValueOrDefault(dayKey);
            if (isClosed && _state.ClosedDays.TryGetValue(dayKey, out var stored))
            {
                if (portions != null && FinancialPacePortionAdjustment.Apply(stored, portions,
                        salaryByDay.GetValueOrDefault(dayKey), now, monthClosed.Value))
                    Save(dayKey);
                days.Add(Clone(stored));
                continue;
            }

            FinancialPaceDaySnapshot calculated = CalculateDay(dayStart, dayEnd, now,
                salaryByDay.TryGetValue(dayKey, out int salary) ? salary : 0, source.Value);
            calculated.OverNormPortionCount = portions?.Portions.Count ?? 0;
            if (isClosed && dayStart >= FeatureEffectiveFrom)
            {
                calculated.IsClosed = true;
                _state.ClosedDays[dayKey] = Clone(calculated);
                _state.OpenDayTimelines.Remove(dayKey);
                Save(dayKey);
            }
            else if (!isClosed && dayStart <= now && AddTimelinePoint(calculated))
            {
                Save(dayKey);
            }
            days.Add(calculated);
        }

        var result = new FinancialPaceMonthSnapshot
        {
            MonthKey = month.Key,
            OwnerWithdrawnAmount = _ownerWithdrawn,
            Days = days.OrderByDescending(day => day.StartInclusive).ToList(),
            GameRevenue = days.Sum(day => day.GameRevenue),
            TotalExpense = days.Sum(day => day.TotalExpense),
            ProfitableDays = days.Count(day => day.HasExpenseBaseline && day.Difference > 0),
            LossDays = days.Count(day => day.HasExpenseBaseline && day.Difference < 0),
            NeutralDays = days.Count(day => day.HasExpenseBaseline && day.Difference == 0)
        };
        result.Difference = result.GameRevenue - result.TotalExpense;
        result.Percent = FinancialPaceCalculator.CalculatePercent(
            result.GameRevenue,
            result.TotalExpense);
        result.Forecast = FinancialPaceCalculator.CalculateMonthForecast(
            days,
            month.StartInclusive,
            month.EndExclusive,
            now);

        FinancialPaceDaySnapshot? latest = days
            .OrderByDescending(day => day.StartInclusive)
            .FirstOrDefault();
        if (latest != null)
        {
            result.HasExpenseBaseline = latest.HasExpenseBaseline;
            result.ExpenseSourceType = latest.ExpenseSourceType;
            result.ExpenseSourceMonthKey = latest.ExpenseSourceMonthKey;
            result.MonthlyFixedExpense = latest.MonthlyFixedExpense;
            result.DailyFixedExpense = latest.DailyFixedExpense;
        }

        FinancialPaceManualExpenseVersion? manual = _state.ManualExpenseVersions
            .Where(version => version.EffectiveFrom <= month.EndExclusive)
            .OrderByDescending(version => version.EffectiveFrom)
            .ThenByDescending(version => version.CreatedAt)
            .FirstOrDefault();
        if (manual != null)
        {
            result.ManualMonthlyExpense = manual.MonthlyExpenseAmount;
            result.ManualExpenseEffectiveFrom = manual.EffectiveFrom;
        }

        return new(result, _changes.ToArray());
    }

    private FinancialPaceDaySnapshot CalculateDay(
        DateTime dayStart,
        DateTime dayEnd,
        DateTime now,
        int salaryAccrued, DaySource source)
    {
        int monthlyExpense = 0;
        string sourceType = "Missing";
        string sourceMonthKey = "";

        if (source.HasActual)
        {
            monthlyExpense = source.MonthlyExpense;
            sourceType = "PreviousMonthActual";
            sourceMonthKey = source.MonthKey;
        }
        else
        {
            FinancialPaceManualExpenseVersion? manual = _state.ManualExpenseVersions
                .Where(version => version.EffectiveFrom <= dayStart)
                .OrderByDescending(version => version.EffectiveFrom)
                .ThenByDescending(version => version.CreatedAt)
                .FirstOrDefault();
            if (manual != null)
            {
                monthlyExpense = manual.MonthlyExpenseAmount;
                sourceType = "OwnerForecast";
            }
        }

        string dayKey = dayStart.ToString("yyyy-MM-dd");
        return FinancialPaceCalculator.CalculateDay(dayStart, dayEnd, now, salaryAccrued,
            source.GamesByDay.GetValueOrDefault(dayKey), monthlyExpense, sourceType, sourceMonthKey,
            _state.OpenDayTimelines.TryGetValue(dayKey, out var timeline) ? timeline : new());
    }

    internal static Dictionary<string, int> BuildSalaryByDay(
        IReadOnlyDictionary<string, List<AutoSalaryDayEarning>> dailyEarningsByEmployee)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (List<AutoSalaryDayEarning> employeeDays in dailyEarningsByEmployee.Values)
        {
            foreach (AutoSalaryDayEarning day in employeeDays)
            {
                string key = day.Date.ToString("yyyy-MM-dd");
                result[key] = result.TryGetValue(key, out int amount)
                    ? amount + Math.Max(0, day.TotalAmount)
                    : Math.Max(0, day.TotalAmount);
            }
        }

        return result;
    }


    private bool AddTimelinePoint(FinancialPaceDaySnapshot day)
    {
        if (!_state.OpenDayTimelines.TryGetValue(day.BusinessDateKey, out var timeline))
        {
            timeline = new List<FinancialPacePoint>();
            _state.OpenDayTimelines[day.BusinessDateKey] = timeline;
        }

        FinancialPacePoint? previous = timeline.LastOrDefault();
        bool valuesChanged = previous == null ||
                             previous.GameRevenue != day.GameRevenue ||
                             previous.TotalExpense != day.TotalExpense;
        bool intervalElapsed = previous == null ||
                               day.CalculatedAt - previous.CreatedAt >= TimeSpan.FromMinutes(15);
        if (!valuesChanged && !intervalElapsed)
            return false;

        timeline.Add(new FinancialPacePoint
        {
            CreatedAt = day.CalculatedAt,
            GameRevenue = day.GameRevenue,
            FixedExpenseAccrued = day.FixedExpenseAccrued,
            SalaryAccrued = day.SalaryAccrued,
            TotalExpense = day.TotalExpense,
            Difference = day.Difference,
            Percent = day.Percent
        });
        if (timeline.Count > 400)
            timeline.RemoveRange(0, timeline.Count - 400);
        day.Timeline = timeline.Select(Clone).ToList();
        return true;
    }


    private static FinancialPaceDaySnapshot Clone(FinancialPaceDaySnapshot source)
    {
        return new FinancialPaceDaySnapshot
        {
            BusinessDateKey = source.BusinessDateKey,
            BusinessMonthKey = source.BusinessMonthKey,
            StartInclusive = source.StartInclusive,
            EndExclusive = source.EndExclusive,
            CalculatedAt = source.CalculatedAt,
            IsClosed = source.IsClosed,
            HasExpenseBaseline = source.HasExpenseBaseline,
            ExpenseSourceType = source.ExpenseSourceType,
            ExpenseSourceMonthKey = source.ExpenseSourceMonthKey,
            MonthlyFixedExpense = source.MonthlyFixedExpense,
            DailyFixedExpense = source.DailyFixedExpense,
            FixedExpenseAccrued = source.FixedExpenseAccrued,
            SalaryAccrued = source.SalaryAccrued,
            TotalExpense = source.TotalExpense,
            GameRevenue = source.GameRevenue,
            Difference = source.Difference,
            Percent = source.Percent,
            OverNormPortionCount = source.OverNormPortionCount,
            RecognitionAdjustments = source.RecognitionAdjustments?.ToList() ?? new(),
            Timeline = source.Timeline?.Select(Clone).ToList() ?? new List<FinancialPacePoint>()
        };
    }

    private static FinancialPacePoint Clone(FinancialPacePoint source)
    {
        return new FinancialPacePoint
        {
            CreatedAt = source.CreatedAt,
            GameRevenue = source.GameRevenue,
            FixedExpenseAccrued = source.FixedExpenseAccrued,
            SalaryAccrued = source.SalaryAccrued,
            TotalExpense = source.TotalExpense,
            Difference = source.Difference,
            Percent = source.Percent
        };
    }

    private static DateTime Min(DateTime first, DateTime second) =>
        first <= second ? first : second;


    private void Save(string dayKey)
    {
        _changes.Add(new(dayKey,
            _state.ClosedDays.TryGetValue(dayKey, out var closed) ? Clone(closed) : null,
            _state.OpenDayTimelines.TryGetValue(dayKey, out var timeline) ? timeline.Select(Clone).ToList() : null));
    }

    private static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.SerializeToUtf8Bytes(value))!;
}
