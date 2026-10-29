using System;
using System.Collections.Generic;
using System.Linq;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public static class FinancialPaceService
    {
        private static readonly object Gate = new();
        private static FinancialPaceState _state = Normalize(
            FinancialPaceStorageService.Load());

        public static FinancialPaceManualExpenseVersion ScheduleManualMonthlyExpense(
            int monthlyExpenseAmount)
        {
            if (monthlyExpenseAmount <= 0)
                throw new ArgumentOutOfRangeException(nameof(monthlyExpenseAmount));

            lock (Gate)
            {
                DateTime now = ClubClock.Current.LocalNow;
                var version = new FinancialPaceManualExpenseVersion
                {
                    CreatedAt = now,
                    EffectiveFrom = ResolveManualExpenseEffectiveFrom(now),
                    MonthlyExpenseAmount = monthlyExpenseAmount
                };
                _state.ManualExpenseVersions.Add(version);
                Save();
                return version;
            }
        }

        public static FinancialPaceMonthSnapshot BuildCurrentMonth()
        {
            using var portions = OverNormPortionService.BeginCalculation();
            var month = BusinessCalendarService.GetBusinessMonth(ClubClock.Current.LocalNow);
            var calculation = AutoSalaryService.BuildMonthCalculation(month.StartInclusive);
            return BuildMonth(month.StartInclusive, calculation.Report, calculation.DailyEarnings);
        }

        public static FinancialPaceMonthSnapshot BuildMonth(DateTime monthAnchor, AutoSalaryReport salaryReport,
            IReadOnlyDictionary<string, List<AutoSalaryDayEarning>>? dailyEarningsByEmployee = null)
        {
            using var portions = OverNormPortionService.BeginCalculation();
            dailyEarningsByEmployee ??= AutoSalaryService.BuildDailyEarningsByEmployee(monthAnchor, salaryReport);
            var result = CaptureMonth(monthAnchor).Calculate(dailyEarningsByEmployee);
            ApplyMonth(result);
            return result.Snapshot;
        }

        internal static CapturedPaceMonth CaptureMonth(DateTime monthAnchor)
        {
            using var portionsBatch = OverNormPortionService.BeginCalculation();
            lock (Gate)
            {
                var month = BusinessCalendarService.GetBusinessMonthByAnchor(monthAnchor);
                DateTime now = ClubClock.Current.LocalNow;
                var portions = new Dictionary<string, OverNormPortionDay?>();
                DateTime last = Min(BusinessCalendarService.GetBusinessDay(now).StartInclusive,
                    month.EndExclusive.AddDays(-1));
                for (DateTime day = month.StartInclusive; day <= last; day = day.AddDays(1))
                    portions[day.ToString("yyyy-MM-dd")] = OverNormPortionService.GetDay(day);
                var source = CaptureDaySource(month);
                var state = new FinancialPaceState
                {
                    ManualExpenseVersions = _state.ManualExpenseVersions,
                    ClosedDays = _state.ClosedDays
                        .Where(pair => pair.Value.StartInclusive >= month.StartInclusive &&
                            pair.Value.StartInclusive < month.EndExclusive)
                        .ToDictionary(pair => pair.Key, pair => pair.Value),
                    OpenDayTimelines = _state.OpenDayTimelines
                        .Where(pair => string.CompareOrdinal(pair.Key, month.StartInclusive.ToString("yyyy-MM-dd")) >= 0 &&
                            string.CompareOrdinal(pair.Key, month.EndExclusive.ToString("yyyy-MM-dd")) < 0)
                        .ToDictionary(pair => pair.Key, pair => pair.Value)
                };
                return new CapturedPaceMonth(month.StartInclusive, now, state, portions,
                    new(source.HasActual, source.MonthlyExpense, source.MonthKey, source.GamesByDay),
                    BusinessAccountingService.IsMonthClosed(month.Key),
                    CashService.GetOwnerWithdrawRecordsByPeriod(month.StartInclusive, month.EndExclusive)
                        .Sum(record => record.Amount));
            }
        }

        internal static void ApplyMonth(CapturedPaceMonth.Calculation calculation)
        {
            lock (Gate)
            {
                foreach (var change in calculation.Changes)
                {
                    if (change.ClosedDay != null)
                    {
                        _state.ClosedDays[change.DayKey] = change.ClosedDay;
                        _state.OpenDayTimelines.Remove(change.DayKey);
                    }
                    else if (change.Timeline != null)
                        _state.OpenDayTimelines[change.DayKey] = change.Timeline;
                    Save();
                }
            }
        }

        private sealed record DaySource(bool HasActual, int MonthlyExpense, string MonthKey,
            IReadOnlyDictionary<string, int> GamesByDay);

        private static DaySource CaptureDaySource(BusinessPeriodRange month)
        {
            var previous = BusinessCalendarService.GetBusinessMonthByAnchor(month.StartInclusive.AddMonths(-1));
            bool hasActual = CashService.HasClubExpenseRecordsByPeriod(previous.StartInclusive, previous.EndExclusive);
            int monthlyExpense = hasActual
                ? CashService.GetClubExpenseTotalByPeriod(previous.StartInclusive, previous.EndExclusive) : 0;
            var games = CashService.Records.Where(record => record.Category == "Игры" &&
                    CashService.GetBusinessTime(record) >= month.StartInclusive &&
                    CashService.GetBusinessTime(record) < month.EndExclusive)
                .GroupBy(record => BusinessCalendarService.GetBusinessDate(CashService.GetBusinessTime(record))
                    .ToString("yyyy-MM-dd"))
                .ToDictionary(group => group.Key, group => group.Sum(record => record.Amount));
            return new(hasActual, monthlyExpense, previous.Key, games);
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

        internal static DateTime ResolveManualExpenseEffectiveFrom(DateTime now)
        {
            return BusinessCalendarService.GetBusinessDay(now).StartInclusive;
        }


        private static FinancialPaceState Normalize(FinancialPaceState state)
        {
            state.ManualExpenseVersions ??= new List<FinancialPaceManualExpenseVersion>();
            state.ClosedDays ??= new Dictionary<string, FinancialPaceDaySnapshot>(
                StringComparer.OrdinalIgnoreCase);
            state.OpenDayTimelines ??= new Dictionary<string, List<FinancialPacePoint>>(
                StringComparer.OrdinalIgnoreCase);
            return state;
        }


        private static DateTime Min(DateTime first, DateTime second) =>
            first <= second ? first : second;

        private static void Save()
        {
            using var measurement = SnapshotBuildMeasurements.Measure("pace.save");
            FinancialPaceStorageService.Save(_state);
        }
    }
}
