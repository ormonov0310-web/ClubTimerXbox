using System;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public static class EmployeeRatingReassignmentService
    {
        public static void CancelOriginal(
            EmployeeRatingEvent original,
            string resolutionNote)
        {
            original.EndedAt = original.EffectiveFrom;
            original.Status = EmployeeRatingEventStatus.CancelledAsError;
            original.CompensationAmount = 0;
            original.ResolutionNote = resolutionNote.Trim();
        }

        public static EmployeeRatingEvent CreateReplacement(
            EmployeeRatingEvent original,
            Employee newEmployee,
            string newSourceId,
            string resolutionNote,
            DateTime now,
            int basePercent)
        {
            TimeSpan duration = original.ScheduledUntil - original.EffectiveFrom;
            if (duration <= TimeSpan.Zero)
                duration = TimeSpan.FromHours(1);

            int targetPercent = original.Direction == EmployeeRatingEffectDirection.Penalty
                ? basePercent - original.ChangePercent
                : basePercent + original.ChangePercent;

            return new EmployeeRatingEvent
            {
                EmployeeId = newEmployee.EmployeeId,
                EmployeeName = newEmployee.Name,
                Branch = original.Branch,
                RuleCode = original.RuleCode,
                RuleVersion = original.RuleVersion,
                Direction = original.Direction,
                ChangePercent = original.ChangePercent,
                BasePercentAtCreation = Math.Clamp(basePercent, 0, 120),
                SourceId = newSourceId.Trim(),
                SourceType = original.SourceType,
                Title = original.Title,
                Description = original.Description +
                    $"\nОтветственность перенесена с {original.EmployeeName} на {newEmployee.Name}.",
                CreatedAt = now,
                EffectiveFrom = now,
                ScheduledUntil = now.Add(duration),
                TargetPercent = Math.Clamp(targetPercent, 0, 120),
                Status = EmployeeRatingEventStatus.Active,
                ResolutionNote = resolutionNote.Trim()
            };
        }
    }
}
