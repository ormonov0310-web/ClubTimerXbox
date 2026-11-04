using ClubTimerXbox.Models;
using ClubTimerXbox.Services;
using System.Text.Json;

internal static class SessionPenaltyTests
{
    private static readonly DateTime Now = new(2026, 11, 4, 16, 0, 0);

    public static void Run()
    {
        int checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Session penalty: " + message);
            checks++;
        }
        var open = Open();
        var start = open.StartTime;
        Check(Price(open, Now) == 60, "30 minutes at 2 som starts at 60");
        Check(SessionPenaltyPolicy.TryApply(open, 10, Now, out int cost) && cost == 20, "open fine accepted");
        Check(Price(open, Now) == 80 && open.StartTime == start, "open fine retains played time and original clock");
        Check(SessionPenaltyPolicy.TryApply(open, 5, Now.AddMinutes(5), out _) &&
            Price(open, Now.AddMinutes(5)) == 100, "repeated fine retains all elapsed time");
        Check(open.PaidAmount == 0 && open.PrepaidCashAmount == 0 && open.PrepaidMBankAmount == 0,
            "open fine is not immediate cash or prepayment");
        open = Open();
        open.AccruedAmountBeforeCurrentSegment = 100;
        Check(SessionPenaltyPolicy.TryApply(open, 10, Now, out _) && Price(open, Now) == 180,
            "earlier transferred charge also retained");
        open = Open();
        open.ActivePricePerMinute = 2.1;
        Check(SessionPenaltyPolicy.TryApply(open, 10, Now, out cost) && cost == 21 && Price(open, Now) == 85,
            "existing minute ceiling and five-som final rounding preserved");
        open = Open();
        open.StartTime = Now.AddSeconds(-59);
        Check(SessionPenaltyPolicy.TryApply(open, 1, Now, out _) && Price(open, Now) == 5 &&
            Price(open, Now.AddSeconds(2)) == 10, "fine does not restart first-minute grace");
        var saved = JsonSerializer.Deserialize<SavedActivePlace>(JsonSerializer.Serialize(open))!;
        var restored = JsonSerializer.Deserialize<ClubPlace>(JsonSerializer.Serialize(saved))!;
        Check(Price(restored, Now.AddSeconds(2)) == 10 && restored.StartTime == open.StartTime,
            "existing active-session storage fields retain fine and clock after restart");

