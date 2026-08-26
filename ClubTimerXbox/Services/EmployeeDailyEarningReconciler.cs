using System;
using System.Collections.Generic;
using System.Linq;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public static class EmployeeDailyEarningReconciler
    {
        public static void Reconcile(
            IList<AutoSalaryDayEarning> days,
            DateTime fallbackDate,
            int timeTarget,
            int gameTarget,
            int productBonusTarget,
            int otherBonusTarget,
            int? timeRatingEarnedTarget = null,
            int? timeRatingLostTarget = null,
            int? gameRatingEarnedTarget = null,
            int? gameRatingLostTarget = null)
        {
            ApplyTarget(
                days,
                fallbackDate,
                Math.Max(0, timeTarget),
                item => item.TimeAmount,
                (item, value) => item.TimeAmount = value);
            ApplyTarget(
                days,
                fallbackDate,
                Math.Max(0, gameTarget),
                item => item.GameAmount,
                (item, value) => item.GameAmount = value);
            ApplyTarget(
                days,
                fallbackDate,
                Math.Max(0, productBonusTarget),
                item => item.ProductServiceBonusAmount,
                (item, value) => item.ProductServiceBonusAmount = value);
            ApplyTarget(
                days,
                fallbackDate,
                Math.Max(0, otherBonusTarget),
                item => item.OtherBonusAmount,
                (item, value) => item.OtherBonusAmount = value);

            ApplyOptionalTarget(
                days,
                fallbackDate,
                timeRatingEarnedTarget,
                item => item.TimeRatingEarnedAmount,
                (item, value) => item.TimeRatingEarnedAmount = value);
            ApplyOptionalTarget(
                days,
                fallbackDate,
                timeRatingLostTarget,
                item => item.TimeRatingLostAmount,
                (item, value) => item.TimeRatingLostAmount = value);
            ApplyOptionalTarget(
                days,
                fallbackDate,
                gameRatingEarnedTarget,
                item => item.GameRatingEarnedAmount,
                (item, value) => item.GameRatingEarnedAmount = value);
            ApplyOptionalTarget(
                days,
                fallbackDate,
                gameRatingLostTarget,
                item => item.GameRatingLostAmount,
                (item, value) => item.GameRatingLostAmount = value);
        }

        private static void ApplyOptionalTarget(
            IList<AutoSalaryDayEarning> days,
            DateTime fallbackDate,
            int? target,
            Func<AutoSalaryDayEarning, int> getter,
            Action<AutoSalaryDayEarning, int> setter)
        {
            if (target.HasValue)
            {
                ApplyTarget(
                    days,
                    fallbackDate,
                    Math.Max(0, target.Value),
                    getter,
                    setter);
            }
        }

        private static void ApplyTarget(
            IList<AutoSalaryDayEarning> days,
            DateTime fallbackDate,
            int target,
            Func<AutoSalaryDayEarning, int> getter,
            Action<AutoSalaryDayEarning, int> setter)
        {
            int difference = target - days.Sum(getter);
            if (difference == 0)
                return;

            if (days.Count == 0)
                days.Add(new AutoSalaryDayEarning { Date = fallbackDate.Date });

            if (difference > 0)
            {
                var day = days
                    .OrderByDescending(item => item.Date)
                    .First();
                setter(day, getter(day) + difference);
                return;
            }

            int remaining = -difference;
            foreach (var day in days
                .Where(item => getter(item) > 0)
                .OrderByDescending(item => item.Date))
            {
                int used = Math.Min(getter(day), remaining);
                setter(day, getter(day) - used);
                remaining -= used;
                if (remaining == 0)
                    break;
            }
        }
    }
}
