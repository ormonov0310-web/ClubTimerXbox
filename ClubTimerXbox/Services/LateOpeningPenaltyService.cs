using System;
using System.Linq;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public static class LateOpeningPenaltyService
    {
        private const string AutoNotePrefix = "AutoLateOpeningPenalty";
        private const string RecommendationNotePrefix = "LateOpeningRecommendation";
        private const int OpeningWindowStartHour = 6;
        private const int OpeningWindowEndHour = 15;

        public static void EvaluateOpenedShift(ShiftLogItem shift)
        {
            var settings = AutoSalaryService.Settings;
            string responsible = settings.OpeningResponsibleEmployeeName.Trim();

            if (string.IsNullOrWhiteSpace(responsible))
                return;

            DateTime scheduleStart = GetScheduleStart(shift.StartedAt.Date, settings);
            DateTime maxAutoTime = scheduleStart.AddMinutes(settings.LateOpeningMaxAutoMinutes);
            DateTime openingWindowStart = shift.StartedAt.Date.AddHours(OpeningWindowStartHour);
            DateTime openingWindowEnd = shift.StartedAt.Date.AddHours(OpeningWindowEndHour);

            if (shift.StartedAt < scheduleStart || shift.StartedAt < openingWindowStart)
                return;

            if (shift.StartedAt >= openingWindowEnd)
                return;

            if (!IsFirstOpeningForDay(shift, scheduleStart))
                return;

            int possibleAmount = CalculatePenaltyAmount(shift.StartedAt, scheduleStart, settings);
            if (possibleAmount <= 0)
                return;

            string dayKey = scheduleStart.ToString("yyyy-MM-dd");
            if (HasAnyLateOpeningRecord(dayKey))
                return;

            bool openedByResponsible = shift.EmployeeName.Trim()
                .Equals(responsible, StringComparison.OrdinalIgnoreCase);

            if (openedByResponsible && shift.StartedAt <= maxAutoTime)
            {
                AddAutomaticPenalty(responsible, shift.StartedAt, scheduleStart, possibleAmount, dayKey);
                return;
            }

            string recommendationEmployee = openedByResponsible
                ? responsible
                : shift.EmployeeName.Trim();
            AddRecommendation(
                recommendationEmployee,
                responsible,
                shift.EmployeeName.Trim(),
                shift.StartedAt,
                scheduleStart,
                possibleAmount,
                dayKey
            );
        }

        public static void EnsureTodayNoOpeningRecommendation()
        {
            var settings = AutoSalaryService.Settings;
            string responsible = settings.OpeningResponsibleEmployeeName.Trim();

            if (string.IsNullOrWhiteSpace(responsible))
                return;

            DateTime now = ClubClock.Current.LocalNow;
            DateTime scheduleStart = GetScheduleStart(now.Date, settings);
            DateTime maxAutoTime = scheduleStart.AddMinutes(settings.LateOpeningMaxAutoMinutes);
            DateTime openingWindowEnd = now.Date.AddHours(OpeningWindowEndHour);

            if (now <= maxAutoTime)
                return;

            if (now >= openingWindowEnd)
                return;

            string dayKey = scheduleStart.ToString("yyyy-MM-dd");
            if (HasAnyLateOpeningRecord(dayKey))
                return;

            if (HasAnyOpeningBy(scheduleStart, now))
                return;

            int possibleAmount = CalculatePenaltyAmount(maxAutoTime, scheduleStart, settings);
            if (possibleAmount <= 0)
                return;

            EmployeeLossService.AddLoss(
                responsibleEmployeeName: responsible,
                checkedByEmployeeName: "Система",
                lossType: "Рекомендация системы",
                title: "Позднее открытие клуба",
                description:
                    $"Клуб не был открыт до {maxAutoTime:HH:mm}.\n" +
                    $"Ответственный: {responsible}.\n" +
                    $"Возможный штраф: {possibleAmount} сом.\n" +
                    "Автоматически не удержано: клуб мог не работать по уважительной причине.",
                amount: possibleAmount,
                note: $"{RecommendationNotePrefix}:{dayKey}",
                lossKind: "violation",
                isFixed: false
            );
        }

        private static void AddAutomaticPenalty(
            string responsible,
            DateTime openedAt,
            DateTime scheduleStart,
            int amount,
            string dayKey)
        {
            EmployeeLossService.AddLoss(
                responsibleEmployeeName: responsible,
                checkedByEmployeeName: "Система",
                lossType: "Автоштраф за опоздание",
                title: "Позднее открытие клуба",
                description:
                    $"Ответственный сотрудник открыл клуб в {openedAt:HH:mm}.\n" +
                    $"График открытия: {scheduleStart:HH:mm}.\n" +
                    $"Автоштраф: {amount} сом.",
                amount: amount,
                note: $"{AutoNotePrefix}:{dayKey}",
                lossKind: "violation",
                isFixed: true
            );
        }

        private static void AddRecommendation(
            string recommendationEmployee,
            string responsible,
            string openedBy,
            DateTime openedAt,
            DateTime scheduleStart,
            int possibleAmount,
            string dayKey)
        {
            EmployeeLossService.AddLoss(
                responsibleEmployeeName: recommendationEmployee,
                checkedByEmployeeName: "Система",
                lossType: "Рекомендация системы",
                title: "Позднее открытие клуба",
                description:
                    $"Клуб открыт в {openedAt:HH:mm}.\n" +
                    $"График открытия: {scheduleStart:HH:mm}.\n" +
                    $"Ответственный: {responsible}.\n" +
                    $"Фактически открыл: {openedBy}.\n" +
                    $"Возможный штраф: {possibleAmount} сом.\n" +
                    "Автоматически не удержано, требуется решение владельца.",
                amount: possibleAmount,
                note: $"{RecommendationNotePrefix}:{dayKey}",
                lossKind: "violation",
                isFixed: false
            );
        }

        private static bool IsFirstOpeningForDay(ShiftLogItem shift, DateTime scheduleStart)
        {
            return !ActionLogService.GetAllShifts()
                .Any(item =>
                    item.Id != shift.Id &&
                    item.StartedAt < shift.StartedAt &&
                    (item.ClosedAt ?? ClubClock.Current.LocalNow) >= scheduleStart);
        }

        private static bool HasAnyOpeningBy(DateTime scheduleStart, DateTime until)
        {
            return ActionLogService.GetAllShifts()
                .Any(shift =>
                    shift.StartedAt <= until &&
                    (shift.ClosedAt ?? ClubClock.Current.LocalNow) >= scheduleStart);
        }

        private static bool HasAnyLateOpeningRecord(string dayKey)
        {
            return EmployeeLossService.HasNote($"{AutoNotePrefix}:{dayKey}") ||
                   EmployeeLossService.HasNote($"{RecommendationNotePrefix}:{dayKey}");
        }

        private static DateTime GetScheduleStart(DateTime day, AutoSalarySettings settings)
        {
            return day.Date.AddHours(settings.WorkDayStartHour);
        }

        private static int CalculatePenaltyAmount(
            DateTime openedAt,
            DateTime scheduleStart,
            AutoSalarySettings settings)
        {
            int grace = Math.Max(0, settings.LateOpeningGraceMinutes);
            int stepMinutes = Math.Max(1, settings.LateOpeningPenaltyStepMinutes);
            int stepAmount = Math.Max(0, settings.LateOpeningPenaltyStepAmount);

            if (stepAmount <= 0 || openedAt <= scheduleStart.AddMinutes(grace))
                return 0;

            int delayMinutes = (int)Math.Ceiling((openedAt - scheduleStart).TotalMinutes);
            int maxMinutes = Math.Max(grace, settings.LateOpeningMaxAutoMinutes);
            delayMinutes = Math.Min(delayMinutes, maxMinutes);
            int chargeableMinutes = Math.Max(0, delayMinutes - grace);
            int steps = (int)Math.Ceiling(chargeableMinutes / (double)stepMinutes);
            return Math.Max(0, steps * stepAmount);
        }
    }
}
