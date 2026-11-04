using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services;

internal static class KnownCashlessLoanRepairPolicy
{
    internal const string OperationId = "repair:telecom-cashless-loan-20261104";
    internal const string ContractId = "d2881aa72db04989971eed9d4a004e7b";
    internal const string EventId = "88bba0aa27444b888309b15c6e3b8f3f";
    internal static readonly Guid CardId = Guid.Parse("ac0b7159-665c-4f59-a2aa-6b7782dd826b");
    internal static readonly Guid InvestigationId = Guid.Parse("e292dc68-dd97-457b-babe-c518f504c5f2");
    internal static readonly Guid LoanId = Guid.Parse("b1082277-8e29-498e-0268-ca1209e22589");
    internal static readonly Guid ResidualId = StableId(OperationId + ":residual");
    internal static readonly Guid CheckpointId = StableId(OperationId + ":checkpoint");
    internal const string Explanation =
        "Исправлено программное ожидание безнала: выдача займа 3000 сом была пропущена. " +
        "Исходное ожидание 6017, правильное 3017, фактически 3069 сом. " +
        "Недостача 2948 сом отменена без списания с сотрудника. " +
        "Осталась необъяснённая разница +52 сом, уже вошедшая в принятую базу 3069. " +
        "Займ и физические остатки повторно не изменялись.";

    internal static KnownDataRepairResult Apply(string clubId, IList<CashReconciliationItem> items,
        EmployeeDebtContract? contract, IReadOnlyList<CashRecord> cash,
        IReadOnlyList<EmployeeLossItem> losses, DateTime now)
    {
        if (clubId != "club_1" || items.Count(item => item.Id == CardId) != 1)
            return KnownDataRepairResult.NotMatched;
        var card = items.Single(item => item.Id == CardId);
        if (card.InvestigationId != InvestigationId || card.OperationId != "cashless_verify_1793780688649" ||
            card.Kind != CashReconciliationKind.CashlessShortage ||
            card.Origin != CashReconciliationOrigin.CashlessVerification || card.IsTechnicalEvent ||
            card.OriginalAmount != 2948 || card.ExpectedAmount != 6017 ||
            card.ProgramExpectedAmount != 6017 || card.ActualAmount != 3069)
            return KnownDataRepairResult.NotMatched;

        var residual = items.Where(item => item.Id == ResidualId).ToList();
        var repairCheckpoint = items.Where(item => item.Id == CheckpointId).ToList();
        if (card.Status == CashReconciliationStatus.Resolved && card.Amount == 0 &&
            card.ResolvedAmount == 2948 && card.FormalizedAmount == 0 && card.PostedFormalizedAmount == 0 &&
            card.Resolution == CashReconciliationResolution.InputCorrection && card.ResolutionNote == Explanation &&
            residual.Count == 1 && residual[0].OperationId == OperationId + ":residual" &&
            residual[0].IsTechnicalEvent && residual[0].Amount == 0 && residual[0].OriginalAmount == 52 &&
            repairCheckpoint.Count == 1 && repairCheckpoint[0].OperationId == OperationId + ":checkpoint" &&
            repairCheckpoint[0].IsTechnicalEvent)
            return KnownDataRepairResult.AlreadyApplied;

        var month = BusinessCalendarService.GetBusinessMonth(card.CreatedAt);
        if (card.CreatedAt.Date != new DateTime(2026, 11, 4) ||
            now < card.CreatedAt || now >= month.EndExclusive ||
            card.Status != CashReconciliationStatus.Open || card.Amount != 2948 ||
            card.ResolvedAmount != 0 || card.FormalizedAmount != 0 || card.PostedFormalizedAmount != 0 ||
            card.ResolvedAt.HasValue || card.LossAllocations.Count != 0 || card.Settlements.Count != 0 ||
            card.ExtraContributions.Count != 0 || !string.IsNullOrWhiteSpace(card.ResponsibleEmployeeName) ||
            !string.IsNullOrWhiteSpace(card.SuspectedEmployeeName) ||
            residual.Count != 0 || repairCheckpoint.Count != 0 ||
            items.Any(item => item.OperationId?.StartsWith(OperationId, StringComparison.Ordinal) == true))
            return KnownDataRepairResult.NotMatched;

        if (contract == null || contract.Id != ContractId ||
            contract.CreditorType != EmployeeDebtCreditorType.Club || contract.CreditorEmployeeId != "@club" ||
            contract.DebtorEmployeeId != "emp_3a6b71d8d38143288f43102200d8f2ca" || contract.FundingMethod != "Безнал")
            return KnownDataRepairResult.NotMatched;
        var loans = cash.Where(item => item.Id == LoanId ||
            (item.Description ?? "").Contains("[club-loan:" + EventId + "]", StringComparison.Ordinal)).ToList();
        if (loans.Count != 1 || loans[0].Id != LoanId || loans[0].Type != CashRecordType.ClubLoanIssued ||
            loans[0].PaymentMethod != "Безнал" || loans[0].Amount != 3000 ||
            !(loans[0].Description ?? "").Contains(ContractId, StringComparison.Ordinal) ||
            !(loans[0].Description ?? "").Contains("[club-loan:" + EventId + "]", StringComparison.Ordinal) ||
            loans[0].CreatedAt.Date != card.CreatedAt.Date || loans[0].CreatedAt >= card.CreatedAt ||
            losses.Any(item => ReferencesCard(item.SourceCode + item.Description + item.Note)) ||
            cash.Any(item => item.Type == CashRecordType.Shortage && ReferencesCard(item.Description ?? "")))
            return KnownDataRepairResult.NotMatched;

        var checkpoint = items.Where(item => item.IsTechnicalEvent &&
                item.Origin == CashReconciliationOrigin.CorrectionCheckpoint && item.CheckpointNumber > 0 &&
                item.CreatedAt >= month.StartInclusive && item.CreatedAt < month.EndExclusive)
            .OrderByDescending(item => item.CheckpointNumber).ThenByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.Id).FirstOrDefault();
        bool acceptedObservation = items.Any(item => item.IsTechnicalEvent &&
            item.Origin == CashReconciliationOrigin.CorrectionCheckpoint && item.ActualAmount == 3069 &&
            item.CreatedAt.Date == card.CreatedAt.Date && item.CreatedAt > card.CreatedAt);
        if (!acceptedObservation || checkpoint == null || checkpoint.CreatedAt > now ||
            card.CheckpointNumber != checkpoint.CheckpointNumber || card.AmountAtCheckpoint != 2948 ||
            checkpoint.CheckpointNumber == long.MaxValue)
            return KnownDataRepairResult.NotMatched;

