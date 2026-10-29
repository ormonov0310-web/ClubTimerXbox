using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services;

// Capture on the accounting thread; complete only against owned data on a worker.
internal sealed class DetachedEmployeeJournals
{
    private readonly Dictionary<(string Employee, DateTime Month), EmployeeStatsService.JournalSeed> _seeds = new();

    public List<EmployeeJournalInfo> Capture(string employee, DateTime month)
    {
        month = BusinessCalendarService.GetBusinessMonthByAnchor(month).StartInclusive;
        if (!_seeds.ContainsKey((employee, month)))
            _seeds.Add((employee, month), EmployeeStatsService.CaptureJournalSeed(employee, month));
        return new List<EmployeeJournalInfo>();
    }

    public FirebaseSnapshotDelivery.CapturedSnapshot Freeze(JsonObject document)
        => Freeze(document, CaptureGameSource(ActionLogService.GetAllGameSessions(), CashService.Records));

    internal FirebaseSnapshotDelivery.CapturedSnapshot FreezeOwnedPayload(object payload)
        => FreezeOwnedPayload(payload, CaptureGameSource(ActionLogService.GetAllGameSessions(), CashService.Records));

    internal FirebaseSnapshotDelivery.CapturedSnapshot FreezeOwnedPayload(object payload, GameSource source)
    {
        return new(new JsonObject(), BuildDocument: token =>
        {
            token.ThrowIfCancellationRequested();
            var document = (JsonObject)JsonSerializer.SerializeToNode(payload)!;
            Freeze(document, source).Complete!(token);
            return document;
        });
    }

    internal DetachedEmployeeJournals() { }

    internal DetachedEmployeeJournals(
        IEnumerable<KeyValuePair<(string Employee, DateTime Month), EmployeeStatsService.JournalSeed>> seeds)
    {
        foreach (var (key, seed) in seeds) _seeds.Add(key, seed);
    }

    internal FirebaseSnapshotDelivery.CapturedSnapshot Freeze(JsonObject document, GameSource source)
    {
        var seeds = _seeds.ToArray();
        return new FirebaseSnapshotDelivery.CapturedSnapshot(document, token =>
        {
            var rows = new Dictionary<(string, DateTime), List<EmployeeJournalInfo>>();
            foreach (var (key, seed) in seeds)
            {
                token.ThrowIfCancellationRequested();
                rows.Add(key, BuildJournal(key.Employee, key.Month, seed, source));
            }
            FillEmployees(document["employees"], document["autoSalary"]?["monthKey"]?.GetValue<string>(), rows);
            if (document["reportsByMonth"] is JsonObject reports)
                foreach (var (month, report) in reports)
                {
                    token.ThrowIfCancellationRequested();
                    FillEmployees(report?["employees"], month, rows);
                }
        });
    }

    internal sealed record GameSource(GameSessionLogItem[] Sessions, IReadOnlySet<Guid> PaymentAttributedIds);

    internal static GameSource CaptureGameSource(IEnumerable<GameSessionLogItem> sessions,
        IEnumerable<CashRecord> cash)
    {
        // No shared model or nested mutable list may cross the worker boundary.
        var owned = sessions.Select(session => new GameSessionLogItem
        {
            Id = session.Id, PlaceName = session.PlaceName,
            StartedByEmployeeName = session.StartedByEmployeeName, StartedAt = session.StartedAt,
            TariffText = session.TariffText, PaidAmount = session.PaidAmount,
            ClosedByEmployeeName = session.ClosedByEmployeeName, ClosedAt = session.ClosedAt,
            IsClosed = session.IsClosed, IncomeEmployeeName = session.IncomeEmployeeName,
            CashIncomeAmount = session.CashIncomeAmount,
            ProductsAndServicesAmount = session.ProductsAndServicesAmount,
            TotalToPayAmount = session.TotalToPayAmount,
            ExtraLines = session.ExtraLines.Select(extra => new GameSessionExtraLine
            {
                CreatedAt = extra.CreatedAt, Type = extra.Type, EmployeeName = extra.EmployeeName,
                Description = extra.Description, Amount = extra.Amount
            }).ToList(),
            SaleLines = session.SaleLines.Select(sale => new GameSessionSaleLine
            {
                Id = sale.Id, CreatedAt = sale.CreatedAt, EmployeeName = sale.EmployeeName,
                CreatedByEmployeeName = sale.CreatedByEmployeeName,
                SettlementSchemaVersion = sale.SettlementSchemaVersion,
                ItemName = sale.ItemName, Quantity = sale.Quantity, TotalAmount = sale.TotalAmount,
                IsPaid = sale.IsPaid, PaymentRecordId = sale.PaymentRecordId,
                PaidAt = sale.PaidAt, PaidByEmployeeName = sale.PaidByEmployeeName
            }).ToList()
        }).ToArray();
        var ids = cash.Where(record => record.Type == CashRecordType.GameSession &&
                record.GameSessionId.HasValue && record.PaymentRecordId.HasValue)
            .Select(record => record.GameSessionId!.Value).ToHashSet();
        return new GameSource(owned, ids);
    }

    internal static List<EmployeeJournalInfo> BuildJournal(string employee, DateTime month,
        EmployeeStatsService.JournalSeed seed, GameSource source)
    {
        var period = BusinessCalendarService.GetBusinessMonthByAnchor(month);
        var rows = new List<EmployeeJournalInfo>(seed.Shifts);
        EmployeeStatsService.AddGameSessionJournal(employee, period.StartInclusive, period.EndExclusive,
            rows, source.Sessions, source.PaymentAttributedIds);
        rows.AddRange(seed.Cash);
        rows.AddRange(seed.Losses);
        return rows.OrderByDescending(row => row.CreatedAt).Take(150).ToList();
    }

    internal static void FillEmployees(JsonNode? employees, string? monthKey,
        IReadOnlyDictionary<(string, DateTime), List<EmployeeJournalInfo>> rows)
    {
        if (employees is not JsonArray array ||
            !BusinessCalendarService.TryParseMonthKey(monthKey ?? "", out DateTime month))
            throw new InvalidOperationException("Snapshot journal section has no business month.");
        month = BusinessCalendarService.GetBusinessMonthByAnchor(month).StartInclusive;
        foreach (JsonObject employee in array.OfType<JsonObject>())
        {
            string name = employee["name"]!.GetValue<string>();
            if (!rows.TryGetValue((name, month), out var journal))
                throw new InvalidOperationException("Snapshot journal capture is incomplete.");
            employee["journal"] = Payload(journal);
            employee["incomeJournal"] = Payload(journal.Where(item =>
                item.Type is "Игры" or "Выручка" or "Товар/услуга" or "Товары и услуги"));
            employee["shortageJournal"] = Payload(journal.Where(item =>
                item.Type is "Недостача" or "Потеря" || item.Type.Contains("Штраф")));
        }
    }

    private static JsonArray Payload(IEnumerable<EmployeeJournalInfo> rows)
        => JsonSerializer.SerializeToNode(rows.Select(item => new
        {
            id = item.Id?.ToString() ?? "", createdAt = item.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
            type = item.Type, lossKind = item.LossKind, title = item.Title,
            description = item.Description, amount = item.Amount, isFixed = item.IsFixed,
            isPaid = item.IsPaid, resolutionStatus = item.ResolutionStatus, sourceCode = item.SourceCode
        }))!.AsArray();
}
