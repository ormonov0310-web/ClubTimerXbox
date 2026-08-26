using System;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public enum CashRecountDecision
    {
        Proceed,
        RecountRequired,
        Locked
    }

    public static class CashAcceptanceRecountPolicy
    {
        public const int DifferenceThreshold = 500;
        public const int LockDurationMinutes = 1;

        public static CashRecountDecision Evaluate(
            ShiftAcceptanceStatus status,
            string acceptanceKey,
            int expectedAmount,
            int actualAmount,
            DateTime now)
        {
            acceptanceKey = acceptanceKey?.Trim() ?? "";

            if (status.CashRecountRequired &&
                !status.CashRecountAcceptanceKey.Equals(
                    acceptanceKey,
                    StringComparison.OrdinalIgnoreCase))
            {
                Clear(status);
            }

            if (IsLocked(status, acceptanceKey, now))
                return CashRecountDecision.Locked;

            if (!status.CashRecountRequired)
            {
                long difference = Math.Abs((long)actualAmount - expectedAmount);
                if (difference < DifferenceThreshold)
                    return CashRecountDecision.Proceed;

                status.CashRecountRequired = true;
                status.CashRecountAcceptanceKey = acceptanceKey;
                status.CashRecountFirstAmount = actualAmount;
                status.CashRecountUnlockAt = now.AddMinutes(LockDurationMinutes);
                return CashRecountDecision.RecountRequired;
            }

            Clear(status);
            return CashRecountDecision.Proceed;
        }

        public static bool IsLocked(
            ShiftAcceptanceStatus status,
            string acceptanceKey,
            DateTime now)
        {
            acceptanceKey = acceptanceKey?.Trim() ?? "";
            return status.CashRecountRequired &&
                   status.CashRecountAcceptanceKey.Equals(
                       acceptanceKey,
                       StringComparison.OrdinalIgnoreCase) &&
                   status.CashRecountUnlockAt.HasValue &&
                   status.CashRecountUnlockAt.Value > now;
        }

        public static void Clear(ShiftAcceptanceStatus status)
        {
            status.CashRecountRequired = false;
            status.CashRecountAcceptanceKey = "";
            status.CashRecountFirstAmount = 0;
            status.CashRecountUnlockAt = null;
        }
    }
}
