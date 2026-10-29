using System.Text.Json;
using System.Text.Json.Nodes;
using ClubTimerXbox.Models;
using ClubTimerXbox.Services;

internal static class DetachedEmployeeJournalTests
{
    private static readonly DateTime Month = new(2026, 10, 1, 6, 0, 0);

    public static void Run(bool benchmark = false)
    {
        var sessions = Sessions();
        var cash = sessions.Where((_, index) => index % 3 == 0).Select(session => new CashRecord
        {
            Type = CashRecordType.GameSession, GameSessionId = session.Id,
            PaymentRecordId = Guid.Empty
        }).ToList();
        var seed = new EmployeeStatsService.JournalSeed(
            new() { Row("Смена", Month, 1) },
            new() { Row("Выручка", Month, 2), Row("Взял", Month.AddDays(4), -600) },
            new() { Row("Потеря", Month, 3) });
        seed.Losses[0].Id = Guid.NewGuid();
        seed.Losses[0].LossKind = "money";
        seed.Losses[0].IsFixed = seed.Losses[0].IsPaid = true;
        seed.Losses[0].ResolutionStatus = "resolved";
        seed.Losses[0].SourceCode = "fixture-source";
        var source = DetachedEmployeeJournals.CaptureGameSource(sessions, cash);
        string before = JsonSerializer.Serialize(new { sessions, cash });
        foreach (string employee in new[] { "Alice", "Bob", "alice", "" })
        foreach (int offset in new[] { -1, 0, 1 })
        {
            DateTime month = Month.AddMonths(offset);
            var expected = new List<EmployeeJournalInfo>(seed.Shifts);
            EmployeeStatsService.AddGameSessionJournal(employee, month, month.AddMonths(1),
                expected, sessions, cash);
            expected.AddRange(seed.Cash);
            expected.AddRange(seed.Losses);
            expected = expected.OrderByDescending(row => row.CreatedAt).Take(150).ToList();
            Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(
                DetachedEmployeeJournals.BuildJournal(employee, month, seed, source)), "exact ordered journal");
        }
        Equal(before, JsonSerializer.Serialize(new { sessions, cash }), "capture does not mutate source");

        string captured = JsonSerializer.Serialize(DetachedEmployeeJournals.BuildJournal("Alice", Month, seed, source));
        foreach (var session in sessions)
        {
            session.StartedAt = Month.AddYears(1);
            session.ClosedAt = null;
            session.IsClosed = false;
            session.PlaceName = "changed";
            session.StartedByEmployeeName = session.IncomeEmployeeName = session.ClosedByEmployeeName = "changed";
            session.TariffText = "changed";
            session.PaidAmount = session.CashIncomeAmount = session.ProductsAndServicesAmount = session.TotalToPayAmount = -999;
            foreach (var extra in session.ExtraLines)
            {
                extra.Description = extra.Type = extra.EmployeeName = "changed";
                extra.CreatedAt = Month.AddYears(1);
                extra.Amount = -999;
            }
            foreach (var sale in session.SaleLines)
            {
                sale.EmployeeName = sale.CreatedByEmployeeName = sale.PaidByEmployeeName = sale.ItemName = "changed";
                sale.SettlementSchemaVersion = 0;
                sale.IsPaid = false;
                sale.PaymentRecordId = null;
                sale.PaidAt = sale.CreatedAt = Month.AddYears(1);
                sale.Quantity = sale.TotalAmount = -999;
            }
            session.ExtraLines.Clear();
            session.SaleLines.Clear();
        }
        sessions.Clear();
        cash.Clear();
        Equal(captured, JsonSerializer.Serialize(DetachedEmployeeJournals.BuildJournal("Alice", Month, seed, source)),
            "nested live edits cannot change captured inputs");

