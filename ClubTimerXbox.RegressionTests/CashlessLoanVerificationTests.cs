using ClubTimerXbox.Models;
using ClubTimerXbox.Services;
using System.Text.Json;

internal static class CashlessLoanVerificationTests
{
    public static void Run()
    {
        int checks = 0;
        void Check(bool result, string message)
        {
            if (!result) throw new InvalidOperationException("Cashless loans: " + message);
            checks++;
        }
        DateTime from = new(2026, 11, 2, 17, 7, 17);
        DateTime to = new(2026, 11, 4, 14, 24, 50);
        var payments = new[] { new PaymentRecord { CreatedAt = from.AddHours(1), MBankAmount = 925 } };
        var rows = new List<CashRecord>
        {
            Row(CashRecordType.Expense, 120, from.AddHours(2)),
            Row(CashRecordType.ClubLoanIssued, 3000, from.AddHours(3))
        };
        string before = JsonSerializer.Serialize(rows);
        int expected = CashlessVerificationBalancePolicy.Calculate(5212, payments, rows, from, to);
        Check(expected == 3017, "TEL-LOAN-002 must deduct the posted loan once: expected 3017");
        Check(3069 - expected == 52, "historical residual is surplus 52, not shortage 2948");
        Check(CashlessVerificationBalancePolicy.Calculate(5212, payments, rows, from, to) == expected &&
            JsonSerializer.Serialize(rows) == before, "repeat calculation is pure, never posts a loan");
        rows.Add(Row(CashRecordType.ClubLoanReturned, 200, from.AddHours(4)));
        Check(CashlessVerificationBalancePolicy.Calculate(5212, payments, rows, from, to) == 3217,
            "real bank return increases bank balance once");
        rows.Add(Row(CashRecordType.ClubLoanIssued, 500, from.AddHours(4), "Наличные"));
        Check(CashlessVerificationBalancePolicy.Calculate(5212, payments, rows, from, to) == 3217,
            "cash-funded issue cannot reduce bank balance");
        Check(CashlessVerificationBalancePolicy.Calculate(3069, Array.Empty<PaymentRecord>(), rows, to, to.AddHours(1)) == 3069,
            "new accepted baseline already contains earlier issue and must not be debited again");
        foreach (bool inclusive in new[] { false, true })
        {
            var boundaries = new[]
            {
                Row(CashRecordType.ClubLoanIssued, 900, from.AddTicks(-1)),
                Row(CashRecordType.ClubLoanIssued, 800, from),
                Row(CashRecordType.ClubLoanIssued, 100, from.AddTicks(1)),
                Row(CashRecordType.ClubLoanReturned, 20, to),
                Row(CashRecordType.ClubLoanIssued, 700, to.AddTicks(1))
            };
            Check(CashlessVerificationBalancePolicy.Calculate(1000, Array.Empty<PaymentRecord>(), boundaries,
                from, to, inclusive) == (inclusive ? 920 : 900), "checkpoint and upper loan boundaries");
        }
        Check(CashlessVerificationBalancePolicy.Calculate(100, Array.Empty<PaymentRecord>(),
            new[] { Row(CashRecordType.ClubLoanIssued, 200, from.AddSeconds(1)) }, from, to) == 0,
            "existing nonnegative display clamp preserved");
        Check(CashlessVerificationBalancePolicy.Calculate(100, Array.Empty<PaymentRecord>(),
            new[] { Row(CashRecordType.ClubLoanReturned, 20, DateTime.MaxValue) },
            DateTime.MinValue, DateTime.MaxValue, true) == 120, "inclusive end does not overflow ticks");
        Console.WriteLine($"Cashless loans: {checks} checks passed.");
    }

    private static CashRecord Row(CashRecordType type, int amount, DateTime time, string method = "Безнал") =>
        new() { Type = type, Amount = amount, CreatedAt = time, PaymentMethod = method };
}
