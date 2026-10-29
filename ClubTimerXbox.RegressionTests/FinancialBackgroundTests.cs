using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClubTimerXbox.Models;
using ClubTimerXbox.Services;

internal static class FinancialBackgroundTests
{
    private static readonly DateTime Month = SalaryAccrualTests.From;

    internal static void Run()
    {
        SalaryChecks();
        PaceChecks();
        OwnerContext.Run(CoordinatorChecks);
        Console.WriteLine("Financial background: detached monthly salary/daily/closed-payroll, pace mutation plans, owner-thread apply, stale retries, bounded fallback, single flight, scope/day changes and close cancellation passed; no GUI/live services/cloud.");
    }

    private static CapturedSalaryMonth CaptureSalary(SalaryAccrualFixture fixture)
    {
        return new(Month, new AutoSalaryReport { MonthKey = "2026-10", GameRevenue = 5000 },
            new[] { new CapturedSalaryMonth.EmployeeInput
            {
                EmployeeId = "Alice-id", EmployeeName = "Alice", WorkHours = 8,
                PaidIntervals = new() { new(Month.AddHours(5), Month.AddHours(13)) },
                Summary = new() { MonthUnpaidLosses = 31 }, PaidSalary = 120,
                DebtTransferred = 23, CarryIn = 17,
                Bonuses = new() { new() { Amount = 47, CreatedAt = Month.AddHours(10), Type = "fixture" } }
            } }, fixture.Capture());
    }

    private static void SalaryChecks()
    {
        var fixture = new SalaryAccrualFixture(430, 120);
        var oracle = new SalaryAccrualLegacyOracle(fixture);
        var captured = CaptureSalary(fixture);
        var result = captured.Calculate();
        var employee = result.Report.Employees.Single();
        var intervals = new List<PaidInterval> { new(Month.AddHours(5), Month.AddHours(13)) };
        int time = oracle.Time(new("Alice", 8, intervals), Month, Month.AddMonths(1)).Amount;
        int games = oracle.Games("Alice", Month, Month.AddMonths(1)).Amount;
        int products = oracle.Products("Alice", Month, Month.AddMonths(1));
        Check(employee.GrossAmount == time + games + products + 47, "monthly frozen formula");
        Check(employee.RemainingAmount == employee.GrossAmount - 31 - 120 - 23 + 17, "net, debt and carry-in unchanged");
        var days = result.DailyEarnings["Alice"];
        Check(days.Sum(day => day.TotalAmount) == employee.GrossAmount, "daily/monthly reconciliation");
        Check(days.Sum(day => day.TimeRatingEarnedAmount) == employee.TimeRatingEarnedAmount &&
            days.Sum(day => day.GameRatingLostAmount) == employee.GameRatingLostAmount, "rating financial-effect totals");
        string frozen = JsonSerializer.Serialize(result);
        fixture.CashRows.Clear(); fixture.ProductRows.Clear(); fixture.Events.Clear(); fixture.Policies.Clear();
        Check(frozen == JsonSerializer.Serialize(captured.Calculate()), "captured salary has no live input references");

        var archived = new EmployeePayrollObligation
        {
            AccruedAmount = 1000, BonusAmount = 15, PenaltyAmount = 20, PaidAmount = 100,
            TimeAmount = 700, GameRevenueAmount = 300, ProductBonusAmount = 5,
            RatingFinancialEffectCaptured = true, TimeRatingEarnedAmount = 12,
            GameRatingLostAmount = 9, DailyEarnings = new() { new() { Date = Month.Date, TimeAmount = 1015 } }
        };
        var input = new CapturedSalaryMonth.EmployeeInput
        {
            EmployeeName = "Alice", ClosedPayroll = archived, DebtTransferred = 30,
            Rating = new() { TimePercent = 90, RevenuePercent = 80, OverallPercent = 85 }
        };
        var old = new CapturedSalaryMonth(Month, new() { MonthKey = "2026-10" },
            new[] { input }, new SalaryAccrualFixture(1, 1).Capture());
        archived.AccruedAmount = 9999; archived.DailyEarnings.Clear();
        var closed = old.Calculate();
        var closedEmployee = closed.Report.Employees.Single();
        Check(closedEmployee.GrossAmount == 1015 && closedEmployee.RemainingAmount == 865, "closed payroll is not repriced");
        Check(closedEmployee.TimeRatingEarnedAmount == 12 && closedEmployee.GameRatingLostAmount == 9,
            "closed captured rating effects");
        Check(closed.DailyEarnings["Alice"].Single().TimeAmount == 1015, "closed daily archive deep copy");
    }

