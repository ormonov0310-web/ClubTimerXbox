using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public static class BusinessArchiveService
    {
        private const int CurrentArchiveSchemaVersion = 2;

        public static void Seal(BusinessMonthLedger month, DateTime archivedAt)
        {
            if (!month.IsClosed)
                throw new InvalidOperationException("Нельзя архивировать незакрытый месяц.");

            month.ArchiveSchemaVersion = CurrentArchiveSchemaVersion;
            month.ArchivedAt = archivedAt;
            month.ArchiveChecksum = CalculateChecksum(month);
            month.IsArchiveVerified = Verify(month);
        }

        public static bool Verify(BusinessMonthLedger month)
        {
            return month.IsClosed &&
                   month.ArchivedAt.HasValue &&
                   !string.IsNullOrWhiteSpace(month.ArchiveChecksum) &&
                   month.ArchiveChecksum.Equals(
                       month.ArchiveSchemaVersion >= CurrentArchiveSchemaVersion
                           ? CalculateChecksum(month)
                           : CalculateLegacyChecksum(month),
                       StringComparison.OrdinalIgnoreCase);
        }

        public static bool CanDeleteRawMonth(
            BusinessMonthLedger month,
            DateTime currentBusinessMonthStart,
            DateTime monthStart,
            int retainDetailedMonths = 6)
        {
            if (!Verify(month))
                return false;
            if (monthStart >= currentBusinessMonthStart)
                return false;
            return monthStart.AddMonths(Math.Max(1, retainDetailedMonths)) <=
                   currentBusinessMonthStart;
        }

        public static string CalculateChecksum(BusinessMonthLedger month)
        {
            var payload = new
            {
                month.ArchiveSchemaVersion,
                month.MonthKey,
                month.GameRevenue,
                month.ProductRevenue,
                month.ProductCostOfGoodsSold,
                month.ServiceRevenue,
                month.OtherRevenue,
                month.ClubExpenses,
                month.UnknownCashShortage,
                month.ExtraReserve,
                month.ArchivedExtra,
                month.ClosedNetProfit,
                month.WorkedHours,
                month.Payroll,
                month.SalaryPolicyVersions,
                month.EmployeeRatings,
                month.IsClosed,
                month.ArchivedAt
            };
            byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
            return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        }

        private static string CalculateLegacyChecksum(BusinessMonthLedger month)
        {
            var legacyPayroll = month.Payroll.Select(item => new
            {
                item.EmployeeId,
                item.EmployeeName,
                item.MonthKey,
                item.AccruedAmount,
                item.BonusAmount,
                item.PenaltyAmount,
                item.PaidAmount,
                item.TimeAmount,
                item.GameRevenueAmount,
                item.ProductBonusAmount,
                item.TimeRatingPercent,
                item.RevenueRatingPercent,
                item.OverallRatingPercent,
                item.TimeRatingEarnedAmount,
                item.TimeRatingLostAmount,
                item.GameRatingEarnedAmount,
                item.GameRatingLostAmount,
                item.RatingFinancialEffectCaptured,
                item.RemainingAmount
            }).ToList();
            var payload = new
            {
                month.MonthKey,
                month.GameRevenue,
                month.ProductRevenue,
                month.ProductCostOfGoodsSold,
                month.ServiceRevenue,
                month.OtherRevenue,
                month.ClubExpenses,
                month.UnknownCashShortage,
                month.ExtraReserve,
                month.ArchivedExtra,
                month.ClosedNetProfit,
                month.WorkedHours,
                Payroll = legacyPayroll,
                month.SalaryPolicyVersions,
                month.EmployeeRatings,
                month.IsClosed,
                month.ArchivedAt
            };
            byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
            return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        }
    }
}
