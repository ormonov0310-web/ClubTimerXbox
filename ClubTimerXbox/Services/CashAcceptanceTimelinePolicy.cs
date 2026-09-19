using System;
using System.Collections.Generic;
using System.Linq;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public static class CashAcceptanceTimelinePolicy
    {
        public static DateTime GetObservationTime(CashAcceptanceItem item)
        {
            return item.UpdatedAt == default ? item.CreatedAt : item.UpdatedAt;
        }

        public static DateTime GetCommitTime(CashAcceptanceItem item)
        {
            return item.FinalizedAt ?? item.CreatedAt;
        }

        public static DateTime GetDueCommitTime(
            CashAcceptanceItem item,
            DateTime processedAt)
        {
            return item.FinalizeAt.HasValue && item.FinalizeAt.Value <= processedAt
                ? item.FinalizeAt.Value
                : processedAt;
        }

        public static CashAcceptanceItem? FindLatestFinalized(
            IEnumerable<CashAcceptanceItem> items,
            DateTime toExclusive)
        {
            return items
                .Where(item =>
                    !item.IsProvisional &&
                    GetCommitTime(item) < toExclusive)
                .OrderByDescending(GetObservationTime)
                .ThenByDescending(GetCommitTime)
                .ThenByDescending(item => item.CreatedAt)
                .FirstOrDefault();
        }

        public static bool CheckpointWins(
            CashAcceptanceItem? acceptance,
            CashBalanceCheckpointItem? checkpoint)
        {
            return checkpoint != null &&
                   (acceptance == null ||
                    checkpoint.CreatedAt >= GetCommitTime(acceptance));
        }
    }
}
