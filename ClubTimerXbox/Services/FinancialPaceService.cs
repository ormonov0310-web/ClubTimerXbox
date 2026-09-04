using System;
using System.Collections.Generic;
using System.Linq;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public static class FinancialPaceService
    {
        private static readonly object Gate = new();
        private static readonly DateTime FeatureEffectiveFrom =
            new(2026, 9, 1, BusinessCalendarService.BusinessDayStartHour, 0, 0);
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
            var month = BusinessCalendarService.GetBusinessMonth(ClubClock.Current.LocalNow);
            AutoSalaryReport salaryReport = AutoSalaryService.BuildReport(month.StartInclusive);
            var dailyEarnings = AutoSalaryService.BuildDailyEarningsByEmployee(
                month.StartInclusive,
                salaryReport);
            return BuildMonth(month.StartInclusive, salaryReport, dailyEarnings);
        }

        public static FinancialPaceMonthSnapshot BuildMonth(
            DateTime monthAnchor,
            AutoSalaryReport salaryReport,
            IReadOnlyDictionary<string, List<AutoSalaryDayEarning>>? dailyEarningsByEmployee = null)
        {
            lock (Gate)
            {
                var month = BusinessCalendarService.GetBusinessMonthByAnchor(monthAnchor);
                DateTime now = ClubClock.Current.LocalNow;
                DateTime lastIncludedStart = now < month.StartInclusive
                    ? month.StartInclusive.AddDays(-1)
                    : Min(
                        BusinessCalendarService.GetBusinessDay(now).StartInclusive,
                        month.EndExclusive.AddDays(-1));
                dailyEarningsByEmployee ??=
                    AutoSalaryService.BuildDailyEarningsByEmployee(
                        month.StartInclusive,
                        salaryReport);
                var salaryByDay = BuildSalaryByDay(dailyEarningsByEmployee);
                var days = new List<FinancialPaceDaySnapshot>();

                for (DateTime dayStart = month.StartInclusive;
                     dayStart <= lastIncludedStart && dayStart < month.EndExclusive;
                     dayStart = dayStart.AddDays(1))
                {
                    DateTime dayEnd = dayStart.AddDays(1);
                    string dayKey = dayStart.ToString("yyyy-MM-dd");
                    bool isClosed = now >= dayEnd;
                    if (isClosed && _state.ClosedDays.TryGetValue(dayKey, out var stored))
                    {
                        days.Add(Clone(stored));
                        continue;
                    }

                    FinancialPaceDaySnapshot calculated = CalculateDay(
                        dayStart,
                        dayEnd,
                        now,
                        salaryByDay.TryGetValue(dayKey, out int salary) ? salary : 0);
                    if (isClosed && dayStart >= FeatureEffectiveFrom)
                    {
                        calculated.IsClosed = true;
                        _state.ClosedDays[dayKey] = Clone(calculated);
                        _state.OpenDayTimelines.Remove(dayKey);
                        Save();
                    }
                    else if (!isClosed && dayStart <= now)
                    {
                        AddTimelinePoint(calculated);
                    }

                    days.Add(calculated);
                }

                var result = new FinancialPaceMonthSnapshot
                {
                    MonthKey = month.Key,
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

                return result;
            }
        }

        private static FinancialPaceDaySnapshot CalculateDay(
            DateTime dayStart,
            DateTime dayEnd,
            DateTime now,
            int salaryAccrued)
        {
            DateTime calculationTime = Min(now, dayEnd);
            var previousMonth = BusinessCalendarService.GetBusinessMonthByAnchor(
                dayStart.AddMonths(-1));
            bool hasActualBaseline = CashService.HasClubExpenseRecordsByPeriod(
                previousMonth.StartInclusive,
                previousMonth.EndExclusive);
            int monthlyExpense = 0;
            string sourceType = "Missing";
            string sourceMonthKey = "";

            if (hasActualBaseline)
            {
                monthlyExpense = CashService.GetClubExpenseTotalByPeriod(
                    previousMonth.StartInclusive,
                    previousMonth.EndExclusive);
                sourceType = "PreviousMonthActual";
                sourceMonthKey = previousMonth.Key;
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

            bool hasBaseline = monthlyExpense > 0;
            int dailyExpense = hasBaseline
                ? (int)Math.Round(monthlyExpense / 30.0)
                : 0;
            int fixedAccrued = FinancialPaceCalculator.CalculateFixedExpenseAccrued(
                dailyExpense,
                dayStart,
                calculationTime);
            int games = CashService.GetTotalByPeriodAndCategory(
                dayStart,
                dayEnd,
                "Игры");
            int totalExpense = fixedAccrued + Math.Max(0, salaryAccrued);
            int difference = games - totalExpense;
            string dayKey = dayStart.ToString("yyyy-MM-dd");

            return new FinancialPaceDaySnapshot
            {
                BusinessDateKey = dayKey,
                BusinessMonthKey = BusinessCalendarService.GetBusinessMonth(dayStart).Key,
                StartInclusive = dayStart,
                EndExclusive = dayEnd,
                CalculatedAt = calculationTime,
                IsClosed = now >= dayEnd,
                HasExpenseBaseline = hasBaseline,
                ExpenseSourceType = sourceType,
                ExpenseSourceMonthKey = sourceMonthKey,
                MonthlyFixedExpense = monthlyExpense,
                DailyFixedExpense = dailyExpense,
                FixedExpenseAccrued = fixedAccrued,
                SalaryAccrued = Math.Max(0, salaryAccrued),
                TotalExpense = totalExpense,
                GameRevenue = games,
                Difference = difference,
                Percent = hasBaseline
                    ? FinancialPaceCalculator.CalculatePercent(games, totalExpense)
                    : 0,
                Timeline = _state.OpenDayTimelines.TryGetValue(dayKey, out var timeline)
                    ? timeline.Select(Clone).ToList()
                    : new List<FinancialPacePoint>()
            };
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

        private static void AddTimelinePoint(FinancialPaceDaySnapshot day)
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
                return;

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
            Save();
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

        private static void Save()
        {
            FinancialPaceStorageService.Save(_state);
        }
    }
}