        var seeds = new Dictionary<(string, DateTime), EmployeeStatsService.JournalSeed>
        {
            [("Alice", Month)] = seed,
            [("Bob", Month)] = new(new(), new(), new()),
            [("Alice", Month.AddMonths(-1))] = new(new(), new(), new())
        };
        var document = JsonNode.Parse("""
            {"autoSalary":{"monthKey":"2026-10"},"cash":{"amount":123},
             "employees":[{"name":"Alice","journal":[]},{"name":"Bob","journal":[]}],
             "reportsByMonth":{"2026-10":{"employees":[{"name":"Alice","journal":[]}]},
             "2026-09":{"employees":[{"name":"Alice","journal":[]}]}}}
            """)!.AsObject();
        var work = new DetachedEmployeeJournals(seeds).Freeze(document, source);
        Task.Run(() => work.Complete!(CancellationToken.None)).GetAwaiter().GetResult();
        Equal(123, document["cash"]!["amount"]!.GetValue<int>(), "worker leaves finances alone");
        var alice = document["employees"]![0]!;
        var history = document["reportsByMonth"]!["2026-10"]!["employees"]![0]!;
        Equal(alice["journal"]!.ToJsonString(), history["journal"]!.ToJsonString(), "current/history same captured rows");
        var expectedRows = DetachedEmployeeJournals.BuildJournal("Alice", Month, seed, source);
        Equal(expectedRows.Count, alice["journal"]!.AsArray().Count, "all captured rows filled");
        var expectedPayload = JsonSerializer.SerializeToNode(expectedRows.Select(item => new
        {
            id = item.Id?.ToString() ?? "", createdAt = item.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
            type = item.Type, lossKind = item.LossKind, title = item.Title,
            description = item.Description, amount = item.Amount, isFixed = item.IsFixed,
            isPaid = item.IsPaid, resolutionStatus = item.ResolutionStatus, sourceCode = item.SourceCode
        }));
        Equal(true, JsonNode.DeepEquals(expectedPayload, alice["journal"]), "complete existing wire values");
        Equal(expectedRows.Count(row => row.Type is "Игры" or "Выручка" or "Товар/услуга" or "Товары и услуги"),
            alice["incomeJournal"]!.AsArray().Count, "income section unchanged");
        Equal(expectedRows.Count(row => row.Type is "Потеря" or "Недостача" || row.Type.Contains("Штраф")),
            alice["shortageJournal"]!.AsArray().Count, "loss section unchanged");
        var first = alice["journal"]![0]!.AsObject();
        Equal("id,createdAt,type,lossKind,title,description,amount,isFixed,isPaid,resolutionStatus,sourceCode",
            string.Join(',', first.Select(property => property.Key)), "existing wire schema/order");

        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        bool stopped = false;
        try { work.Complete!(canceled.Token); }
        catch (OperationCanceledException) { stopped = true; }
        Equal(true, stopped, "close cancellation stops background journal build");
        Console.WriteLine("Detached journals: full ordered equality, 150-row cap, employee/month boundaries, owned nested inputs, wire sections and cancellation passed; no live stores/UI/cloud.");
        if (benchmark) Benchmark();
    }

    private static void Benchmark()
    {
        var sessions = Sessions(4638);
        var cash = Enumerable.Range(0, 7377).Select(index => new CashRecord
        {
            Type = index % 3 == 0 ? CashRecordType.Correction : CashRecordType.GameSession,
            GameSessionId = sessions[index % sessions.Count].Id,
            PaymentRecordId = index % 4 == 0 ? null : Guid.Empty
        }).ToArray();
        var keys = Enumerable.Range(-5, 6).SelectMany(offset => new[] { "Alice", "Bob", "alice", "" }
            .Select(employee => (Employee: employee, Month: Month.AddMonths(offset)))).ToArray();
        var empty = new EmployeeStatsService.JournalSeed(new(), new(), new());
        double[] synchronous = new double[5], capture = new double[5], background = new double[5];
        for (int trial = -1; trial < 5; trial++)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            foreach (var key in keys)
            {
                var rows = new List<EmployeeJournalInfo>();
                EmployeeStatsService.AddGameSessionJournal(key.Employee, key.Month, key.Month.AddMonths(1),
                    rows, sessions, cash);
                _ = rows.OrderByDescending(row => row.CreatedAt).Take(150).ToList();
            }
            double oldMilliseconds = watch.Elapsed.TotalMilliseconds;
            watch.Restart();
            var owned = DetachedEmployeeJournals.CaptureGameSource(sessions, cash);
            double captureMilliseconds = watch.Elapsed.TotalMilliseconds;
            watch.Restart();
            Task.Run(() =>
            {
                foreach (var key in keys)
                    _ = DetachedEmployeeJournals.BuildJournal(key.Employee, key.Month, empty, owned);
            }).GetAwaiter().GetResult();
            if (trial >= 0)
            {
                synchronous[trial] = oldMilliseconds;
                capture[trial] = captureMilliseconds;
                background[trial] = watch.Elapsed.TotalMilliseconds;
            }
        }
        static double Median(double[] values) => Math.Round(values.Order().ElementAt(values.Length / 2), 3);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            sessions = sessions.Count, cashRecords = cash.Length, journals = keys.Length,
            synchronousGameJournalsMilliseconds = Median(synchronous),
            ownedGameInputCaptureMilliseconds = Median(capture),
            backgroundGameJournalsMilliseconds = Median(background),
            scope = "Synthetic game journals only; excludes UI seed capture, financial calculations, JSON mapping and network. Not Salikh UI latency."
        }));
    }

    private static EmployeeJournalInfo Row(string type, DateTime at, int amount)
        => new() { CreatedAt = at, Type = type, Amount = amount, Title = type, Description = "fixture" };

    private static List<GameSessionLogItem> Sessions(int count = 260)
    {
        var result = new List<GameSessionLogItem>();
        for (int index = 0; index < count; index++)
        {
            DateTime at = Month.AddDays(index % 35 - 2).AddMinutes(index % 7);
            string employee = index % 2 == 0 ? "Alice" : "Bob";
            result.Add(new GameSessionLogItem
            {
                PlaceName = "TV " + index, StartedAt = at, ClosedAt = at.AddHours(1),
                StartedByEmployeeName = employee, ClosedByEmployeeName = "Alice", IncomeEmployeeName = employee,
                IsClosed = index % 4 != 0, PaidAmount = 120, CashIncomeAmount = index % 5 == 0 ? -120 : 120,
                ProductsAndServicesAmount = 30, TotalToPayAmount = 150, TariffText = "1 hour",
                ExtraLines = new() { new() { CreatedAt = at, EmployeeName = "Alice", Type = "Extra", Amount = 20 } },
                SaleLines = new() { new()
                {
                    CreatedAt = at, EmployeeName = employee, CreatedByEmployeeName = employee,
                    SettlementSchemaVersion = index % 2 == 0 ? 2 : 0, IsPaid = index % 3 != 0,
                    PaidAt = at.AddHours(1), PaidByEmployeeName = "Bob", PaymentRecordId = Guid.Empty,
                    ItemName = "Water", Quantity = 2, TotalAmount = 30
                } }
            });
        }
        return result;
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception("Detached journal: " + label);
    }
}
