using System;
using System.Collections.Generic;
using System.Linq;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public sealed class GamePaymentAllocation
    {
        public PaymentRecord Payment { get; init; } = new();

        public int Amount { get; init; }
    }

    public static class GamePaymentAttributionService
    {
        public static int GetLegacyPrepaidAllocation(
            GameSessionLogItem session,
            IEnumerable<PaymentRecord> payments,
            int finalGameAmount)
        {
            if (session.IsOpenMode || session.PaidAmount <= 0 || finalGameAmount <= 0)
                return 0;

            bool hasLinkedInitialPayment = payments.Any(payment =>
                payment.GameSessionId == session.Id &&
                (payment.OperationTitle ?? string.Empty).Equals(
                    "Предоплаченный тариф",
                    StringComparison.OrdinalIgnoreCase));
            return hasLinkedInitialPayment
                ? 0
                : Math.Min(session.PaidAmount, finalGameAmount);
        }

        public static IReadOnlyList<GamePaymentAllocation> Allocate(
            IEnumerable<PaymentRecord> payments,
            Guid gameSessionId,
            int finalGameAmount)
        {
            var result = new List<GamePaymentAllocation>();
            int allocated = 0;

            foreach (var payment in payments
                         .Where(item => item.GameSessionId == gameSessionId)
                         .OrderBy(item => item.CreatedAt))
            {
                int paymentGameAmount = (payment.Items ?? new List<CheckoutItem>())
                    .Where(item =>
                        item.Category == "Игры" &&
                        (!item.SourceGameSessionId.HasValue ||
                         item.SourceGameSessionId == gameSessionId))
                    .Sum(item => item.TotalAmount);
                int amount = Math.Min(paymentGameAmount, finalGameAmount - allocated);
                if (amount <= 0)
                    continue;

                result.Add(new GamePaymentAllocation
                {
                    Payment = payment,
                    Amount = amount
                });
                allocated += amount;
                if (allocated >= finalGameAmount)
                    break;
            }

            return result;
        }
    }
}
