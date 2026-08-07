using System;
using System.Collections.Generic;

namespace ClubTimerXbox.Services
{
    public sealed class OwnerWithdrawalAvailability
    {
        public int CurrentMonthAmount { get; init; }

        public int CarriedAmount { get; init; }

        public int TotalAmount => CurrentMonthAmount + CarriedAmount;

        public static OwnerWithdrawalAvailability FromBalances(
            int totalAvailable,
            int openingBalance)
        {
            int total = Math.Max(0, totalAvailable);
            int carried = Math.Min(total, Math.Max(0, openingBalance));
            return new OwnerWithdrawalAvailability
            {
                CurrentMonthAmount = total - carried,
                CarriedAmount = carried
            };
        }
    }

    public sealed class OwnerWithdrawalAllocation
    {
        public string SourceMonthKey { get; init; } = "";

        public int Amount { get; init; }

        public bool IsCarriedBalance { get; init; }
    }

    public static class OwnerWithdrawalAllocator
    {
        public static IReadOnlyList<OwnerWithdrawalAllocation> Allocate(
            int amount,
            OwnerWithdrawalAvailability availability,
            string currentMonthKey,
            string carriedMonthKey,
            bool carriedOnly = false)
        {
            ArgumentNullException.ThrowIfNull(availability);
            if (amount <= 0)
                throw new ArgumentOutOfRangeException(nameof(amount));

            int maximum = carriedOnly
                ? availability.CarriedAmount
                : availability.TotalAmount;
            if (amount > maximum)
            {
                throw new InvalidOperationException(
                    $"Owner withdrawal available {maximum}, requested {amount}.");
            }

            var result = new List<OwnerWithdrawalAllocation>();
            int remaining = amount;
            if (!carriedOnly)
            {
                int currentAmount = Math.Min(remaining, availability.CurrentMonthAmount);
                if (currentAmount > 0)
                {
                    result.Add(new OwnerWithdrawalAllocation
                    {
                        SourceMonthKey = currentMonthKey,
                        Amount = currentAmount,
                        IsCarriedBalance = false
                    });
                    remaining -= currentAmount;
                }
            }

            if (remaining > 0)
            {
                result.Add(new OwnerWithdrawalAllocation
                {
                    SourceMonthKey = carriedMonthKey,
                    Amount = remaining,
                    IsCarriedBalance = true
                });
            }

            return result;
        }
    }
}