        var prepaid = Prepaid();
        Check(SessionPenaltyPolicy.TryApply(prepaid, 5, Now, out cost) && cost == 10 &&
            prepaid.RemainingSeconds == 45 * 60, "regular tariff deducts elapsed 10 and fine 5 minutes");
        Check(prepaid.PaidAmount == 120 && prepaid.PrepaidCashAmount == 70 && prepaid.PrepaidMBankAmount == 50,
            "regular fine preserves full paid amount and both original payment channels");
        Check(ActualPrepaid(prepaid, Now) == 30 && prepaid.PaidAmount - ActualPrepaid(prepaid, Now) == 90,
            "early closure recognizes 30 and refunds only unused 90");
        Check(SessionPenaltyPolicy.TryApply(prepaid, 5, Now.AddMinutes(5), out _) &&
            prepaid.RemainingSeconds == 35 * 60 && ActualPrepaid(prepaid, Now.AddMinutes(5)) == 50,
            "repeated regular fine includes time since previous fine");
        prepaid = Prepaid();
        Check(SessionPenaltyPolicy.TryApply(prepaid, 60, Now, out cost) && prepaid.RemainingSeconds == 0 &&
            prepaid.StartTime == null && ActualPrepaid(prepaid, Now) == 120 && cost == 100,
            "fine larger than remainder ends tariff without overcharging");
        Check(!SessionPenaltyPolicy.TryApply(prepaid, 1, Now, out _), "ended tariff cannot be fined again");
        prepaid = Prepaid();
        Check(SessionPenaltyPolicy.TryApply(prepaid, int.MaxValue / 60 + 1, Now, out _) &&
            prepaid.RemainingSeconds == 0, "large positive minutes cannot overflow into added time");
        prepaid = Prepaid();
        prepaid.StartTime = prepaid.StartTime!.Value.AddMilliseconds(-600);
        Check(SessionPenaltyPolicy.TryApply(prepaid, 5, Now, out _) &&
            SessionPenaltyPolicy.GetPrepaidRemainingSeconds(prepaid, Now.AddMilliseconds(400)) == 2699,
            "fractional clock remainder survives fine");
        prepaid = Prepaid();
        prepaid.StartTime = Now.AddMinutes(-61);
        string before = JsonSerializer.Serialize(prepaid);
        Check(!SessionPenaltyPolicy.TryApply(prepaid, 1, Now, out _) && JsonSerializer.Serialize(prepaid) == before,
            "tariff expired while dialog was open cannot be revived");
        foreach (int minutes in new[] { 0, -1, int.MaxValue })
        {
            var candidate = Prepaid();
            before = JsonSerializer.Serialize(candidate);
            Check(!SessionPenaltyPolicy.TryApply(candidate, minutes, Now, out _) &&
                JsonSerializer.Serialize(candidate) == before, "invalid minutes never mutate session");
        }
        foreach (Action<ClubPlace> mutate in new Action<ClubPlace>[]
        {
            p => p.IsBusy = false, p => p.IsCalculating = true,
            p => p.IsTimeExpiredAwaitingAcknowledgement = true,
            p => p.ActivePricePerMinute = double.NaN, p => p.ActivePricePerMinute = -2
        })
        {
            var candidate = Prepaid();
            mutate(candidate);
            Check(!SessionPenaltyPolicy.TryApply(candidate, 5, Now, out _), "ineligible session/rate rejected");
        }
        prepaid = Prepaid();
        prepaid.ActivePricePerMinute = 0;
        Check(SessionPenaltyPolicy.TryApply(prepaid, 5, Now, out cost) && cost == 0 && prepaid.RemainingSeconds == 2700,
            "zero-price tariff retains existing time-only penalty semantics");
        foreach (int paid in new[] { 0, 120, 199, 1000 })
        foreach (double rate in new[] { 0.0, 1.0, 2.0, 2.1, 199.0 / 60 })
        foreach (int seconds in new[] { 0, 1, 29, 60, 601, 3600 })
        {
            var candidate = Prepaid();
            candidate.PaidAmount = paid;
            candidate.StartTime = Now;
            candidate.RemainingSeconds = seconds;
            int unused = seconds <= 0 ? 0 : (int)Math.Ceiling(seconds * (rate / 60.0));
            candidate.ActivePricePerMinute = rate;
            Check(ActualPrepaid(candidate, Now) == Math.Min(paid, Math.Max(0, paid - unused)),
                "shared prepaid settlement is exactly the previous positive-domain formula");
        }
        Console.WriteLine($"Session penalties: {checks} checks passed; owned memory only, no app or cloud.");
    }

    private static int Price(ClubPlace place, DateTime now) => TariffService.CalculateOpenModePrice(
        place.AccruedAmountBeforeCurrentSegment, place.ActivePricePerMinute, place.StartTime, now);

    private static int ActualPrepaid(ClubPlace place, DateTime now) => SessionPenaltyPolicy.GetPrepaidUsedAmount(place, now);

    private static ClubPlace Open() => new()
    {
        IsBusy = true, IsOpenMode = true, ActivePricePerMinute = 2, StartTime = Now.AddMinutes(-30)
    };

    private static ClubPlace Prepaid() => new()
    {
        IsBusy = true, ActivePricePerMinute = 2, StartTime = Now.AddMinutes(-10), RemainingSeconds = 3600,
        PaidAmount = 120, PrepaidCashAmount = 70, PrepaidMBankAmount = 50
    };
}
