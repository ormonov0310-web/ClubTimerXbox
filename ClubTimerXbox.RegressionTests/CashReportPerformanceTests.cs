using System.Diagnostics;
using System.Text.Json;
using ClubTimerXbox.Models;
using ClubTimerXbox.Services;

internal static class CashReportPerformanceTests
{
    private static readonly DateTime Day = new(2026, 10, 26);

    public static void Run(bool benchmark = false)
    {
        var (payments, sessions) = Fixture(160, 90);
        foreach (var section in new[] { CashReportSection.Games, CashReportSection.ProductsAndServices,
                     CashReportSection.Employees })
        foreach (var period in Enum.GetValues<CashReportPeriodMode>())
        foreach (var view in Enum.GetValues<CashReportViewMode>())
        {
            var filter = Filter(section, period);
            filter.ViewMode = view;
            var original = CashReportService.BuildReport(filter, payments,
                id => sessions.FirstOrDefault(item => item.Id == id));
            var indexed = CashReportService.BuildReport(filter, payments,
                CashReportService.CreateSessionLookup(() => sessions));
            Equal(JsonSerializer.Serialize(original), JsonSerializer.Serialize(indexed));
            var summary = CashReportService.BuildPaymentSummary(filter, payments,
                CashReportService.CreateSessionLookup(() => sessions));
            Equal(JsonSerializer.Serialize(original.Summary), JsonSerializer.Serialize(summary));
        }

        for (int day = 0; day < 6; day++)
        {
            var games = Filter(CashReportSection.Games);
            games.SelectedDay = Day.AddDays(-day);
            var products = Filter(CashReportSection.ProductsAndServices);
            products.SelectedDay = games.SelectedDay;
            int original = CashReportService.BuildReport(games, payments,
                id => sessions.FirstOrDefault(item => item.Id == id)).Summary.TotalAmount +
                CashReportService.BuildReport(products, payments,
                    id => sessions.FirstOrDefault(item => item.Id == id)).Summary.TotalAmount;
            int combined = CashReportService.BuildPaymentSummary(games, payments,
                CashReportService.CreateSessionLookup(() => sessions), includeAllRevenue: true).TotalAmount;
            Equal(original, combined);
        }

        LegacyDates();
        LazyLookup();
        CacheInvalidation();
        Console.WriteLine("Cash report optimization: fixture equivalence, legacy dates, lookup and cache checks passed; no live stores/cloud used.");
        if (benchmark) Benchmark();
    }

    private static void LegacyDates()
    {
        var session = new GameSessionLogItem { IsClosed = true };
        session.SaleLines.Add(new GameSessionSaleLine
            { ItemName = "Tea", Quantity = 1, UnitPrice = 50, CreatedAt = Day.AddDays(-1).AddHours(8) });
        var payment = new PaymentRecord
        {
            CreatedAt = Day.AddHours(8), GameSessionId = session.Id,
            TotalAmount = 150, CashAmount = 100, MBankAmount = 50,
            Items = new()
            {
                new() { Name = "Game", Category = "Игры", Quantity = 1, UnitPrice = 100 },
                new() { Name = "Tea", Category = "Товар", Quantity = 1, UnitPrice = 50 }
            }
        };
        var filter = Filter(CashReportSection.ProductsAndServices);
        Func<Guid, GameSessionLogItem?> lookup = _ => session;
        Equal(0, CashReportService.BuildPaymentSummary(filter, new[] { payment }, lookup).TotalAmount);
        filter.SelectedDay = Day.AddDays(-1);
        Equal(50, CashReportService.BuildPaymentSummary(filter, new[] { payment }, lookup).TotalAmount);
        payment.Items[1].SourceSaleLineId = Guid.NewGuid();
        Equal(0, CashReportService.BuildPaymentSummary(filter, new[] { payment }, lookup).TotalAmount);
        filter.SelectedDay = Day;
        Equal(50, CashReportService.BuildPaymentSummary(filter, new[] { payment }, lookup).TotalAmount);
    }

    private static void LazyLookup()
    {
        int loads = 0;
        var first = new GameSessionLogItem { IsClosed = true };
        var duplicate = new GameSessionLogItem { Id = first.Id, IsClosed = true };
        var sessions = new List<GameSessionLogItem> { first, duplicate };
        var lookup = CashReportService.CreateSessionLookup(() => { loads++; return sessions; });
        Equal(0, loads);
        if (!ReferenceEquals(first, lookup(first.Id))) throw new Exception("First matching session changed");
        _ = lookup(Guid.NewGuid());
        Equal(1, loads);
        sessions.Clear();
        var fresh = CashReportService.CreateSessionLookup(() => sessions);
        Equal<GameSessionLogItem?>(null, fresh(first.Id));
    }

