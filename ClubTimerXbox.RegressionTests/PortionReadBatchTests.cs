using System.Diagnostics;
using System.Text.Json;
using ClubTimerXbox.Services;

internal static class PortionReadBatchTests
{
    internal static void Run(bool benchmark = false)
    {
        string folder = Path.Combine(Path.GetTempPath(), "clubtimer-portion-batch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            DateTime first = new(2026, 10, 1, 6, 0, 0);
            string original = Path.Combine(folder, "original.json"), batched = Path.Combine(folder, "batched.json");
            var plain = new OverNormPortionStore(original, "fixture", first.AddDays(-1));
            var cached = new OverNormPortionStore(batched, "fixture", first.AddDays(-1));
            for (int day = 0; day < 30; day++)
                _ = plain.GetDay(first.AddDays(day), first.AddMonths(1), 5000, 10,
                    Revenue(first.AddDays(day)), Work(first.AddDays(day)));
            File.Copy(original, batched, overwrite: true);
            long plainBefore = plain.DiskReadCount, cachedBefore = cached.DiskReadCount;
            string before = File.ReadAllText(original);
            Cycle(plain, first, false);
            Cycle(cached, first, true);
            Equal(before, File.ReadAllText(original), "unchanged ordinary reads do not write");
            Equal(before, File.ReadAllText(batched), "batch produces same durable ledger bytes");
            Equal(120L, plain.DiskReadCount - plainBefore, "uncached reference read count");
            Equal(1L, cached.DiskReadCount - cachedBefore, "one validated disk read per calculation batch");
            using (var profile = SnapshotBuildMeasurements.Begin())
            {
                Cycle(cached, first, true);
                Equal(1, profile.Export()["portion.load"].Calls, "local profile distinguishes disk read from cache hits");
            }

            using (cached.BeginReadBatch())
            {
                long reads = cached.DiskReadCount;
                _ = cached.EffectiveFrom;
                using (cached.BeginReadBatch()) _ = cached.ReadDays(first, first.AddMonths(1));
                _ = cached.EffectiveFrom;
                Equal(1L, cached.DiskReadCount - reads, "nested scope shares the read, disposal keeps outer alive");
                var day = cached.GetDay(first, first.AddMonths(1), 5000, 10, Revenue(first), Work(first))!;
                day.Portions[0].Shares[0].Amount = 999;
                day.Portions[0].RevenueRecordIds.Clear();
                var reread = cached.GetDay(first, first.AddMonths(1), 5000, 10, Revenue(first), Work(first))!;
                Equal(false, reread.Portions[0].Shares[0].Amount == 999, "returned day cannot mutate cached ledger");
                Equal(true, reread.Portions[0].RevenueRecordIds.Count > 0, "nested lists are detached");
                AtomicFileStorageService.WriteAllText(batched, "not-json");
                bool failed = false;
                try { _ = cached.EffectiveFrom; }
                catch (JsonException) { failed = true; }
                Equal(true, failed, "external changed/corrupt file is not hidden by batch cache");
                File.Copy(original, batched, overwrite: true);
                _ = cached.EffectiveFrom;
            }
            // Even preserving file metadata cannot hide corruption at the next calculation boundary.
            var stamp = File.GetLastWriteTimeUtc(batched);
            string valid = File.ReadAllText(batched);
            AtomicFileStorageService.WriteAllText(batched, new string('!', valid.Length));
            File.SetLastWriteTimeUtc(batched, stamp);
            bool invalid = false;
            using (cached.BeginReadBatch())
                try { _ = cached.EffectiveFrom; }
                catch (JsonException) { invalid = true; }
            Equal(true, invalid, "next scope always revalidates durable file");
            File.Copy(original, batched, overwrite: true);

            using (cached.BeginReadBatch())
            {
                long reads = cached.DiskReadCount;
                _ = cached.EffectiveFrom;
                _ = Task.Run(() => cached.EffectiveFrom).GetAwaiter().GetResult();
                _ = cached.EffectiveFrom;
                Equal(2L, cached.DiskReadCount - reads, "read scope never flows into a worker thread");
            }

            DateTime next = first.AddMonths(1);
            using (cached.BeginReadBatch())
            {
                var bad = new[]
                {
                    new PortionRevenue(Guid.NewGuid(), next.AddHours(4), 5500),
                    new PortionRevenue(Guid.NewGuid(), next.AddHours(5), 60000000)
                };
                bool rejected = false;
                try { _ = cached.GetDay(next, next.AddHours(8), 5000, 10, bad, Work(next)); }
                catch (InvalidDataException) { rejected = true; }
                Equal(true, rejected, "invalid later revenue rejected after possible partial accrual");
                var recovered = cached.GetDay(next, next.AddHours(8), 5000, 10, Array.Empty<PortionRevenue>(), Work(next))!;
                Equal(0, recovered.Portions.Count, "failed calculation cannot leak unsaved portions into next call");
                var awarded = cached.GetDay(next, next.AddHours(8), 5000, 10, Revenue(next), Work(next))!;
                Equal(4, awarded.Portions.Count, "new awards still accrue within the same batch");
                Equal(4, new OverNormPortionStore(batched, "fixture", first)
                    .GetDay(next, next.AddHours(8), 5000, 10, Revenue(next), Work(next), readOnly: true)!.Portions.Count,
                    "each changed award is durable before the batch ends");
            }
            Console.WriteLine("Portion read batches: exact ledger bytes, 120->1 reads, nested scopes, detached returns, external corruption, failed-accrual recovery and immediate award durability passed; temporary files only.");
            if (benchmark)
            {
                double[] uncached = new double[5], batch = new double[5];
                for (int trial = -1; trial < 5; trial++)
                {
                    var watch = Stopwatch.StartNew(); Cycle(plain, first, false);
                    double elapsed = watch.Elapsed.TotalMilliseconds;
                    watch.Restart(); Cycle(plain, first, true);
                    if (trial >= 0) { uncached[trial] = elapsed; batch[trial] = watch.Elapsed.TotalMilliseconds; }
                }
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    ledgerBytes = new FileInfo(original).Length, days = 30, queries = 120,
                    uncachedMilliseconds = Math.Round(uncached.Order().ElementAt(2), 3),
                    batchedMilliseconds = Math.Round(batch.Order().ElementAt(2), 3),
                    scope = "Synthetic local portion ledger, not full payroll/pace or actual club UI latency."
                }));
            }
        }
        finally
        {
            foreach (string file in Directory.EnumerateFiles(folder)) File.Delete(file);
            Directory.Delete(folder);
        }
    }

    private static void Cycle(OverNormPortionStore store, DateTime first, bool batched)
    {
        using var scope = batched ? store.BeginReadBatch() : null;
        for (int day = 0; day < 30; day++)
        {
            DateTime start = first.AddDays(day);
            _ = store.EffectiveFrom;
            _ = store.GetDay(start, first.AddMonths(1), 5000, 10, Revenue(start), Work(start));
            _ = store.GetDay(start, first.AddMonths(1), 5000, 10, Revenue(start), Work(start));
            _ = store.ReadDays(start, start.AddDays(1));
        }
    }

    private static PortionRevenue[] Revenue(DateTime day) => new[]
    {
        new PortionRevenue(Id(day.Ticks), day.AddHours(4), 6000),
        new PortionRevenue(Id(day.Ticks + 1), day.AddHours(7), 1000)
    };
    private static Guid Id(long ticks)
    {
        byte[] bytes = new byte[16];
        BitConverter.GetBytes(ticks).CopyTo(bytes, 0);
        bytes[15] = 1;
        return new Guid(bytes);
    }
    private static PortionWork[] Work(DateTime day) => new[]
    {
        new PortionWork("a", "Alice", day, day.AddHours(12)),
        new PortionWork("b", "Bob", day.AddHours(1), day.AddHours(11))
    };
    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception("Portion batch: " + label);
    }
}
