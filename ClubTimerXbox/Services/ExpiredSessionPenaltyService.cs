using System;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public static class ExpiredSessionPenaltyService
    {
        public const int GraceMinutes = 5;
        public const int SomPerMinute = 1;

        public static int CalculateChargeableMinutes(DateTime expiredAt, DateTime now)
        {
            if (now <= expiredAt)
                return 0;

            int completedMinutes = (int)Math.Floor((now - expiredAt).TotalMinutes);
            return Math.Max(0, completedMinutes - GraceMinutes);
        }

        public static int GetElapsedSeconds(ClubPlace place, DateTime now)
        {
            if (place.TimeExpiredAt == null || now <= place.TimeExpiredAt.Value)
                return 0;

            return Math.Max(0, (int)Math.Floor((now - place.TimeExpiredAt.Value).TotalSeconds));
        }

        public static bool Evaluate(ClubPlace place, string currentEmployeeName, DateTime now)
        {
            if (!place.IsBusy ||
                place.IsOpenMode ||
                !place.IsTimeExpiredAwaitingAcknowledgement)
            {
                return false;
            }

            if (place.TimeExpiredAt == null)
            {
                // Migration guard for an expired card saved by an older app version.
                place.TimeExpiredAt = now;
                place.ExpiredGameSessionId ??= Guid.NewGuid();
                if (EmployeeService.FindByName(currentEmployeeName.Trim()) != null)
                    place.ExpiredPenaltyEmployeeName = currentEmployeeName.Trim();
                return true;
            }

            int chargeableMinutes = CalculateChargeableMinutes(place.TimeExpiredAt.Value, now);
            if (chargeableMinutes <= place.ExpiredPenaltyChargedMinutes)
            {
                if (chargeableMinutes == 0 &&
                    EmployeeService.FindByName(currentEmployeeName.Trim()) != null &&
                    !currentEmployeeName.Trim().Equals(
                        place.ExpiredPenaltyEmployeeName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    place.ExpiredPenaltyEmployeeName = currentEmployeeName.Trim();
                    return true;
                }

                return false;
            }

            currentEmployeeName = currentEmployeeName.Trim();
            string employeeName = place.ExpiredPenaltyEmployeeName?.Trim() ?? "";
            if (EmployeeService.FindByName(employeeName) == null)
            {
                if (EmployeeService.FindByName(currentEmployeeName) == null)
                    return false;

                employeeName = currentEmployeeName;
                place.ExpiredPenaltyEmployeeName = employeeName;
            }

            if (string.IsNullOrWhiteSpace(employeeName))
            {
                return false;
            }

            string monthKey = BusinessCalendarService.GetBusinessMonthKey(now);
            bool sameOpenLossMonth = monthKey.Equals(
                place.ExpiredPenaltyLossMonthKey,
                StringComparison.OrdinalIgnoreCase);
            int lossBaseMinutes = sameOpenLossMonth
                ? Math.Max(0, place.ExpiredPenaltyLossBaseMinutes)
                : place.ExpiredPenaltyChargedMinutes;
            int amount = Math.Max(1, chargeableMinutes - lossBaseMinutes) * SomPerMinute;
            string description = BuildDescription(
                place,
                chargeableMinutes,
                amount,
                monthKey);
            Guid lossId = place.ExpiredPenaltyLossId ?? Guid.Empty;
            bool updated = sameOpenLossMonth &&
                           place.ExpiredPenaltyLossId != null &&
                           EmployeeLossService.TryIncreaseFixedViolation(
                               lossId,
                               amount,
                               description);

            if (!updated)
            {
                // A new record starts for a new month or after the owner cancelled
                // the previous deduction while the TV was still left unattended.
                lossBaseMinutes = place.ExpiredPenaltyChargedMinutes;
                amount = Math.Max(1, chargeableMinutes - lossBaseMinutes) * SomPerMinute;
                description = BuildDescription(
                    place,
                    chargeableMinutes,
                    amount,
                    monthKey);
                var loss = EmployeeLossService.AddLoss(
                    responsibleEmployeeName: employeeName,
                    checkedByEmployeeName: "Система",
                    lossType: "Нарушение правил",
                    title: $"{place.Name}: не остановлен после тарифа",
                    description: description,
                    amount: amount,
                    note: "Автоматический штраф: 1 сом за каждую полную минуту после пяти льготных минут.",
                    lossKind: "violation",
                    isFixed: true,
                    salaryMonthKey: monthKey,
                    suppressAutomaticRating: true,
                    sourceCode: "ExpiredTimedSession",
                    resolutionStatus: "Confirmed");
                lossId = loss.Id;
                place.ExpiredPenaltyLossId = lossId;
                place.ExpiredPenaltyLossMonthKey = monthKey;
                place.ExpiredPenaltyLossBaseMinutes = lossBaseMinutes;
            }

            place.ExpiredPenaltyChargedMinutes = chargeableMinutes;

            ExpiredSessionViolationService.RecordOrUpdate(place, lossId, now);

            EmployeeRatingService.AddRuleEvent(
                employeeName,
                "TIME_EXPIRED_TV_UNATTENDED",
                "loss:" + lossId.ToString("N"),
                "ExpiredTimedSession",
                $"{place.Name}: сотрудник не подтвердил окончание тарифа в течение пяти минут.",
                place.ExpiredPenaltyLossBaseMinutes == 0
                    ? place.TimeExpiredAt.Value.AddMinutes(GraceMinutes + 1)
                    : now);

            return true;
        }

        public static string BuildStatusText(ClubPlace place, DateTime now)
        {
            int elapsedSeconds = GetElapsedSeconds(place, now);
            int chargeableMinutes = place.TimeExpiredAt == null
                ? 0
                : CalculateChargeableMinutes(place.TimeExpiredAt.Value, now);

            if (chargeableMinutes > 0)
                return $"Просрочка: {FormatElapsed(elapsedSeconds)} · штраф: {chargeableMinutes * SomPerMinute} сом";

            int firstChargeAtSeconds = (GraceMinutes + 1) * 60;
            int secondsUntilCharge = Math.Max(0, firstChargeAtSeconds - elapsedSeconds);
            if (secondsUntilCharge > 0)
                return $"До первого штрафа: {secondsUntilCharge / 60:00}:{secondsUntilCharge % 60:00}";

            return "Началась первая штрафная минута";
        }

        private static string BuildDescription(
            ClubPlace place,
            int totalChargedMinutes,
            int amount,
            string monthKey)
        {
            return $"{place.Name}. После окончания тарифа не подтверждено освобождение места. " +
                   $"Всего штрафных минут: {totalChargedMinutes}. " +
                   $"Удержано в периоде {monthKey}: {amount} сом.";
        }

        private static string FormatElapsed(int totalSeconds)
        {
            int hours = totalSeconds / 3600;
            int minutes = totalSeconds % 3600 / 60;
            int seconds = totalSeconds % 60;
            return hours > 0
                ? $"{hours:00}:{minutes:00}:{seconds:00}"
                : $"{minutes:00}:{seconds:00}";
        }
    }
}
