using System;
using System.Collections.Generic;
using System.Linq;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public sealed record LateActiveSessionReward(
        string EmployeeName,
        string PlaceName,
        DateTime QualifiedAt,
        DateTime OneAm,
        bool QualifiedAtOneAm);

    public static class LateActiveSessionRewardPolicy
    {
        public const int RequiredMinutesBeforeOneAm = 20;
        public const int RequiredMinutesAfterOneAm = 30;

        public static LateActiveSessionReward? FindFirstReward(
            IEnumerable<GameSessionLogItem> sessions,
            IEnumerable<ShiftLogItem> shifts,
            DateTime businessDayStart,
            DateTime businessDayEnd,
            DateTime evaluatedAt)
        {
            DateTime evaluationEnd = evaluatedAt < businessDayEnd
                ? evaluatedAt
                : businessDayEnd;
            if (evaluationEnd <= businessDayStart)
                return null;

            DateTime oneAm = businessDayStart.Date.AddDays(1).AddHours(1);
            var candidates = sessions
                .Select(session => new
                {
                    Session = session,
                    QualifiedAt = GetQualificationTime(
                        session.StartedAt,
                        session.ClosedAt ?? evaluationEnd,
                        oneAm,
                        businessDayEnd)
                })
                .Where(candidate =>
                    candidate.QualifiedAt.HasValue &&
                    candidate.QualifiedAt.Value <= evaluationEnd)
                .OrderBy(candidate => candidate.QualifiedAt)
                .ThenBy(candidate => candidate.Session.StartedAt)
                .ThenBy(candidate => candidate.Session.Id)
                .ToList();

            foreach (var candidate in candidates)
            {
                DateTime qualifiedAt = candidate.QualifiedAt!.Value;
                ShiftLogItem? responsibleShift = shifts
                    .Where(shift =>
                        shift.StartedAt <= qualifiedAt &&
                        (shift.ClosedAt ?? DateTime.MaxValue) > qualifiedAt &&
                        !string.IsNullOrWhiteSpace(shift.EmployeeName))
                    .OrderByDescending(shift => shift.StartedAt)
                    .FirstOrDefault();
                if (responsibleShift == null)
                    continue;

                return new LateActiveSessionReward(
                    responsibleShift.EmployeeName.Trim(),
                    candidate.Session.PlaceName,
                    qualifiedAt,
                    oneAm,
                    qualifiedAt == oneAm);
            }

            return null;
        }

        public static DateTime? GetQualificationTime(
            DateTime sessionStart,
            DateTime sessionEnd,
            DateTime oneAm,
            DateTime businessDayEnd)
        {
            if (sessionEnd <= sessionStart)
                return null;

            if (sessionStart <= oneAm.AddMinutes(-RequiredMinutesBeforeOneAm) &&
                sessionEnd > oneAm)
            {
                return oneAm;
            }

            DateTime afterOneStart = sessionStart > oneAm ? sessionStart : oneAm;
            DateTime qualifiedAt = afterOneStart.AddMinutes(RequiredMinutesAfterOneAm);
            return qualifiedAt < businessDayEnd && sessionEnd >= qualifiedAt
                ? qualifiedAt
                : null;
        }

        public static string BuildDescription(LateActiveSessionReward reward)
        {
            return reward.QualifiedAtOneAm
                ? $"К 01:00 клиент непрерывно играл не менее 20 минут. Место: {reward.PlaceName}."
                : $"После 01:00 клиент непрерывно играл не менее 30 минут. Место: {reward.PlaceName}.";
        }
    }
}
