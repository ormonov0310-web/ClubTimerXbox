using System.Collections;
using System.Diagnostics;
using System.Text.Json;
using ClubTimerXbox.Models;
using ClubTimerXbox.Services;

internal static class EmployeeJournalPerformanceTests
{
    private static readonly DateTime From = new(2026, 10, 1, 6, 0, 0);
    private static readonly DateTime Until = From.AddMonths(1);

    public static void Run(bool benchmark = false)
    {
        PredicateCompatibility();
        FullJournalCompatibility();
        RepeatedBuildsAreFresh();
        SingleCashTraversal();
        Console.WriteLine("Employee journal: predicate, complete ordered rows, month/employee boundaries, fresh inputs and single-pass checks passed; no UI/live stores/cloud used.");
        if (benchmark) Benchmark();
    }

    private static void PredicateCompatibility()
    {
        var session = Session(0, From);
        session.IsClosed = true;
        session.ClosedAt = From.AddHours(1);
        var records = new List<CashRecord>();
        Compare(new[] { session }, records, "Alice", From, Until);
        foreach (var type in Enum.GetValues<CashRecordType>())
        foreach (Guid? sessionId in new Guid?[] { null, session.Id, Id(1) })
        foreach (Guid? paymentId in new Guid?[] { null, Guid.Empty, Id(500) })
        {
            records.Clear();
            records.Add(new CashRecord
            {
                Type = type, GameSessionId = sessionId, PaymentRecordId = paymentId,
                Amount = -120, CreatedAt = Until.AddYears(1), EmployeeName = "Other"
            });
            var actual = Build(new[] { session }, records, "Alice", From, Until);
            bool attributed = records.Any(row => row.Type == CashRecordType.GameSession &&
                row.GameSessionId == session.Id && row.PaymentRecordId.HasValue);
            Equal(!attributed, actual.Any(row => row.Type == "Выручка"));
            Compare(new[] { session }, records, "Alice", From, Until);
        }

        records.Add(new CashRecord { Type = CashRecordType.GameSession,
            GameSessionId = session.Id, PaymentRecordId = Guid.Empty, Amount = 0 });
        records.Add(records[^1]);
        Compare(new[] { session, session }, records, "Alice", From, Until);
    }

    private static void FullJournalCompatibility()
    {
        var (sessions, records) = Fixture(180, 370);
        sessions.AddRange(new[] { Session(1000, From.AddTicks(-1)), Session(1001, From),
            Session(1002, Until.AddTicks(-1)), Session(1003, Until) });
        string before = JsonSerializer.Serialize(new { sessions, records });
        foreach (string employee in new[] { "Alice", "Bob", "alice", "", "Unknown" })
        foreach (int monthOffset in new[] { -1, 0, 1 })
        {
            DateTime from = From.AddMonths(monthOffset);
            Compare(sessions, records, employee, from, from.AddMonths(1));
        }
        Equal(before, JsonSerializer.Serialize(new { sessions, records }));

        var random = new Random(57281);
        for (int trial = 0; trial < 40; trial++)
        {
            foreach (var record in records)
            {
                record.Type = (CashRecordType)random.Next(7);
                record.GameSessionId = random.Next(5) == 0 ? null : sessions[random.Next(sessions.Count)].Id;
                record.PaymentRecordId = random.Next(3) == 0 ? null : Id(random.Next(20));
            }
            Compare(sessions, records, trial % 2 == 0 ? "Alice" : "Bob", From, Until);
        }
    }

    private static void RepeatedBuildsAreFresh()
    {
        var sessions = new[] { Session(2, From) };
        sessions[0].StartedByEmployeeName = "Alice";
        sessions[0].IncomeEmployeeName = "Alice";
        var records = new List<CashRecord>();
        Equal(1, Build(sessions, records, "Alice", From, Until).Count(row => row.Type == "Выручка"));
        records.Add(new CashRecord { Type = CashRecordType.GameSession,
            GameSessionId = sessions[0].Id, PaymentRecordId = Id(99) });
        Equal(0, Build(sessions, records, "Alice", From, Until).Count(row => row.Type == "Выручка"));
        records[0].PaymentRecordId = null;
        Equal(1, Build(sessions, records, "Alice", From, Until).Count(row => row.Type == "Выручка"));
        records[0].PaymentRecordId = Guid.Empty;
        Equal(0, Build(sessions, records, "Alice", From, Until).Count(row => row.Type == "Выручка"));
        records.Clear();
        Equal(1, Build(sessions, records, "Alice", From, Until).Count(row => row.Type == "Выручка"));
    }

