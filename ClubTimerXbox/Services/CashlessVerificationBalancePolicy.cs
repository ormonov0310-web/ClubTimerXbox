using System;
using System.Collections.Generic;
using System.Linq;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services;

internal static class CashlessVerificationBalancePolicy
{
    public static int Calculate(int baseline, IEnumerable<PaymentRecord> payments,
        IEnumerable<CashRecord> records, DateTime fromExclusive, DateTime to,
        bool includeTo = false)
    {
        bool InPeriod(DateTime time) => time > fromExclusive &&
            (includeTo ? time <= to : time < to);
        int income = payments.Where(item => InPeriod(item.CreatedAt)).Sum(item => item.MBankAmount);
        int expenses = records.Where(item => InPeriod(item.CreatedAt) &&
            item.Type == CashRecordType.Expense &&
            item.PaymentMethod.Equals("Безнал", StringComparison.OrdinalIgnoreCase))
            .Sum(item => item.Amount);
        int loans = ClubLoanCashPolicy.NetOut(records, "Безнал", fromExclusive, to, includeTo: includeTo);
        return Math.Max(0, baseline + income - expenses - loans);
    }
}
