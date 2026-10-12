using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services;

internal static class ClubLoanCashPolicy
{
    public const string CreditorId = "@club";

    public static void ValidateMethod(string method)
    {
        if (method != "Наличные" && method != "Безнал")
            throw new InvalidOperationException("Выберите наличные или безнал для займа клуба.");
    }

    public static CashRecord? BuildMovement(EmployeeDebtContract contract, EmployeeDebtEvent item)
    {
        if (contract.CreditorType != EmployeeDebtCreditorType.Club) return null;
        bool issue = item.Kind is EmployeeDebtEventKind.Created or EmployeeDebtEventKind.Increased;
        bool returned = item.Kind == EmployeeDebtEventKind.HandToHandRepayment;
        if (!issue && !returned) return null;
        ValidateMethod(item.PaymentMethod);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("club-loan:" + item.Id));
        return new CashRecord
        {
            Id = new Guid(hash.AsSpan(0, 16)),
            CreatedAt = item.CreatedAt,
            BusinessOccurredAt = item.CreatedAt,
            EmployeeName = item.ActorName,
            RelatedEmployeeName = contract.DebtorNameSnapshot,
            Type = issue ? CashRecordType.ClubLoanIssued : CashRecordType.ClubLoanReturned,
            Category = "Займы",
            Title = (issue ? "Выдача займа: " : "Возврат займа: ") + contract.DebtorNameSnapshot,
            Description = $"Договор: {contract.Id}\n[club-loan:{item.Id}]",
            Amount = item.Amount,
            PaymentMethod = item.PaymentMethod
        };
    }

    public static bool IsMovement(CashRecord record) =>
        record.Type is CashRecordType.ClubLoanIssued or CashRecordType.ClubLoanReturned;

    internal static void Recover(EmployeeDebtState state, Action<CashRecord> ensure,
        Action save)
    {
        foreach (var item in state.Events.Where(item => !item.CashMovementPosted))
        {
            var contract = state.Contracts.FirstOrDefault(contract => contract.Id == item.ContractId);
            if (contract == null) continue;
            CashRecord? record = BuildMovement(contract, item);
            if (record == null) continue;
            ensure(record);
            item.CashMovementPosted = true;
            save();
        }
    }

    internal static bool TryRecoverCommitted(EmployeeDebtState state, Action<CashRecord> ensure,
        Action save)
    {
        try { Recover(state, ensure, save); return true; }
        catch { return false; } // The durable intent is already committed; retry the posting, not the loan.
    }

    public static int NetOut(IEnumerable<CashRecord> records, string method,
        DateTime from, DateTime to, bool includeFrom = false) => records
        .Where(item => IsMovement(item) && item.PaymentMethod == method &&
            (includeFrom ? item.CreatedAt >= from : item.CreatedAt > from) && item.CreatedAt < to)
        .Sum(item => item.Type == CashRecordType.ClubLoanIssued ? item.Amount : -item.Amount);
}
