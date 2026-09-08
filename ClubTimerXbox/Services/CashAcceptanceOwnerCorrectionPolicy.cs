using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public static class CashAcceptanceOwnerCorrectionPolicy
    {
        public static string OperationKey(CashAcceptanceItem item) =>
            !string.IsNullOrWhiteSpace(item.RootAcceptanceKey) ? item.RootAcceptanceKey.Trim() :
            !string.IsNullOrWhiteSpace(item.AcceptanceKey) ? item.AcceptanceKey.Trim() : item.Id.ToString("N");

        public static Guid InvestigationId(CashAcceptanceItem item)
        {
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(
                $"cash-acceptance:{OperationKey(item).ToLowerInvariant()}"));
            return new Guid(hash.AsSpan(0, 16));
        }

        public static string ReviewRevision(CashAcceptanceItem item)
        {
            if (!item.IsProvisional && !string.IsNullOrWhiteSpace(item.FinalizedReviewRevision))
                return item.FinalizedReviewRevision;
            var pending = item.PendingCashlessVerification;
            string value = FormattableString.Invariant(
                $"{item.Id}:{CashAcceptanceTimelinePolicy.GetObservationTime(item).Ticks}:{item.ExpectedCashAmount}:{item.ActualCashAmount}:{item.ResponsibleEmployeeName}:{pending?.CommandId}:{pending?.ObservedAt.Ticks}:{pending?.ExpectedAmount}:{pending?.ActualAmount}:{pending?.SuspectedEmployeeName}");
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..24];
        }

        public static void Validate(CashAcceptanceItem item, string acceptanceId, string revision)
        {
            if (!Guid.TryParse(acceptanceId, out Guid id) || id != item.Id ||
                !ReviewRevision(item).Equals(revision, StringComparison.Ordinal))
                throw new InvalidOperationException("Приёмка изменилась. Обновите сверку и проверьте последние факты.");
            if (item.IsProvisional && item.PendingCashlessVerification == null)
                throw new InvalidOperationException("Сначала сверьте безнал для этой приёмки.");
        }

        public static void ValidateFinalizedReview(CashAcceptanceItem item, DateTime? latestCashlessVerificationAt)
        {
            if (!item.IsProvisional && item.FinalizedAt.HasValue &&
                latestCashlessVerificationAt > item.FinalizedAt.Value)
                throw new InvalidOperationException("После завершения приёмки безнал уже пересверен. Обновите сверку перед корректировкой.");
        }

        // The caller persists this entire mutation atomically, including the correction marker.
        public static CashAccountingResult Apply(
            List<CashReconciliationItem> items, CashAcceptanceItem acceptance,
            DateTime from, DateTime to, DateTime now, string commandId,
            int actualCash, int actualCashless)
        {
            if (CashConstitutionEngine.HasAppliedOperation(items, commandId))
                return new CashAccountingResult();
            DateTime observedAt = CashAcceptanceTimelinePolicy.GetObservationTime(acceptance);
            if (observedAt < from || observedAt >= to)
                throw new InvalidOperationException("Приёмка относится к другому расчётному месяцу. Дождитесь завершения перехода месяца на ПК.");
            var pending = acceptance.PendingCashlessVerification ??
                throw new InvalidOperationException("Нет связанной сверки безнала.");
            string key = OperationKey(acceptance);
            Guid investigation = InvestigationId(acceptance);
            CashConstitutionEngine.RecordCashAcceptance(
                items, from, to, now, acceptance.CheckedByEmployeeName,
                acceptance.ResponsibleEmployeeName, acceptance.ExpectedCashAmount,
                acceptance.ActualCashAmount, acceptance.Note, $"{key}:ledger",
                CashAcceptanceTimelinePolicy.GetObservationTime(acceptance), true, investigation);
            var verification = CashConstitutionEngine.RecordCashlessVerification(
                items, from, to, pending.ObservedAt == default ? observedAt : pending.ObservedAt, pending.ExpectedAmount,
                pending.ActualAmount, pending.SuspectedEmployeeName, pending.Note,
                $"{key}:cashless-ledger", pending.ProgramExpectedAmount, investigation);
            var correction = CashConstitutionEngine.ApplyCorrection(
                items, from, to, now, now.ToUniversalTime().Ticks, commandId,
                actualCash, actualCashless);
            return new CashAccountingResult
            {
                PairedAmount = verification.PairedAmount,
                SettledAmount = verification.SettledAmount + correction.SettledAmount,
                Assignments = correction.Assignments,
                Breakdown = correction.Breakdown,
                RecommendationTotal = correction.RecommendationTotal,
                CheckpointNumber = correction.CheckpointNumber
            };
        }
    }
}