    private static void PaceChecks()
    {
        var state = new FinancialPaceState
        {
            ManualExpenseVersions = new() { new() { EffectiveFrom = Month, MonthlyExpenseAmount = 37200 } }
        };
        var daily = new Dictionary<string, List<AutoSalaryDayEarning>>
        {
            ["Alice"] = new() { new() { Date = Month.Date, TimeAmount = 100 }, new() { Date = Month.AddDays(1).Date, TimeAmount = 200 } },
            ["Bob"] = new() { new() { Date = Month.Date, TimeAmount = -7 } }
        };
        var games = new Dictionary<string, int> { ["2026-10-01"] = 3000, ["2026-10-02"] = 1500 };
        var portions = new Dictionary<string, OverNormPortionDay?>();
        var captured = new CapturedPaceMonth(Month, Month.AddDays(1).AddHours(19), state, portions,
            new(false, 0, "", games), false, 42);
        string before = JsonSerializer.Serialize(state);
        state.ManualExpenseVersions[0].MonthlyExpenseAmount = 999999; games["2026-10-01"] = 99999;
        var result = captured.Calculate(daily);
        Check(result.Snapshot.OwnerWithdrawnAmount == 42 && result.Snapshot.Days.Count == 2, "pace monthly capture");
        Check(result.Snapshot.GameRevenue == 4500 && result.Snapshot.Days.All(day => day.MonthlyFixedExpense == 37200),
            "pace source isolation");
        Check(result.Changes.Select(change => change.DayKey).SequenceEqual(new[] { "2026-10-01", "2026-10-02" }),
            "durable checkpoint order");
        var first = result.Snapshot.Days.Single(day => day.IsClosed);
        Check(first.SalaryAccrued == 100, "negative employee does not reduce pace salary expense");
        Check(result.Changes[0].ClosedDay != null && result.Changes[0].Timeline == null &&
            result.Changes[1].ClosedDay == null && result.Changes[1].Timeline?.Count == 1, "close then open timeline changes");
        var persisted = new FinancialPaceState
        {
            ManualExpenseVersions = new() { new() { EffectiveFrom = Month, MonthlyExpenseAmount = 37200 } },
            ClosedDays = new() { ["2026-10-01"] = result.Changes[0].ClosedDay! },
            OpenDayTimelines = new() { ["2026-10-02"] = result.Changes[1].Timeline! }
        };
        string persistedBefore = JsonSerializer.Serialize(persisted);
        var again = new CapturedPaceMonth(Month, Month.AddDays(1).AddHours(19).AddMinutes(1), persisted, portions,
            new(false, 0, "", new Dictionary<string, int> { ["2026-10-01"] = 4000, ["2026-10-02"] = 1500 }), false, 42)
            .Calculate(daily);
        Check(again.Changes.Count == 0, "closed immutable and unchanged open timeline under 15 minutes");
        Check(again.Snapshot.Days.Single(day => day.IsClosed).GameRevenue == 3000, "closed revenue remains factual");
        Check(persistedBefore == JsonSerializer.Serialize(persisted), "pace plan does not mutate authoritative state");
        var latePortion = new OverNormPortionDay
        {
            Start = Month, Revenue = 3500, Portions = new() { new() { Number = 1 } }
        };
        var correctionInputs = new Dictionary<string, OverNormPortionDay?> { ["2026-10-01"] = latePortion };
        var correctionCapture = new CapturedPaceMonth(Month, Month.AddDays(1).AddHours(19).AddMinutes(1),
            persisted, correctionInputs, new(false, 0, "", new Dictionary<string, int> { ["2026-10-02"] = 1500 }), false, 42);
        latePortion.Revenue = 99999; latePortion.Portions.Clear();
        var correction = correctionCapture.Calculate(daily);
        var correctedDay = correction.Snapshot.Days.Single(day => day.IsClosed);
        Check(correctedDay.GameRevenue == 3500 && correctedDay.RecognitionAdjustments.Count == 1 &&
            correctedDay.OverNormPortionCount == 1, "detached late portion retains audited closed-day adjustment");
        Check(correction.Changes.Single().ClosedDay?.GameRevenue == 3500, "correction has one immediate durable checkpoint");
        Check(JsonSerializer.Serialize(persisted) == persistedBefore, "late correction stays a plan until guarded apply");
        var closedMonth = new CapturedPaceMonth(Month, Month.AddDays(1).AddHours(19).AddMinutes(1),
            persisted, new Dictionary<string, OverNormPortionDay?>
            {
                ["2026-10-01"] = new() { Start = Month, Revenue = 3500, Portions = new() { new() { Number = 1 } } }
            }, new(false, 0, "", new Dictionary<string, int> { ["2026-10-02"] = 1500 }), true, 42).Calculate(daily);
        Check(closedMonth.Changes.Count == 0 && closedMonth.Snapshot.Days.Single(day => day.IsClosed).GameRevenue == 3000,
            "closed payroll month blocks late portion adjustment");
        var sourceActual = new CapturedPaceMonth(Month, Month.AddHours(12), persisted, portions,
            new(true, 31000, "2026-09", new Dictionary<string, int>()), false, 0).Calculate(daily);
        Check(sourceActual.Snapshot.Days.Single().ExpenseSourceType == "PreviousMonthActual" &&
            sourceActual.Snapshot.Days.Single().MonthlyFixedExpense == 31000, "actual baseline precedes manual");
        Check(before.Contains("37200"), "fixture baseline recorded");
    }

