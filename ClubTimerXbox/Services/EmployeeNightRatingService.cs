using System;
using System.Linq;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public static class EmployeeNightRatingService
    {
        private static readonly TimeSpan NightEnd = TimeSpan.FromHours(6);

        public static void Evaluate()
        {
            DateTime now = ClubClock.Current.LocalNow;
            if (now.TimeOfDay < TimeSpan.FromHours(1) || now.TimeOfDay >= NightEnd)
                return;

            DateTime oneAm = now.Date.AddHours(1);
            ApplyLateClientReward(now);
            ApplyUnattendedPcPenalty(now, oneAm);
        }

        private static void ApplyLateClientReward(DateTime now)
        {
            BusinessPeriodRange businessDay = BusinessCalendarService.GetBusinessDay(now);
            LateActiveSessionReward? reward = LateActiveSessionRewardPolicy.FindFirstReward(
                ActionLogService.GetAllGameSessions(),
                ActionLogService.GetAllShifts(),
                businessDay.StartInclusive,
                businessDay.EndExclusive,
                now);
            if (reward == null)
                return;

            string nightKey = reward.OneAm.ToString("yyyy-MM-dd");
            EmployeeRatingService.AddRuleEvent(
                reward.EmployeeName,
                "TIME_LATE_CLIENT_REWARD",
                $"late-client-rating:{nightKey}",
                "LateActiveSession",
                LateActiveSessionRewardPolicy.BuildDescription(reward),
                reward.QualifiedAt);
        }

        private static void ApplyUnattendedPcPenalty(DateTime now, DateTime oneAm)
        {
            var currentShift = ActionLogService.CurrentShift;
            if (currentShift == null || string.IsNullOrWhiteSpace(currentShift.EmployeeName))
                return;

            if (ActionLogService.GetActiveGameSessions().Any())
                return;

            DateTime businessStart = oneAm.Date.AddDays(-1).AddHours(
                BusinessCalendarService.BusinessDayStartHour);
            DateTime? lastClientEnd = ActionLogService.GetAllGameSessions()
                .Where(session =>
                    session.ClosedAt.HasValue &&
                    session.ClosedAt.Value >= businessStart &&
                    session.ClosedAt.Value <= now)
                .Max(session => session.ClosedAt);
            DateTime violationAt = CalculateUnattendedViolationAt(
                oneAm,
                currentShift.StartedAt,
                lastClientEnd);
            if (now < violationAt)
                return;

            string nightKey = oneAm.ToString("yyyy-MM-dd");
            EmployeeRatingService.AddRuleEvent(
                currentShift.EmployeeName,
                "TIME_PC_LEFT_UNATTENDED",
                $"unattended-pc-rating:{nightKey}",
                "UnattendedPc",
                $"После 01:00 программа оставалась открытой без клиентов более 2 часов. Контрольное время: {violationAt:dd.MM.yyyy HH:mm}.",
                violationAt);
        }

        private static DateTime Max(DateTime first, DateTime second) =>
            first >= second ? first : second;

        public static bool IsQualifiedLateSession(
            DateTime sessionStart,
            DateTime sessionEnd,
            DateTime oneAm)
        {
            return sessionStart <= oneAm.AddMinutes(-20) && sessionEnd > oneAm;
        }

        public static DateTime CalculateUnattendedViolationAt(
            DateTime oneAm,
            DateTime shiftStart,
            DateTime? lastClientEnd)
        {
            DateTime idleStart = Max(oneAm, shiftStart);
            if (lastClientEnd.HasValue)
                idleStart = Max(idleStart, lastClientEnd.Value);
            return idleStart.AddHours(2);
        }
    }
}
