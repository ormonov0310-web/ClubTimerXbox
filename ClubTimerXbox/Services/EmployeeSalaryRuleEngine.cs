using System;
using System.Collections.Generic;
using System.Linq;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public readonly record struct RatingFinancialEffect(
        int ActualAmount,
        int EarnedAmount,
        int LostAmount)
    {
        public int NetAmount => EarnedAmount - LostAmount;
    }

    public static class EmployeeSalaryRuleEngine
    {
        public static int ResolveRating(
            EmployeeRatingProfile profile,
            IEnumerable<EmployeeRatingEvent> events,
            EmployeeRatingBranch branch,
            DateTime at)
        {
            var baseVersion = profile.BaseVersions
                .Where(item => item.EffectiveFrom <= at)
                .OrderByDescending(item => item.EffectiveFrom)
                .ThenByDescending(item => item.CreatedAt)
                .FirstOrDefault()
                ?? profile.BaseVersions.OrderBy(item => item.EffectiveFrom).FirstOrDefault()
                ?? new EmployeeRatingBaseVersion();
            int basePercent = branch == EmployeeRatingBranch.Time
                ? baseVersion.TimePercent
                : baseVersion.RevenuePercent;
            var activeEvents = events
                .Where(item => item.Branch == branch &&
                               item.EffectiveFrom <= at &&
                               at < item.EffectiveUntil)
                .ToList();
            int rewardPercent = activeEvents
                .Where(item => item.Direction == EmployeeRatingEffectDirection.Reward)
                .Sum(ResolveChangePercent);
            int penaltyPercent = activeEvents
                .Where(item => item.Direction == EmployeeRatingEffectDirection.Penalty)
                .Sum(ResolveChangePercent);

            return Math.Clamp(
                Math.Clamp(basePercent, 0, 120) + rewardPercent - penaltyPercent,
                0,
                120);
        }

        private static int ResolveChangePercent(EmployeeRatingEvent item)
        {
            if (item.ChangePercent > 0)
                return item.ChangePercent;

            int baseAtCreation = Math.Clamp(item.BasePercentAtCreation, 0, 120);
            return Math.Abs(Math.Clamp(item.TargetPercent, 0, 120) - baseAtCreation);
        }

        public static int CalculateOverallRating(int timePercent, int revenuePercent)
        {
            return (int)Math.Floor((timePercent + revenuePercent) / 2.0 + 0.5);
        }

        public static double CalculateTimeAccrual(
            double hours,
            AutoSalarySettings settings,
            int timeRatingPercent)
        {
            if (hours <= 0 || settings.TimeMonthlyPlannedHours <= 0)
                return 0;
            double hourlyRate = settings.TimeMonthlyFundAmount /
                                (double)settings.TimeMonthlyPlannedHours;
            return hours * hourlyRate * Math.Clamp(timeRatingPercent, 0, 120) / 100.0;
        }

        public static double CalculateGameAccrual(
            int gameRevenue,
            AutoSalarySettings settings,
            int revenueRatingPercent)
        {
            if (gameRevenue <= 0)
                return 0;
            double afterReserve = gameRevenue *
                                  (100 - Math.Clamp(settings.ExpenseReservePercent, 0, 100)) /
                                  100.0;
            return afterReserve * Math.Clamp(settings.SalaryFundPercent, 0, 100) / 100.0 *
                   Math.Clamp(revenueRatingPercent, 0, 120) / 100.0;
        }

        public static double CalculateProductBonus(
            int productRevenue,
            AutoSalarySettings settings)
        {
            return productRevenue <= 0
                ? 0
                : productRevenue * Math.Clamp(settings.ProductBonusPercent, 0, 100) / 100.0;
        }

        public static RatingFinancialEffect CalculateRatingFinancialEffect(
            double amountAtOneHundredPercent,
            int ratingPercent)
        {
            double baseline = Math.Max(0, amountAtOneHundredPercent);
            int baselineAmount = (int)Math.Round(baseline);
            int actualAmount = (int)Math.Round(
                baseline * Math.Clamp(ratingPercent, 0, 120) / 100.0);
            int difference = actualAmount - baselineAmount;
            return new RatingFinancialEffect(
                actualAmount,
                Math.Max(0, difference),
                Math.Max(0, -difference));
        }
    }
}
