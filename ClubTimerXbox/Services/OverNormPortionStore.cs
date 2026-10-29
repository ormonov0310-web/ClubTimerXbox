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
    [ThreadStatic] private static Dictionary<OverNormPortionStore, ReadBatch>? _readBatches;
    internal long DiskReadCount { get; private set; }

    private sealed class ReadBatch
    {
        public int Depth;
        public OverNormPortionState? State;
        public (long Length, DateTime WriteTime) Stamp;
    }

    // Synchronous calculation scope only; never inherited by a worker or kept across await.
    internal IDisposable BeginReadBatch()
    {
        _readBatches ??= new();
        if (!_readBatches.TryGetValue(this, out var batch))
            _readBatches.Add(this, batch = new());
        batch.Depth++;
        return new BatchLease(this, Environment.CurrentManagedThreadId);
    }

    private sealed class BatchLease(OverNormPortionStore store, int threadId) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            if (Environment.CurrentManagedThreadId != threadId)
                throw new InvalidOperationException("A portion read batch cannot cross threads or await.");
            _disposed = true;
            if (--_readBatches![store].Depth == 0) _readBatches.Remove(store);
        }
    }

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
            return Load().Days.Values.Where(day => day.Start >= from && day.Start < to).Select(Clone).ToList();
    }

    public OverNormPortionDay? GetDay(DateTime start, DateTime now, int norm, int percent,
        IEnumerable<PortionRevenue> revenue, IEnumerable<PortionWork> work, bool readOnly = false)
    {
        lock (_gate)
        {
            try
            {
                var state = Load();
                if (start < state.EffectiveFrom) return null;
                string key = OverNormPortionEngine.DayKey(start);
                state.Days.TryGetValue(key, out var day);
                if (readOnly || start > now) return day == null ? new() { Start = start } : Clone(day);
                string before = JsonSerializer.Serialize(day, Json);
                day ??= new OverNormPortionDay { Start = start, Norm = norm, Percent = percent };
                OverNormPortionEngine.Accrue(day, now, revenue, work);
                state.Days[key] = day;
                if (JsonSerializer.Serialize(day, Json) != before) Save(state);
                return Clone(day);
            }
            catch
            {
                // Failed accrual/save must not leave a partially changed in-memory ledger.
                if (_readBatches?.TryGetValue(this, out var batch) == true) batch.State = null;
                throw;
            }
        }
    }

    private OverNormPortionState Load()
    {
        ReadBatch? batch = null;
        if (_readBatches?.TryGetValue(this, out batch) == true && batch.State != null &&
            batch.Stamp == Stamp()) return batch.State;
        using var measurement = SnapshotBuildMeasurements.Measure("portion.load");
        var beforeRead = Stamp();
        // Financial state corruption must never silently switch back to the old formula.
        DiskReadCount++;
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
        if (beforeRead != Stamp()) throw new IOException("Portion ledger changed while being read.");
        if (batch != null)
        {
            batch.State = state;
            batch.Stamp = beforeRead;
        }
        return state;
    }

    private (long Length, DateTime WriteTime) Stamp()
    {
        var file = new FileInfo(_path);
        return (file.Length, file.LastWriteTimeUtc);
    }

    private void Save(OverNormPortionState state)
    {
        FinancialInputRevision.Changed();
        AtomicFileStorageService.WriteAllText(_path, JsonSerializer.Serialize(state, Json));
        if (_readBatches?.TryGetValue(this, out var batch) == true)
        {
            batch.State = state;
            batch.Stamp = Stamp();
        }
    }

    internal static OverNormPortionDay Clone(OverNormPortionDay day) => new()
    {
        Start = day.Start, Norm = day.Norm, Percent = day.Percent, Revenue = day.Revenue,
        WaitingPortions = day.WaitingPortions, RevenueBelowAwardedThreshold = day.RevenueBelowAwardedThreshold,
        Portions = day.Portions.Select(portion => new OverNormPortion
        {
            Number = portion.Number, AllocatedAt = portion.AllocatedAt, RecordedAt = portion.RecordedAt,
            Fund = portion.Fund, UncreditedAmount = portion.UncreditedAmount,
            RevenueRecordIds = portion.RevenueRecordIds.ToList(),
            Shares = portion.Shares.Select(share => new OverNormPortionShare
            {
                EmployeeId = share.EmployeeId, EmployeeName = share.EmployeeName,
                WorkedTicks = share.WorkedTicks, Amount = share.Amount
            }).ToList()
        }).ToList()
    };
}
