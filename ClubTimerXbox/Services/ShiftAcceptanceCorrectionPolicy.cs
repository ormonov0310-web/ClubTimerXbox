using System;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public static class ShiftAcceptanceCorrectionPolicy
    {
        public const int OriginalEmployeeResponsibilityMinutes = 10;

        public static void CaptureInitialProductsAndCashCompletion(
            ShiftAcceptanceStatus status,
            DateTime now,
            bool isCorrectionAttempt)
        {
            if (isCorrectionAttempt ||
                status.InitialProductsAndCashAcceptedAt.HasValue ||
                !status.ProductsAccepted ||
                !status.CashAccepted)
            {
                return;
            }

            status.InitialProductsAndCashAcceptedAt = now;
        }

        public static string ResolveResponsibleEmployee(
            ShiftAcceptanceStatus status,
            string originalResponsibleEmployeeName,
            string currentEmployeeName,
            DateTime now)
        {
            originalResponsibleEmployeeName = originalResponsibleEmployeeName.Trim();
            currentEmployeeName = currentEmployeeName.Trim();

            DateTime? windowStart = status.InitialProductsAndCashAcceptedAt;
            if (!windowStart.HasValue && status.ProductsAccepted && status.CashAccepted)
                windowStart = status.CompletedAt;

            bool originalWindowIsActive = !windowStart.HasValue ||
                now < windowStart.Value.AddMinutes(OriginalEmployeeResponsibilityMinutes);

            if (originalWindowIsActive &&
                !string.IsNullOrWhiteSpace(originalResponsibleEmployeeName))
            {
                return originalResponsibleEmployeeName;
            }

            return string.IsNullOrWhiteSpace(currentEmployeeName)
                ? originalResponsibleEmployeeName
                : currentEmployeeName;
        }

        public static bool ShouldStageInitialCashAcceptance(
            ShiftAcceptanceStatus status,
            string rootAcceptanceKey,
            DateTime now)
        {
            if (status.IsManualSelfAcceptance ||
                string.IsNullOrWhiteSpace(rootAcceptanceKey) ||
                status.NewEmployeeName.Trim().Equals(
                    status.ResponsibleEmployeeName.Trim(),
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return status.InitialProductsAndCashAcceptedAt == null ||
                   now < status.InitialProductsAndCashAcceptedAt.Value
                       .AddMinutes(OriginalEmployeeResponsibilityMinutes);
        }
    }
}
