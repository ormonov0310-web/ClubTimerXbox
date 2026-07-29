using System;
using System.Collections.Generic;
using System.Linq;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public sealed class CashAccountingAssignment
    {
        public Guid AllocationId { get; init; }

        public string EmployeeName { get; init; } = "";

        public int Amount { get; init; }

        public Guid ReconciliationId { get; init; }

        public string Reason { get; init; } = "";
    }

    public sealed class CashAccountingResult
    {
        public CashReconciliationItem? Item { get; init; }

        public int EventAmount { get; init; }

        public int EventRemainingAmount { get; init; }

        public int PairedAmount { get; init; }

        public int SettledAmount { get; init; }

        public int Breakdown { get; init; }

        public int RecommendationTotal { get; init; }

        public long CheckpointNumber { get; init; }

        public IReadOnlyList<CashAccountingAssignment> Assignments { get; init; } =
            Array.Empty<CashAccountingAssignment>();
    }

    public sealed class CashMonthCloseResult
    {
        public int ClosingBreakdown { get; init; }

        public int ArchivedExtra { get; init; }

        public bool IsDeferred { get; init; }

        public IReadOnlyList<CashAccountingAssignment> Assignments { get; init; } =
            Array.Empty<CashAccountingAssignment>();
    }

    public static class CashConstitutionEngine
    {
        public static CashAccountingResult RecordCashAcceptance(
            IList<CashReconciliationItem> items,
            DateTime fromInclusive,
            DateTime toExclusive,
            DateTime now,
            string checkedByEmployeeName,
            string responsibleEmployeeName,
            int expectedAmount,
            int actualAmount,
            string note)
        {
            Normalize(items, fromInclusive, toExclusive);
            Guid investigationId = Guid.NewGuid();

            int difference = actualAmount - expectedAmount;
            int eventAmount = Math.Abs(difference);
            int paired = 0;
            CashReconciliationItem? eventItem = null;

            if (difference >= 0)
            {
                int available = difference;
                paired += SettleAwaitingShortages(
                    items,
                    fromInclusive,
                    toExclusive,
                    CashReconciliationKind.CashlessShortage,
                    ref available,
                    now,
                    CashReconciliationResolution.PairedTender,
                    "Закрыто связанной приёмкой наличных."
                );

                MarkAwaitingShortagesReady(
                    items,
                    fromInclusive,
                    toExclusive,
                    CashReconciliationKind.CashlessShortage
                );

                if (available > 0)
                {
                    eventItem = AddExtraContribution(
                        items,
                        fromInclusive,
                        toExclusive,
                        now,
                        CashReconciliationKind.CashExtra,
                        CashReconciliationOrigin.CashAcceptance,
                        CashReconciliationStage.AwaitingCashlessVerification,
                        available,
                        expectedAmount,
                        actualAmount,
                        checkedByEmployeeName,
                        note,
                        investigationId
                    );
                }
            }
            else
            {
                MarkAwaitingShortagesReady(
                    items,
                    fromInclusive,
                    toExclusive,
                    CashReconciliationKind.CashlessShortage
                );

                eventItem = AddShortage(
                    items,
                    now,
                    CashReconciliationKind.CashShortage,
                    CashReconciliationOrigin.CashAcceptance,
                    CashReconciliationStage.AwaitingCashlessVerification,
                    CashResponsibilityLevel.Confirmed,
                    eventAmount,
                    expectedAmount,
                    actualAmount,
                    checkedByEmployeeName,
                    responsibleEmployeeName,
                    "",
                    note,
                    investigationId
                );
            }

            int settled = SettleEligibleExtra(items, fromInclusive, toExclusive, now);
            var result = BuildResult(
                items,
                fromInclusive,
                toExclusive,
                eventItem,
                eventAmount,
                Math.Max(0, eventAmount - paired),
                paired,
                settled,
                Array.Empty<CashAccountingAssignment>()
            );
            AddTechnicalEvent(
                items,
                now,
                CashReconciliationOrigin.CashAcceptance,
                investigationId,
                Array.Empty<Guid>(),
                expectedAmount,
                actualAmount,
                note
            );
            return result;
        }

        public static CashAccountingResult RecordCashlessVerification(
            IList<CashReconciliationItem> items,
            DateTime fromInclusive,
            DateTime toExclusive,
            DateTime now,
            int expectedAmount,
            int actualAmount,
            string suspectedEmployeeName,
            string note,
            string operationId = "")
        {
            Normalize(items, fromInclusive, toExclusive);
            if (HasAppliedOperation(items, operationId))
            {
                return BuildResult(
                    items,
                    fromInclusive,
                    toExclusive,
                    null,
                    0,
                    0,
                    0,
                    0,
                    Array.Empty<CashAccountingAssignment>()
                );
            }

            if (HasCurrentCashlessVerification(
                    items,
                    fromInclusive,
                    toExclusive,
                    expectedAmount,
                    actualAmount))
            {
                return BuildResult(
                    items,
                    fromInclusive,
                    toExclusive,
                    null,
                    0,
                    0,
                    0,
                    0,
                    Array.Empty<CashAccountingAssignment>()
                );
            }

            var linkedInvestigationIds = GetPendingCashInvestigationIds(
                items,
                fromInclusive,
                toExclusive
            );
            Guid investigationId = linkedInvestigationIds.Count == 1
                ? linkedInvestigationIds[0]
                : Guid.NewGuid();

            int difference = actualAmount - expectedAmount;
            int eventAmount = Math.Abs(difference);
            int availableExtra = Math.Max(0, difference);
            int availableShortage = Math.Max(0, -difference);
            int paired = 0;
            CashReconciliationItem? eventItem = null;

            if (availableExtra > 0)
            {
                paired += SettleAwaitingShortages(
                    items,
                    fromInclusive,
                    toExclusive,
                    CashReconciliationKind.CashShortage,
                    ref availableExtra,
                    now,
                    CashReconciliationResolution.PairedTender,
                    "Закрыто связанной сверкой безнала."
                );
            }

            if (availableShortage > 0)
            {
                paired += SettleAwaitingExtraContributions(
                    items,
                    fromInclusive,
                    toExclusive,
                    CashReconciliationKind.CashExtra,
                    ref availableShortage,
                    now,
                    "Закрыто связанной сверкой безнала."
                );
            }

            MarkAwaitingShortagesReady(
                items,
                fromInclusive,
                toExclusive,
                CashReconciliationKind.CashShortage
            );
            MarkAwaitingExtraContributionsReady(
                items,
                fromInclusive,
                toExclusive,
                CashReconciliationKind.CashExtra
            );

            if (availableExtra > 0)
            {
                eventItem = AddExtraContribution(
                    items,
                    fromInclusive,
                    toExclusive,
                    now,
                    CashReconciliationKind.CashlessExtra,
                    CashReconciliationOrigin.CashlessVerification,
                    CashReconciliationStage.Ready,
                    availableExtra,
                    expectedAmount,
                    actualAmount,
                    "Владелец",
                    note,
                    investigationId
                );
            }
            else if (availableShortage > 0)
            {
                eventItem = AddShortage(
                    items,
                    now,
                    CashReconciliationKind.CashlessShortage,
                    CashReconciliationOrigin.CashlessVerification,
                    CashReconciliationStage.AwaitingCashAcceptance,
                    string.IsNullOrWhiteSpace(suspectedEmployeeName)
                        ? CashResponsibilityLevel.Unknown
                        : CashResponsibilityLevel.Suspected,
                    availableShortage,
                    expectedAmount,
                    actualAmount,
                    "Владелец",
                    "",
                    suspectedEmployeeName,
                    note,
                    investigationId
                );
            }

            int settled = SettleEligibleExtra(items, fromInclusive, toExclusive, now);
            var assignments = FormalizeReadyConfirmed(items, fromInclusive, toExclusive, now);

            var result = BuildResult(
                items,
                fromInclusive,
                toExclusive,
                eventItem,
                eventAmount,
                Math.Max(0, eventAmount - paired),
                paired,
                settled,
                assignments
            );
            AddTechnicalEvent(
                items,
                now,
                CashReconciliationOrigin.CashlessVerification,
                investigationId,
                linkedInvestigationIds,
                expectedAmount,
                actualAmount,
                note,
                operationId
            );
            return result;
        }

        public static CashAccountingResult ApplyCorrection(
            IList<CashReconciliationItem> items,
            DateTime fromInclusive,
            DateTime toExclusive,
            DateTime now,
            long checkpointNumber,
            string operationId = "",
            int? actualCashAtCheckpoint = null,
            int? actualCashlessAtCheckpoint = null)
        {
            Normalize(items, fromInclusive, toExclusive);
            if (HasAppliedOperation(items, operationId))
            {
                return BuildResult(
                    items,
                    fromInclusive,
                    toExclusive,
                    null,
                    0,
                    0,
                    0,
                    0,
                    Array.Empty<CashAccountingAssignment>()
                );
            }

            MarkAwaitingShortagesReady(
                items,
                fromInclusive,
                toExclusive,
                CashReconciliationKind.CashShortage
            );
            MarkAwaitingShortagesReady(
                items,
                fromInclusive,
                toExclusive,
                CashReconciliationKind.CashlessShortage
            );
            MarkAwaitingExtraContributionsReady(
                items,
                fromInclusive,
                toExclusive,
                CashReconciliationKind.CashExtra
            );
            MarkAwaitingExtraContributionsReady(
                items,
                fromInclusive,
                toExclusive,
                CashReconciliationKind.CashlessExtra
            );

            int settled = SettleEligibleExtra(items, fromInclusive, toExclusive, now);
            var assignments = FormalizeReadyConfirmed(items, fromInclusive, toExclusive, now)
                .Concat(FormalizeReadySuspected(items, fromInclusive, toExclusive, now))
                .ToList();

            if (checkpointNumber > 0)
            {
                foreach (var item in CurrentOpen(items, fromInclusive, toExclusive))
                {
                    item.CheckpointNumber = checkpointNumber;
                    item.AmountAtCheckpoint = item.Amount;
                }

                int checkpointBreakdown = GetBreakdown(
                    items,
                    fromInclusive,
                    toExclusive
                );
                items.Add(new CashReconciliationItem
                {
                    AccountingSchemaVersion = 3,
                    Id = Guid.NewGuid(),
                    InvestigationId = Guid.NewGuid(),
                    IsTechnicalEvent = true,
                    OperationId = operationId.Trim(),
                    CreatedAt = now,
                    Kind = CashReconciliationKind.Other,
                    Origin = CashReconciliationOrigin.CorrectionCheckpoint,
                    Status = CashReconciliationStatus.Resolved,
                    Stage = CashReconciliationStage.Ready,
                    Resolution = CashReconciliationResolution.InputCorrection,
                    CheckpointNumber = checkpointNumber,
                    AmountAtCheckpoint = checkpointBreakdown,
                    ExpectedAmount = actualCashAtCheckpoint ?? -1,
                    ActualAmount = actualCashlessAtCheckpoint ?? -1,
                    ResolvedAt = now,
                    ResolvedBy = "Система",
                    ResolutionNote = "Снимок разбора итоговой корректировки."
                });
            }

            return BuildResult(
                items,
                fromInclusive,
                toExclusive,
                null,
                0,
                0,
                0,
                settled,
                assignments,
                checkpointNumber
            );
        }

        public static CashAccountingResult RecordRawDifference(
            IList<CashReconciliationItem> items,
            DateTime fromInclusive,
            DateTime toExclusive,
            DateTime now,
            int difference,
            int expectedAmount,
            int actualAmount,
            string responsibleEmployeeName,
            string suspectedEmployeeName,
            string note)
        {
            Normalize(items, fromInclusive, toExclusive);

            CashReconciliationItem? item = null;
            if (difference > 0)
            {
                item = AddExtraContribution(
                    items,
                    fromInclusive,
                    toExclusive,
                    now,
                    CashReconciliationKind.CashlessExtra,
                    CashReconciliationOrigin.BalanceRawDifference,
                    CashReconciliationStage.Ready,
                    difference,
                    expectedAmount,
                    actualAmount,
                    "Владелец",
                    note
                );
            }
            else if (difference < 0)
            {
                var level = !string.IsNullOrWhiteSpace(responsibleEmployeeName)
                    ? CashResponsibilityLevel.Confirmed
                    : !string.IsNullOrWhiteSpace(suspectedEmployeeName)
                        ? CashResponsibilityLevel.Suspected
                        : CashResponsibilityLevel.Unknown;
                item = AddShortage(
                    items,
                    now,
                    CashReconciliationKind.CashlessShortage,
                    CashReconciliationOrigin.BalanceRawDifference,
                    CashReconciliationStage.Ready,
                    level,
                    Math.Abs(difference),
                    expectedAmount,
                    actualAmount,
                    "Владелец",
                    responsibleEmployeeName,
                    suspectedEmployeeName,
                    note
                );
            }

            int settled = SettleEligibleExtra(items, fromInclusive, toExclusive, now);
            return BuildResult(
                items,
                fromInclusive,
                toExclusive,
                item,
                Math.Abs(difference),
                Math.Abs(difference),
                0,
                settled,
                Array.Empty<CashAccountingAssignment>()
            );
        }

        public static CashAccountingResult ApplyManualLoss(
            IList<CashReconciliationItem> items,
            DateTime fromInclusive,
            DateTime toExclusive,
            DateTime now,
            string employeeName,
            int amount,
            string operationId = "")
        {
            Normalize(items, fromInclusive, toExclusive);
            if (HasAppliedOperation(items, operationId))
            {
                return BuildResult(
                    items,
                    fromInclusive,
                    toExclusive,
                    null,
                    0,
                    0,
                    0,
                    0,
                    Array.Empty<CashAccountingAssignment>()
                );
            }

            int remaining = Math.Max(0, amount);
            var assignments = new List<CashAccountingAssignment>();

            foreach (var shortage in CurrentOpen(items, fromInclusive, toExclusive)
                .Where(IsShortage)
                .OrderBy(ManualLossPriority)
                .ThenBy(item => item.CreatedAt)
                .ThenBy(item => item.Id))
            {
                if (remaining <= 0)
                    break;

                int used = Math.Min(shortage.Amount, remaining);
                var allocation = Formalize(
                    shortage,
                    used,
                    employeeName,
                    now,
                    CashLossAllocationSource.OwnerManual,
                    "Оформлено владельцем."
                );
                assignments.Add(new CashAccountingAssignment
                {
                    AllocationId = allocation?.Id ?? Guid.Empty,
                    EmployeeName = employeeName,
                    Amount = used,
                    ReconciliationId = shortage.Id,
                    Reason = "Ручной штраф за потери"
                });
                remaining -= used;
            }

            if (remaining > 0)
            {
                var syntheticShortage = AddShortage(
                    items,
                    now,
                    CashReconciliationKind.CashShortage,
                    CashReconciliationOrigin.BalanceRawDifference,
                    CashReconciliationStage.Ready,
                    CashResponsibilityLevel.Confirmed,
                    remaining,
                    0,
                    0,
                    "Владелец",
                    employeeName,
                    "",
                    "Ручной штраф назначен сверх открытых потерь."
                );
                var allocation = Formalize(
                    syntheticShortage,
                    remaining,
                    employeeName,
                    now,
                    CashLossAllocationSource.OwnerManual,
                    "Оформлено владельцем сверх открытых потерь."
                );
                AddExtraContribution(
                    items,
                    fromInclusive,
                    toExclusive,
                    now,
                    CashReconciliationKind.CashExtra,
                    CashReconciliationOrigin.OwnerPenaltyOverage,
                    CashReconciliationStage.Ready,
                    remaining,
                    0,
                    remaining,
                    "Владелец",
                    "Излишек создан суммой ручного штрафа сверх открытых потерь."
                );
                assignments.Add(new CashAccountingAssignment
                {
                    AllocationId = allocation?.Id ?? Guid.Empty,
                    EmployeeName = employeeName,
                    Amount = remaining,
                    ReconciliationId = syntheticShortage.Id,
                    Reason = "Ручной штраф сверх потерь"
                });
            }

            var result = BuildResult(
                items,
                fromInclusive,
                toExclusive,
                null,
                amount,
                0,
                0,
                0,
                assignments
            );
            AddTechnicalEvent(
                items,
                now,
                CashReconciliationOrigin.OwnerManualPenalty,
                Guid.NewGuid(),
                Array.Empty<Guid>(),
                0,
                amount,
                $"Ручной штраф за потери: {employeeName.Trim()}, {amount} сом.",
                operationId
            );
            return result;
        }

        public static CashMonthCloseResult CloseMonth(
            IList<CashReconciliationItem> items,
            DateTime monthStart,
            DateTime nextMonthStart,
            DateTime now,
            IReadOnlyDictionary<string, double> workedHours)
        {
            Normalize(items, monthStart, nextMonthStart);
            string operationId = $"month-close:{monthStart:yyyy-MM}";
            var existingMarker = items.FirstOrDefault(item =>
                item.IsTechnicalEvent &&
                item.Origin == CashReconciliationOrigin.MonthClose &&
                item.OperationId.Equals(operationId, StringComparison.Ordinal));
            if (existingMarker != null)
            {
                return new CashMonthCloseResult
                {
                    ClosingBreakdown = existingMarker.ExpectedAmount,
                    ArchivedExtra = existingMarker.ActualAmount,
                    Assignments = (existingMarker.LossAllocations ??
                        new List<CashLossAllocation>())
                        .Select(allocation => new CashAccountingAssignment
                        {
                            AllocationId = allocation.Id,
                            EmployeeName = allocation.EmployeeName,
                            Amount = allocation.Amount,
                            ReconciliationId = existingMarker.Id,
                            Reason = allocation.Reason
                        })
                        .ToList()
                };
            }

            SettleAllAtMonthClose(items, monthStart, nextMonthStart, now);

            int breakdown = GetBreakdown(items, monthStart, nextMonthStart);
            int archivedExtra = Math.Max(0, breakdown);
            if (breakdown < 0 && workedHours.Values.All(hours => hours <= 0))
            {
                return new CashMonthCloseResult
                {
                    ClosingBreakdown = breakdown,
                    IsDeferred = true
                };
            }

            var assignments = breakdown < 0
                ? DistributeByHours(Math.Abs(breakdown), workedHours)
                : new List<CashAccountingAssignment>();

            foreach (var item in CurrentOpen(items, monthStart, nextMonthStart).ToList())
            {
                if (item.Amount > 0)
                {
                    if (IsShortage(item))
                    {
                        item.FormalizedAmount += item.Amount;
                        item.PostedFormalizedAmount = item.FormalizedAmount;
                    }
                    else
                    {
                        item.ResolvedAmount += item.Amount;
                        foreach (var contribution in item.ExtraContributions)
                        {
                            contribution.ResolvedAmount += contribution.Amount;
                            contribution.Amount = 0;
                        }
                    }
                }

                item.Amount = 0;
                item.Status = CashReconciliationStatus.Resolved;
                item.Stage = CashReconciliationStage.Ready;
                item.Resolution = CashReconciliationResolution.MonthClosed;
                item.ResolvedAt = now;
                item.ResolvedBy = "Система";
                item.ResolutionNote = breakdown < 0
                    ? "Остаток месяца распределён пропорционально рабочим часам."
                    : "Положительный остаток месяца архивирован как неизвестный межклубный перевод.";
                SyncExtra(item);
            }

            var marker = new CashReconciliationItem
            {
                AccountingSchemaVersion = 3,
                Id = Guid.NewGuid(),
                InvestigationId = Guid.NewGuid(),
                IsTechnicalEvent = true,
                OperationId = operationId,
                CreatedAt = now,
                Kind = CashReconciliationKind.Other,
                Origin = CashReconciliationOrigin.MonthClose,
                Status = CashReconciliationStatus.Resolved,
                Stage = CashReconciliationStage.Ready,
                Resolution = CashReconciliationResolution.MonthClosed,
                ExpectedAmount = breakdown,
                ActualAmount = archivedExtra,
                ResolvedAt = now,
                ResolvedBy = "Система",
                ResolutionNote = "Неизменяемый акт закрытия месяца.",
                LossAllocations = assignments
                    .Select(assignment => new CashLossAllocation
                    {
                        Id = assignment.AllocationId == Guid.Empty
                            ? Guid.NewGuid()
                            : assignment.AllocationId,
                        CreatedAt = now,
                        EmployeeName = assignment.EmployeeName,
                        Amount = assignment.Amount,
                        PostedAmount = assignment.Amount,
                        Source = CashLossAllocationSource.MonthClose,
                        Reason = assignment.Reason
                    })
                    .ToList()
            };
            items.Add(marker);
            var persistedAssignments = marker.LossAllocations
                .Select(allocation => new CashAccountingAssignment
                {
                    AllocationId = allocation.Id,
                    EmployeeName = allocation.EmployeeName,
                    Amount = allocation.Amount,
                    ReconciliationId = marker.Id,
                    Reason = allocation.Reason
                })
                .ToList();

            return new CashMonthCloseResult
            {
                ClosingBreakdown = breakdown,
                ArchivedExtra = archivedExtra,
                Assignments = persistedAssignments
            };
        }

        public static int GetBreakdown(
            IEnumerable<CashReconciliationItem> items,
            DateTime fromInclusive,
            DateTime toExclusive)
        {
            int extras = CurrentOpen(items, fromInclusive, toExclusive)
                .Where(IsExtra)
                .Sum(item => item.Amount);
            int shortages = CurrentOpen(items, fromInclusive, toExclusive)
                .Where(IsShortage)
                .Sum(item => item.Amount);

            return extras - shortages;
        }

        public static int GetRecommendationTotal(
            IEnumerable<CashReconciliationItem> items,
            DateTime fromInclusive,
            DateTime toExclusive)
        {
            return CurrentOpen(items, fromInclusive, toExclusive)
                .Where(item =>
                    IsShortage(item) &&
                    item.ResponsibilityLevel != CashResponsibilityLevel.Unknown)
                .Sum(item => item.Amount);
        }

        public static int CalculateUnrepresentedDifference(
            int observedDifference,
            int alreadyFormalizedLosses,
            int representedCycleDifference,
            int checkpointBreakdown = 0)
        {
            int targetOpenBreakdown =
                checkpointBreakdown +
                observedDifference +
                Math.Max(0, alreadyFormalizedLosses);

            return targetOpenBreakdown - representedCycleDifference;
        }

        public static int GetLatestCheckpointBreakdown(
            IEnumerable<CashReconciliationItem> items,
            DateTime fromInclusive,
            DateTime toExclusive)
        {
            var marker = items
                .Where(item =>
                    item.IsTechnicalEvent &&
                    item.Origin == CashReconciliationOrigin.CorrectionCheckpoint &&
                    item.CheckpointNumber > 0 &&
                    item.CreatedAt >= fromInclusive &&
                    item.CreatedAt < toExclusive)
                .OrderByDescending(item => item.CheckpointNumber)
                .ThenByDescending(item => item.CreatedAt)
                .ThenByDescending(item => item.Id)
                .FirstOrDefault();
            if (marker != null)
                return marker.AmountAtCheckpoint;

            long checkpointNumber = items
                .Where(item =>
                    !item.IsTechnicalEvent &&
                    item.CreatedAt >= fromInclusive &&
                    item.CreatedAt < toExclusive)
                .Select(item => item.CheckpointNumber)
                .DefaultIfEmpty(0)
                .Max();
            if (checkpointNumber <= 0)
                return 0;

            return items
                .Where(item =>
                    !item.IsTechnicalEvent &&
                    item.CreatedAt >= fromInclusive &&
                    item.CreatedAt < toExclusive &&
                    item.CheckpointNumber == checkpointNumber)
                .Sum(item =>
                    IsExtra(item)
                        ? Math.Max(0, item.AmountAtCheckpoint)
                        : IsShortage(item)
                            ? -Math.Max(0, item.AmountAtCheckpoint)
                            : 0);
        }

        public static bool HasCurrentCashlessVerification(
            IEnumerable<CashReconciliationItem> items,
            DateTime fromInclusive,
            DateTime toExclusive,
            int expectedAmount,
            int actualAmount)
        {
            var latestVerification = items
                .Where(item =>
                    item.CreatedAt >= fromInclusive &&
                    item.CreatedAt < toExclusive &&
                    item.Origin == CashReconciliationOrigin.CashlessVerification &&
                    (item.IsTechnicalEvent ||
                     item.Kind == CashReconciliationKind.CashlessShortage ||
                     item.Kind == CashReconciliationKind.CashlessExtra) &&
                    item.ExpectedAmount == expectedAmount &&
                    item.ActualAmount == actualAmount)
                .OrderByDescending(item => item.CreatedAt)
                .ThenByDescending(item => item.Id)
                .FirstOrDefault();

            if (latestVerification == null)
                return false;

            return !items.Any(item =>
                item.CreatedAt > latestVerification.CreatedAt &&
                item.CreatedAt < toExclusive &&
                item.Origin == CashReconciliationOrigin.CashAcceptance &&
                (item.IsTechnicalEvent ||
                 item.Kind == CashReconciliationKind.CashShortage ||
                 item.Kind == CashReconciliationKind.CashExtra));
        }

        public static bool HasAppliedOperation(
            IEnumerable<CashReconciliationItem> items,
            string operationId)
        {
            operationId = operationId.Trim();
            return !string.IsNullOrWhiteSpace(operationId) &&
                   items.Any(item =>
                       item.IsTechnicalEvent &&
                       item.OperationId.Equals(
                           operationId,
                           StringComparison.Ordinal));
        }

        public static void Normalize(
            IList<CashReconciliationItem> items,
            DateTime fromInclusive,
            DateTime toExclusive)
        {
            foreach (var item in items)
            {
                if (item.AccountingSchemaVersion <= 0)
                {
                    item.PostedFormalizedAmount = Math.Max(
                        item.PostedFormalizedAmount,
                        item.FormalizedAmount
                    );
                    item.AccountingSchemaVersion = 2;
                }

                item.ExtraContributions ??= new List<CashExtraContribution>();
                item.LossAllocations ??= new List<CashLossAllocation>();
                item.LinkedInvestigationIds ??= new List<Guid>();
                if (item.InvestigationId == Guid.Empty)
                    item.InvestigationId = item.Id == Guid.Empty ? Guid.NewGuid() : item.Id;

                if (item.AccountingSchemaVersion < 3)
                {
                    if (item.CheckpointNumber > 0 &&
                        item.AmountAtCheckpoint == 0 &&
                        item.Status == CashReconciliationStatus.Open &&
                        item.Amount > 0)
                    {
                        item.AmountAtCheckpoint = item.Amount;
                    }

                    if (item.FormalizedAmount > 0 &&
                        item.LossAllocations.Count == 0 &&
                        !string.IsNullOrWhiteSpace(item.ResponsibleEmployeeName))
                    {
                        item.LossAllocations.Add(new CashLossAllocation
                        {
                            CreatedAt = item.ResolvedAt ?? item.CreatedAt,
                            EmployeeName = item.ResponsibleEmployeeName.Trim(),
                            Amount = item.FormalizedAmount,
                            PostedAmount = Math.Min(
                                item.FormalizedAmount,
                                Math.Max(0, item.PostedFormalizedAmount)
                            ),
                            Source = CashLossAllocationSource.Legacy,
                            Reason = string.IsNullOrWhiteSpace(item.ResolutionNote)
                                ? "Историческая оформленная потеря"
                                : item.ResolutionNote
                        });
                    }

                    item.AccountingSchemaVersion = 3;
                }

                if (item.ResponsibilityLevel == CashResponsibilityLevel.Unknown)
                {
                    if (!string.IsNullOrWhiteSpace(item.ResponsibleEmployeeName))
                        item.ResponsibilityLevel = CashResponsibilityLevel.Confirmed;
                    else if (!string.IsNullOrWhiteSpace(item.SuspectedEmployeeName))
                        item.ResponsibilityLevel = CashResponsibilityLevel.Suspected;
                }

                if (IsExtra(item) &&
                    item.Status == CashReconciliationStatus.Open &&
                    item.Amount > 0 &&
                    item.ExtraContributions.Count == 0)
                {
                    int resolvedAmount = Math.Max(0, item.ResolvedAmount);
                    item.ExtraContributions.Add(new CashExtraContribution
                    {
                        InvestigationId = item.InvestigationId,
                        CreatedAt = item.CreatedAt,
                        Kind = item.Kind,
                        Origin = item.Origin,
                        Stage = item.Stage,
                        OriginalAmount = Math.Max(
                            item.OriginalAmount,
                            item.Amount + resolvedAmount
                        ),
                        Amount = item.Amount,
                        ResolvedAmount = resolvedAmount
                    });
                }
            }

            MergeOpenExtras(items, fromInclusive, toExclusive);
        }

        private static List<Guid> GetPendingCashInvestigationIds(
            IEnumerable<CashReconciliationItem> items,
            DateTime fromInclusive,
            DateTime toExclusive)
        {
            DateTime latestVerificationAt = items
                .Where(item =>
                    item.IsTechnicalEvent &&
                    item.Origin == CashReconciliationOrigin.CashlessVerification &&
                    item.CreatedAt >= fromInclusive &&
                    item.CreatedAt < toExclusive)
                .Select(item => item.CreatedAt)
                .DefaultIfEmpty(fromInclusive)
                .Max();

            var ids = items
                .Where(item =>
                    item.IsTechnicalEvent &&
                    item.Origin == CashReconciliationOrigin.CashAcceptance &&
                    item.CreatedAt > latestVerificationAt &&
                    item.CreatedAt < toExclusive)
                .Select(item => item.InvestigationId)
                .Concat(CurrentOpen(items, fromInclusive, toExclusive)
                    .Where(item =>
                        item.Kind == CashReconciliationKind.CashShortage &&
                        item.Stage == CashReconciliationStage.AwaitingCashlessVerification)
                    .Select(item => item.InvestigationId))
                .Concat(CurrentOpen(items, fromInclusive, toExclusive)
                    .Where(IsExtra)
                    .SelectMany(item => item.ExtraContributions)
                    .Where(item =>
                        item.Kind == CashReconciliationKind.CashExtra &&
                        item.Stage == CashReconciliationStage.AwaitingCashlessVerification &&
                        item.Amount > 0)
                    .Select(item => item.InvestigationId))
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToList();

            return ids;
        }

        private static void AddTechnicalEvent(
            ICollection<CashReconciliationItem> items,
            DateTime now,
            CashReconciliationOrigin origin,
            Guid investigationId,
            IEnumerable<Guid> linkedInvestigationIds,
            int expectedAmount,
            int actualAmount,
            string note,
            string operationId = "")
        {
            items.Add(new CashReconciliationItem
            {
                AccountingSchemaVersion = 3,
                Id = Guid.NewGuid(),
                InvestigationId = investigationId,
                LinkedInvestigationIds = linkedInvestigationIds
                    .Where(id => id != Guid.Empty)
                    .Distinct()
                    .ToList(),
                IsTechnicalEvent = true,
                CreatedAt = now,
                Kind = CashReconciliationKind.Other,
                Origin = origin,
                Status = CashReconciliationStatus.Resolved,
                Stage = CashReconciliationStage.Ready,
                Resolution = CashReconciliationResolution.InputCorrection,
                ExpectedAmount = expectedAmount,
                ActualAmount = actualAmount,
                CheckedByEmployeeName = origin == CashReconciliationOrigin.CashAcceptance
                    ? "Сотрудник"
                    : "Владелец",
                Note = note.Trim(),
                OperationId = operationId.Trim(),
                ResolvedAt = now,
                ResolvedBy = "Система",
                ResolutionNote = "Технический журнал идемпотентности кассы."
            });
        }

        private static CashReconciliationItem AddShortage(
            IList<CashReconciliationItem> items,
            DateTime now,
            CashReconciliationKind kind,
            CashReconciliationOrigin origin,
            CashReconciliationStage stage,
            CashResponsibilityLevel responsibilityLevel,
            int amount,
            int expectedAmount,
            int actualAmount,
            string checkedBy,
            string responsible,
            string suspected,
            string note,
            Guid? investigationId = null)
        {
            var item = new CashReconciliationItem
            {
                AccountingSchemaVersion = 3,
                Id = Guid.NewGuid(),
                InvestigationId = investigationId ?? Guid.NewGuid(),
                CreatedAt = now,
                Kind = kind,
                Origin = origin,
                Status = CashReconciliationStatus.Open,
                Stage = stage,
                ResponsibilityLevel = responsibilityLevel,
                Amount = amount,
                OriginalAmount = amount,
                ExpectedAmount = expectedAmount,
                ActualAmount = actualAmount,
                CheckedByEmployeeName = checkedBy.Trim(),
                ResponsibleEmployeeName = responsible.Trim(),
                SuspectedEmployeeName = suspected.Trim(),
                Title = kind == CashReconciliationKind.CashShortage
                    ? "Недостача наличных"
                    : "Недостача безнала",
                Note = note.Trim()
            };

            items.Add(item);
            return item;
        }

        private static CashReconciliationItem AddExtraContribution(
            IList<CashReconciliationItem> items,
            DateTime fromInclusive,
            DateTime toExclusive,
            DateTime now,
            CashReconciliationKind kind,
            CashReconciliationOrigin origin,
            CashReconciliationStage stage,
            int amount,
            int expectedAmount,
            int actualAmount,
            string checkedBy,
            string note,
            Guid? investigationId = null)
        {
            var pool = CurrentOpen(items, fromInclusive, toExclusive)
                .FirstOrDefault(IsExtra);

            if (pool == null)
            {
                pool = new CashReconciliationItem
                {
                    AccountingSchemaVersion = 3,
                    Id = Guid.NewGuid(),
                    InvestigationId = investigationId ?? Guid.NewGuid(),
                    CreatedAt = now,
                    Kind = kind,
                    Origin = origin,
                    Status = CashReconciliationStatus.Open,
                    Stage = stage,
                    ResponsibilityLevel = CashResponsibilityLevel.Unknown,
                    ExpectedAmount = expectedAmount,
                    ActualAmount = actualAmount,
                    CheckedByEmployeeName = checkedBy.Trim(),
                    Title = "Общий излишек кассы",
                    Note = note.Trim()
                };
                items.Add(pool);
            }
            else if (!string.IsNullOrWhiteSpace(note))
            {
                pool.Note = string.IsNullOrWhiteSpace(pool.Note)
                    ? note.Trim()
                    : $"{pool.Note.Trim()}\n{note.Trim()}";
            }

            pool.ExtraContributions.Add(new CashExtraContribution
            {
                Id = Guid.NewGuid(),
                InvestigationId = investigationId ?? Guid.NewGuid(),
                CreatedAt = now,
                Kind = kind,
                Origin = origin,
                Stage = stage,
                OriginalAmount = amount,
                Amount = amount
            });
            SyncExtra(pool);
            return pool;
        }

        private static int SettleAwaitingShortages(
            IList<CashReconciliationItem> items,
            DateTime fromInclusive,
            DateTime toExclusive,
            CashReconciliationKind shortageKind,
            ref int available,
            DateTime now,
            CashReconciliationResolution resolution,
            string note)
        {
            int initial = available;

            foreach (var shortage in CurrentOpen(items, fromInclusive, toExclusive)
                .Where(item =>
                    item.Kind == shortageKind &&
                    ((shortageKind == CashReconciliationKind.CashShortage &&
                      item.Stage == CashReconciliationStage.AwaitingCashlessVerification) ||
                     (shortageKind == CashReconciliationKind.CashlessShortage &&
                      item.Stage == CashReconciliationStage.AwaitingCashAcceptance)))
                .OrderBy(item => item.CreatedAt)
                .ThenBy(item => item.Id))
            {
                if (available <= 0)
                    break;

                int used = Math.Min(shortage.Amount, available);
                shortage.Amount -= used;
                shortage.ResolvedAmount += used;
                available -= used;

                if (shortage.Amount == 0)
                    Resolve(shortage, now, resolution, note);
            }

            return initial - available;
        }

        private static int SettleAwaitingExtraContributions(
            IList<CashReconciliationItem> items,
            DateTime fromInclusive,
            DateTime toExclusive,
            CashReconciliationKind extraKind,
            ref int availableShortage,
            DateTime now,
            string note)
        {
            int initial = availableShortage;
            var pool = CurrentOpen(items, fromInclusive, toExclusive)
                .FirstOrDefault(IsExtra);

            if (pool == null)
                return 0;

            foreach (var contribution in pool.ExtraContributions
                .Where(item =>
                    item.Kind == extraKind &&
                    item.Stage == CashReconciliationStage.AwaitingCashlessVerification &&
                    item.Amount > 0)
                .OrderBy(item => item.CreatedAt)
                .ThenBy(item => item.Id))
            {
                if (availableShortage <= 0)
                    break;

                int used = Math.Min(contribution.Amount, availableShortage);
                contribution.Amount -= used;
                contribution.ResolvedAmount += used;
                availableShortage -= used;
            }

            SyncExtra(pool);
            if (pool.Amount == 0)
                Resolve(pool, now, CashReconciliationResolution.PairedTender, note);

            return initial - availableShortage;
        }

        private static void MarkAwaitingShortagesReady(
            IEnumerable<CashReconciliationItem> items,
            DateTime fromInclusive,
            DateTime toExclusive,
            CashReconciliationKind kind)
        {
            foreach (var item in CurrentOpen(items, fromInclusive, toExclusive)
                .Where(item => item.Kind == kind))
            {
                item.Stage = CashReconciliationStage.Ready;
            }
        }

        private static void MarkAwaitingExtraContributionsReady(
            IEnumerable<CashReconciliationItem> items,
            DateTime fromInclusive,
            DateTime toExclusive,
            CashReconciliationKind kind)
        {
            foreach (var pool in CurrentOpen(items, fromInclusive, toExclusive).Where(IsExtra))
            {
                foreach (var contribution in pool.ExtraContributions.Where(item => item.Kind == kind))
                    contribution.Stage = CashReconciliationStage.Ready;

                SyncExtra(pool);
            }
        }

        private static int SettleEligibleExtra(
            IList<CashReconciliationItem> items,
            DateTime fromInclusive,
            DateTime toExclusive,
            DateTime now)
        {
            var pool = CurrentOpen(items, fromInclusive, toExclusive).FirstOrDefault(IsExtra);
            if (pool == null)
                return 0;

            int settled = 0;
            var shortages = CurrentOpen(items, fromInclusive, toExclusive)
                .Where(item =>
                    IsShortage(item) &&
                    item.Stage == CashReconciliationStage.Ready)
                .OrderBy(ExtraSettlementPriority)
                .ThenBy(item => item.CreatedAt)
                .ThenBy(item => item.Id)
                .ToList();

            foreach (var shortage in shortages)
            {
                foreach (var contribution in pool.ExtraContributions
                    .Where(item =>
                        item.Stage == CashReconciliationStage.Ready &&
                        item.Amount > 0 &&
                        CanExtraSettleShortage(item, shortage))
                    .OrderBy(item => item.CreatedAt)
                    .ThenBy(item => item.Id))
                {
                    if (shortage.Amount <= 0)
                        break;

                    int used = Math.Min(shortage.Amount, contribution.Amount);
                    shortage.Amount -= used;
                    shortage.ResolvedAmount += used;
                    contribution.Amount -= used;
                    contribution.ResolvedAmount += used;
                    settled += used;

                    if (shortage.Amount == 0)
                    {
                        Resolve(
                            shortage,
                            now,
                            CashReconciliationResolution.ExtraSettlement,
                            "Закрыто свободным излишком по приоритету Конституции кассы."
                        );
                    }
                }
            }

            SyncExtra(pool);
            if (pool.Amount == 0)
            {
                Resolve(
                    pool,
                    now,
                    CashReconciliationResolution.ExtraSettlement,
                    "Излишек полностью выполнил свою миссию."
                );
            }

            return settled;
        }

        private static int SettleAllAtMonthClose(
            IList<CashReconciliationItem> items,
            DateTime fromInclusive,
            DateTime toExclusive,
            DateTime now)
        {
            var pool = CurrentOpen(items, fromInclusive, toExclusive).FirstOrDefault(IsExtra);
            if (pool == null)
                return 0;

            int settled = 0;
            foreach (var shortage in CurrentOpen(items, fromInclusive, toExclusive)
                .Where(IsShortage)
                .OrderBy(item => item.CreatedAt)
                .ThenBy(item => item.Id)
                .ToList())
            {
                foreach (var contribution in pool.ExtraContributions
                    .Where(item => item.Amount > 0)
                    .OrderBy(item => item.CreatedAt)
                    .ThenBy(item => item.Id))
                {
                    if (shortage.Amount <= 0)
                        break;

                    int used = Math.Min(shortage.Amount, contribution.Amount);
                    shortage.Amount -= used;
                    shortage.ResolvedAmount += used;
                    contribution.Amount -= used;
                    contribution.ResolvedAmount += used;
                    settled += used;

                    if (shortage.Amount == 0)
                    {
                        Resolve(
                            shortage,
                            now,
                            CashReconciliationResolution.ExtraSettlement,
                            "Финальный взаимный зачёт перед закрытием месяца."
                        );
                    }
                }
            }

            SyncExtra(pool);
            if (pool.Amount == 0)
            {
                Resolve(
                    pool,
                    now,
                    CashReconciliationResolution.ExtraSettlement,
                    "Финальный взаимный зачёт перед закрытием месяца."
                );
            }

            return settled;
        }

        private static List<CashAccountingAssignment> FormalizeReadyConfirmed(
            IEnumerable<CashReconciliationItem> items,
            DateTime fromInclusive,
            DateTime toExclusive,
            DateTime now)
        {
            return FormalizeReady(
                items,
                fromInclusive,
                toExclusive,
                now,
                CashResponsibilityLevel.Confirmed,
                item => item.ResponsibleEmployeeName,
                CashLossAllocationSource.AutomaticVerification,
                "Подтверждённая потеря после связанной сверки."
            );
        }

        private static List<CashAccountingAssignment> FormalizeReadySuspected(
            IEnumerable<CashReconciliationItem> items,
            DateTime fromInclusive,
            DateTime toExclusive,
            DateTime now)
        {
            return FormalizeReady(
                items,
                fromInclusive,
                toExclusive,
                now,
                CashResponsibilityLevel.Suspected,
                item => item.SuspectedEmployeeName,
                CashLossAllocationSource.AutomaticCorrection,
                "Рекомендация оформлена итоговой корректировкой."
            );
        }

        private static List<CashAccountingAssignment> FormalizeReady(
            IEnumerable<CashReconciliationItem> items,
            DateTime fromInclusive,
            DateTime toExclusive,
            DateTime now,
            CashResponsibilityLevel level,
            Func<CashReconciliationItem, string> employeeSelector,
            CashLossAllocationSource source,
            string reason)
        {
            var result = new List<CashAccountingAssignment>();

            foreach (var item in CurrentOpen(items, fromInclusive, toExclusive)
                .Where(item =>
                    IsShortage(item) &&
                    item.Stage == CashReconciliationStage.Ready &&
                    item.ResponsibilityLevel == level)
                .OrderBy(item => item.CreatedAt)
                .ThenBy(item => item.Id)
                .ToList())
            {
                string employee = employeeSelector(item).Trim();
                if (string.IsNullOrWhiteSpace(employee) || item.Amount <= 0)
                    continue;

                int amount = item.Amount;
                var allocation = Formalize(
                    item,
                    amount,
                    employee,
                    now,
                    source,
                    reason
                );
                result.Add(new CashAccountingAssignment
                {
                    AllocationId = allocation?.Id ?? Guid.Empty,
                    EmployeeName = employee,
                    Amount = amount,
                    ReconciliationId = item.Id,
                    Reason = reason
                });
            }

            return result;
        }

        private static CashLossAllocation? Formalize(
            CashReconciliationItem item,
            int amount,
            string employeeName,
            DateTime now,
            CashLossAllocationSource source,
            string note)
        {
            amount = Math.Max(0, Math.Min(item.Amount, amount));
            if (amount == 0)
                return null;

            item.Amount -= amount;
            item.FormalizedAmount += amount;
            item.LossAllocations ??= new List<CashLossAllocation>();
            var allocation = new CashLossAllocation
            {
                CreatedAt = now,
                EmployeeName = employeeName.Trim(),
                Amount = amount,
                Source = source,
                Reason = note
            };
            item.LossAllocations.Add(allocation);

            if (item.Amount == 0)
                Resolve(item, now, CashReconciliationResolution.FormalizedLoss, note);

            return allocation;
        }

        private static int ExtraSettlementPriority(CashReconciliationItem item)
        {
            return item.ResponsibilityLevel switch
            {
                CashResponsibilityLevel.Unknown => 0,
                CashResponsibilityLevel.Suspected => 1,
                CashResponsibilityLevel.Confirmed => 2,
                _ => 3
            };
        }

        private static int ManualLossPriority(CashReconciliationItem item)
        {
            return item.ResponsibilityLevel switch
            {
                CashResponsibilityLevel.Confirmed => 0,
                CashResponsibilityLevel.Suspected => 1,
                CashResponsibilityLevel.Unknown => 2,
                _ => 3
            };
        }

        private static bool CanExtraSettleShortage(
            CashExtraContribution contribution,
            CashReconciliationItem shortage)
        {
            if (shortage.ResponsibilityLevel != CashResponsibilityLevel.Confirmed)
                return true;

            return contribution.CreatedAt >= shortage.CreatedAt;
        }

        private static List<CashAccountingAssignment> DistributeByHours(
            int amount,
            IReadOnlyDictionary<string, double> workedHours)
        {
            var eligible = workedHours
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && pair.Value > 0)
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();
            double totalHours = eligible.Sum(pair => pair.Value);

            if (amount <= 0 || totalHours <= 0)
                return new List<CashAccountingAssignment>();

            var shares = eligible
                .Select(pair =>
                {
                    double exact = amount * pair.Value / totalHours;
                    int floor = (int)Math.Floor(exact);
                    return new
                    {
                        EmployeeName = pair.Key.Trim(),
                        Amount = floor,
                        Fraction = exact - floor
                    };
                })
                .ToList();
            int remainder = amount - shares.Sum(item => item.Amount);
            var orderedRemainders = shares
                .OrderByDescending(item => item.Fraction)
                .ThenBy(item => item.EmployeeName, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var result = shares.ToDictionary(
                item => item.EmployeeName,
                item => item.Amount,
                StringComparer.OrdinalIgnoreCase
            );

            for (int index = 0; index < remainder; index++)
                result[orderedRemainders[index % orderedRemainders.Count].EmployeeName]++;

            return result
                .Where(pair => pair.Value > 0)
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair => new CashAccountingAssignment
                {
                    EmployeeName = pair.Key,
                    Amount = pair.Value,
                    Reason = "Закрытие месяца пропорционально рабочим часам"
                })
                .ToList();
        }

        private static void MergeOpenExtras(
            IList<CashReconciliationItem> items,
            DateTime fromInclusive,
            DateTime toExclusive)
        {
            var extras = CurrentOpen(items, fromInclusive, toExclusive)
                .Where(IsExtra)
                .OrderBy(item => item.CreatedAt)
                .ThenBy(item => item.Id)
                .ToList();

            if (extras.Count <= 1)
                return;

            var target = extras[0];
            foreach (var duplicate in extras.Skip(1))
            {
                target.ExtraContributions.AddRange(duplicate.ExtraContributions);
                duplicate.ResolvedAmount += duplicate.Amount;
                duplicate.Amount = 0;
                duplicate.Status = CashReconciliationStatus.Resolved;
                duplicate.Resolution = CashReconciliationResolution.Legacy;
                duplicate.ResolvedAt ??= DateTime.Now;
                duplicate.ResolvedBy = "Система";
                duplicate.ResolutionNote = "Объединено в единую активную карточку излишка.";
            }

            SyncExtra(target);
        }

        private static CashAccountingResult BuildResult(
            IEnumerable<CashReconciliationItem> items,
            DateTime fromInclusive,
            DateTime toExclusive,
            CashReconciliationItem? item,
            int eventAmount,
            int eventRemaining,
            int paired,
            int settled,
            IReadOnlyList<CashAccountingAssignment> assignments,
            long checkpointNumber = 0)
        {
            return new CashAccountingResult
            {
                Item = item,
                EventAmount = eventAmount,
                EventRemainingAmount = eventRemaining,
                PairedAmount = paired,
                SettledAmount = settled,
                Breakdown = GetBreakdown(items, fromInclusive, toExclusive),
                RecommendationTotal = GetRecommendationTotal(
                    items,
                    fromInclusive,
                    toExclusive
                ),
                CheckpointNumber = checkpointNumber,
                Assignments = assignments
            };
        }

        private static IEnumerable<CashReconciliationItem> CurrentOpen(
            IEnumerable<CashReconciliationItem> items,
            DateTime fromInclusive,
            DateTime toExclusive)
        {
            return items.Where(item =>
                item.Status == CashReconciliationStatus.Open &&
                item.CreatedAt >= fromInclusive &&
                item.CreatedAt < toExclusive &&
                item.Amount > 0);
        }

        private static bool IsExtra(CashReconciliationItem item)
        {
            return item.Kind == CashReconciliationKind.CashExtra ||
                   item.Kind == CashReconciliationKind.CashlessExtra;
        }

        private static bool IsShortage(CashReconciliationItem item)
        {
            return item.Kind == CashReconciliationKind.CashShortage ||
                   item.Kind == CashReconciliationKind.CashlessShortage;
        }

        private static void SyncExtra(CashReconciliationItem item)
        {
            if (!IsExtra(item))
                return;

            item.Amount = item.ExtraContributions.Sum(contribution =>
                Math.Max(0, contribution.Amount));
            item.OriginalAmount = Math.Max(
                item.OriginalAmount,
                item.ExtraContributions.Sum(contribution =>
                    Math.Max(0, contribution.OriginalAmount))
            );
            item.ResolvedAmount = item.ExtraContributions.Sum(contribution =>
                Math.Max(0, contribution.ResolvedAmount));
            item.Stage = item.ExtraContributions.Any(contribution =>
                contribution.Amount > 0 &&
                contribution.Stage != CashReconciliationStage.Ready)
                    ? item.ExtraContributions
                        .Where(contribution => contribution.Amount > 0)
                        .Select(contribution => contribution.Stage)
                        .First()
                    : CashReconciliationStage.Ready;
        }

        private static void Resolve(
            CashReconciliationItem item,
            DateTime now,
            CashReconciliationResolution resolution,
            string note)
        {
            item.Amount = 0;
            item.Status = CashReconciliationStatus.Resolved;
            item.Stage = CashReconciliationStage.Ready;
            item.Resolution = resolution;
            item.ResolvedAt = now;
            item.ClosedAtCheckpointNumber = now.Ticks;
            item.ResolvedBy = resolution == CashReconciliationResolution.FormalizedLoss
                ? "Система"
                : "Система";
            item.ResolutionNote = note;
        }
    }
}
