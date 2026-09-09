using System.IO;
using System.Text.Json;
using ClubTimerXbox.Models;
using ClubTimerXbox.Services;

internal sealed class OverNormPortionTests
{
    private static readonly DateTime Day = new(2026, 9, 10, 6, 0, 0);
    private int _passed;
    private static DateTime At(double hours) => Day.AddHours(hours);
    private static PortionRevenue Revenue(int amount, double at) => new(Guid.NewGuid(), At(at), amount);
    private static PortionWork Work(string id, double from, double to) => new(id, id, At(from), At(to));
    private static OverNormPortionDay NewDay() => new() { Start = Day, Norm = 5000, Percent = 10 };
    private static int Paid(OverNormPortionDay day, string id) => day.Portions
        .SelectMany(p => p.Shares).Where(s => s.EmployeeId == id).Sum(s => s.Amount);
    private static void Equal<T>(T expected, T actual) where T : notnull
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception($"Expected {expected}, got {actual}.");
    }
    private static void Reject(Action action)
    {
        try { action(); } catch (InvalidDataException) { return; } catch (JsonException) { return; }
        throw new Exception("Invalid data was accepted.");
    }
    private void Test(string title, Action test)
    {
        test(); _passed++; Console.WriteLine("PASS portion: " + title);
    }

    public void Run()
    {
        Test("only complete 500 portions qualify", () =>
        {
            foreach (int amount in new[] { 0, 5000, 5490, 5499, 5500, 5999, 6000 })
            {
                var day = NewDay();
                OverNormPortionEngine.Accrue(day, At(4), new[] { Revenue(amount, 3) }, new[] { Work("A", 0, 4) });
                Equal(Math.Max(0, amount - 5000) / 500, day.Portions.Count);
            }
        });
        Test("owner example freezes 25 then 16 and 33", () =>
        {
            var day = NewDay();
            var receipts = new[] { Revenue(5500, 6), Revenue(500, 9) };
            var work = new[] { Work("A", 0, 3), Work("B", 3, 9) };
            OverNormPortionEngine.Accrue(day, At(6), receipts, work);
            Equal(25, Paid(day, "A")); Equal(25, Paid(day, "B"));
            OverNormPortionEngine.Accrue(day, At(9), receipts, work);
            Equal(41, Paid(day, "A")); Equal(58, Paid(day, "B"));
            Equal(1m, day.Portions.Sum(p => p.UncreditedAmount));
        });
        Test("30.99 is floored and never transferred", () =>
        {
            var day = NewDay();
            OverNormPortionEngine.Accrue(day, At(10), new[] { Revenue(5500, 10) },
                new[] { Work("A", 0, 6.198), Work("B", 6.198, 10) });
            Equal(30, Paid(day, "A")); Equal(19, Paid(day, "B"));
        });
        Test("each portion floors independently", () =>
        {
            var day = NewDay();
            OverNormPortionEngine.Accrue(day, At(6), new[] { Revenue(6000, 6) },
                new[] { Work("A", 0, 2), Work("B", 2, 4), Work("C", 4, 6) });
            Equal(32, Paid(day, "A")); Equal(32, Paid(day, "B")); Equal(32, Paid(day, "C"));
            Equal(4m, day.Portions.Sum(p => p.UncreditedAmount));
        });
        Test("waits and releases at first two hours without another payment", () =>
        {
            var day = NewDay(); var receipt = new[] { Revenue(6000, 1) };
            OverNormPortionEngine.Accrue(day, At(1.99), receipt, new[] { Work("A", 0, 1.99) });
            Equal(2, day.WaitingPortions); Equal(0, Paid(day, "A"));
            OverNormPortionEngine.Accrue(day, At(3), receipt, new[] { Work("A", 0, 3) });
            Equal(100, Paid(day, "A")); Equal(At(2), day.Portions[0].AllocatedAt);
            Equal(At(3), day.Portions[0].RecordedAt); Equal(0, day.WaitingPortions);
        });
        Test("later eligibility cannot claim past portions", () =>
        {
            var day = NewDay();
            OverNormPortionEngine.Accrue(day, At(5), new[] { Revenue(6000, 1) },
                new[] { Work("A", 0, 3), Work("B", 3, 5) });
            Equal(100, Paid(day, "A")); Equal(0, Paid(day, "B"));
        });
        Test("multiple eligible workers share a waiting portion at the same instant", () =>
        {
            var day = NewDay();
            OverNormPortionEngine.Accrue(day, At(3), new[] { Revenue(5500, 1) },
                new[] { Work("A", 0, 3), Work("B", 0, 3) });
            Equal(25, Paid(day, "A")); Equal(25, Paid(day, "B"));
        });
        Test("split shifts count, overlapping records do not double hours", () =>
        {
            var day = NewDay();
            OverNormPortionEngine.Accrue(day, At(4), new[] { Revenue(5500, 1) },
                new[] { Work("A", 0, 1), Work("A", 0, 1), Work("A", 2, 3) });
            Equal(At(3), day.Portions[0].AllocatedAt);
            Equal(TimeSpan.FromHours(2).Ticks, day.Portions[0].Shares[0].WorkedTicks);
        });
        Test("repeat reports and renamed employee cannot move earned shares", () =>
        {
            var day = NewDay(); var receipts = new[] { Revenue(5500, 4), Revenue(500, 6) };
            OverNormPortionEngine.Accrue(day, At(4), receipts, new[] { Work("A", 0, 4) });
            string frozen = JsonSerializer.Serialize(day.Portions[0]);
            var renamed = new PortionWork("A", "Renamed", At(0), At(6));
            OverNormPortionEngine.Accrue(day, At(6), receipts, new[] { renamed });
            OverNormPortionEngine.Accrue(day, At(7), receipts, new[] { renamed });
            Equal(2, day.Portions.Count); Equal(100, Paid(day, "A"));
            Equal(frozen, JsonSerializer.Serialize(day.Portions[0]));
        });
        Test("no incomplete or waiting portion crosses business boundary", () =>
        {
            var yesterday = NewDay();
            OverNormPortionEngine.Accrue(yesterday, At(27), new[] { Revenue(6000, 23) },
                new[] { Work("A", 23, 27) });
            Equal(0, Paid(yesterday, "A")); Equal(0, yesterday.WaitingPortions);
            var next = new OverNormPortionDay { Start = At(24), Norm = 5000, Percent = 10 };
            OverNormPortionEngine.Accrue(next, At(28), new[] { Revenue(490, 27) }, new[] { Work("A", 24, 28) });
            Equal(0, next.Portions.Count);
        });
        Test("late final game is allocated in source day using only source day hours", () =>
        {
            var day = NewDay();
            OverNormPortionEngine.Accrue(day, At(26), new[] { Revenue(5500, 25) },
                new[] { Work("A", 20, 24), Work("B", 24, 26) });
            Equal(50, Paid(day, "A")); Equal(0, Paid(day, "B"));
            Equal(At(24), day.Portions[0].AllocatedAt);
        });
        Test("refund-adjusted final revenue cannot create a prepaid bonus", () =>
        {
            var day = NewDay();
            OverNormPortionEngine.Accrue(day, At(5), new[] { Revenue(5400, 3), Revenue(2, 4) },
                new[] { Work("A", 0, 5) });
            Equal(5402L, day.Revenue); Equal(0, day.Portions.Count);
        });
        Test("correction and rebound never award the same threshold twice", () =>
        {
            var day = NewDay();
            var first = Revenue(5500, 3); var refund = Revenue(-500, 4); var rebound = Revenue(500, 5);
            var work = new[] { Work("A", 0, 6) };
            OverNormPortionEngine.Accrue(day, At(3), new[] { first }, work);
            OverNormPortionEngine.Accrue(day, At(4), new[] { first, refund }, work);
            Equal(500L, day.RevenueBelowAwardedThreshold);
            OverNormPortionEngine.Accrue(day, At(5), new[] { first, refund, rebound }, work);
            Equal(50, Paid(day, "A")); Equal(0L, day.RevenueBelowAwardedThreshold);
        });
        Test("duplicate source ids are counted once and conflicts rejected", () =>
        {
            var day = NewDay(); var r = Revenue(3000, 3);
            OverNormPortionEngine.Accrue(day, At(4), new[] { r, r }, new[] { Work("A", 0, 4) });
            Equal(3000L, day.Revenue); Equal(0, day.Portions.Count);
            Reject(() => OverNormPortionEngine.Accrue(day, At(4), new[] { r, r with { Amount = 3001 } }, Array.Empty<PortionWork>()));
        });
        Test("new source business date survives month boundary without rewriting actual time", () =>
        {
            var bonus = new AutoSalaryBonusItem
            {
                CreatedAt = new DateTime(2026, 10, 1, 6, 15, 0),
                EarnedBusinessDate = new DateTime(2026, 9, 30)
            };
            Equal(new DateTime(2026, 9, 30), bonus.GetBusinessDate());
            Equal(6, bonus.CreatedAt.Hour);
            bonus.EarnedBusinessDate = null;
            Equal(new DateTime(2026, 10, 1), bonus.GetBusinessDate());
        });
        Test("late source revenue updates only source pace with baseline and audit preserved", () =>
        {
            var snapshot = new FinancialPaceDaySnapshot
            {
                StartInclusive = Day, IsClosed = true, HasExpenseBaseline = true,
                GameRevenue = 5400, SalaryAccrued = 1000, FixedExpenseAccrued = 1200,
                MonthlyFixedExpense = 36000, TotalExpense = 2200, CalculatedAt = At(24)
            };
            var day = NewDay();
            OverNormPortionEngine.Accrue(day, At(26), new[] { Revenue(5500, 25) }, new[] { Work("A", 0, 4) });
            Equal(true, FinancialPacePortionAdjustment.Apply(snapshot, day, 1050, At(26), false));
            Equal(5500, snapshot.GameRevenue); Equal(2250, snapshot.TotalExpense);
            Equal(36000, snapshot.MonthlyFixedExpense); Equal(At(24), snapshot.CalculatedAt);
            Equal(5400, snapshot.RecognitionAdjustments[0].PreviousGameRevenue);
            Equal(false, FinancialPacePortionAdjustment.Apply(snapshot, day, 1050, At(27), false));
            day.Revenue += 500;
            Equal(false, FinancialPacePortionAdjustment.Apply(snapshot, day, 1100, At(28), true));
            Equal(1, snapshot.RecognitionAdjustments.Count);
        });
        Test("disabled bonus still reports source revenue without waiting portions", () =>
        {
            var day = NewDay(); day.Percent = 0;
            OverNormPortionEngine.Accrue(day, At(4), new[] { Revenue(10000, 3) }, new[] { Work("A", 0, 4) });
            Equal(10000L, day.Revenue); Equal(0, day.Portions.Count); Equal(0, day.WaitingPortions);
        });
        Test("persistent ledger migration, restart, policy lock and club isolation", () => WithFolder(folder =>
        {
            string path = Path.Combine(folder, "club_2.json");
            var store = new OverNormPortionStore(path, "club_2", At(-1));
            Equal(Day, store.EffectiveFrom);
            Equal(true, store.GetDay(At(-24), At(6), 5000, 10, Array.Empty<PortionRevenue>(), Array.Empty<PortionWork>()) == null);
            var receipts = new[] { Revenue(5500, 4) }; var work = new[] { Work("A", 0, 6) };
            var first = store.GetDay(Day, At(4), 5000, 10, receipts, work)!;
            Equal(50, Paid(first, "A"));
            var reopened = new OverNormPortionStore(path, "club_2", At(50));
            Equal(Day, reopened.EffectiveFrom);
            Equal(50, Paid(reopened.GetDay(Day, At(6), 2000, 100, receipts, work)!, "A"));
            var other = new OverNormPortionStore(Path.Combine(folder, "club_1.json"), "club_1", At(-1));
            Equal(0, other.GetDay(Day, At(6), 5000, 10, Array.Empty<PortionRevenue>(), work)!.Portions.Count);
            Reject(() => new OverNormPortionStore(path, "club_1", At(6)));
        }));
        Test("closed payroll is read only and missing file is not invented on corruption", () => WithFolder(folder =>
        {
            string path = Path.Combine(folder, "club_2.json");
            var store = new OverNormPortionStore(path, "club_2", At(-1));
            var work = new[] { Work("A", 0, 4) };
            store.GetDay(Day, At(4), 5000, 10, new[] { Revenue(5500, 3) }, work);
            string before = File.ReadAllText(path);
            Equal(50, Paid(store.GetDay(Day, At(30), 5000, 10, new[] { Revenue(10000, 3) }, work, readOnly: true)!, "A"));
            Equal(before, File.ReadAllText(path));
            File.WriteAllText(path, "{}");
            Reject(() => new OverNormPortionStore(path, "club_2", At(6)));
            File.WriteAllText(path, "broken");
            Reject(() => store.GetDay(Day, At(6), 5000, 10, Array.Empty<PortionRevenue>(), work));
        }));
        Console.WriteLine($"Portion bonus: {_passed} scenarios passed.");
    }

    private static void WithFolder(Action<string> test)
    {
        string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ClubTimerPortionTests"));
        string folder = Path.Combine(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try { test(folder); }
        finally
        {
            if (!Path.GetFullPath(folder).StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Unsafe test cleanup path.");
            Directory.Delete(folder, recursive: true);
        }
    }
}
