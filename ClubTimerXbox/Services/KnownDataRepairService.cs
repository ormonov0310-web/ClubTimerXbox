using System;

namespace ClubTimerXbox.Services
{
    public static class KnownDataRepairService
    {
        private const string CashCorrectionClubId = "club_1";
        private const string CashCorrectionEmployee = "\u0421\u0442\u0430\u043b\u0431\u0435\u043a";
        private const string TelecomInstallationId = "cff5545ec1db49fa8c9200dad4a48379";
        private const string TelecomSecondEmployee = "\u041c\u0438\u0440\u0431\u0435\u043a";
        private const double TelecomStalbekRecoveredHours = 213.55;
        private const double TelecomMirbekRecoveredHours = 8.57;
        private const string EmployeeRenameClubId = "club_2";
        private const string RenamedEmployeeId = "emp_b50acc8d89e3452486c3600a32b6188b";
        private const string PreviousEmployeeName = "\u041c\u0438\u0440\u0431\u0435\u043a";
        private const string CurrentEmployeeName = "\u041c\u0443\u0445\u0430\u043c\u043c\u0435\u0434";

        private static readonly Guid IncorrectLossId =
            Guid.Parse("12f5de06-13c9-44a5-aac4-103435ad79c6");

        private static readonly Guid IncorrectCashRecordId =
            Guid.Parse("a1aec547-9fe8-48d3-b0dc-0826b3fd3d0c");

        private static readonly Guid IncorrectRawReconciliationId =
            Guid.Parse("666b7c6d-a0ab-4178-8b53-91516880c343");

        private static readonly Guid CorrectCashShortageReconciliationId =
            Guid.Parse("5db40c93-bfc5-417e-8a12-0030925e611b");

        public static void Apply()
        {
            var identity = PcIdentityService.Current;
            string clubId = identity.ClubId;

            if (clubId.Equals(CashCorrectionClubId, StringComparison.OrdinalIgnoreCase))
            {
                ApplyCashCorrectionRepair();
                ApplyTelecomSalaryRepair(identity.InstallationId);
            }

            if (clubId.Equals(EmployeeRenameClubId, StringComparison.OrdinalIgnoreCase))
                ApplyEmployeeRenameRepair();
        }

        private static void ApplyTelecomSalaryRepair(string installationId)
        {
            if (!installationId.Equals(
                    TelecomInstallationId,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var monthStart = new DateTime(2026, 7, 1);

            AutoSalaryService.SetRecoveredWorkHours(
                monthStart,
                CashCorrectionEmployee,
                TelecomStalbekRecoveredHours);

            AutoSalaryService.SetRecoveredWorkHours(
                monthStart,
                TelecomSecondEmployee,
                TelecomMirbekRecoveredHours);
        }

        private static void ApplyCashCorrectionRepair()
        {

            EmployeeLossService.TryCorrectKnownFixedLoss(
                IncorrectLossId,
                incorrectAmount: 1221,
                correctedAmount: 672,
                responsibleEmployeeName: CashCorrectionEmployee);

            CashService.TryCorrectKnownShortage(
                IncorrectCashRecordId,
                incorrectAmount: 1221,
                correctedAmount: 672,
                responsibleEmployeeName: CashCorrectionEmployee);

            CashReconciliationService.TryDeleteKnownItem(
                IncorrectRawReconciliationId,
                expectedOriginalAmount: 549,
                responsibleEmployeeName: CashCorrectionEmployee);

            CashReconciliationService.TryCorrectKnownResolutionText(
                CorrectCashShortageReconciliationId,
                incorrectAmountText: "1221 \u0441\u043e\u043c",
                correctedAmountText: "672 \u0441\u043e\u043c");
        }

        private static void ApplyEmployeeRenameRepair()
        {
            var employee = EmployeeService.FindById(RenamedEmployeeId);

            if (employee == null ||
                !employee.Name.Equals(
                    CurrentEmployeeName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            EmployeeReferenceRenameService.RenameAll(
                PreviousEmployeeName,
                CurrentEmployeeName);
        }
    }
}