    private static async Task CoordinatorChecks()
    {
        var context = SynchronizationContext.Current!;
        int owner = Environment.CurrentManagedThreadId;
        var coordinator = new BackgroundFinancialCalculation();
        long revision = 0; int value = 10, captures = 0, applies = 0, worker = 0;
        bool scope = true;
        var profile = default(BackgroundFinancialCalculation.Profile);
        int applied = 0;
        int result = await coordinator.RunAsync<int>(() =>
        {
            Check(Environment.CurrentManagedThreadId == owner, "capture owner thread");
            int captured = value; int attempt = ++captures;
            return token =>
            {
                worker = Environment.CurrentManagedThreadId;
                if (attempt == 1)
                {
                    using var changed = new ManualResetEventSlim();
                    context.Post(_ => { value = 20; revision++; changed.Set(); }, null);
                    Check(changed.Wait(TimeSpan.FromSeconds(5), token), "in-flight owner mutation");
                }
                return captured;
            };
        }, () => revision, () => scope, number =>
        {
            Check(Environment.CurrentManagedThreadId == owner, "apply owner thread");
            applies++; applied = number;
        }, default, item => profile = item);
        Check(result == 20 && applied == 20 && captures == 2 && applies == 1 && worker != owner,
            "stale result never applied, fresh retry only");
        Check(profile?.Attempts == 2 && profile.OwnerThreadId == owner && profile.WorkerThreadId != owner,
            "separate owner/worker measurements");

        captures = 0; applies = 0;
        await coordinator.RunAsync<int>(() =>
        {
            int attempt = ++captures;
            return token =>
            {
                if (attempt < 3)
                {
                    using var changed = new ManualResetEventSlim();
                    context.Post(_ => { revision++; changed.Set(); }, null);
                    Check(changed.Wait(TimeSpan.FromSeconds(5), token), "continuous change");
                }
                else Check(Environment.CurrentManagedThreadId == owner, "bounded fallback owner");
                return attempt;
            };
        }, () => revision, () => true, _ => applies++, default, item => profile = item);
        Check(captures == 3 && applies == 1 && profile?.SynchronousFallback == true, "continuous activity cannot starve sync");

        foreach (bool dayBoundary in new[] { false, true })
        {
            captures = 0; applies = 0;
            DateTime day = Month;
            await coordinator.RunAsync<int>(() =>
            {
                int attempt = ++captures;
                return token =>
                {
                    if (attempt == 1)
                    {
                        using var changed = new ManualResetEventSlim();
                        context.Post(_ =>
                        {
                            if (dayBoundary) day = day.AddDays(1);
                            else { scope = false; revision++; scope = true; revision++; }
                            changed.Set();
                        }, null);
                        Check(changed.Wait(TimeSpan.FromSeconds(5), token), "boundary mutation");
                    }
                    return attempt;
                };
            }, () => (revision, day), () => true, number =>
            {
                Check(number == 2, "day boundary or A-B-A invalidates old calculation");
                applies++;
            }, default);
            Check(captures == 2 && applies == 1, "boundary retry is applied only once");
        }

        int active = 0, maximum = 0;
        Func<Func<CancellationToken, int>> capture = () => token =>
        {
            int count = Interlocked.Increment(ref active); maximum = Math.Max(maximum, count);
            Thread.Sleep(30); Interlocked.Decrement(ref active); return 1;
        };
        await Task.WhenAll(
            coordinator.RunAsync(capture, () => 0, () => true, _ => { }, default),
            coordinator.RunAsync(capture, () => 0, () => true, _ => { }, default));
        Check(maximum == 1, "full and overview share one financial worker");

        foreach (bool closing in new[] { false, true })
        {
            using var cancel = new CancellationTokenSource();
            scope = true; applies = 0;
            try
            {
                await coordinator.RunAsync<int>(() => token =>
                {
                    using var changed = new ManualResetEventSlim();
                    context.Post(_ => { if (closing) cancel.Cancel(); else scope = false; changed.Set(); }, null);
                    changed.Wait(TimeSpan.FromSeconds(5)); token.ThrowIfCancellationRequested(); return 10;
                }, () => revision, () => scope, _ => applies++, cancel.Token);
                throw new Exception("Expected cancellation");
            }
            catch (OperationCanceledException) { }
            Check(applies == 0, "scope switch or close never applies stale finance");
        }
        long before = FinancialInputRevision.Value;
        FinancialInputRevision.Changed();
        Check(FinancialInputRevision.Value == before + 1, "same-count replacement has a new financial revision");
        await DeliveryChecks(coordinator);
    }

