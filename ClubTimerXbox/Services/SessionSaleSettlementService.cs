using System;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public static class SessionSaleSettlementService
    {
        public const int CurrentSchemaVersion = 2;

        public static bool NormalizeActiveUnpaidLine(GameSessionSaleLine line)
        {
            if (line == null || line.IsPaid)
                return false;

            bool changed = false;

            if (line.SettlementSchemaVersion < CurrentSchemaVersion)
            {
                line.SettlementSchemaVersion = CurrentSchemaVersion;
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(line.CreatedByEmployeeName))
            {
                line.CreatedByEmployeeName = (line.EmployeeName ?? "").Trim();
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(line.EmployeeName) &&
                !string.IsNullOrWhiteSpace(line.CreatedByEmployeeName))
            {
                line.EmployeeName = line.CreatedByEmployeeName.Trim();
                changed = true;
            }

            return changed;
        }

        public static string GetCreatedByEmployeeName(GameSessionSaleLine line)
        {
            if (!string.IsNullOrWhiteSpace(line.CreatedByEmployeeName))
                return line.CreatedByEmployeeName.Trim();

            return (line.EmployeeName ?? "").Trim();
        }

        public static bool IsFinanciallyPaid(GameSessionSaleLine line)
        {
            if (line == null || !line.IsPaid)
                return false;

            // Закрытая история до схемы 2 сохраняет прежнюю атрибуцию.
            if (line.SettlementSchemaVersion < CurrentSchemaVersion)
                return true;

            return line.PaymentRecordId.HasValue &&
                   line.PaidAt.HasValue &&
                   !string.IsNullOrWhiteSpace(line.PaidByEmployeeName);
        }

        public static string GetFinancialEmployeeName(GameSessionSaleLine line)
        {
            if (line.SettlementSchemaVersion >= CurrentSchemaVersion &&
                !string.IsNullOrWhiteSpace(line.PaidByEmployeeName))
            {
                return line.PaidByEmployeeName.Trim();
            }

            return GetCreatedByEmployeeName(line);
        }

        public static DateTime GetFinancialOccurredAt(GameSessionSaleLine line)
        {
            if (line.SettlementSchemaVersion >= CurrentSchemaVersion &&
                line.PaidAt.HasValue)
            {
                return line.PaidAt.Value;
            }

            return line.CreatedAt;
        }

        public static void MarkPaid(
            GameSessionSaleLine line,
            Guid paymentRecordId,
            DateTime paidAt,
            string paidByEmployeeId,
            string paidByEmployeeName,
            Guid? paidShiftId)
        {
            NormalizeActiveUnpaidLine(line);

            line.IsPaid = true;
            line.PaymentRecordId = paymentRecordId;
            line.PaidAt = paidAt;
            line.PaidByEmployeeId = (paidByEmployeeId ?? "").Trim();
            line.PaidByEmployeeName = (paidByEmployeeName ?? "").Trim();
            line.PaidShiftId = paidShiftId;
        }

        public static void AcceptDebtResponsibility(
            GameSessionSaleLine line,
            string employeeName,
            Guid? shiftId,
            DateTime acceptedAt)
        {
            if (line == null || line.IsPaid)
                return;

            NormalizeActiveUnpaidLine(line);
            line.DebtResponsibleEmployeeName = (employeeName ?? "").Trim();
            line.DebtResponsibleShiftId = shiftId;
            line.DebtAcceptedAt = acceptedAt;
        }
    }
}
