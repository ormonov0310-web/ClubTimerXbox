using System;

namespace ClubTimerXbox.Services
{
    public static class KnownDataRepairService
    {
        private const string CorrectedClubId = "club_1";
        private const string ResponsibleEmployee = "\u0421\u0442\u0430\u043b\u0431\u0435\u043a";

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
            if (!PcIdentityService.Current.ClubId.Equals(
                    CorrectedClubId,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            EmployeeLossService.TryCorrectKnownFixedLoss(
                IncorrectLossId,
                incorrectAmount: 1221,
                correctedAmount: 672,
                responsibleEmployeeName: ResponsibleEmployee);

            CashService.TryCorrectKnownShortage(
                IncorrectCashRecordId,
                incorrectAmount: 1221,
                correctedAmount: 672,
                responsibleEmployeeName: ResponsibleEmployee);

            CashReconciliationService.TryDeleteKnownItem(
                IncorrectRawReconciliationId,
                expectedOriginalAmount: 549,
                responsibleEmployeeName: ResponsibleEmployee);

            CashReconciliationService.TryCorrectKnownResolutionText(
                CorrectCashShortageReconciliationId,
                incorrectAmountText: "1221 \u0441\u043e\u043c",
                correctedAmountText: "672 \u0441\u043e\u043c");
        }
    }
}
