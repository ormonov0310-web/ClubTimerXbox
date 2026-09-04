using System;
using System.Collections.Generic;
using System.Linq;
using ClubTimerXbox.Models;

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

        public static double CalculateOperatingProgress(
            DateTime businessDayStart,
            DateTime asOf)
        {
            DateTime operatingStart = businessDayStart.Date.AddHours(11);
            DateTime operatingEnd = businessDayStart.Date.AddDays(1).AddHours(1);
            if (asOf <= operatingStart)
                return 0;
            if (asOf >= operatingEnd)
                return 1;

            return (asOf - operatingStart).TotalSeconds /
                   (operatingEnd - operatingStart).TotalSeconds;
        }

        public static FinancialPaceForecastSnapshot CalculateMonthForecast(
            IReadOnlyCollection<FinancialPaceDaySnapshot> days,
            DateTime monthStart,
            DateTime monthEnd,
            DateTime asOf)
        {
            var forecast = new FinancialPaceForecastSnapshot();
            FinancialPaceDaySnapshot? firstAvailableDay = days
                .Where(day => day.HasExpenseBaseline)
                .OrderBy(day => day.StartInclusive)
                .FirstOrDefault();
            if (firstAvailableDay == null)
                return forecast;

            DateTime periodStart = firstAvailableDay.StartInclusive < monthStart
                ? monthStart
                : firstAvailableDay.StartInclusive;
            int totalDays = Math.Max(0, (int)(monthEnd - periodStart).TotalDays);
            if (totalDays == 0)
                return forecast;

            var eligibleDays = days
                .Where(day =>
                    day.HasExpenseBaseline &&
                    day.StartInclusive >= periodStart &&
                    day.StartInclusive < monthEnd)
                .OrderBy(day => day.StartInclusive)
                .ToList();
            var closedDays = eligibleDays.Where(day => day.IsClosed).ToList();
            int closedDifference = closedDays.Sum(day => day.Difference);

            FinancialPaceDaySnapshot? currentDay = eligibleDays
                .Where(day => !day.IsClosed && day.StartInclusive <= asOf)
                .OrderByDescending(day => day.StartInclusive)
                .FirstOrDefault();
            double currentProgress = currentDay == null
                ? 0
                : CalculateOperatingProgress(currentDay.StartInclusive, asOf);
            bool includesCurrent = currentDay != null && currentProgress > 0;
            int projectedCurrent = includesCurrent
                ? (int)Math.Round(currentDay!.Difference / currentProgress)
                : 0;

            int sampleDays = closedDays.Count + (includesCurrent ? 1 : 0);
            if (sampleDays == 0)
            {
                forecast.PeriodStartKey = periodStart.ToString("yyyy-MM-dd");
                forecast.TotalDays = totalDays;
                forecast.RemainingDays = totalDays;
                return forecast;
            }

            double average = (closedDifference + projectedCurrent) / (double)sampleDays;
            int remainingDays = Math.Max(0, totalDays - sampleDays);
            int projectedDifference = (int)Math.Round(
                closedDifference + projectedCurrent + average * remainingDays);
            double observedDays = closedDays.Count + (includesCurrent ? currentProgress : 0);

            forecast.IsAvailable = true;
            forecast.IsFinal = asOf >= monthEnd && closedDays.Count >= totalDays;
            forecast.IncludesCurrentDayProjection = includesCurrent;
            forecast.PeriodStartKey = periodStart.ToString("yyyy-MM-dd");
            forecast.ProjectedDifference = forecast.IsFinal
                ? closedDifference
                : projectedDifference;
            forecast.AverageDayDifference = (int)Math.Round(average);
            forecast.ClosedDaysDifference = closedDifference;
            forecast.CurrentDayProjectedDifference = projectedCurrent;
            forecast.CompletedDays = closedDays.Count;
            forecast.RemainingDays = remainingDays;
            forecast.TotalDays = totalDays;
            forecast.CoveragePercent = forecast.IsFinal
                ? 100
                : Math.Clamp(
                    (int)Math.Round(observedDays * 100.0 / totalDays),
                    0,
                    99);
            return forecast;
        }
    }
}
