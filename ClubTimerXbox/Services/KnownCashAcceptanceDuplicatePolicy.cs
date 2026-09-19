using System;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public enum KnownDataRepairResult
    {
        NotMatched,
        AlreadyApplied,
        Applied
    }

    public sealed record KnownCashAcceptanceDuplicateSpec(
        Guid AcceptanceId,
        Guid ReconciliationId,
        Guid InvestigationId,
        string RootAcceptanceKey,
        string CheckedByEmployeeName,
        string ResponsibleEmployeeName,
        int IncorrectExpectedAmount,
        int ActualAmount)
    {
        public string OperationId => $"{RootAcceptanceKey}:ledger";

        public int ShortageAmount => IncorrectExpectedAmount - ActualAmount;
    }

    public static class KnownCashAcceptanceDuplicatePolicy
    {
        private const string AcceptanceRepairNote =
            "Технический дубль ожидания налички исправлен обновлением.";

        private const string ReconciliationRepairNote =
            "Повторная недостача отменена: старая приёмка завершилась с опозданием.";

        public static KnownDataRepairResult RepairAcceptance(
            CashAcceptanceItem item,
            KnownCashAcceptanceDuplicateSpec spec)
        {
            if (item.Id != spec.AcceptanceId ||
                !Matches(item.RootAcceptanceKey, spec.RootAcceptanceKey) ||
                !Matches(item.CheckedByEmployeeName, spec.CheckedByEmployeeName) ||
                !Matches(item.ResponsibleEmployeeName, spec.ResponsibleEmployeeName))
            {
                return KnownDataRepairResult.NotMatched;
            }

            if (item.ExpectedCashAmount == spec.ActualAmount &&
                item.ActualCashAmount == spec.ActualAmount &&
                item.Difference == 0 &&
                item.Note.Contains(AcceptanceRepairNote, StringComparison.Ordinal))
            {
                return KnownDataRepairResult.AlreadyApplied;
            }

            if (item.IsProvisional ||
                !item.FinalizedAt.HasValue ||
                item.ExpectedCashAmount != spec.IncorrectExpectedAmount ||
                item.ActualCashAmount != spec.ActualAmount ||
                item.Difference != spec.ActualAmount - spec.IncorrectExpectedAmount ||
                !string.IsNullOrWhiteSpace(item.OwnerCorrectionCommandId) ||
                item.OwnerCorrectionCompletedAt.HasValue)
            {
                return KnownDataRepairResult.NotMatched;
            }

            item.ExpectedCashAmount = spec.ActualAmount;
            item.Difference = 0;
            item.Note = AppendNote(item.Note, AcceptanceRepairNote);
            item.FinalizedReviewRevision = "";
            item.FinalizedReviewRevision =
                CashAcceptanceOwnerCorrectionPolicy.ReviewRevision(item);
            return KnownDataRepairResult.Applied;
        }

        public static KnownDataRepairResult RepairReconciliation(
            CashReconciliationItem item,
            KnownCashAcceptanceDuplicateSpec spec,
            DateTime resolvedAt)
        {
            if (item.Id != spec.ReconciliationId ||
                item.InvestigationId != spec.InvestigationId ||
                !Matches(item.OperationId, spec.OperationId) ||
                !Matches(item.CheckedByEmployeeName, spec.CheckedByEmployeeName) ||
                !Matches(item.ResponsibleEmployeeName, spec.ResponsibleEmployeeName) ||
                item.Kind != CashReconciliationKind.CashShortage ||
                item.Origin != CashReconciliationOrigin.CashAcceptance ||
                item.OriginalAmount != spec.ShortageAmount ||
                item.ExpectedAmount != spec.IncorrectExpectedAmount ||
                item.ActualAmount != spec.ActualAmount ||
                item.ProgramExpectedAmount != spec.IncorrectExpectedAmount)
            {
                return KnownDataRepairResult.NotMatched;
            }

            if (item.Status == CashReconciliationStatus.Resolved &&
                item.Resolution == CashReconciliationResolution.InputCorrection &&
                item.Amount == 0 &&
                item.ResolvedAmount == spec.ShortageAmount &&
                item.FormalizedAmount == 0 &&
                item.PostedFormalizedAmount == 0 &&
                item.ResolutionNote.Equals(
                    ReconciliationRepairNote,
                    StringComparison.Ordinal))
            {
                return KnownDataRepairResult.AlreadyApplied;
            }

            if (item.Status != CashReconciliationStatus.Open ||
                item.Amount != spec.ShortageAmount ||
                item.ResolvedAmount != 0 ||
                item.FormalizedAmount != 0 ||
                item.PostedFormalizedAmount != 0 ||
                item.LossAllocations.Count != 0 ||
                item.Settlements.Count != 0)
            {
                return KnownDataRepairResult.NotMatched;
            }

            item.Amount = 0;
            item.ResolvedAmount = spec.ShortageAmount;
            item.Status = CashReconciliationStatus.Resolved;
            item.Stage = CashReconciliationStage.Ready;
            item.Resolution = CashReconciliationResolution.InputCorrection;
            item.ClosedAtCheckpointNumber = resolvedAt.ToUniversalTime().Ticks;
            item.ResolvedAt = resolvedAt;
            item.ResolvedBy = "Система";
            item.ResolutionNote = ReconciliationRepairNote;
            return KnownDataRepairResult.Applied;
        }

        private static bool Matches(string left, string right)
        {
            return string.Equals(
                left?.Trim(),
                right?.Trim(),
                StringComparison.OrdinalIgnoreCase);
        }

        private static string AppendNote(string current, string note)
        {
            if (string.IsNullOrWhiteSpace(current))
                return note;
            if (current.Contains(note, StringComparison.Ordinal))
                return current;
            return $"{current.Trim()} {note}";
        }
    }
}