        // Correct only the carried investigation balance, never the physical bank baseline.
        int correctedCarry = checked(checkpoint.AmountAtCheckpoint + 2948);
        long nextCheckpoint = Math.Max(now.ToUniversalTime().Ticks, checkpoint.CheckpointNumber + 1);
        card.Amount = 0;
        card.ResolvedAmount = 2948;
        card.Status = CashReconciliationStatus.Resolved;
        card.Stage = CashReconciliationStage.Ready;
        card.Resolution = CashReconciliationResolution.InputCorrection;
        card.ResolvedAt = now;
        card.ClosedAtCheckpointNumber = nextCheckpoint;
        card.ResolvedBy = "Система";
        card.ResolutionNote = Explanation;
        items.Add(new CashReconciliationItem
        {
            AccountingSchemaVersion = CashConstitutionEngine.CurrentSchemaVersion,
            Id = ResidualId, InvestigationId = InvestigationId, IsTechnicalEvent = true,
            OperationId = OperationId + ":residual", CreatedAt = now,
            Kind = CashReconciliationKind.CashlessExtra, Origin = CashReconciliationOrigin.CashlessVerification,
            Status = CashReconciliationStatus.Resolved, Resolution = CashReconciliationResolution.OwnerBaseline,
            OriginalAmount = 52, ResolvedAmount = 52, ExpectedAmount = 3017, ProgramExpectedAmount = 3017,
            ActualAmount = 3069, Title = "Необъяснённая разница безнала: +52 сом",
            Note = Explanation, ResolutionNote = Explanation, ResolvedAt = now, ResolvedBy = "Система"
        });
        items.Add(new CashReconciliationItem
        {
            AccountingSchemaVersion = CashConstitutionEngine.CurrentSchemaVersion,
            Id = CheckpointId, InvestigationId = InvestigationId, IsTechnicalEvent = true,
            OperationId = OperationId + ":checkpoint", CreatedAt = now,
            Kind = CashReconciliationKind.Other, Origin = CashReconciliationOrigin.CorrectionCheckpoint,
            Status = CashReconciliationStatus.Resolved, Resolution = CashReconciliationResolution.InputCorrection,
            CheckpointNumber = nextCheckpoint, AmountAtCheckpoint = correctedCarry,
            ExpectedAmount = -1, ActualAmount = -1,
            Title = "Исправление перенесённого итога разбора",
            Note = $"Предыдущий итог разбора: {checkpoint.AmountAtCheckpoint}; новый: {correctedCarry}. " + Explanation,
            ResolutionNote = Explanation, ResolvedAt = now, ResolvedBy = "Система"
        });
        return KnownDataRepairResult.Applied;
    }

    private static bool ReferencesCard(string text) => text.Contains(CardId.ToString(), StringComparison.OrdinalIgnoreCase) ||
        text.Contains(CardId.ToString("N"), StringComparison.OrdinalIgnoreCase) ||
        text.Contains(InvestigationId.ToString(), StringComparison.OrdinalIgnoreCase) ||
        text.Contains(InvestigationId.ToString("N"), StringComparison.OrdinalIgnoreCase);

    private static Guid StableId(string text) => new(SHA256.HashData(Encoding.UTF8.GetBytes(text)).AsSpan(0, 16));
}
