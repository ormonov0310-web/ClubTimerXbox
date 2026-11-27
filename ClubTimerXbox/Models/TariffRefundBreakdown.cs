using System;

namespace ClubTimerXbox.Models;

internal readonly record struct TariffRefundBreakdown(
    int? PaidCash,
    int? PaidCashless,
    int ReturnCash,
    int ReturnCashless)
{
    public static TariffRefundBreakdown FromOriginalPayment(int cashPaid, int mBankPaid, int refundAmount)
    {
        if (refundAmount <= 0)
            return default;

        int totalPaidByMethods = cashPaid + mBankPaid;

        if (totalPaidByMethods <= 0)
            return new(null, null, 0, refundAmount);

        int cashRefund = Math.Min(cashPaid, refundAmount);
        int mBankRefund = refundAmount - cashRefund;

        if (mBankRefund > mBankPaid)
        {
            int extra = mBankRefund - mBankPaid;
            mBankRefund = mBankPaid;
            cashRefund += extra;
        }

        return new(cashPaid, mBankPaid, cashRefund, mBankRefund);
    }
}