    private static void CacheInvalidation()
    {
        var cache = new CashDayRevenueCache();
        int calls = 0;
        int value = 150;
        int Calculate() { calls++; return value; }
        for (int second = 0; second <= 300; second++)
            Equal(150, cache.Get(Day.AddHours(8).AddSeconds(second), 1, 1, Calculate));
        Equal(1, calls);
        value = 130;
        Equal(130, cache.Get(Day, 2, 1, Calculate));
        Equal(2, calls);
        value = 110;
        Equal(110, cache.Get(Day, 2, 2, Calculate));
        Equal(3, calls);
        value = 0;
        Equal(0, cache.Get(Day.AddDays(1), 2, 2, Calculate));
        Equal(4, calls);
        try { cache.Get(Day.AddDays(1), 3, 2, () => throw new IOException("Fixture")); }
        catch (IOException) { }
        value = 70;
        Equal(70, cache.Get(Day.AddDays(1), 3, 2, Calculate));
        Equal(5, calls);
    }

    private static CashReportFilter Filter(CashReportSection section,
        CashReportPeriodMode period = CashReportPeriodMode.Day) => new()
    {
        Section = section, PeriodMode = period, SelectedDay = Day,
        SelectedYear = 2026, SelectedMonth = 10,
        PeriodStart = Day.AddDays(-4), PeriodEnd = Day
    };

    private static (List<PaymentRecord>, List<GameSessionLogItem>) Fixture(int paymentCount, int sessionCount)
    {
        var sessions = Enumerable.Range(0, sessionCount).Select(index => new GameSessionLogItem
        {
            IsClosed = true,
            SaleLines = new()
            {
                new() { ItemName = "Tea", Quantity = 1, UnitPrice = 50,
                    CreatedAt = Day.AddDays(-(index % 5)).AddHours(8) }
            }
        }).ToList();
        var random = new Random(297);
        var payments = new List<PaymentRecord>(paymentCount);
        for (int index = 0; index < paymentCount; index++)
        {
            int sign = index % 13 == 0 ? -1 : 1;
            var items = new List<CheckoutItem>
            {
                new() { Name = "Game", Category = "Игры", Quantity = 1, UnitPrice = sign * 100 },
                new() { Name = "Tea", Category = "Товар", Quantity = 1, UnitPrice = sign * 50 },
                new() { Name = "Service", Category = "Услуга", Quantity = 1, UnitPrice = sign * 20 }
            };
            if (index % 3 == 0) items[1].SourceSaleLineId = Guid.NewGuid();
            if (index % 7 == 0) items.Add(new CheckoutItem
                { Name = "Other", Category = "Other", Quantity = 1, UnitPrice = sign * 5 });
            int total = items.Sum(item => item.TotalAmount) + (index % 19 == 0 ? sign : 0);
            int cash = sign * random.Next(Math.Abs(total) + 1);
            payments.Add(new PaymentRecord
            {
                CreatedAt = Day.AddDays(-(index % 5)).AddHours(index % 2 == 0 ? 8 : 3).AddMinutes(index % 50),
                GameSessionId = sessions[index % sessionCount].Id,
                Items = index % 41 == 0 ? new() : items,
                EmployeeName = index % 2 == 0 ? "Fixture A" : "Fixture B",
                PlaceName = "TV " + index % 12, OperationTitle = "Fixture checkout",
                TotalAmount = total, CashAmount = cash, MBankAmount = total - cash
            });
        }
        return (payments, sessions);
    }

    private static void Benchmark()
    {
        // Counts match the report; contents are synthetic, not copied club data.
        var (payments, sessions) = Fixture(7417, 4608);
        var games = Filter(CashReportSection.Games);
        var products = Filter(CashReportSection.ProductsAndServices);
        int Before() => CashReportService.BuildReport(games, payments,
            id => sessions.FirstOrDefault(item => item.Id == id)).Summary.TotalAmount +
            CashReportService.BuildReport(products, payments,
                id => sessions.FirstOrDefault(item => item.Id == id)).Summary.TotalAmount;
        int After() => CashReportService.BuildPaymentSummary(games, payments,
            CashReportService.CreateSessionLookup(() => sessions), includeAllRevenue: true).TotalAmount;
        Equal(Before(), After());
        var before = Measure(Before);
        var after = Measure(After);
        var cache = new CashDayRevenueCache();
        _ = cache.Get(Day, 1, 1, After);
        var unchanged = Measure(() => cache.Get(Day, 1, 1, After));
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            fixturePayments = payments.Count, fixtureSessions = sessions.Count,
            baselineMilliseconds = before.Milliseconds, optimizedMilliseconds = after.Milliseconds,
            baselineAllocatedBytes = before.Bytes, optimizedAllocatedBytes = after.Bytes,
            unchangedMilliseconds = unchanged.Milliseconds, unchangedAllocatedBytes = unchanged.Bytes,
            sameResult = before.Result == after.Result,
            note = "Median of five local synthetic runs; not a measured Salikh UI latency or root cause."
        }));
    }

    private static (double Milliseconds, long Bytes, int Result) Measure(Func<int> action)
    {
        var elapsed = new List<double>();
        var allocated = new List<long>();
        int result = 0;
        for (int index = 0; index < 5; index++)
        {
            long bytes = GC.GetAllocatedBytesForCurrentThread();
            var watch = Stopwatch.StartNew();
            result = action();
            watch.Stop();
            elapsed.Add(watch.Elapsed.TotalMilliseconds);
            allocated.Add(GC.GetAllocatedBytesForCurrentThread() - bytes);
        }
        return (elapsed.Order().ElementAt(2), allocated.Order().ElementAt(2), result);
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception($"Expected {expected}, got {actual}");
    }
}