    private static async Task DeliveryChecks(BackgroundFinancialCalculation coordinator)
    {
        var seeds = new Dictionary<(string, DateTime), EmployeeStatsService.JournalSeed>
        {
            [("Alice", Month)] = new(new(), new() { new() { CreatedAt = Month, Amount = 31, Type = "Salary" } }, new())
        };
        var ownedPayload = new
        {
            autoSalary = new { monthKey = "2026-10" },
            employees = new[] { new { name = "Alice" } },
            reportsByMonth = new Dictionary<string, object>()
        };
        var detached = new DetachedEmployeeJournals(seeds).FreezeOwnedPayload(ownedPayload, new(Array.Empty<GameSessionLogItem>(), new HashSet<Guid>()));
        int ownerThread = Environment.CurrentManagedThreadId;
        var serialized = await Task.Run(() =>
        {
            Check(Environment.CurrentManagedThreadId != ownerThread, "owned DTO serialization runs on worker");
            return detached.BuildDocument!(default);
        });
        Check(serialized["employees"]![0]!["journal"]![0]!["amount"]!.GetValue<int>() == 31, "deferred serialization fills real detached journals");
        string folder = Path.Combine(Path.GetTempPath(), "clubtimer-financial-async-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            long now = 100000; int amount = 100, sends = 0; bool offline = false;
            string path = Path.Combine(folder, "delivery.json");
            var delivery = new FirebaseSnapshotDelivery(path, () => now);
            async Task<FirebaseSnapshotDelivery.CapturedSnapshot> Capture()
            {
                int owned = await coordinator.RunAsync<int>(() => { int current = amount; return _ => current; },
                    () => amount, () => true, _ => { }, default);
                return new(new JsonObject(), BuildDocument: _ => new JsonObject { ["amount"] = owned });
            }
            Task Send(FirebaseSnapshotDelivery.PreparedPatch patch)
            {
                if (offline) throw new HttpRequestException("offline fixture");
                sends++;
                Check(patch.Patch["amount"]?.GetValue<int>() == amount, "async capture/deferred serialization wire value");
                return Task.CompletedTask;
            }
            delivery.Request("fixture", now, true);
            Check(await delivery.PublishCapturedAsync(Capture, Send, now), "async first publication");
            now += 3000; delivery.Request("fixture", now, true);
            Check(await delivery.PublishCapturedAsync(Capture, Send, now) && sends == 1, "async idle dedup");
            amount = 200; offline = true; now += 3000; delivery.Request("fixture", now, true);
            Check(!await delivery.PublishCapturedAsync(Capture, Send, now) && delivery.Pending, "offline remains pending");
            delivery = new(path, () => now); offline = false; now += 200000;
            Check(await delivery.PublishCapturedAsync(Capture, Send, now) && sends == 2 && !delivery.Pending, "restart and reconnect preserve work");
        }
        finally
        {
            foreach (var file in Directory.EnumerateFiles(folder)) File.Delete(file);
            Directory.Delete(folder);
        }
    }

