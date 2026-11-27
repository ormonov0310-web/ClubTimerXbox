using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services;

internal static class DeferredCheckoutPosting
{
    public static bool ShouldRestoreActivePlace(SavedActivePlace saved,
        IEnumerable<DeferredCheckout> pending, IReadOnlyList<GameSessionLogItem> sessions) =>
        saved.GameSessionId is not { } id ||
        !pending.Any(entry => entry.GameSessionId == id) &&
        !sessions.Any(game => game.Id == id && game.DeferredCheckoutId.HasValue);

    public static void Detach(DeferredCheckout entry, IReadOnlyList<GameSessionLogItem> sessions,
        string stoppedBy, Action saveLogs)
    {
        var old = RequireSession(sessions, entry.GameSessionId);
        if (old.IsClosed)
        {
            if (old.DeferredCheckoutId != entry.Id)
                throw new InvalidDataException("The source game was closed by a different operation.");
            saveLogs();
            return;
        }
        if (!old.IsOpenMode || !old.PlaceName.Equals(entry.PlaceName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The deferred checkout does not match its source game.");

        old.DeferredCheckoutId = entry.Id;
        old.ClosedAt = ClubLocal(entry.StoppedAt);
        old.ClosedByEmployeeName = stoppedBy;
        old.ActualPlayedAmount = GameAmount(entry);
        old.CashIncomeAmount = 0;
        old.NeedToPayAmount = old.ActualPlayedAmount;
        old.ProductsAndServicesAmount = old.SaleLines.Sum(line => line.TotalAmount);
        old.TotalToPayAmount = entry.Total;
        old.IncomeEmployeeName = "";
        old.IsClosed = true;
        saveLogs();
    }

    public static void ValidateOrigins(DeferredCheckout entry, IReadOnlyList<GameSessionLogItem> sessions,
        Guid? paymentId) => ValidateOrigins(entry.Lines, sessions, paymentId);

    public static void ValidateOrigins(IEnumerable<DeferredCheckoutLine> lines, IReadOnlyList<GameSessionLogItem> sessions,
        Guid? paymentId)
    {
        foreach (var line in lines)
        {
            var source = RequireSession(sessions, line.SourceGameSessionId);
            if (line.SourceSaleLineId is not { } id)
            {
                if (!source.IsOpenMode || source.IsGameIncomePosted && paymentId is null)
                    throw new InvalidDataException("Unexpected game charge in deferred checkout.");
                continue;
            }
            var sale = source.SaleLines.SingleOrDefault(item => item.Id == id);
            if (sale is null || sale.UnitPrice != line.UnitPrice || sale.Quantity != line.Quantity ||
                sale.IsPaid && (paymentId is null || sale.PaymentRecordId != paymentId))
                throw new InvalidDataException("The original sale is missing, changed or already paid.");
        }
    }

    public static void Complete(DeferredCheckoutLedger ledger, DeferredCheckout entry,
        IReadOnlyList<GameSessionLogItem> sessions, string cashierName, Action<PaymentRecord> saveReceipt,
        Action<IReadOnlyList<CashRecord>> saveIncome, Action saveLogs)
    {
        if (entry.IsPaid)
            return;
        if (entry.PaymentIntent is not { } intent || string.IsNullOrWhiteSpace(cashierName))
            throw new InvalidOperationException("Payment does not have a saved intent and employee.");
        var old = RequireSession(sessions, entry.GameSessionId);
        if (!old.IsClosed || old.DeferredCheckoutId != entry.Id)
            throw new InvalidDataException("The original game has not been safely detached.");
        ValidateOrigins(entry, sessions, intent.PaymentId);

        var payment = new PaymentRecord
        {
            Id = intent.PaymentId,
            GameSessionId = entry.GameSessionId,
            DeferredCheckoutId = entry.Id,
            CreatedAt = ClubLocal(intent.AcceptedAt),
            EmployeeName = cashierName,
            PlaceName = entry.PlaceName,
            OperationTitle = $"Отложенный ККМ: остановка {entry.StopNumber}",
            Items = entry.Lines.Select(line => line.ToCheckoutItem()).ToList(),
            TotalAmount = entry.Total,
            CashAmount = intent.CashAmount,
            MBankAmount = intent.MBankAmount
        };
        payment.BusinessDateKey = BusinessCalendarService.GetBusinessDay(payment.CreatedAt).Key;
        payment.BusinessMonthKey = BusinessCalendarService.GetBusinessMonthKey(payment.CreatedAt);
        saveReceipt(payment);

        var income = new List<CashRecord>();
        foreach (var group in entry.Lines.Where(line => line.SourceSaleLineId is null)
                     .GroupBy(line => line.SourceGameSessionId))
        {
            int amount = group.Sum(line => line.UnitPrice * line.Quantity);
            if (amount > 0)
                income.Add(Income(payment, CashRecordType.GameSession, group.Key, amount));
        }
        int goods = entry.Lines.Where(line => line.SourceSaleLineId.HasValue)
            .Sum(line => line.UnitPrice * line.Quantity);
        if (goods > 0)
            income.Add(Income(payment, CashRecordType.ProductOrService, old.Id, goods));
        saveIncome(income);

        foreach (var line in entry.Lines)
        {
            var source = RequireSession(sessions, line.SourceGameSessionId);
            if (line.SourceSaleLineId is { } saleId)
            {
                SessionSaleSettlementService.MarkPaid(source.SaleLines.Single(sale => sale.Id == saleId),
                    payment.Id, payment.CreatedAt, intent.Actor.EmployeeId, cashierName, intent.Actor.ShiftId);
            }
            else if (line.UnitPrice > 0)
            {
                source.IsGameIncomePosted = true;
                source.GameIncomePostedAt = payment.CreatedAt;
                source.GameIncomeEmployeeName = cashierName;
            }
        }
        old.IncomeEmployeeName = cashierName;
        old.CashIncomeAmount = GameAmount(entry);
        old.NeedToPayAmount = 0;
        old.DeferredCheckoutPaidAt = payment.CreatedAt;
        saveLogs();
        ledger.ConfirmPersistedPayment(entry.Id, intent);
    }

    public static void SaveReceipt(List<PaymentRecord> records, PaymentRecord receipt,
        Action<List<PaymentRecord>> save)
    {
        var related = records.Where(row => row.Id == receipt.Id ||
            row.DeferredCheckoutId == receipt.DeferredCheckoutId).ToArray();
        if (related.Length == 0)
        {
            var updated = new List<PaymentRecord>(records) { receipt };
            save(updated);
            records.Add(receipt);
        }
        else if (related.Length != 1 || related[0].Id != receipt.Id ||
            related[0].DeferredCheckoutId != receipt.DeferredCheckoutId ||
            related[0].GameSessionId != receipt.GameSessionId || related[0].CashAmount != receipt.CashAmount ||
            related[0].MBankAmount != receipt.MBankAmount || related[0].TotalAmount != receipt.TotalAmount ||
            related[0].CreatedAt != receipt.CreatedAt ||
            !related[0].Items.Select(DeferredCheckoutLine.Freeze).SequenceEqual(receipt.Items.Select(DeferredCheckoutLine.Freeze)))
        {
            throw new InvalidDataException("The saved receipt conflicts with the deferred checkout.");
        }
    }

    public static void SaveIncome(List<CashRecord> records, IReadOnlyList<CashRecord> income,
        Action<List<CashRecord>> save)
    {
        var added = new List<CashRecord>();
        foreach (var expected in income)
        {
            var matches = records.Where(row => row.PaymentRecordId == expected.PaymentRecordId &&
                row.GameSessionId == expected.GameSessionId && row.Type == expected.Type).ToArray();
            if (matches.Length == 0)
                added.Add(expected);
            else if (matches.Length != 1 || matches[0].Amount != expected.Amount)
                throw new InvalidDataException("The saved income conflicts with the deferred receipt.");
        }
        if (added.Count == 0)
            return;

        var updated = new List<CashRecord>(records);
        updated.AddRange(added);
        save(updated);
        records.AddRange(added);
    }

    private static CashRecord Income(PaymentRecord payment, CashRecordType type, Guid gameId, int amount) => new()
    {
        CreatedAt = payment.CreatedAt,
        BusinessOccurredAt = payment.CreatedAt,
        BusinessDateKey = payment.BusinessDateKey,
        BusinessMonthKey = payment.BusinessMonthKey,
        EmployeeName = payment.EmployeeName,
        IncomeEmployeeName = payment.EmployeeName,
        PlaceName = payment.PlaceName,
        Title = payment.OperationTitle,
        Category = type == CashRecordType.GameSession ? "Игры" : "Товары и услуги",
        Type = type,
        Amount = amount,
        GameSessionId = gameId,
        PaymentRecordId = payment.Id,
        IsAttachedToGameSession = true
    };

    private static int GameAmount(DeferredCheckout entry) => entry.Lines
        .Where(line => line.SourceSaleLineId is null && line.SourceGameSessionId == entry.GameSessionId)
        .Sum(line => line.Quantity * line.UnitPrice);

    private static DateTime ClubLocal(DateTimeOffset moment) => BusinessCalendarService.ToClubLocal(moment.UtcDateTime);

    private static GameSessionLogItem RequireSession(IReadOnlyList<GameSessionLogItem> sessions, Guid id) =>
        sessions.SingleOrDefault(session => session.Id == id) ??
        throw new InvalidDataException("A source game for deferred checkout is missing.");
}
