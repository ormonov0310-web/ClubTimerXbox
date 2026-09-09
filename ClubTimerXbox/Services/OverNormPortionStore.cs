using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClubTimerXbox.Services;

internal sealed class OverNormPortionState
{
    [JsonRequired] public int Schema { get; set; } = 1;
    [JsonRequired] public string ClubId { get; set; } = "";
    [JsonRequired] public DateTime EffectiveFrom { get; set; }
    [JsonRequired] public Dictionary<string, OverNormPortionDay> Days { get; set; } = new();
}

internal sealed class OverNormPortionStore
{
    private readonly string _path;
    private readonly string _clubId;
    private readonly object _gate = new();
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public OverNormPortionStore(string path, string clubId, DateTime activatedAt)
    {
        _path = path;
        _clubId = clubId;
        if (!File.Exists(path))
            Save(new OverNormPortionState
            {
                ClubId = clubId,
                EffectiveFrom = BusinessCalendarService.GetBusinessDay(activatedAt).EndExclusive
            });
        _ = Load();
    }

    public DateTime EffectiveFrom { get { lock (_gate) return Load().EffectiveFrom; } }

    public List<OverNormPortionDay> ReadDays(DateTime from, DateTime to)
    {
        lock (_gate)
            return Load().Days.Values.Where(day => day.Start >= from && day.Start < to).ToList();
    }

    public OverNormPortionDay? GetDay(DateTime start, DateTime now, int norm, int percent,
        IEnumerable<PortionRevenue> revenue, IEnumerable<PortionWork> work, bool readOnly = false)
    {
        lock (_gate)
        {
            var state = Load();
            if (start < state.EffectiveFrom) return null;
            string key = OverNormPortionEngine.DayKey(start);
            state.Days.TryGetValue(key, out var day);
            if (readOnly || start > now) return day ?? new OverNormPortionDay { Start = start };
            string before = JsonSerializer.Serialize(day, Json);
            day ??= new OverNormPortionDay { Start = start, Norm = norm, Percent = percent };
            OverNormPortionEngine.Accrue(day, now, revenue, work);
            state.Days[key] = day;
            if (JsonSerializer.Serialize(day, Json) != before) Save(state);
            return day;
        }
    }

    private OverNormPortionState Load()
    {
        // Financial state corruption must never silently switch back to the old formula.
        var state = JsonSerializer.Deserialize<OverNormPortionState>(File.ReadAllText(_path))
            ?? throw new InvalidDataException("Missing portion bonus ledger.");
        if (state.Schema != 1 || state.ClubId != _clubId || state.Days == null ||
            state.EffectiveFrom == default ||
            state.EffectiveFrom != BusinessCalendarService.GetBusinessDay(state.EffectiveFrom).StartInclusive)
            throw new InvalidDataException("Invalid portion bonus ledger.");
        foreach (var entry in state.Days)
        {
            var day = entry.Value;
            if (day == null || day.Portions == null || day.Start < state.EffectiveFrom ||
                entry.Key != OverNormPortionEngine.DayKey(day.Start) || day.Norm < 0 ||
                day.Percent is < 0 or > 100)
                throw new InvalidDataException("Invalid portion bonus day.");
            for (int i = 0; i < day.Portions.Count; i++)
            {
                var p = day.Portions[i];
                if (p == null || p.Number != i + 1 || p.Shares == null || p.Shares.Count == 0 ||
                    p.RevenueRecordIds == null || p.Fund != 500m * day.Percent / 100m ||
                    p.UncreditedAmount < 0 || p.Fund != p.Shares.Sum(s => s.Amount) + p.UncreditedAmount ||
                    p.AllocatedAt < day.Start || p.AllocatedAt > day.Start.AddDays(1) ||
                    p.RecordedAt < p.AllocatedAt ||
                    p.Shares.Any(s => s.Amount < 0 || s.WorkedTicks < OverNormPortionEngine.MinimumWorkTicks ||
                        string.IsNullOrWhiteSpace(s.EmployeeId)) ||
                    p.Shares.Select(s => s.EmployeeId).Distinct().Count() != p.Shares.Count)
                    throw new InvalidDataException("Invalid portion bonus allocation.");
                decimal ticks = p.Shares.Sum(s => (decimal)s.WorkedTicks);
                if (p.Shares.Any(s => s.Amount != decimal.Floor(p.Fund * s.WorkedTicks / ticks)))
                    throw new InvalidDataException("Invalid portion share rounding.");
            }
        }
        return state;
    }

    private void Save(OverNormPortionState state) =>
        AtomicFileStorageService.WriteAllText(_path, JsonSerializer.Serialize(state, Json));
}
