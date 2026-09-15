using System;
using System.Collections.Generic;
using System.Linq;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public enum CashPhysicalBalanceSource
    {
        MonthStart,
        Acceptance,
        Checkpoint
    }

    public sealed class CashPhysicalBalanceCalculation
    {
        public CashPhysicalBalanceSource Source { get; init; }

        public Guid? SourceId { get; init; }

        public DateTime SourceObservedAt { get; init; }

        public DateTime SourceCommittedAt { get; init; }

        public int BaseAmount { get; init; }

        public int CashIncome { get; init; }

        public int CashExpenses { get; init; }

        public int RawAmount => BaseAmount + CashIncome - CashExpenses;

        public int Amount => Math.Max(0, RawAmount);

        public bool HasObservedSource => Source != CashPhysicalBalanceSource.MonthStart;
    }

    public static class CashPhysicalBalancePolicy
    {
        public static CashPhysicalBalanceCalculation Calculate(
            IEnumerable<CashAcceptanceItem> acceptances,
            IEnumerable<CashBalanceCheckpointItem> checkpoints,
            IEnumerable<PaymentRecord> payments,
            IEnumerable<CashRecord> cashRecords,
            DateTime fallbackFromInclusive,
            DateTime toExclusive)
        {
            var latestAcceptance = acceptances
                .Where(item =>
                    !item.IsProvisional &&
                    CashAcceptanceTimelinePolicy.GetCommitTime(item) < toExclusive)
                .OrderByDescending(CashAcceptanceTimelinePolicy.GetCommitTime)
                .FirstOrDefault();
            var latestCheckpoint = checkpoints
                .Where(item => item.CreatedAt < toExclusive)
                .OrderByDescending(item => item.CreatedAt)
                .FirstOrDefault();

            CashPhysicalBalanceSource source;
            Guid? sourceId;
            DateTime observedAt;
            DateTime committedAt;
            int baseAmount;
            bool includeLowerBound;

            if (CashAcceptanceTimelinePolicy.CheckpointWins(
                    latestAcceptance,
                    latestCheckpoint))
            {
                source = CashPhysicalBalanceSource.Checkpoint;
                sourceId = latestCheckpoint!.Id;
                observedAt = latestCheckpoint.CreatedAt;
                committedAt = latestCheckpoint.CreatedAt;
                baseAmount = latestCheckpoint.CashAmount;
                includeLowerBound = false;
            }
            else if (latestAcceptance != null)
            {
                source = CashPhysicalBalanceSource.Acceptance;
                sourceId = latestAcceptance.Id;
                observedAt = CashAcceptanceTimelinePolicy.GetObservationTime(latestAcceptance);
                committedAt = CashAcceptanceTimelinePolicy.GetCommitTime(latestAcceptance);
                baseAmount = latestAcceptance.ActualCashAmount;
                includeLowerBound = false;
            }
            else
            {
                source = CashPhysicalBalanceSource.MonthStart;
                sourceId = null;
                observedAt = fallbackFromInclusive;
                committedAt = fallbackFromInclusive;
                baseAmount = 0;
                includeLowerBound = true;
            }

            bool IsAfterSource(DateTime createdAt)
            {
                return includeLowerBound
                    ? createdAt >= observedAt
                    : createdAt > observedAt;
            }

            int cashIncome = payments
                .Where(record =>
                    IsAfterSource(record.CreatedAt) &&
                    record.CreatedAt < toExclusive)
                .Sum(record => record.CashAmount);
            int cashExpenses = cashRecords
                .Where(record =>
                    IsAfterSource(record.CreatedAt) &&
                    record.CreatedAt < toExclusive &&
                    record.Category.Equals("Расходы", StringComparison.Ordinal) &&
                    record.PaymentMethod.Equals("Наличные", StringComparison.Ordinal))
                .Sum(record => record.Amount);

            return new CashPhysicalBalanceCalculation
            {
                Source = source,
                SourceId = sourceId,
                SourceObservedAt = observedAt,
                SourceCommittedAt = committedAt,
                BaseAmount = baseAmount,
                CashIncome = cashIncome,
                CashExpenses = cashExpenses
            };
        }
    }
}
