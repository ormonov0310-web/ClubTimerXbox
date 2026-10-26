using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Runtime.ExceptionServices;
using System.Windows.Threading;
using ClubTimerXbox.Models;
using ClubTimerXbox.Services;

internal static class SnapshotPipelinePerformanceTests
{
    public static void MeasureUiIdle()
    {
        var source = JsonSerializer.SerializeToNode(BuildFixture())!.AsObject();
        Exception? failure = null;
        object? result = null;
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            var frame = new DispatcherFrame();
            dispatcher.BeginInvoke(new Action(async () =>
            {
                string root = Path.Combine(Path.GetTempPath(), "clubtimer-ui-idle-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(root);
                var heartbeat = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
                var watch = Stopwatch.StartNew();
                double lastTick = 0, maxGap = 0;
                int ticks = 0, attempts = 0, sends = 0, bytes = 0;
                heartbeat.Tick += (_, _) =>
                {
                    double elapsed = watch.Elapsed.TotalMilliseconds;
                    maxGap = Math.Max(maxGap, elapsed - lastTick);
                    lastTick = elapsed;
                    ticks++;
                };
                try
                {
                    var delivery = new FirebaseSnapshotDelivery(Path.Combine(root, "delivery.json"));
                    int caller = Environment.CurrentManagedThreadId;
                    Console.WriteLine("WPF detached-snapshot idle measurement started:300s,90s refresh,synthetic data,no Firebase.");
                    heartbeat.Start();
                    double next = 0;
                    while (watch.Elapsed.TotalSeconds < 300)
                    {
                        if (watch.Elapsed.TotalSeconds >= next)
                        {
                            attempts++;
                            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                            delivery.Request("fixture:ui", now, true);
                            bool ok = await delivery.PublishPreparedAsync(() =>
                            {
                                if (Environment.CurrentManagedThreadId != caller)
                                    throw new Exception("Builder left dispatcher thread");
                                var snapshot = (JsonObject)source.DeepClone();
                                snapshot["updatedAtUnixMs"] = now;
                                return snapshot;
                            }, async patch =>
                            {
                                if (patch.ThreadId == caller) throw new Exception("Preparation blocked caller thread");
                                sends++;
                                bytes += patch.Utf8.Length;
                                await Task.Delay(10);
                            }, now);
                            if (!ok) throw new Exception("Idle fixture send failed");
                            next += 90;
                        }
                        await Task.Delay(100);
                    }
                    if (attempts != 4 || sends != 1 || ticks == 0)
                        throw new Exception("Idle refresh/dedup/dispatcher invariant failed");
                    result = new { elapsedSeconds = Math.Round(watch.Elapsed.TotalSeconds, 3),
                        refreshes = attempts, sends, bytes, redundantWrites = sends - 1,
                        dispatcherTicks = ticks, maxDispatcherGapMilliseconds = Math.Round(maxGap, 3),
                        scope = "WPF fixture/owned JSON,not the real monthly financial builder or production network." };
                }
                catch (Exception error) { failure = error; }
                finally
                {
                    heartbeat.Stop();
                    foreach (string file in Directory.EnumerateFiles(root)) File.Delete(file);
                    Directory.Delete(root);
                    frame.Continue = false;
                }
            }));
            Dispatcher.PushFrame(frame);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
        Console.WriteLine(JsonSerializer.Serialize(result));
    }

    public static void Run(bool benchmark = false)
    {
        DeliveryChecks().GetAwaiter().GetResult();
        MeasurementChecks();
        JsonObject source = benchmark
            ? JsonSerializer.SerializeToNode(BuildFixture())!.AsObject()
            : JsonNode.Parse("{\"cash\":{\"amount\":100},\"history\":[1,2,3]}")!.AsObject();
        var affinity = UiPreparation(source);
        Console.WriteLine("Snapshot pipeline: exact UTF8, dedup, cancellation, scope, private metrics and WPF thread-affinity checks passed.");
        if (!benchmark) return;

        long now = new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        int beforeSize = 0, afterSize = 0;
        void Before()
        {
            var patch = FirebaseSnapshotDelivery.BuildPatch(null, source);
            patch["updatedAtUnixMs"] = now;
            patch["updatedAt"] = DateTimeOffset.FromUnixTimeMilliseconds(now).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
            int count = Encoding.UTF8.GetByteCount(patch.ToJsonString());
            byte[] sent = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(patch));
            if (count != sent.Length) throw new Exception("Legacy count differs from sent bytes");
            beforeSize = count;
        }
        void After() => afterSize = FirebaseSnapshotDelivery.PreparePatch(null, source, now, 0).Utf8.Length;
        Before(); After();
        var before = Measure(Before);
        var after = Measure(After);
        if (beforeSize != afterSize) throw new Exception("Wire size changed");
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            bytes = afterSize,
            baselinePrepare = new { before.Milliseconds, before.AllocatedBytes },
            preparedUtf8 = new { after.Milliseconds, after.AllocatedBytes },
            ui = affinity,
            scope = "Synthetic detached JSON only; not full financial services or measured Salikh responsiveness."
        }));
    }

    private static async Task DeliveryChecks()
    {
        string root = Path.Combine(Path.GetTempPath(), "clubtimer-pipeline-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string path = Path.Combine(root, "delivery.json");
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var delivery = new FirebaseSnapshotDelivery(path);
            var source = JsonNode.Parse("{\"cash\":{\"amount\":123},\"history\":[{\"sum\":100}]}")!.AsObject();
            int sends = 0;
            Task Send(FirebaseSnapshotDelivery.PreparedPatch data)
            {
                sends++;
                if (!data.Utf8.SequenceEqual(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(data.Patch))))
                    throw new Exception("Prepared body differs from existing JSON serializer");
                if (!JsonNode.DeepEquals(JsonNode.Parse(data.Utf8), data.Patch))
                    throw new Exception("Prepared body content differs");
                string sent = Encoding.UTF8.GetString(data.Utf8);
                if (sent.Contains("BuildStages") || sent.Contains("BuildThreadId") || sent.Contains("BuildMilliseconds") ||
                    sent.Contains("CheckpointMilliseconds"))
                    throw new Exception("Private profiling metadata was sent as business data");
                return Task.CompletedTask;
            }
            delivery.Request("fixture:pc", now, true);
            bool ok = await delivery.PublishPreparedAsync(() =>
            {
                using var scope = SnapshotBuildMeasurements.Measure("fixture.build");
                return (JsonObject)source.DeepClone();
            }, Send, now);
            if (!ok || delivery.Pending || sends != 1) throw new Exception("Prepared send did not acknowledge");
            var state = JsonSerializer.Deserialize<FirebaseSnapshotDelivery.State>(File.ReadAllText(path))!;
            if (state.PayloadBytes <= 0 || !state.BuildStages.ContainsKey("fixture.build") ||
                state.PrepareThreadId == 0 || state.BuildThreadId == 0 || state.CheckpointMilliseconds <= 0)
                throw new Exception("Local stage diagnostics missing");
            string checkpoint = File.ReadAllText(path);
            delivery.RecordOverviewBuild(new FirebaseSnapshotDelivery.BuildProfile(now, 700, true,
                new() { ["fixture.overview"] = new(1, 700, 1000) }));
            if (File.ReadAllText(path) != checkpoint)
                throw new Exception("Overview profiling added an idle diagnostic write");
            source["updatedAtUnixMs"] = now + 10;
            delivery.Request("fixture:pc", now + 10, true);
            if (!await delivery.PublishPreparedAsync(() => (JsonObject)source.DeepClone(), Send, now + 10) || sends != 1)
                throw new Exception("Local measurement/generated clock caused a redundant write");
            state = JsonSerializer.Deserialize<FirebaseSnapshotDelivery.State>(File.ReadAllText(path))!;
            if (state.OverviewBuild?.Milliseconds != 700 || state.LastSlowOverviewBuild?.Stages.Count != 1)
                throw new Exception("Overview profile did not persist with an ordinary checkpoint");

            delivery.Request("fixture:pc", now + 20, true);
            int builds = 0;
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            ok = await delivery.PublishPreparedAsync(() => { builds++; return source; }, Send, now + 20, canceled.Token);
            if (ok || !delivery.Pending || builds != 0 || sends != 1)
                throw new Exception("Canceled preparation ran financial builder or acknowledged work");

            var scoped = new FirebaseSnapshotDelivery(Path.Combine(root, "scope.json"));
            scoped.Request("old:pc", now, true);
            int wrongSends = 0;
            ok = await scoped.PublishPreparedAsync(() =>
            {
                scoped.Request("new:pc", now + 1, true);
                return (JsonObject)source.DeepClone();
            }, _ => { wrongSends++; return Task.CompletedTask; }, now);
            if (ok || wrongSends != 0 || !scoped.Pending)
                throw new Exception("Old prepared data crossed a binding/scope change");
            if (JsonSerializer.Serialize(source).Contains("BuildStages"))
                throw new Exception("Diagnostics leaked into the business snapshot");

            string brokenPath = Path.Combine(root, "broken.json");
            var broken = new FirebaseSnapshotDelivery(brokenPath);
            broken.Request("fixture:broken", now, true);
            ok = await broken.PublishPreparedAsync(() =>
            {
                using var measurement = SnapshotBuildMeasurements.Measure("fixture.failure");
                throw new InvalidDataException("Sensitive exception text must not be saved");
            }, Send, now);
            var errorState = JsonSerializer.Deserialize<FirebaseSnapshotDelivery.State>(File.ReadAllText(brokenPath))!;
            if (ok || !errorState.Pending || !errorState.BuildStages.ContainsKey("fixture.failure") ||
                File.ReadAllText(brokenPath).Contains("Sensitive exception text"))
                throw new Exception("Failed-build metrics lost pending state or exposed exception text");
        }
        finally
        {
            foreach (string file in Directory.EnumerateFiles(root)) File.Delete(file);
            Directory.Delete(root);
        }
    }

    private static void MeasurementChecks()
    {
        using (SnapshotBuildMeasurements.Measure("disabled")) { }
        using var capture = SnapshotBuildMeasurements.Begin();
        using (SnapshotBuildMeasurements.Measure("outer"))
        using (SnapshotBuildMeasurements.Measure("inner")) { _ = new byte[100]; }
        var values = capture.Export();
        if (values.Count != 2 || values["inner"].Calls != 1 || values["outer"].AllocatedBytes < 100)
            throw new Exception("Stage accounting failed");
        for (int index = 0; index < 30; index++)
            using (SnapshotBuildMeasurements.Measure("bounded-" + index)) { }
        if (capture.Export().Count != 16) throw new Exception("Unbounded performance labels");
        var escaped = SnapshotBuildMeasurements.Begin();
        var context = ExecutionContext.Capture()!;
        Task.Run(() => { using (SnapshotBuildMeasurements.Measure("other-thread")) { } })
            .GetAwaiter().GetResult();
        escaped.Dispose();
        ExecutionContext.Run(context, _ => { using (SnapshotBuildMeasurements.Measure("after-dispose")) { } }, null);
        if (escaped.Export().Count != 0)
            throw new Exception("Escaped async context wrote into a completed synchronous profile");
    }

    private static (double Milliseconds, long AllocatedBytes) Measure(Action action)
    {
        var times = new List<double>();
        var bytes = new List<long>();
        for (int index = 0; index < 5; index++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            var watch = Stopwatch.StartNew();
            action();
            watch.Stop();
            times.Add(watch.Elapsed.TotalMilliseconds);
            bytes.Add(GC.GetAllocatedBytesForCurrentThread() - before);
        }
        return (times.Order().ElementAt(2), bytes.Order().ElementAt(2));
    }

    private static object UiPreparation(JsonObject source)
    {
        Exception? failure = null;
        object? result = null;
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            var frame = new DispatcherFrame();
            dispatcher.BeginInvoke(new Action(async () =>
            {
                string root = Path.Combine(Path.GetTempPath(), "clubtimer-ui-preparation-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(root);
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2) };
                var watch = Stopwatch.StartNew();
                long last = 0;
                double maxGap = 0;
                int ticks = 0;
                timer.Tick += (_, _) =>
                {
                    maxGap = Math.Max(maxGap, (watch.ElapsedTicks - last) * 1000d / Stopwatch.Frequency);
                    last = watch.ElapsedTicks;
                    ticks++;
                };
                try
                {
                    int caller = Environment.CurrentManagedThreadId;
                    int builder = 0;
                    int worker = 0;
                    var delivery = new FirebaseSnapshotDelivery(Path.Combine(root, "delivery.json"));
                    long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    delivery.Request("fixture:ui", now, true);
                    timer.Start();
                    bool ok = await delivery.PublishPreparedAsync(() =>
                    {
                        builder = Environment.CurrentManagedThreadId;
                        return source;
                    }, patch => { worker = patch.ThreadId; return Task.CompletedTask; }, now);
                    maxGap = Math.Max(maxGap, (watch.ElapsedTicks - last) * 1000d / Stopwatch.Frequency);
                    if (!ok || builder != caller || worker == caller)
                        throw new Exception("Financial builder/JSON preparation thread ownership changed");
                    result = new { builderStayedOnCaller = true, detachedPreparationOnWorker = true,
                        dispatcherTicks = ticks, maxDispatcherGapMilliseconds = Math.Round(maxGap, 3),
                        elapsedMilliseconds = Math.Round(watch.Elapsed.TotalMilliseconds, 3) };
                }
                catch (Exception error) { failure = error; }
                finally
                {
                    timer.Stop();
                    foreach (string file in Directory.EnumerateFiles(root)) File.Delete(file);
                    Directory.Delete(root);
                    frame.Continue = false;
                }
            }));
            Dispatcher.PushFrame(frame);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
        return result!;
    }

    public static void RunBaseline()
    {
        using var measurements = SnapshotBuildMeasurements.Begin();
        object fixture;
        using (SnapshotBuildMeasurements.Measure("fixture.assemble")) fixture = BuildFixture();
        JsonObject snapshot;
        using (SnapshotBuildMeasurements.Measure("fixture.serialize-node"))
            snapshot = JsonSerializer.SerializeToNode(fixture)!.AsObject();
        JsonObject patch;
        using (SnapshotBuildMeasurements.Measure("baseline.diff-clone"))
            patch = FirebaseSnapshotDelivery.BuildPatch(null, snapshot);
        patch["updatedAtUnixMs"] = 1;
        patch["updatedAt"] = "2026-10-26 17:00:00";
        int byteCount;
        using (SnapshotBuildMeasurements.Measure("baseline.count-utf8"))
            byteCount = Encoding.UTF8.GetByteCount(patch.ToJsonString());
        byte[] bytes;
        using (SnapshotBuildMeasurements.Measure("baseline.serialize-send"))
            bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(patch));
        if (bytes.Length != byteCount) throw new Exception("Serialized size mismatch");
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            mode = "baseline", byteCount, stages = measurements.Export(),
            scope = "Synthetic DTO/pure salary and pace cores. Not AutoSalaryService.BuildReport on club data; no stores/cloud."
        }));
    }

    internal static object BuildFixture()
    {
        var months = new Dictionary<string, object>();
        DateTime first = new(2026, 7, 1, 6, 0, 0);
        var settings = new AutoSalarySettings { TimeMonthlyFundAmount = 15000,
            TimeMonthlyPlannedHours = 180, ExpenseReservePercent = 30, SalaryFundPercent = 20 };
        for (int month = 0; month < 4; month++)
        {
            DateTime start = first.AddMonths(month);
            var employees = new List<object>();
            using (SnapshotBuildMeasurements.Measure("fixture.salary-core"))
            {
                for (int employee = 0; employee < 7; employee++)
                {
                    var days = Enumerable.Range(0, 30).Select(day => new
                    {
                        date = start.AddDays(day).ToString("yyyy-MM-dd"),
                        timeAmount = EmployeeSalaryRuleEngine.CalculateTimeAccrual(6, settings, 100),
                        gameAmount = EmployeeSalaryRuleEngine.CalculateGameAccrual(600 + day, settings, 100),
                        otherBonusAmount = EmployeeSalaryRuleEngine.CalculateProductBonus(100, settings)
                    }).ToArray();
                    employees.Add(new
                    {
                        id = $"fixture-{employee}", name = $"Fixture employee {employee}", dailyEarnings = days,
                        journal = Enumerable.Range(0, 150).Select(row => new
                        {
                            id = $"fixture-{employee}-{row}", createdAt = start.AddMinutes(row).ToString("O"),
                            title = "Synthetic paid game operation", amount = 120,
                            description = "Synthetic history row; no real employees, transactions, credentials or financial records."
                        }).ToArray()
                    });
                }
            }
            object[] pace;
            using (SnapshotBuildMeasurements.Measure("fixture.pace-core"))
            {
                pace = Enumerable.Range(0, 30).Select(day =>
                {
                    DateTime at = start.AddDays(day);
                    int fixedExpense = FinancialPaceCalculator.CalculateFixedExpenseAccrued(1200, at, at.AddHours(14));
                    return (object)new
                    {
                        businessDateKey = at.ToString("yyyy-MM-dd"), fixedExpenseAccrued = fixedExpense,
                        salaryAccrued = 1000, gameRevenue = 5000,
                        percent = FinancialPaceCalculator.CalculatePercent(5000, fixedExpense + 1000),
                        timeline = Enumerable.Range(0, 200).Select(point => new
                        {
                            createdAt = at.AddMinutes(point).ToString("O"), gameRevenue = point * 10,
                            fixedExpenseAccrued = point * 3, salaryAccrued = point * 2,
                            totalExpense = point * 5, difference = point * 5, percent = 100d
                        }).ToArray()
                    };
                }).ToArray();
            }
            months[start.ToString("yyyy-MM")] = new { employees, financialPace = new { days = pace } };
        }
        return new { cash = new { actualCashlessBalanceMonth = 34478 }, reportsByMonth = months };
    }
}
