using System;
using System.Collections.Generic;
using System.Linq;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public static class CashAcceptanceProvisionalPolicy
    {
        public static CashAcceptanceItem Upsert(
            IList<CashAcceptanceItem> items,
            string rootAcceptanceKey,
            string attemptKey,
            string checkedByEmployeeName,
            string responsibleEmployeeName,
            int expectedCashAmount,
            int actualCashAmount,
            string note,
            DateTime now)
        {
            rootAcceptanceKey = rootAcceptanceKey.Trim();
            attemptKey = attemptKey.Trim();
            var item = items.FirstOrDefault(candidate =>
                candidate.IsProvisional &&
                candidate.RootAcceptanceKey.Equals(
                    rootAcceptanceKey,
                    StringComparison.OrdinalIgnoreCase));

            if (item == null)
            {
                item = new CashAcceptanceItem
                {
                    Id = Guid.NewGuid(),
                    AcceptanceKey = rootAcceptanceKey,
                    RootAcceptanceKey = rootAcceptanceKey,
                    IsProvisional = true,
                    CreatedAt = now
                };
                items.Add(item);
            }

            item.UpdatedAt = now;
            item.CheckedByEmployeeName = checkedByEmployeeName.Trim();
            item.ResponsibleEmployeeName = responsibleEmployeeName.Trim();
            item.ExpectedCashAmount = Math.Max(0, expectedCashAmount);
            item.ActualCashAmount = Math.Max(0, actualCashAmount);
            item.Difference = item.ActualCashAmount - item.ExpectedCashAmount;
            item.Note = note.Trim();
            item.FinalizedAt = null;

            if (!string.IsNullOrWhiteSpace(attemptKey) &&
                !item.AttemptKeys.Any(key =>
                    key.Equals(attemptKey, StringComparison.OrdinalIgnoreCase)))
            {
                item.AttemptKeys.Add(attemptKey);
            }

            return item;
        }

        public static bool Schedule(
            IEnumerable<CashAcceptanceItem> items,
            string rootAcceptanceKey,
            DateTime finalizeAt)
        {
            rootAcceptanceKey = rootAcceptanceKey.Trim();
            var item = items.FirstOrDefault(candidate =>
                candidate.IsProvisional &&
                candidate.RootAcceptanceKey.Equals(
                    rootAcceptanceKey,
                    StringComparison.OrdinalIgnoreCase));
            if (item == null)
                return false;

            if (item.FinalizeAt == null || finalizeAt < item.FinalizeAt.Value)
                item.FinalizeAt = finalizeAt;
            return true;
        }

        public static CashAcceptanceItem? FindLatestUnfinalized(
            IEnumerable<CashAcceptanceItem> items)
        {
            return items
                .Where(item => item.IsProvisional)
                .OrderByDescending(item => item.CreatedAt)
                .FirstOrDefault();
        }

        public static bool SetPendingCashlessVerification(
            IEnumerable<CashAcceptanceItem> items,
            Guid acceptanceId,
            PendingCashlessVerification verification)
        {
            var item = items.FirstOrDefault(candidate =>
                candidate.Id == acceptanceId && candidate.IsProvisional);
            if (item == null)
                return false;

            item.PendingCashlessVerification = verification;
            return true;
        }

        public static List<CashAcceptanceItem> GetDue(
            IEnumerable<CashAcceptanceItem> items,
            DateTime now)
        {
            return items
                .Where(item =>
                    item.IsProvisional &&
                    item.FinalizeAt != null &&
                    item.FinalizeAt.Value <= now)
                .OrderBy(item => item.FinalizeAt)
                .ThenBy(item => item.CreatedAt)
                .ToList();
        }
    }
}
