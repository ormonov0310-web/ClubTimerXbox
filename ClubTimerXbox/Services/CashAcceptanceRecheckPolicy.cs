using System;
using System.Collections.Generic;
using System.Linq;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public static class CashAcceptanceRecheckPolicy
    {
        public static CashAcceptanceItem? FindImmediateHandoverForRecheck(
            IEnumerable<CashAcceptanceItem> items,
            string employeeName,
            DateTime now,
            int windowMinutes)
        {
            employeeName = employeeName.Trim();

            if (string.IsNullOrWhiteSpace(employeeName) || windowMinutes <= 0)
                return null;

            return items
                .Where(item =>
                    item.IsProvisional &&
                    NamesMatch(item.CheckedByEmployeeName, employeeName) &&
                    !NamesMatch(item.ResponsibleEmployeeName, employeeName))
                .Where(item =>
                {
                    DateTime observedAt = item.UpdatedAt == default
                        ? item.CreatedAt
                        : item.UpdatedAt;
                    return observedAt <= now &&
                           now - observedAt < TimeSpan.FromMinutes(windowMinutes);
                })
                .OrderByDescending(item => item.UpdatedAt == default
                    ? item.CreatedAt
                    : item.UpdatedAt)
                .FirstOrDefault();
        }

        private static bool NamesMatch(string left, string right)
        {
            return left.Trim().Equals(
                right.Trim(),
                StringComparison.OrdinalIgnoreCase);
        }
    }
}