    private static void SingleCashTraversal()
    {
        var (sessions, records) = Fixture(150, 240);
        var counted = new CountedRecords(records);
        Build(sessions, counted, "Alice", From, Until);
        Equal(1, counted.Enumerations);
        Equal(records.Count, counted.ItemsRead);
    }

    private static void Compare(IReadOnlyList<GameSessionLogItem> sessions,
        IReadOnlyList<CashRecord> records, string employee, DateTime from, DateTime until)
    {
        var expected = Legacy(sessions, records, employee, from, until);
        var actual = Build(sessions, records, employee, from, until);
        Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual));
        Equal(expected.Sum(row => (long)row.Amount), actual.Sum(row => (long)row.Amount));
    }

    private static List<EmployeeJournalInfo> Build(IEnumerable<GameSessionLogItem> sessions,
        IEnumerable<CashRecord> records, string employee, DateTime from, DateTime until)
    {
        var result = new List<EmployeeJournalInfo>();
        EmployeeStatsService.AddGameSessionJournal(employee, from, until, result, sessions, records);
        return result;
    }

    // Frozen 1.6.0 game-journal behavior, with only live-store reads replaced by fixtures.
    private static List<EmployeeJournalInfo> Legacy(IEnumerable<GameSessionLogItem> sessions,
        IEnumerable<CashRecord> records, string employee, DateTime from, DateTime until)
    {
        var result = new List<EmployeeJournalInfo>();
        foreach (var session in sessions)
        {
            if (session.StartedByEmployeeName == employee && session.StartedAt >= from && session.StartedAt < until)
                result.Add(new EmployeeJournalInfo { CreatedAt = session.StartedAt, Type = "Игры",
                    Title = $"Открыл {session.PlaceName}",
                    Description = $"Тариф: {session.TariffText}\nОплачено: {session.PaidAmount} сом" });

            if (session.IsClosed && session.ClosedAt != null && session.ClosedAt.Value >= from &&
                session.ClosedAt.Value < until && session.ClosedByEmployeeName == employee)
                result.Add(new EmployeeJournalInfo { CreatedAt = session.ClosedAt.Value, Type = "Игры",
                    Title = $"Закрыл {session.PlaceName}",
                    Description = $"Игра: {session.CashIncomeAmount} сом\n" +
                        $"Оформлено товаров/услуг: {session.ProductsAndServicesAmount} сом\n" +
                        $"Итого: {session.TotalToPayAmount} сом", Amount = session.TotalToPayAmount });

            bool attributed = records.Any(row => row.Type == CashRecordType.GameSession &&
                row.GameSessionId == session.Id && row.PaymentRecordId.HasValue);
            if (!attributed && session.IncomeEmployeeName == employee && session.IsClosed &&
                session.ClosedAt != null && session.ClosedAt.Value >= from && session.ClosedAt.Value < until)
                result.Add(new EmployeeJournalInfo { CreatedAt = session.ClosedAt.Value, Type = "Выручка",
                    Title = $"Выручка за {session.PlaceName}",
                    Description = $"Выручка относится к сотруднику: {employee}\n" +
                        $"Игра: {session.CashIncomeAmount} сом\nТовары/услуги учитываются отдельными платежами.",
                    Amount = session.CashIncomeAmount });

            foreach (var extra in session.ExtraLines)
            {
                if (extra.EmployeeName != employee || extra.CreatedAt < from || extra.CreatedAt >= until) continue;
                result.Add(new EmployeeJournalInfo { CreatedAt = extra.CreatedAt, Type = extra.Type,
                    Title = $"{extra.Type} • {session.PlaceName}", Description = extra.Description, Amount = extra.Amount });
            }
            foreach (var sale in session.SaleLines)
            {
                string createdBy = SessionSaleSettlementService.GetCreatedByEmployeeName(sale);
                if (createdBy.Equals(employee, StringComparison.OrdinalIgnoreCase) && sale.CreatedAt >= from && sale.CreatedAt < until)
                    result.Add(new EmployeeJournalInfo { CreatedAt = sale.CreatedAt, Type = "Товар/услуга",
                        Title = $"Выдал {sale.ItemName}", Description = $"Место: {session.PlaceName}\n" +
                            $"Количество: {sale.Quantity}\nСумма: {sale.TotalAmount} сом\n" +
                            (SessionSaleSettlementService.IsFinanciallyPaid(sale)
                                ? $"Оплату принял: {SessionSaleSettlementService.GetFinancialEmployeeName(sale)}" : "Статус: долг клиента"),
                        Amount = sale.TotalAmount });
                if (sale.SettlementSchemaVersion >= SessionSaleSettlementService.CurrentSchemaVersion &&
                    SessionSaleSettlementService.IsFinanciallyPaid(sale))
                {
                    DateTime paidAt = SessionSaleSettlementService.GetFinancialOccurredAt(sale);
                    string paidBy = SessionSaleSettlementService.GetFinancialEmployeeName(sale);
                    if (paidBy.Equals(employee, StringComparison.OrdinalIgnoreCase) && paidAt >= from && paidAt < until)
                        result.Add(new EmployeeJournalInfo { CreatedAt = paidAt, Type = "Оплата товара/услуги",
                            Title = $"Принял оплату за {sale.ItemName}", Description = $"Место: {session.PlaceName}\n" +
                                $"Оформил: {createdBy}\nКоличество: {sale.Quantity}\nСумма: {sale.TotalAmount} сом",
                            Amount = sale.TotalAmount });
                }
            }
        }
        return result;
    }

    private static (List<GameSessionLogItem>, List<CashRecord>) Fixture(int sessionCount, int recordCount)
    {
        var sessions = Enumerable.Range(0, sessionCount).Select(i => Session(i, From.AddDays(i % 45 - 7))).ToList();
        var records = Enumerable.Range(0, recordCount).Select(i => new CashRecord
        {
            Id = Id(100000 + i), Type = (CashRecordType)(i % 7),
            GameSessionId = i % 11 == 0 ? null : sessions[i % sessionCount].Id,
            PaymentRecordId = i % 5 == 0 ? null : Id(200000 + i),
            EmployeeName = i % 2 == 0 ? "Alice" : "Bob", Amount = i % 3 == 0 ? -100 : 100,
            CreatedAt = From.AddMonths(i % 3 - 1)
        }).ToList();
        return (sessions, records);
    }

    private static GameSessionLogItem Session(int i, DateTime at)
    {
        var session = new GameSessionLogItem
        {
            Id = Id(i), PlaceName = "TV" + i % 10, StartedByEmployeeName = i % 2 == 0 ? "Alice" : "Bob",
            StartedAt = at, ClosedAt = i % 7 == 0 ? null : at.AddHours(1),
            ClosedByEmployeeName = i % 3 == 0 ? "Alice" : "Bob", IncomeEmployeeName = i % 2 == 0 ? "Alice" : "Bob",
            IsClosed = i % 5 != 0, TariffText = "Fixture", PaidAmount = 120,
            CashIncomeAmount = i % 3 == 0 ? -20 : 100, ProductsAndServicesAmount = 50, TotalToPayAmount = 150
        };
        session.ExtraLines.Add(new GameSessionExtraLine { EmployeeName = "Alice", CreatedAt = at,
            Type = "Extra", Description = "Fixture", Amount = -10 });
        session.SaleLines.Add(new GameSessionSaleLine { CreatedAt = at, EmployeeName = "Bob", ItemName = "Tea",
            Quantity = 1, TotalAmount = 50, IsPaid = i % 2 == 0, SettlementSchemaVersion = i % 3 == 0 ? 1 : 2,
            CreatedByEmployeeName = "Bob", PaidByEmployeeName = "Alice", PaidAt = at.AddDays(1),
            PaymentRecordId = i % 4 == 0 ? null : Id(300000 + i) });
        return session;
    }

    private static void Benchmark()
    {
        var (sessions, records) = Fixture(4638, 7377);
        Compare(sessions, records, "Alice", From, Until);
        static (double Ms, long Bytes) Measure(Func<List<EmployeeJournalInfo>> create)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            var watch = Stopwatch.StartNew();
            var result = create();
            watch.Stop();
            GC.KeepAlive(result);
            return (watch.Elapsed.TotalMilliseconds, GC.GetAllocatedBytesForCurrentThread() - before);
        }
        var legacy = Measure(() => Legacy(sessions, records, "Alice", From, Until));
        var indexed = Measure(() => Build(sessions, records, "Alice", From, Until));
        Console.WriteLine($"Synthetic journal (4638 sessions/7377 cash rows), one journal: legacy={legacy.Ms:F3}ms/{legacy.Bytes}B; indexed={indexed.Ms:F3}ms/{indexed.Bytes}B. Not measured club UI latency.");
    }

    private static Guid Id(int value) => new(value, 0, 0, new byte[8]);

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException("Employee journal fixture mismatch");
    }

    private sealed class CountedRecords(IEnumerable<CashRecord> records) : IEnumerable<CashRecord>
    {
        public int Enumerations { get; private set; }
        public int ItemsRead { get; private set; }
        public IEnumerator<CashRecord> GetEnumerator()
        {
            Enumerations++;
            foreach (var row in records) { ItemsRead++; yield return row; }
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
