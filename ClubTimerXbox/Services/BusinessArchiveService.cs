using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public static class BusinessArchiveService
    {
        public static void Seal(BusinessMonthLedger month, DateTime archivedAt)
        {
            if (!month.IsClosed)
                throw new InvalidOperationException("Нельзя архивировать незакрытый месяц.");

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
                       CalculateChecksum(month),
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
    }
}
