using System;
using System.Collections.Generic;
using System.Linq;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public static class LateOpeningPenaltyService
    {
        private const string RecommendationNotePrefix = "LateOpeningRecommendation";
        private const string SourceCode = "LateOpening";
        private const int OpeningWindowStartHour = 6;
        private const int MoneyCardEndHour = 15;
        private const int AutoDecisionHour = 23;

        public static void EvaluateOpenedShift(ShiftLogItem shift)
        {
            var settings = AutoSalaryService.Settings;
            DateTime dayStart = shift.StartedAt.Date.AddHours(OpeningWindowStartHour);
            if (shift.StartedAt < dayStart || !IsFirstOpeningForDay(shift, dayStart))
                return;

            DateTime scheduleStart = GetScheduleStart(shift.StartedAt.Date, settings);
            var ratingEvent = ApplyOpeningRating(shift, scheduleStart);

            int possibleAmount = CalculatePenaltyAmount(shift.StartedAt, scheduleStart, settings);
            if (possibleAmount <= 0 || shift.StartedAt >= shift.StartedAt.Date.AddHours(MoneyCardEndHour))
                return;

            string dayKey = shift.StartedAt.ToString("yyyy-MM-dd");
            if (HasAnyLateOpeningRecord(dayKey))
                return;

            AddRecommendation(
                shift.EmployeeName.Trim(),
                shift.StartedAt,
                scheduleStart,
                possibleAmount,
                dayKey,
                ratingEvent);
        }

        public static void EvaluatePendingRecommendations()
        {
            DateTime now = ClubClock.Current.LocalNow;
            foreach (var item in GetPendingRecommendations()
                         .Where(item => item.DecisionDueAt.HasValue && item.DecisionDueAt.Value <= now)
                         .ToList())
            {
                EmployeeLossService.TryFormalizeViolationRecommendation(
                    item.Id,
                    "Автоматически оформлено в 23:00: владелец не отменил рекомендацию.");
            }
        }

        // Kept for old callers and update compatibility.
        public static void EnsureTodayNoOpeningRecommendation()
        {
            EvaluatePendingRecommendations();
        }

        public static IReadOnlyList<EmployeeLossItem> GetPendingRecommendations()
        {
            return EmployeeLossService.Items
                .Where(item =>
                    item.SourceCode.Equals(SourceCode, StringComparison.OrdinalIgnoreCase) &&
                    item.ResolutionStatus.Equals("Pending", StringComparison.OrdinalIgnoreCase) &&
                    !item.IsFixed &&
                    !item.IsPaid)
                .OrderBy(item => item.CreatedAt)
                .ToList();
        }

        public static bool FormalizeNow(Guid id)
        {
            var item = GetPendingRecommendations().FirstOrDefault(value => value.Id == id);
            return item != null && EmployeeLossService.TryFormalizeViolationRecommendation(
                id,
                "Оформлено владельцем до автоматического срока.");
        }

        public static bool Cancel(Guid id, string reason)
        {
            var item = GetPendingRecommendations().FirstOrDefault(value => value.Id == id);
            string note = string.IsNullOrWhiteSpace(reason)
                ? "Отменено владельцем по уважительной причине."
                : $"Отменено владельцем. Причина: {reason.Trim()}";
            return item != null && EmployeeLossService.TryCancelViolationRecommendation(id, note);
        }

        public static bool Reassign(
            Guid id,
            string newEmployeeName,
            string changedBy,
            string reason)
        {
            var item = GetPendingRecommendations().FirstOrDefault(value => value.Id == id);
            var newEmployee = EmployeeService.FindByName(newEmployeeName);

            if (item == null || newEmployee?.IsActive != true)
                return false;

            string oldEmployeeName = item.ResponsibleEmployeeName.Trim();
            if (oldEmployeeName.Equals(newEmployee.Name, StringComparison.OrdinalIgnoreCase))
                return true;

            item.ReassignmentHistory ??= new List<EmployeeLossReassignmentItem>();
            string currentRatingSourceId = string.IsNullOrWhiteSpace(item.RatingSourceId)
                ? $"opening-rating:{item.CreatedAt:yyyy-MM-dd}"
                : item.RatingSourceId;
            string newRatingSourceId = $"{currentRatingSourceId}:reassigned:{item.Id:N}";
            EmployeeRatingEvent? replacement = null;

            if (EmployeeRatingService.FindBySource(currentRatingSourceId) != null)
            {
                replacement = EmployeeRatingService.ReassignRuleEvent(
                    currentRatingSourceId,
                    newRatingSourceId,
                    newEmployee.Name,
                    $"Переназначено владельцем на {newEmployee.Name}. " +
                    (string.IsNullOrWhiteSpace(reason) ? "" : reason.Trim()));
            }

            item.ReassignmentHistory.Add(new EmployeeLossReassignmentItem
            {
                CreatedAt = ClubClock.Current.LocalNow,
                FromEmployeeName = oldEmployeeName,
                ToEmployeeName = newEmployee.Name,
                ChangedBy = string.IsNullOrWhiteSpace(changedBy) ? "Владелец" : changedBy.Trim(),
                Reason = reason.Trim(),
                PreviousRatingEventId = item.RatingEventId,
                NewRatingEventId = replacement?.Id
            });
            item.ResponsibleEmployeeName = newEmployee.Name;
            item.RatingSourceId = replacement?.SourceId ?? item.RatingSourceId;
            item.RatingEventId = replacement?.Id ?? item.RatingEventId;
            item.Description +=
                $"\nОтветственность перенесена: {oldEmployeeName} → {newEmployee.Name}.";
            EmployeeLossService.SaveChanges();
            return true;
        }

        private static EmployeeRatingEvent? ApplyOpeningRating(
            ShiftLogItem shift,
            DateTime scheduleStart)
        {
            string ruleCode;
            if (shift.StartedAt < scheduleStart)
            {
                ruleCode = "TIME_FIRST_OPEN_PUNCTUAL";
            }
            else if (shift.StartedAt == scheduleStart)
            {
                return null;
            }
            else if (shift.StartedAt <= scheduleStart.AddMinutes(30))
            {
                ruleCode = "TIME_FIRST_OPEN_SLIGHTLY_LATE";
            }
            else
            {
                ruleCode = "TIME_FIRST_OPEN_LATE";
            }

            string dayKey = shift.StartedAt.ToString("yyyy-MM-dd");
            return EmployeeRatingService.AddRuleEvent(
                shift.EmployeeName,
                ruleCode,
                $"opening-rating:{dayKey}",
                "FirstClubOpening",
                $"Первое открытие клуба: {shift.StartedAt:dd.MM.yyyy HH:mm}. График: {scheduleStart:HH:mm}.",
                shift.StartedAt);
        }

        private static void AddRecommendation(
            string employeeName,
            DateTime openedAt,
            DateTime scheduleStart,
            int possibleAmount,
            string dayKey,
            EmployeeRatingEvent? ratingEvent)
        {
            var item = EmployeeLossService.AddLoss(
                responsibleEmployeeName: employeeName,
                checkedByEmployeeName: "Система",
                lossType: "Рекомендация системы",
                title: "Опоздание при открытии клуба",
                description:
                    $"Первым открыл: {employeeName}.\n" +
                    $"Клуб открыт в {openedAt:HH:mm}.\n" +
                    $"График открытия: {scheduleStart:HH:mm}.\n" +
                    $"Рекомендованный штраф: {possibleAmount} сом.\n" +
                    $"Автоматическое оформление: {openedAt.Date.AddHours(AutoDecisionHour):dd.MM HH:mm}.",
                amount: possibleAmount,
                note: $"{RecommendationNotePrefix}:{dayKey}",
                lossKind: "violation",
                isFixed: false,
                suppressAutomaticRating: true,
                sourceCode: SourceCode,
                resolutionStatus: "Pending",
                decisionDueAt: openedAt.Date.AddHours(AutoDecisionHour));
            item.RatingSourceId = ratingEvent?.SourceId ?? "";
            item.RatingEventId = ratingEvent?.Id;
            EmployeeLossService.SaveChanges();
        }

        private static bool IsFirstOpeningForDay(ShiftLogItem shift, DateTime dayStart)
        {
            return !ActionLogService.GetAllShifts().Any(item =>
                item.Id != shift.Id &&
                item.StartedAt < shift.StartedAt &&
                (item.StartedAt >= dayStart ||
                 (item.ClosedAt ?? ClubClock.Current.LocalNow) >= dayStart));
        }

        private static bool HasAnyLateOpeningRecord(string dayKey)
        {
            return EmployeeLossService.HasNote($"{RecommendationNotePrefix}:{dayKey}");
        }

        private static DateTime GetScheduleStart(DateTime day, AutoSalarySettings settings)
        {
            return day.Date.AddHours(settings.WorkDayStartHour);
        }

        public static int CalculatePenaltyAmount(
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
