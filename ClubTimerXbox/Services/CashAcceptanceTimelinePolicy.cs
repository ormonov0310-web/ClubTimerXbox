using System;
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
