using System;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services;

internal static class SessionPenaltyPolicy
{
    internal static int GetPrepaidRemainingSeconds(ClubPlace place, DateTime now)
    {
        if (place.IsOpenMode) return 0;
        int remaining = Math.Max(0, place.RemainingSeconds);
        if (!place.StartTime.HasValue) return remaining;
        double elapsed = Math.Max(0, Math.Floor((now - place.StartTime.Value).TotalSeconds));
        return elapsed >= remaining ? 0 : remaining - (int)elapsed;
    }

    internal static bool TryGetPrice(double pricePerMinute, int minutes, out int price)
    {
        price = 0;
        double amount = Math.Ceiling(pricePerMinute * minutes);
        if (minutes <= 0 || pricePerMinute < 0 || !double.IsFinite(amount) || amount > int.MaxValue)
            return false;
        price = (int)amount;
        return true;
    }

    internal static int GetPrepaidUsedAmount(ClubPlace place, DateTime now)
    {
        int remaining = GetPrepaidRemainingSeconds(place, now);
        int unused = remaining <= 0 ? 0 : (int)Math.Ceiling(remaining * (place.ActivePricePerMinute / 60.0));
        return Math.Clamp(place.PaidAmount - unused, 0, place.PaidAmount);
    }

    internal static bool TryApply(ClubPlace place, int minutes, DateTime now, out int price)
    {
        price = 0;
        if (!place.IsBusy || place.IsCalculating || place.IsTimeExpiredAwaitingAcknowledgement ||
            !TryGetPrice(place.ActivePricePerMinute, minutes, out int penaltyPrice))
            return false;
        if (place.IsOpenMode)
        {
            double accrued = place.AccruedAmountBeforeCurrentSegment + penaltyPrice;
            double total = accrued + TariffService.CalculateOpenModePlayedMinutes(place.StartTime, now) * place.ActivePricePerMinute;
            if (!place.StartTime.HasValue || !double.IsFinite(total) || total > int.MaxValue - 5)
                return false;
            // A fine is an extra charge, not a new elapsed-time segment.
            place.AccruedAmountBeforeCurrentSegment = accrued;
        }
        else
        {
            int remaining = GetPrepaidRemainingSeconds(place, now);
            if (remaining == 0) return false;
            int usedBefore = GetPrepaidUsedAmount(place, now);
            DateTime nextStart = place.StartTime?.AddSeconds(place.RemainingSeconds - remaining) ?? now;
            place.RemainingSeconds = (int)Math.Max(0L, remaining - (long)minutes * 60);
            place.StartTime = place.RemainingSeconds > 0 ? nextStart : null;
            penaltyPrice = Math.Max(0, GetPrepaidUsedAmount(place, now) - usedBefore);
        }
        price = penaltyPrice;
        return true;
    }
}
