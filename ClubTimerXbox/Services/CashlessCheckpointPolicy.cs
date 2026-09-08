using System;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public static class CashlessCheckpointPolicy
    {
        public static CashlessDayRecord? Build(
            CashlessDayRecord? current, DateTime committedAt,
            int amount, string note, int? expectedAmount)
        {
            amount = Math.Max(0, amount);
            if (current != null &&
                (current.UpdatedAt > committedAt ||
                 (current.UpdatedAt == committedAt && current.Amount == amount &&
                  current.ExpectedAmount == expectedAmount && current.Note == note)))
                return null;

            // Recovery must retain the original day/time, not consume later receipts.
            return new CashlessDayRecord
            {
                Date = BusinessCalendarService.GetBusinessDate(committedAt),
                UpdatedAt = committedAt,
                Amount = amount,
                ExpectedAmount = expectedAmount,
                Note = note
            };
        }
    }
}