    internal static void MeasureIdle() => OwnerContext.Run(MeasureIdleAsync);

    private static async Task MeasureIdleAsync()
    {
        string folder = Path.Combine(Path.GetTempPath(), "clubtimer-financial-idle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var delivery = new FirebaseSnapshotDelivery(Path.Combine(folder, "delivery.json"));
            var coordinator = new BackgroundFinancialCalculation();
            var fixture = new SalaryAccrualFixture(7377, 1600);
            var watch = Stopwatch.StartNew();
            int refreshes = 0, sends = 0, bytes = 0;
            Console.WriteLine("Integrated detached financial idle: 300 real seconds, 90s cadence, mocked transport, no windows/live services.");
            while (watch.Elapsed.TotalSeconds < 300)
            {
                if (watch.Elapsed.TotalSeconds >= refreshes * 90)
                {
                    refreshes++;
                    long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    delivery.Request("fixture:financial", now, true);
                    Check(await delivery.PublishCapturedAsync(async () =>
                    {
                        var state = new FinancialPaceState();
                        var asOf = Month.AddHours(1) + watch.Elapsed;
                        var salary = CaptureSalary(fixture);
                        var pace = new CapturedPaceMonth(Month, asOf, state, new Dictionary<string, OverNormPortionDay?>(),
                            new(false, 0, "", new Dictionary<string, int>()), false, 0);
                        var calculated = await coordinator.RunAsync<(SalaryMonthCalculation Salary, CapturedPaceMonth.Calculation Pace)>(() => token =>
                        {
                            var s = salary.Calculate();
                            return (Salary: s, Pace: pace.Calculate(s.DailyEarnings));
                        }, () => 0, () => true, _ => { }, default);
                        return new FirebaseSnapshotDelivery.CapturedSnapshot(new JsonObject(), BuildDocument: _ =>
                        {
                            var days = calculated.Pace.Snapshot.Days.Select(day => new
                            {
                                calculatedAt = day.CalculatedAt.ToString("O"), gameRevenue = day.GameRevenue,
                                totalExpense = day.TotalExpense, isClosed = day.IsClosed
                            }).ToArray();
                            return JsonSerializer.SerializeToNode(new
                            {
                                autoSalary = calculated.Salary.Report,
                                financialPace = new { days },
                                reportsByMonth = new Dictionary<string, object> { ["2026-10"] = new { financialPace = new { days } } },
                                history = new string('x', 7000000)
                            })!.AsObject();
                        });
                    }, patch => { sends++; bytes += patch.Utf8.Length; return Task.CompletedTask; }, now), "integrated idle delivery");
                }
                await Task.Delay(250);
            }
            Check(refreshes == 4 && sends == 1, "integrated idle zero redundant writes");
            Console.WriteLine(JsonSerializer.Serialize(new { elapsedSeconds = Math.Round(watch.Elapsed.TotalSeconds, 3),
                refreshes, sends, bytes, redundantWrites = sends - 1, scope = "Owned salary/pace/deferred JSON/coordinator/mock delivery; excludes live capture IO, UI, presence, TLS, reconnect fan-out and phone downloads." }));
        }
        finally
        {
            foreach (var file in Directory.EnumerateFiles(folder)) File.Delete(file);
            Directory.Delete(folder);
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("Financial background: " + message);
    }

    private sealed class OwnerContext : SynchronizationContext
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = new();
        public override void Post(SendOrPostCallback callback, object? state) => _queue.Add((callback, state));
        internal static void Run(Func<Task> action)
        {
            var previous = Current;
            var owner = new OwnerContext();
            SetSynchronizationContext(owner);
            Task? task = null;
            owner.Post(async _ =>
            {
                task = action();
                try { await task; }
                catch { /* Rethrow synchronously after draining this private owner queue. */ }
                finally { owner._queue.CompleteAdding(); }
            }, null);
            try
            {
                foreach (var item in owner._queue.GetConsumingEnumerable()) item.Callback(item.State);
                task!.GetAwaiter().GetResult();
            }
            finally { SetSynchronizationContext(previous); owner._queue.Dispose(); }
        }
    }
}
