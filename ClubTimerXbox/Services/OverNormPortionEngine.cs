using System.IO;
using System.Globalization;

namespace ClubTimerXbox.Services;

public sealed record PortionRevenue(Guid Id, DateTime RecordedAt, int Amount);
public sealed record PortionWork(string EmployeeId, string EmployeeName, DateTime Start, DateTime End);

public sealed class OverNormPortionShare
{
    public string EmployeeId { get; set; } = "";
    public string EmployeeName { get; set; } = "";
    public long WorkedTicks { get; set; }
    public int Amount { get; set; }
}

public sealed class OverNormPortion
{
    public int Number { get; set; }
    public DateTime AllocatedAt { get; set; }
    public DateTime RecordedAt { get; set; }
    public decimal Fund { get; set; }
    public decimal UncreditedAmount { get; set; }
    public List<Guid> RevenueRecordIds { get; set; } = new();
    public List<OverNormPortionShare> Shares { get; set; } = new();
}

public sealed class OverNormPortionDay
{
    public DateTime Start { get; set; }
    public int Norm { get; set; }
    public int Percent { get; set; }
    public long Revenue { get; set; }
    public int WaitingPortions { get; set; }
    public long RevenueBelowAwardedThreshold { get; set; }
    public List<OverNormPortion> Portions { get; set; } = new();
}

// Replays recognition and eligibility events, not report refresh times.
public static class OverNormPortionEngine
{
    public const int RevenueStep = 500;
    public static string DayKey(DateTime start) => start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    public static readonly long MinimumWorkTicks = TimeSpan.FromHours(2).Ticks;

    public static void Accrue(OverNormPortionDay day, DateTime now,
        IEnumerable<PortionRevenue> revenue, IEnumerable<PortionWork> work)
    {
        DateTime end = day.Start.AddDays(1);
        DateTime until = now < end ? now : end;
        if (until < day.Start) return;
        var receipts = revenue.Where(r => r.RecordedAt <= now)
            .GroupBy(r => r.Id).Select(group =>
            {
                var first = group.First();
                if (first.Id == Guid.Empty || group.Any(r => r != first))
                    throw new InvalidDataException("Conflicting bonus revenue identity.");
                return first;
            }).OrderBy(r => r.RecordedAt).ThenBy(r => r.Id).ToList();
        var intervals = MergeWork(work, day.Start, until);
        DateTime Clip(DateTime at) => at < day.Start ? day.Start : at > end ? end : at;
        var receiptEvents = receipts.GroupBy(r => Clip(r.RecordedAt))
            .ToDictionary(g => g.Key, g => g.ToList());
        var events = new SortedSet<DateTime>(receiptEvents.Keys);
        foreach (var employee in intervals.GroupBy(w => w.EmployeeId))
        {
            long accumulated = 0;
            foreach (var interval in employee.OrderBy(w => w.Start))
            {
                long ticks = (interval.End - interval.Start).Ticks;
                if (accumulated + ticks >= MinimumWorkTicks)
                {
                    events.Add(interval.Start.AddTicks(MinimumWorkTicks - accumulated));
                    break;
                }
                accumulated += ticks;
            }
        }
        long netRevenue = 0;
        var sourceIds = new List<Guid>();
        int last = day.Portions.Count;
        foreach (DateTime at in events.Where(at => at <= until))
        {
            if (receiptEvents.TryGetValue(at, out var additions))
                foreach (var receipt in additions)
                {
                    netRevenue = checked(netRevenue + receipt.Amount);
                    sourceIds.Add(receipt.Id);
                }
            long fullPortions = Math.Max(0L, netRevenue - day.Norm) / RevenueStep;
            if (fullPortions <= last || day.Norm <= 0 || day.Percent <= 0) continue;
            var participants = intervals.Where(w => w.Start < at)
                .GroupBy(w => w.EmployeeId)
                .Select(g => new OverNormPortionShare
                {
                    EmployeeId = g.Key,
                    EmployeeName = g.Last().EmployeeName,
                    WorkedTicks = g.Sum(w => ((w.End < at ? w.End : at) - w.Start).Ticks)
                }).Where(p => p.WorkedTicks >= MinimumWorkTicks)
                .OrderBy(p => p.EmployeeId, StringComparer.Ordinal).ToList();
            if (participants.Count == 0) continue;
            if (fullPortions > 100_000)
                throw new InvalidDataException("Bonus revenue exceeds supported daily limit.");
            decimal totalTicks = participants.Sum(p => (decimal)p.WorkedTicks);
            decimal fund = RevenueStep * (decimal)day.Percent / 100m;
            while (last < fullPortions)
            {
                var shares = participants.Select(p => new OverNormPortionShare
                {
                    EmployeeId = p.EmployeeId, EmployeeName = p.EmployeeName,
                    WorkedTicks = p.WorkedTicks,
                    Amount = checked((int)decimal.Floor(fund * p.WorkedTicks / totalTicks))
                }).ToList();
                day.Portions.Add(new OverNormPortion
                {
                    Number = ++last, AllocatedAt = at, RecordedAt = now, Fund = fund,
                    UncreditedAmount = fund - shares.Sum(p => p.Amount),
                    RevenueRecordIds = sourceIds.ToList(), Shares = shares
                });
            }
        }
        day.Revenue = netRevenue;
        day.WaitingPortions = now >= end || day.Norm <= 0 || day.Percent <= 0 ? 0 : checked((int)Math.Max(0L,
            Math.Max(0L, netRevenue - day.Norm) / RevenueStep - last));
        // Corrections cannot re-award an already consumed threshold or erase paid salary.
        day.RevenueBelowAwardedThreshold = last == 0 ? 0 :
            Math.Max(0L, day.Norm + (long)last * RevenueStep - netRevenue);
    }

    private static List<PortionWork> MergeWork(IEnumerable<PortionWork> work,
        DateTime start, DateTime end)
    {
        var result = new List<PortionWork>();
        foreach (var group in work.Where(w => !string.IsNullOrWhiteSpace(w.EmployeeId))
                     .GroupBy(w => w.EmployeeId, StringComparer.Ordinal))
        {
            PortionWork? previous = null;
            foreach (var item in group.OrderBy(w => w.Start))
            {
                DateTime from = item.Start < start ? start : item.Start;
                DateTime to = item.End > end ? end : item.End;
                if (to <= from) continue;
                if (previous != null && from <= previous.End)
                {
                    previous = previous with { End = to > previous.End ? to : previous.End };
                    result[^1] = previous;
                }
                else
                {
                    previous = item with { Start = from, End = to };
                    result.Add(previous);
                }
            }
        }
        return result;
    }
}
