using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public static class CashReconciliationService
    {
        public const int AutoResolveLimit = 500;

        private static readonly string FolderPath =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ClubTimerXbox"
            );

        private static readonly string FilePath =
            Path.Combine(FolderPath, "cash_reconciliation.json");

        private static readonly object Gate = new();

        private static readonly List<CashReconciliationItem> _items;

        static CashReconciliationService()
        {
            _items = Load(out bool originMigrated);
            bool constitutionMigrated = false;
            foreach (var item in _items)
            {
                if (item.AccountingSchemaVersion <= 0)
                {
                    item.PostedFormalizedAmount = Math.Max(
                        item.PostedFormalizedAmount,
                        item.FormalizedAmount
                    );
                    item.AccountingSchemaVersion = 2;
                    constitutionMigrated = true;
                }

                item.LossAllocations ??= new List<CashLossAllocation>();
                item.ExtraContributions ??= new List<CashExtraContribution>();
                item.Settlements ??= new List<CashSettlementEntry>();
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
                    constitutionMigrated = true;
                }

                if (item.AccountingSchemaVersion < CashConstitutionEngine.CurrentSchemaVersion)
                {
                    if (!item.IsTechnicalEvent)
                    {
                        int allocated = item.LossAllocations.Sum(allocation =>
                            Math.Max(0, allocation.Amount));
                        if (item.FormalizedAmount > allocated)
                        {
                            int missingAllocation = item.FormalizedAmount - allocated;
                            string employee = !string.IsNullOrWhiteSpace(
                                item.ResponsibleEmployeeName)
                                    ? item.ResponsibleEmployeeName.Trim()
                                    : item.SuspectedEmployeeName.Trim();
                            item.LossAllocations.Add(new CashLossAllocation
                            {
                                CreatedAt = item.ResolvedAt ?? item.CreatedAt,
                                EmployeeName = employee,
                                Amount = missingAllocation,
                                PostedAmount = missingAllocation,
                                Source = CashLossAllocationSource.Legacy,
                                Reason = "Историческая проводка оформленной потери"
                            });
                        }
                        else if (allocated > item.FormalizedAmount)
                        {
                            item.FormalizedAmount = allocated;
                        }

                        if (IsExtraKind(item.Kind) &&
                            item.ExtraContributions.Count == 0 &&
                            item.OriginalAmount > 0)
                        {
                            int active = Math.Max(0, item.Amount);
                            item.ExtraContributions.Add(new CashExtraContribution
                            {
                                InvestigationId = item.InvestigationId,
                                CreatedAt = item.CreatedAt,
                                Kind = item.Kind,
                                Origin = item.Origin,
                                Stage = item.Stage,
                                OriginalAmount = item.OriginalAmount,
                                Amount = active,
                                ResolvedAmount = Math.Max(0, item.OriginalAmount - active),
                                ExpectedAmount = item.ExpectedAmount,
                                ActualAmount = item.ActualAmount,
                                ProgramExpectedAmount = item.ProgramExpectedAmount
                            });
                        }

                        foreach (var contribution in item.ExtraContributions)
                        {
                            contribution.OriginalAmount =
                                Math.Max(0, contribution.Amount) +
                                Math.Max(0, contribution.ResolvedAmount);
                        }

                        if (IsShortageKind(item.Kind))
                        {
                            item.OriginalAmount =
                                Math.Max(0, item.Amount) +
                                Math.Max(0, item.ResolvedAmount) +
                                Math.Max(0, item.FormalizedAmount);
                        }
                    }

                    item.AccountingSchemaVersion = CashConstitutionEngine.CurrentSchemaVersion;
                    constitutionMigrated = true;
                }
            }

            if (!originMigrated && !constitutionMigrated)
                return;

            try
            {
                Save();
            }
            catch
            {
                // The migration will be retried on the next launch.
            }
        }

        public static IReadOnlyList<CashReconciliationItem> Items => _items;

        public static CashAccountingResult ProcessCashAcceptance(
            DateTime fromInclusive,
            DateTime toExclusive,
            string checkedByEmployeeName,
            string responsibleEmployeeName,
            int expectedAmount,
            int actualAmount,
            string note,
            string operationId = "",
            DateTime? occurredAt = null)
        {
            lock (Gate)
            {
                return MutateAndSave(() =>
                    CashConstitutionEngine.RecordCashAcceptance(
                        _items,
                        fromInclusive,
                        toExclusive,
                        ClubClock.Current.LocalNow,
                        checkedByEmployeeName,
                        responsibleEmployeeName,
                        expectedAmount,
                        actualAmount,
                        note,
                        operationId,
                        occurredAt
                    ));
            }
        }

        public static CashAccountingResult ProcessCashlessVerification(
            DateTime fromInclusive,
            DateTime toExclusive,
            int expectedAmount,
            int actualAmount,
            string suspectedEmployeeName,
            string note,
            string operationId = "",
            int? programExpectedAmount = null)
        {
            lock (Gate)
            {
                return MutateAndSave(() =>
                    CashConstitutionEngine.RecordCashlessVerification(
                        _items,
                        fromInclusive,
                        toExclusive,
                        ClubClock.Current.LocalNow,
                        expectedAmount,
                        actualAmount,
                        suspectedEmployeeName,
                        note,
                        operationId,
                        programExpectedAmount
                    ));
            }
        }

        public static bool HasCurrentCashlessVerification(
            DateTime fromInclusive,
            DateTime toExclusive,
            int expectedAmount,
            int actualAmount)
        {
            lock (Gate)
            {
                return CashConstitutionEngine.HasCurrentCashlessVerification(
                    _items,
                    fromInclusive,
                    toExclusive,
                    expectedAmount,
                    actualAmount
                );
            }
        }

        public static CashAccountingResult ApplyConstitutionCorrection(
            DateTime fromInclusive,
            DateTime toExclusive,
            long checkpointNumber,
            string operationId = "",
            int? actualCashAtCheckpoint = null,
            int? actualCashlessAtCheckpoint = null)
        {
            lock (Gate)
            {
                return MutateAndSave(() =>
                    CashConstitutionEngine.ApplyCorrection(
                        _items,
                        fromInclusive,
                        toExclusive,
                        ClubClock.Current.LocalNow,
                        checkpointNumber,
                        operationId,
                        actualCashAtCheckpoint,
                        actualCashlessAtCheckpoint
                    ));
            }
        }

        public static CashAccountingResult RecordConstitutionCheckpoint(
            DateTime fromInclusive,
            DateTime toExclusive,
            long checkpointNumber,
            string operationId,
            int? actualCashAtCheckpoint,
            int? actualCashlessAtCheckpoint)
        {
            lock (Gate)
            {
                return MutateAndSave(() =>
                    CashConstitutionEngine.RecordCheckpoint(
                        _items,
                        fromInclusive,
                        toExclusive,
                        ClubClock.Current.LocalNow,
                        checkpointNumber,
                        operationId,
                        actualCashAtCheckpoint,
                        actualCashlessAtCheckpoint
                    ));
            }
        }

        public static CashAccountingResult ApplyConstitutionManualLoss(
            DateTime fromInclusive,
            DateTime toExclusive,
            string employeeName,
            int amount,
            string operationId = "")
        {
            lock (Gate)
            {
                return MutateAndSave(() =>
                    CashConstitutionEngine.ApplyManualLoss(
                        _items,
                        fromInclusive,
                        toExclusive,
                        ClubClock.Current.LocalNow,
                        employeeName,
                        amount,
                        operationId
                    ));
            }
        }

        public static bool TryGetConstitutionCorrectionCommit(
            string operationId,
            out DateTime committedAt,
            out int? actualCash,
            out int actualCashless)
        {
            lock (Gate)
            {
                if (string.IsNullOrWhiteSpace(operationId))
                {
                    committedAt = default;
                    actualCash = null;
                    actualCashless = 0;
                    return false;
                }

                var marker = _items
                    .Where(item =>
                        item.IsTechnicalEvent &&
                        item.Origin == CashReconciliationOrigin.CorrectionCheckpoint &&
                        item.OperationId.Equals(
                            operationId.Trim(),
                            StringComparison.Ordinal))
                    .OrderByDescending(item => item.CreatedAt)
                    .ThenByDescending(item => item.Id)
                    .FirstOrDefault();
                if (marker == null)
                {
                    committedAt = default;
                    actualCash = null;
                    actualCashless = 0;
                    return false;
                }

                committedAt = marker.CreatedAt;
                actualCash = marker.ExpectedAmount >= 0
                    ? marker.ExpectedAmount
                    : null;
                actualCashless = Math.Max(0, marker.ActualAmount);
                return true;
            }
        }

        public static CashMonthCloseResult CloseConstitutionMonth(
            DateTime monthStart,
            DateTime nextMonthStart,
            IReadOnlyDictionary<string, double> workedHours)
        {
            lock (Gate)
            {
                return MutateAndSave(() =>
                    CashConstitutionEngine.CloseMonth(
                        _items,
                        monthStart,
                        nextMonthStart,
                        ClubClock.Current.LocalNow,
                        workedHours
                    ));
            }
        }

        public static int GetConstitutionBreakdown(
            DateTime fromInclusive,
            DateTime toExclusive)
        {
            lock (Gate)
            {
                CashConstitutionEngine.Normalize(_items, fromInclusive, toExclusive);
                return CashConstitutionEngine.GetBreakdown(
                    _items,
                    fromInclusive,
                    toExclusive
                );
            }
        }

        public static int GetConstitutionRecommendationTotal(
            DateTime fromInclusive,
            DateTime toExclusive)
        {
            lock (Gate)
            {
                CashConstitutionEngine.Normalize(_items, fromInclusive, toExclusive);
                return CashConstitutionEngine.GetRecommendationTotal(
                    _items,
                    fromInclusive,
                    toExclusive
                );
            }
        }

        public static int GetConstitutionFormalizedTotal(
            DateTime fromInclusive,
            DateTime toExclusive)
        {
            lock (Gate)
            {
                return _items
                    .Where(item => !item.IsTechnicalEvent)
                    .SelectMany(item =>
                        (item.LossAllocations ?? new List<CashLossAllocation>())
                        .Where(allocation =>
                            allocation.CreatedAt >= fromInclusive &&
                            allocation.CreatedAt < toExclusive))
                    .Sum(allocation => Math.Max(0, allocation.Amount));
            }
        }

        public static int GetLatestConstitutionCheckpointBreakdown(
            DateTime fromInclusive,
            DateTime toExclusive)
        {
            lock (Gate)
            {
                return CashConstitutionEngine.GetLatestCheckpointBreakdown(
                    _items,
                    fromInclusive,
                    toExclusive
                );
            }
        }

        public static IReadOnlyList<CashAccountingAssignment>
            GetUnpostedFormalizedAssignments()
        {
            lock (Gate)
            {
                return _items
                    .SelectMany(item =>
                        (item.LossAllocations ?? new List<CashLossAllocation>())
                        .Where(allocation =>
                            allocation.Amount > allocation.PostedAmount &&
                            !string.IsNullOrWhiteSpace(allocation.EmployeeName))
                        .Select(allocation => new
                        {
                            Item = item,
                            Allocation = allocation
                        }))
                    .OrderBy(entry => entry.Allocation.CreatedAt)
                    .ThenBy(entry => entry.Allocation.Id)
                    .Select(entry => new CashAccountingAssignment
                    {
                        AllocationId = entry.Allocation.Id,
                        EmployeeName = entry.Allocation.EmployeeName.Trim(),
                        Amount = entry.Allocation.Amount - entry.Allocation.PostedAmount,
                        ReconciliationId = entry.Item.Id,
                        Reason = string.IsNullOrWhiteSpace(entry.Allocation.Reason)
                            ? "Оформленная потеря кассы"
                            : entry.Allocation.Reason
                    })
                    .ToList();
            }
        }

        public static bool TryGetFormalizedPosting(
            Guid reconciliationId,
            Guid allocationId,
            out string employeeName,
            out int unpostedAmount,
            out int targetAllocationAmount)
        {
            lock (Gate)
            {
                var item = _items.FirstOrDefault(entry => entry.Id == reconciliationId);
                var allocation = item?.LossAllocations?.FirstOrDefault(entry =>
                    entry.Id == allocationId);
                if (allocation == null)
                {
                    employeeName = "";
                    unpostedAmount = 0;
                    targetAllocationAmount = 0;
                    return false;
                }

                employeeName = allocation.EmployeeName.Trim();
                targetAllocationAmount = Math.Max(0, allocation.Amount);
                unpostedAmount = Math.Max(
                    0,
                    targetAllocationAmount - allocation.PostedAmount
                );
                return unpostedAmount > 0 &&
                       !string.IsNullOrWhiteSpace(employeeName);
            }
        }

        public static bool TryGetFormalizedPosting(
            Guid reconciliationId,
            out string employeeName,
            out int unpostedAmount,
            out int targetFormalizedAmount)
        {
            lock (Gate)
            {
                var item = _items.FirstOrDefault(entry => entry.Id == reconciliationId);
                if (item == null)
                {
                    employeeName = "";
                    unpostedAmount = 0;
                    targetFormalizedAmount = 0;
                    return false;
                }

                employeeName = item.ResponsibleEmployeeName.Trim();
                targetFormalizedAmount = Math.Max(0, item.FormalizedAmount);
                unpostedAmount = Math.Max(
                    0,
                    targetFormalizedAmount - item.PostedFormalizedAmount
                );
                return unpostedAmount > 0 &&
                       !string.IsNullOrWhiteSpace(employeeName);
            }
        }

        public static void MarkFormalizedPosted(
            Guid reconciliationId,
            int targetFormalizedAmount)
        {
            lock (Gate)
            {
                var item = _items.FirstOrDefault(entry => entry.Id == reconciliationId);
                if (item == null)
                    return;

                item.PostedFormalizedAmount = Math.Max(
                    item.PostedFormalizedAmount,
                    Math.Min(item.FormalizedAmount, targetFormalizedAmount)
                );
                Save();
            }
        }

        public static void MarkFormalizedPosted(
            Guid reconciliationId,
            Guid allocationId,
            int targetAllocationAmount)
        {
            lock (Gate)
            {
                var item = _items.FirstOrDefault(entry => entry.Id == reconciliationId);
                var allocation = item?.LossAllocations?.FirstOrDefault(entry =>
                    entry.Id == allocationId);
                if (item == null || allocation == null)
                    return;

                allocation.PostedAmount = Math.Max(
                    allocation.PostedAmount,
                    Math.Min(allocation.Amount, targetAllocationAmount)
                );
                item.PostedFormalizedAmount = Math.Min(
                    item.FormalizedAmount,
                    item.LossAllocations.Sum(entry => Math.Max(0, entry.PostedAmount))
                );
                Save();
            }
        }

        public static int RenameEmployeeReferences(
            string oldEmployeeName,
            string newEmployeeName)
        {
            int changed = 0;

            foreach (var item in _items)
            {
                bool itemChanged = false;

                if (EmployeeReferenceRenameService.Matches(
                        item.CheckedByEmployeeName,
                        oldEmployeeName))
                {
                    item.CheckedByEmployeeName = newEmployeeName;
                    itemChanged = true;
                }

                if (EmployeeReferenceRenameService.Matches(
                        item.ResponsibleEmployeeName,
                        oldEmployeeName))
                {
                    item.ResponsibleEmployeeName = newEmployeeName;
                    itemChanged = true;
                }

                if (EmployeeReferenceRenameService.Matches(
                        item.SuspectedEmployeeName,
                        oldEmployeeName))
                {
                    item.SuspectedEmployeeName = newEmployeeName;
                    itemChanged = true;
                }

                foreach (var allocation in item.LossAllocations ??
                         new List<CashLossAllocation>())
                {
                    if (!EmployeeReferenceRenameService.Matches(
                            allocation.EmployeeName,
                            oldEmployeeName))
                    {
                        continue;
                    }

                    allocation.EmployeeName = newEmployeeName;
                    itemChanged = true;
                }

                if (EmployeeReferenceRenameService.Matches(item.ResolvedBy, oldEmployeeName))
                {
                    item.ResolvedBy = newEmployeeName;
                    itemChanged = true;
                }

                if (!itemChanged)
                    continue;

                item.Title = EmployeeReferenceRenameService.RenameText(
                    item.Title,
                    oldEmployeeName,
                    newEmployeeName);
                item.Note = EmployeeReferenceRenameService.RenameText(
                    item.Note,
                    oldEmployeeName,
                    newEmployeeName);
                item.ResolutionNote = EmployeeReferenceRenameService.RenameText(
                    item.ResolutionNote,
                    oldEmployeeName,
                    newEmployeeName);
                changed++;
            }

            if (changed > 0)
                Save();

            return changed;
        }

        public static bool TryDeleteKnownItem(
            Guid id,
            int expectedOriginalAmount,
            string responsibleEmployeeName)
        {
            var item = _items.FirstOrDefault(entry => entry.Id == id);

            if (item == null ||
                item.OriginalAmount != expectedOriginalAmount ||
                !item.ResponsibleEmployeeName.Equals(
                    responsibleEmployeeName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            _items.Remove(item);
            Save();
            return true;
        }

        public static bool TryCorrectKnownResolutionText(
            Guid id,
            string incorrectAmountText,
            string correctedAmountText)
        {
            var item = _items.FirstOrDefault(entry => entry.Id == id);

            if (item == null ||
                (!item.Note.Contains(incorrectAmountText, StringComparison.Ordinal) &&
                 !item.ResolutionNote.Contains(incorrectAmountText, StringComparison.Ordinal)))
            {
                return false;
            }

            item.Note = item.Note.Replace(
                incorrectAmountText,
                correctedAmountText,
                StringComparison.Ordinal);
            item.ResolutionNote = item.ResolutionNote.Replace(
                incorrectAmountText,
                correctedAmountText,
                StringComparison.Ordinal);
            Save();
            return true;
        }

        public static bool TryRepairKnownMirroredCorrection(
            Guid extraId,
            Guid shortageId,
            int expectedAmount)
        {
            lock (Gate)
            {
                var extra = _items.FirstOrDefault(item => item.Id == extraId);
                var shortage = _items.FirstOrDefault(item => item.Id == shortageId);

                if (extra == null ||
                    shortage == null ||
                    extra.Kind != CashReconciliationKind.CashlessExtra ||
                    extra.Origin != CashReconciliationOrigin.BalanceRawDifference ||
                    extra.OriginalAmount != expectedAmount ||
                    shortage.Kind != CashReconciliationKind.CashlessShortage ||
                    shortage.Origin != CashReconciliationOrigin.CashlessVerification ||
                    shortage.OriginalAmount != expectedAmount ||
                    extra.ExpectedAmount != shortage.ExpectedAmount ||
                    extra.ActualAmount != shortage.ActualAmount)
                {
                    return false;
                }

                bool alreadyRepaired =
                    shortage.FormalizedAmount == 0 &&
                    shortage.Resolution == CashReconciliationResolution.InputCorrection &&
                    extra.Resolution == CashReconciliationResolution.InputCorrection;

                if (alreadyRepaired)
                    return true;

                if (shortage.FormalizedAmount != expectedAmount)
                    return false;

                shortage.Amount = 0;
                shortage.ResolvedAmount = expectedAmount;
                shortage.FormalizedAmount = 0;
                shortage.PostedFormalizedAmount = 0;
                shortage.Status = CashReconciliationStatus.Resolved;
                shortage.Resolution = CashReconciliationResolution.InputCorrection;
                shortage.ResolutionNote =
                    "Технический дубль повторной сверки отменён восстановлением данных.";
                shortage.ResolvedBy = "Система";

                extra.Amount = 0;
                extra.ResolvedAmount = expectedAmount;
                extra.Status = CashReconciliationStatus.Resolved;
                extra.Resolution = CashReconciliationResolution.InputCorrection;
                extra.ResolutionNote =
                    "Зеркальный технический излишек отменён восстановлением данных.";
                extra.ResolvedBy = "Система";

                Save();
                return true;
            }
        }

        public static bool TryRepairKnownAccumulatedCashlessSnapshots(
            Guid extraId,
            Guid shortageId,
            Guid allocationId,
            int incorrectExtraAmount,
            int incorrectFormalizedAmount,
            string employeeName)
        {
            lock (Gate)
            {
                bool recognized = CashConstitutionEngine
                    .TryRepairKnownAccumulatedCashlessSnapshots(
                        _items,
                        extraId,
                        shortageId,
                        allocationId,
                        incorrectExtraAmount,
                        incorrectFormalizedAmount,
                        employeeName
                    );
                if (recognized)
                    Save();

                return recognized;
            }
        }

        public static bool TryReopenKnownSupersededRawDifference(
            Guid id,
            int expectedOriginalAmount,
            string suspectedEmployeeName)
        {
            var item = _items.FirstOrDefault(entry => entry.Id == id);

            if (item == null)
                return false;

            NormalizeItem(item);
            NormalizeLegacyOrigin(item);

            bool matchesSuspect =
                item.SuspectedEmployeeName.Equals(
                    suspectedEmployeeName,
                    StringComparison.OrdinalIgnoreCase);
            bool wasClosedByCashlessVerification =
                item.ResolutionNote.Contains(
                    "Закрыто новой полной сверкой безнала",
                    StringComparison.OrdinalIgnoreCase);

            if (item.Origin != CashReconciliationOrigin.BalanceRawDifference ||
                item.Kind != CashReconciliationKind.CashlessShortage ||
                item.Status != CashReconciliationStatus.Resolved ||
                item.OriginalAmount != expectedOriginalAmount ||
                item.ResolvedAmount != expectedOriginalAmount ||
                item.FormalizedAmount != 0 ||
                !matchesSuspect ||
                !wasClosedByCashlessVerification)
            {
                return false;
            }

            item.Amount = expectedOriginalAmount;
            item.ResolvedAmount = 0;
            item.Status = CashReconciliationStatus.Open;
            item.ResolvedAt = null;
            item.ResolvedBy = "";
            item.ResolutionNote = "";
            AppendNote(
                item,
                "Восстановлено после исправления ошибочного закрытия новой сверкой безнала.");
            Save();
            return true;
        }

        public static CashReconciliationItem AddCashAcceptanceDifference(
            string checkedByEmployeeName,
            string responsibleEmployeeName,
            int expectedAmount,
            int actualAmount,
            string note = "Приёмка налички")
        {
            int difference = actualAmount - expectedAmount;

            if (difference == 0)
                return new CashReconciliationItem
                {
                    Status = CashReconciliationStatus.Resolved,
                    Origin = CashReconciliationOrigin.CashAcceptance,
                    ExpectedAmount = expectedAmount,
                    ActualAmount = actualAmount
                };

            var item = new CashReconciliationItem
            {
                Id = Guid.NewGuid(),
                CreatedAt = ClubClock.Current.LocalNow,
                Kind = difference > 0
                    ? CashReconciliationKind.CashExtra
                    : CashReconciliationKind.CashShortage,
                Status = CashReconciliationStatus.Open,
                Origin = CashReconciliationOrigin.CashAcceptance,
                Amount = Math.Abs(difference),
                OriginalAmount = Math.Abs(difference),
                ExpectedAmount = expectedAmount,
                ActualAmount = actualAmount,
                CheckedByEmployeeName = checkedByEmployeeName.Trim(),
                ResponsibleEmployeeName = responsibleEmployeeName.Trim(),
                Title = difference > 0
                    ? "Излишек налички"
                    : "Недостача налички",
                Note = note.Trim()
            };

            _items.Add(item);
            Save();

            return item;
        }

        public static CashReconciliationItem AddCashlessVerification(
            int expectedAmount,
            int actualAmount,
            int amount,
            CashReconciliationStatus status,
            string note,
            string suspectedEmployeeName = "")
        {
            var item = new CashReconciliationItem
            {
                Id = Guid.NewGuid(),
                CreatedAt = ClubClock.Current.LocalNow,
                Kind = actualAmount >= expectedAmount
                    ? CashReconciliationKind.CashlessExtra
                    : CashReconciliationKind.CashlessShortage,
                Status = status,
                Origin = CashReconciliationOrigin.CashlessVerification,
                Amount = Math.Max(0, amount),
                OriginalAmount = Math.Max(0, amount),
                ExpectedAmount = expectedAmount,
                ActualAmount = actualAmount,
                CheckedByEmployeeName = "Владелец",
                ResponsibleEmployeeName = "",
                SuspectedEmployeeName = suspectedEmployeeName.Trim(),
                Title = actualAmount >= expectedAmount
                    ? "Излишек безнала"
                    : "Недостача безнала",
                Note = note.Trim()
            };

            if (status == CashReconciliationStatus.Resolved)
            {
                item.ResolvedAmount = item.Amount;
                item.Amount = 0;
                item.ResolvedAt = ClubClock.Current.LocalNow;
                item.ResolvedBy = "Система";
                item.ResolutionNote = note.Trim();
            }

            _items.Add(item);
            Save();

            return item;
        }

        public static bool SetSuspectedEmployee(
            Guid id,
            string suspectedEmployeeName,
            string note = "")
        {
            if (string.IsNullOrWhiteSpace(suspectedEmployeeName))
                return false;

            var item = _items.FirstOrDefault(entry => entry.Id == id);

            if (item == null)
                return false;

            string nextName = suspectedEmployeeName.Trim();

            if (item.SuspectedEmployeeName.Equals(nextName, StringComparison.OrdinalIgnoreCase))
                return false;

            item.SuspectedEmployeeName = nextName;
            if (string.IsNullOrWhiteSpace(item.ResponsibleEmployeeName))
                item.ResponsibilityLevel = CashResponsibilityLevel.Suspected;

            if (!string.IsNullOrWhiteSpace(note))
                AppendNote(item, note.Trim());

            Save();
            return true;
        }

        public static int NetOpenMoneyCorrections(
            DateTime fromInclusive,
            DateTime toExclusive,
            string resolvedBy,
            string note)
        {
            (fromInclusive, toExclusive) = LimitToSingleMonth(fromInclusive, toExclusive);

            var extras = _items
                .Where(item =>
                    item.Status == CashReconciliationStatus.Open &&
                    item.CreatedAt >= fromInclusive &&
                    item.CreatedAt < toExclusive &&
                    IsExtraKind(item.Kind) &&
                    item.Amount > 0)
                .OrderBy(item => item.CreatedAt)
                .ToList();

            var shortages = _items
                .Where(item =>
                    item.Status == CashReconciliationStatus.Open &&
                    item.CreatedAt >= fromInclusive &&
                    item.CreatedAt < toExclusive &&
                    IsShortageKind(item.Kind) &&
                    item.Amount > 0)
                .OrderBy(item => item.CreatedAt)
                .ToList();

            int consumed = 0;
            string finalResolvedBy = string.IsNullOrWhiteSpace(resolvedBy)
                ? "Система"
                : resolvedBy.Trim();
            string finalNote = string.IsNullOrWhiteSpace(note)
                ? "Закрыто общим зачётом излишков и недостач после сверки кассы."
                : note.Trim();

            foreach (var shortage in shortages)
            {
                NormalizeItem(shortage);

                foreach (var extra in extras)
                {
                    NormalizeItem(extra);

                    if (shortage.Amount <= 0)
                        break;

                    if (extra.Amount <= 0)
                        continue;

                    int amount = Math.Min(shortage.Amount, extra.Amount);

                    ApplyAutomaticSettlement(shortage, amount);
                    ApplyAutomaticSettlement(extra, amount);
                    consumed += amount;

                    string pairNote = $"{finalNote} Зачтено встречной суммой: {amount} сом.";

                    AppendNote(shortage, pairNote);
                    AppendNote(extra, pairNote);

                    ResolveIfEmpty(shortage, finalResolvedBy, pairNote);
                    ResolveIfEmpty(extra, finalResolvedBy, pairNote);
                }
            }

            if (consumed > 0)
                Save();

            return consumed;
        }

        public static int ResolveStaleCashlessZeroBaselineArtifacts(
            DateTime fromInclusive,
            DateTime toExclusive,
            int expectedAmount,
            int actualAmount)
        {
            (fromInclusive, toExclusive) = LimitToSingleMonth(fromInclusive, toExclusive);

            if (expectedAmount != actualAmount)
                return 0;

            int resolved = 0;

            foreach (var item in _items
                .Where(item =>
                    item.Status == CashReconciliationStatus.Open &&
                    item.Kind == CashReconciliationKind.CashlessExtra &&
                    item.Origin == CashReconciliationOrigin.CashlessVerification &&
                    item.CreatedAt >= fromInclusive &&
                    item.CreatedAt < toExclusive &&
                    item.ExpectedAmount == 0 &&
                    item.ActualAmount == actualAmount &&
                    item.Amount == actualAmount)
                .ToList())
            {
                item.Status = CashReconciliationStatus.Resolved;
                item.ResolvedAt = ClubClock.Current.LocalNow;
                item.ResolvedBy = "Система";
                item.ResolutionNote =
                    "Закрыто автоматически: повторная сверка показала, что фактический безнал равен программе.";
                resolved++;
            }

            if (resolved > 0)
                Save();

            return resolved;
        }

        public static CashReconciliationItem AddBalanceRawDifference(
            int expectedAmount,
            int actualAmount,
            int amount,
            bool isShortage,
            string note,
            string responsibleEmployeeName = "",
            string suspectedEmployeeName = "")
        {
            var item = new CashReconciliationItem
            {
                Id = Guid.NewGuid(),
                CreatedAt = ClubClock.Current.LocalNow,
                Kind = isShortage
                    ? CashReconciliationKind.CashlessShortage
                    : CashReconciliationKind.CashlessExtra,
                Status = CashReconciliationStatus.Open,
                Origin = CashReconciliationOrigin.BalanceRawDifference,
                Amount = Math.Max(0, amount),
                OriginalAmount = Math.Max(0, amount),
                ExpectedAmount = expectedAmount,
                ActualAmount = actualAmount,
                CheckedByEmployeeName = "Владелец",
                ResponsibleEmployeeName = responsibleEmployeeName.Trim(),
                SuspectedEmployeeName = suspectedEmployeeName.Trim(),
                Title = isShortage
                    ? "Сырые потери"
                    : "Излишек после корректировки",
                Note = note.Trim()
            };

            _items.Add(item);
            Save();

            return item;
        }

        public static string GetSuggestedResponsibleForOpenShortages(
            DateTime fromInclusive,
            DateTime toExclusive)
        {
            (fromInclusive, toExclusive) = LimitToSingleMonth(fromInclusive, toExclusive);

            return _items
                .Where(item =>
                    item.Status == CashReconciliationStatus.Open &&
                    item.CreatedAt >= fromInclusive &&
                    item.CreatedAt < toExclusive &&
                    IsShortageKind(item.Kind) &&
                    item.Amount > 0 &&
                    !string.IsNullOrWhiteSpace(item.ResponsibleEmployeeName))
                .GroupBy(item => item.ResponsibleEmployeeName.Trim(), StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(group => group.Sum(item => item.Amount))
                .ThenByDescending(group => group.Max(item => item.CreatedAt))
                .Select(group => group.Key)
                .FirstOrDefault() ?? "";
        }

        public static IReadOnlyList<(string EmployeeName, int Amount)>
            GetOpenResponsibleShortageTotals(
                DateTime fromInclusive,
                DateTime toExclusive)
        {
            (fromInclusive, toExclusive) = LimitToSingleMonth(fromInclusive, toExclusive);

            return _items
                .Where(item =>
                    item.Status == CashReconciliationStatus.Open &&
                    item.CreatedAt >= fromInclusive &&
                    item.CreatedAt < toExclusive &&
                    IsShortageKind(item.Kind) &&
                    item.Amount > 0 &&
                    !string.IsNullOrWhiteSpace(item.ResponsibleEmployeeName))
                .GroupBy(
                    item => item.ResponsibleEmployeeName.Trim(),
                    StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Min(item => item.CreatedAt))
                .Select(group => (
                    EmployeeName: group.Key,
                    Amount: group.Sum(item => item.Amount)))
                .ToList();
        }

        public static string GetSuggestedSuspectForOpenShortages(
            DateTime fromInclusive,
            DateTime toExclusive)
        {
            (fromInclusive, toExclusive) = LimitToSingleMonth(fromInclusive, toExclusive);

            return _items
                .Where(item =>
                    item.Status == CashReconciliationStatus.Open &&
                    item.CreatedAt >= fromInclusive &&
                    item.CreatedAt < toExclusive &&
                    IsShortageKind(item.Kind) &&
                    item.Amount > 0 &&
                    string.IsNullOrWhiteSpace(item.ResponsibleEmployeeName) &&
                    !string.IsNullOrWhiteSpace(item.SuspectedEmployeeName))
                .GroupBy(item => item.SuspectedEmployeeName.Trim(), StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(group => group.Sum(item => item.Amount))
                .ThenByDescending(group => group.Max(item => item.CreatedAt))
                .Select(group => group.Key)
                .FirstOrDefault() ?? "";
        }

        public static string GetSuggestedResponsibleForShortageHistory(
            DateTime fromInclusive,
            DateTime toExclusive)
        {
            (fromInclusive, toExclusive) = LimitToSingleMonth(fromInclusive, toExclusive);

            return _items
                .Where(item =>
                    item.CreatedAt >= fromInclusive &&
                    item.CreatedAt < toExclusive &&
                    IsShortageKind(item.Kind) &&
                    item.FormalizedAmount > 0 &&
                    !string.IsNullOrWhiteSpace(item.ResponsibleEmployeeName))
                .OrderByDescending(item => item.CreatedAt)
                .Select(item => item.ResponsibleEmployeeName.Trim())
                .FirstOrDefault() ?? "";
        }

        public static string GetSuggestedSuspectForShortageHistory(
            DateTime fromInclusive,
            DateTime toExclusive)
        {
            (fromInclusive, toExclusive) = LimitToSingleMonth(fromInclusive, toExclusive);

            return _items
                .Where(item =>
                    item.CreatedAt >= fromInclusive &&
                    item.CreatedAt < toExclusive &&
                    IsShortageKind(item.Kind) &&
                    (item.OriginalAmount > 0 || item.Amount > 0 || item.FormalizedAmount > 0) &&
                    string.IsNullOrWhiteSpace(item.ResponsibleEmployeeName) &&
                    !string.IsNullOrWhiteSpace(item.SuspectedEmployeeName))
                .OrderByDescending(item => item.CreatedAt)
                .Select(item => item.SuspectedEmployeeName.Trim())
                .FirstOrDefault() ?? "";
        }

        public static void AutoResolveSmallPaymentMistakes(
            DateTime fromInclusive,
            DateTime toExclusive)
        {
            (fromInclusive, toExclusive) = LimitToSingleMonth(fromInclusive, toExclusive);

            bool changed = false;

            changed |= AutoResolveOppositeSmallItems(
                fromInclusive,
                toExclusive,
                extraKind: CashReconciliationKind.CashExtra,
                shortageKind: CashReconciliationKind.CashlessShortage,
                extraResolvedNote: "Автоматически закрыто: безнал указали в программе, а деньги оказались наличными.",
                shortageResolvedNote: "Автоматически закрыто излишком налички: ошибка типа оплаты."
            );

            changed |= AutoResolveOppositeSmallItems(
                fromInclusive,
                toExclusive,
                extraKind: CashReconciliationKind.CashlessExtra,
                shortageKind: CashReconciliationKind.CashShortage,
                extraResolvedNote: "Автоматически закрыто: наличку указали в программе, а деньги оказались безналом.",
                shortageResolvedNote: "Автоматически закрыто излишком безнала: ошибка типа оплаты."
            );

            if (changed)
                Save();
        }

        private static bool AutoResolveOppositeSmallItems(
            DateTime fromInclusive,
            DateTime toExclusive,
            CashReconciliationKind extraKind,
            CashReconciliationKind shortageKind,
            string extraResolvedNote,
            string shortageResolvedNote)
        {
            bool changed = false;
            var extras = _items
                .Where(item =>
                    item.Status == CashReconciliationStatus.Open &&
                    item.Kind == extraKind &&
                    IsPaymentMistakeCandidate(item) &&
                    item.CreatedAt >= fromInclusive &&
                    item.CreatedAt < toExclusive &&
                    IsAutoResolvablePaymentMistakeAmount(item.Amount))
                .OrderBy(item => item.CreatedAt)
                .ToList();

            var shortages = _items
                .Where(item =>
                    item.Status == CashReconciliationStatus.Open &&
                    item.Kind == shortageKind &&
                    IsPaymentMistakeCandidate(item) &&
                    item.CreatedAt >= fromInclusive &&
                    item.CreatedAt < toExclusive &&
                    IsAutoResolvablePaymentMistakeAmount(item.Amount))
                .OrderBy(item => item.CreatedAt)
                .ToList();

            foreach (var shortage in shortages)
            {
                foreach (var extra in extras)
                {
                    if (shortage.Amount <= 0)
                        break;

                    if (extra.Amount <= 0)
                        continue;

                    int amount = Math.Min(shortage.Amount, extra.Amount);

                    ApplyAutomaticSettlement(shortage, amount);
                    ApplyAutomaticSettlement(extra, amount);
                    changed = true;

                    if (extra.Amount == 0)
                    {
                        extra.Status = CashReconciliationStatus.Resolved;
                        extra.ResolvedAt = ClubClock.Current.LocalNow;
                        extra.ResolvedBy = "Система";
                        extra.ResolutionNote = extraResolvedNote;
                    }

                    if (shortage.Amount == 0)
                    {
                        shortage.Status = CashReconciliationStatus.Resolved;
                        shortage.ResolvedAt = ClubClock.Current.LocalNow;
                        shortage.ResolvedBy = "Система";
                        shortage.ResolutionNote = shortageResolvedNote;
                    }
                }
            }

            return changed;
        }

        private static bool IsAutoResolvablePaymentMistakeAmount(int amount)
        {
            return amount > 0 && amount <= AutoResolveLimit;
        }

        private static bool IsPaymentMistakeCandidate(CashReconciliationItem item)
        {
            if (item.Kind == CashReconciliationKind.CashlessExtra ||
                item.Kind == CashReconciliationKind.CashlessShortage)
            {
                return item.Origin == CashReconciliationOrigin.CashlessVerification;
            }

            return true;
        }

        [Obsolete("Старый путь закрытия карточек запрещён Конституцией кассы.", true)]
        public static int ConsumeOpenCashExtra(
            int amount,
            DateTime fromInclusive,
            DateTime toExclusive)
        {
            if (amount <= 0)
                return 0;

            (fromInclusive, toExclusive) = LimitToSingleMonth(fromInclusive, toExclusive);

            int remaining = amount;
            int consumed = 0;

            var extras = _items
                .Where(item =>
                    item.Status == CashReconciliationStatus.Open &&
                    item.Kind == CashReconciliationKind.CashExtra &&
                    item.CreatedAt >= fromInclusive &&
                    item.CreatedAt < toExclusive &&
                    item.Amount > 0)
                .OrderBy(item => item.CreatedAt)
                .ToList();

            foreach (var item in extras)
            {
                if (remaining <= 0)
                    break;

                int useAmount = Math.Min(item.Amount, remaining);

                    ApplyAutomaticSettlement(item, useAmount);
                    consumed += useAmount;
                    remaining -= useAmount;

                if (item.Amount == 0)
                {
                    item.Status = CashReconciliationStatus.Resolved;
                item.ResolvedAt = ClubClock.Current.LocalNow;
                    item.ResolvedBy = "Система";
                    item.ResolutionNote = "Зачтено как ошибка типа оплаты: безнал был принят наличными.";
                }
            }

            Save();

            return consumed;
        }

        [Obsolete("Старый путь закрытия карточек запрещён Конституцией кассы.", true)]
        public static int ConsumeOpenCashlessExtra(
            int amount,
            DateTime fromInclusive,
            DateTime toExclusive)
        {
            if (amount <= 0)
                return 0;

            (fromInclusive, toExclusive) = LimitToSingleMonth(fromInclusive, toExclusive);

            int remaining = amount;
            int consumed = 0;

            var extras = _items
                .Where(item =>
                    item.Status == CashReconciliationStatus.Open &&
                    item.Kind == CashReconciliationKind.CashlessExtra &&
                    item.Origin == CashReconciliationOrigin.CashlessVerification &&
                    item.CreatedAt >= fromInclusive &&
                    item.CreatedAt < toExclusive &&
                    item.Amount > 0)
                .OrderBy(item => item.CreatedAt)
                .ToList();

            foreach (var item in extras)
            {
                if (remaining <= 0)
                    break;

                int useAmount = Math.Min(item.Amount, remaining);

                    ApplyAutomaticSettlement(item, useAmount);
                    consumed += useAmount;
                    remaining -= useAmount;

                if (item.Amount == 0)
                {
                    item.Status = CashReconciliationStatus.Resolved;
                item.ResolvedAt = ClubClock.Current.LocalNow;
                    item.ResolvedBy = "Система";
                    item.ResolutionNote = "Зачтено как ошибка типа оплаты: наличка была принята безналом.";
                }
            }

            Save();

            return consumed;
        }

        public static List<CashReconciliationItem> GetOpenItems()
        {
            return _items
                .Where(item => item.Status == CashReconciliationStatus.Open)
                .OrderByDescending(item => item.CreatedAt)
                .ToList();
        }

        public static List<CashReconciliationItem> GetOpenSmallCashlessShortages(
            DateTime fromInclusive,
            DateTime toExclusive)
        {
            (fromInclusive, toExclusive) = LimitToSingleMonth(fromInclusive, toExclusive);

            return _items
                .Where(item =>
                    item.Status == CashReconciliationStatus.Open &&
                    item.Kind == CashReconciliationKind.CashlessShortage &&
                    item.Origin == CashReconciliationOrigin.CashlessVerification &&
                    item.CreatedAt >= fromInclusive &&
                    item.CreatedAt < toExclusive &&
                    IsAutoResolvablePaymentMistakeAmount(item.Amount))
                .OrderBy(item => item.CreatedAt)
                .ToList();
        }

        public static List<CashReconciliationItem> GetRecentItems(int count = 100)
        {
            var open = _items
                .Where(item =>
                    !item.IsTechnicalEvent &&
                    item.Status == CashReconciliationStatus.Open)
                .ToList();
            var resolved = _items
                .Where(item =>
                    !item.IsTechnicalEvent &&
                    item.Status == CashReconciliationStatus.Resolved)
                .OrderByDescending(item => item.CreatedAt)
                .Take(Math.Max(0, count))
                .ToList();

            return open
                .Concat(resolved)
                .GroupBy(item => item.Id)
                .Select(group => group.First())
                .OrderByDescending(item => item.CreatedAt)
                .ToList();
        }

        [Obsolete("Ручное изменение суммы карточки запрещено Конституцией кассы.", true)]
        public static CashReconciliationItem UpdateOpenAmount(
            Guid id,
            int amount,
            string note = "")
        {
            var item = _items.FirstOrDefault(entry => entry.Id == id);

            if (item == null)
                throw new Exception("Сверочная запись не найдена.");

            if (item.Status == CashReconciliationStatus.Resolved)
                return item;

            NormalizeItem(item);

            int nextAmount = Math.Max(0, amount);
            if (nextAmount < item.Amount)
                item.ResolvedAmount += item.Amount - nextAmount;

            item.Amount = nextAmount;

            if (!string.IsNullOrWhiteSpace(note))
            {
                item.Note = string.IsNullOrWhiteSpace(item.Note)
                    ? note.Trim()
                    : $"{item.Note.Trim()}\n{note.Trim()}";
            }

            if (item.Amount == 0)
            {
                item.Status = CashReconciliationStatus.Resolved;
                item.ResolvedAt = ClubClock.Current.LocalNow;
                item.ResolvedBy = "Система";
                item.ResolutionNote = "Активная сумма стала 0, карточка закрыта.";
            }

            Save();

            return item;
        }

        [Obsolete("Массовое закрытие карточек запрещено Конституцией кассы.", true)]
        public static int CloseOpenItemsForBalance(
            DateTime fromInclusive,
            DateTime toExclusive,
            string resolvedBy,
            string note)
        {
            (fromInclusive, toExclusive) = LimitToSingleMonth(fromInclusive, toExclusive);

            int closed = 0;

            foreach (var item in _items
                .Where(item =>
                    item.Status == CashReconciliationStatus.Open &&
                    item.CreatedAt >= fromInclusive &&
                    item.CreatedAt < toExclusive)
                .OrderBy(item => item.CreatedAt)
                .ToList())
            {
                NormalizeItem(item);
                if (item.Amount > 0)
                {
                    item.ResolvedAmount += item.Amount;
                    item.Amount = 0;
                }

                item.Status = CashReconciliationStatus.Resolved;
                item.ResolvedAt = ClubClock.Current.LocalNow;
                item.ResolvedBy = string.IsNullOrWhiteSpace(resolvedBy)
                    ? "Владелец"
                    : resolvedBy.Trim();
                item.ResolutionNote = note.Trim();
                closed++;
            }

            if (closed > 0)
                Save();

            return closed;
        }

        [Obsolete("Замена открытых сверок запрещена Конституцией кассы.", true)]
        public static int SupersedeOpenCashlessVerifications(
            DateTime fromInclusive,
            DateTime toExclusive,
            string note)
        {
            (fromInclusive, toExclusive) = LimitToSingleMonth(fromInclusive, toExclusive);

            int closed = 0;

            foreach (var item in _items
                .Where(item =>
                    item.Status == CashReconciliationStatus.Open &&
                    item.CreatedAt >= fromInclusive &&
                    item.CreatedAt < toExclusive &&
                    item.Origin == CashReconciliationOrigin.CashlessVerification &&
                    (item.Kind == CashReconciliationKind.CashlessShortage ||
                     item.Kind == CashReconciliationKind.CashlessExtra))
                .OrderBy(item => item.CreatedAt)
                .ToList())
            {
                NormalizeItem(item);

                if (item.Amount > 0)
                {
                    item.ResolvedAmount += item.Amount;
                    item.Amount = 0;
                }

                item.Status = CashReconciliationStatus.Resolved;
                item.ResolvedAt = ClubClock.Current.LocalNow;
                item.ResolvedBy = "Система";
                item.ResolutionNote = note.Trim();
                AppendNote(item, note.Trim());
                closed++;
            }

            if (closed > 0)
                Save();

            return closed;
        }

        [Obsolete("Используйте конституционный движок и неизменяемые назначения.", true)]
        public static int FormalizeOpenShortagesForPeriod(
            DateTime fromInclusive,
            DateTime toExclusive,
            int amount,
            string resolvedBy,
            string note)
        {
            return FormalizeOpenShortages(
                fromInclusive,
                toExclusive,
                amount,
                resolvedBy,
                note,
                responsibleEmployeeName: ""
            );
        }

        [Obsolete("Используйте конституционный движок и неизменяемые назначения.", true)]
        public static int FormalizeOpenShortagesForEmployee(
            DateTime fromInclusive,
            DateTime toExclusive,
            string responsibleEmployeeName,
            int amount,
            string resolvedBy,
            string note)
        {
            responsibleEmployeeName = responsibleEmployeeName.Trim();

            if (string.IsNullOrWhiteSpace(responsibleEmployeeName))
                return 0;

            return FormalizeOpenShortages(
                fromInclusive,
                toExclusive,
                amount,
                resolvedBy,
                note,
                responsibleEmployeeName
            );
        }

        private static int FormalizeOpenShortages(
            DateTime fromInclusive,
            DateTime toExclusive,
            int amount,
            string resolvedBy,
            string note,
            string responsibleEmployeeName)
        {
            (fromInclusive, toExclusive) = LimitToSingleMonth(fromInclusive, toExclusive);
            responsibleEmployeeName = responsibleEmployeeName.Trim();

            if (amount <= 0)
                return 0;

            int remaining = amount;
            int formalized = 0;

            foreach (var item in _items
                .Where(item =>
                    item.Status == CashReconciliationStatus.Open &&
                    item.CreatedAt >= fromInclusive &&
                    item.CreatedAt < toExclusive &&
                    IsShortageKind(item.Kind) &&
                    (string.IsNullOrWhiteSpace(responsibleEmployeeName) ||
                        item.ResponsibleEmployeeName.Trim().Equals(
                            responsibleEmployeeName,
                            StringComparison.OrdinalIgnoreCase)) &&
                    item.Amount > 0)
                .OrderBy(item => item.CreatedAt)
                .ToList())
            {
                if (remaining <= 0)
                    break;

                NormalizeItem(item);

                int useAmount = Math.Min(item.Amount, remaining);
                item.Amount -= useAmount;
                item.FormalizedAmount += useAmount;
                formalized += useAmount;
                remaining -= useAmount;

                if (!string.IsNullOrWhiteSpace(note))
                {
                    item.Note = string.IsNullOrWhiteSpace(item.Note)
                        ? note.Trim()
                        : $"{item.Note.Trim()}\n{note.Trim()}";
                }

                if (item.Amount == 0)
                {
                    item.Status = CashReconciliationStatus.Resolved;
                item.ResolvedAt = ClubClock.Current.LocalNow;
                    item.ResolvedBy = string.IsNullOrWhiteSpace(resolvedBy)
                        ? "Владелец"
                        : resolvedBy.Trim();
                    item.ResolutionNote = string.IsNullOrWhiteSpace(note)
                        ? "Оформлено штрафом владельца."
                        : note.Trim();
                }
            }

            if (formalized > 0)
                Save();

            return formalized;
        }

        public static int GetOpenShortageTotal(
            DateTime fromInclusive,
            DateTime toExclusive)
        {
            (fromInclusive, toExclusive) = LimitToSingleMonth(fromInclusive, toExclusive);

            return _items
                .Where(item =>
                    item.Status == CashReconciliationStatus.Open &&
                    item.CreatedAt >= fromInclusive &&
                    item.CreatedAt < toExclusive &&
                    IsShortageKind(item.Kind) &&
                    item.Amount > 0)
                .Sum(item => item.Amount);
        }

        public static int GetOpenExtraTotal(
            DateTime fromInclusive,
            DateTime toExclusive)
        {
            (fromInclusive, toExclusive) = LimitToSingleMonth(fromInclusive, toExclusive);

            return _items
                .Where(item =>
                    item.Status == CashReconciliationStatus.Open &&
                    item.CreatedAt >= fromInclusive &&
                    item.CreatedAt < toExclusive &&
                    IsExtraKind(item.Kind) &&
                    item.Amount > 0)
                .Sum(item => item.Amount);
        }

        public static int ResolveRecentCashAcceptanceInputMistakes(
            string checkedByEmployeeName,
            int amount,
            TimeSpan correctionWindow,
            string note,
            DateTime fromInclusive,
            DateTime toExclusive,
            string operationId = "")
        {
            lock (Gate)
            {
                return MutateAndSave(() =>
                    ResolveRecentCashAcceptanceInputMistakesCore(
                        checkedByEmployeeName,
                        amount,
                        correctionWindow,
                        note,
                        fromInclusive,
                        toExclusive,
                        operationId));
            }
        }

        private static int ResolveRecentCashAcceptanceInputMistakesCore(
            string checkedByEmployeeName,
            int amount,
            TimeSpan correctionWindow,
            string note,
            DateTime fromInclusive,
            DateTime toExclusive,
            string operationId)
        {
            operationId = operationId.Trim();
            if (!string.IsNullOrWhiteSpace(operationId))
            {
                var existing = _items.FirstOrDefault(item =>
                    item.IsTechnicalEvent &&
                    item.OperationId.Equals(operationId, StringComparison.Ordinal));
                if (existing != null)
                    return Math.Max(0, existing.ActualAmount);
            }

            checkedByEmployeeName = checkedByEmployeeName.Trim();

            if (string.IsNullOrWhiteSpace(checkedByEmployeeName) || amount <= 0)
                return 0;

            (fromInclusive, toExclusive) = LimitToSingleMonth(fromInclusive, toExclusive);

            DateTime fromTime = ClubClock.Current.LocalNow.Subtract(correctionWindow);
            int remaining = amount;
            int resolved = 0;

            foreach (var item in _items
                .Where(item =>
                    item.Status == CashReconciliationStatus.Open &&
                    item.Kind == CashReconciliationKind.CashShortage &&
                    item.CreatedAt >= fromTime &&
                    item.CreatedAt >= fromInclusive &&
                    item.CreatedAt < toExclusive &&
                    item.Amount > 0 &&
                    item.FormalizedAmount <= 0 &&
                    item.CheckedByEmployeeName.Trim().Equals(
                        checkedByEmployeeName,
                        StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(item => item.CreatedAt)
                .ToList())
            {
                if (remaining <= 0)
                    break;

                NormalizeItem(item);

                int useAmount = Math.Min(item.Amount, remaining);
                item.Amount -= useAmount;
                item.ResolvedAmount += useAmount;
                resolved += useAmount;
                remaining -= useAmount;

                if (!string.IsNullOrWhiteSpace(note))
                {
                    item.Note = string.IsNullOrWhiteSpace(item.Note)
                        ? note.Trim()
                        : $"{item.Note.Trim()}\n{note.Trim()}";
                }

                if (item.Amount == 0)
                {
                    item.Status = CashReconciliationStatus.Resolved;
                item.ResolvedAt = ClubClock.Current.LocalNow;
                    item.ResolvedBy = checkedByEmployeeName;
                    item.ResolutionNote = "Закрыто повторной приёмкой: сотрудник исправил свою ошибку ввода налички.";
                }
            }

            if (!string.IsNullOrWhiteSpace(operationId))
            {
                _items.Add(new CashReconciliationItem
                {
                    AccountingSchemaVersion = CashConstitutionEngine.CurrentSchemaVersion,
                    Id = Guid.NewGuid(),
                    InvestigationId = Guid.NewGuid(),
                    IsTechnicalEvent = true,
                    OperationId = operationId,
                    CreatedAt = ClubClock.Current.LocalNow,
                    Kind = CashReconciliationKind.Other,
                    Origin = CashReconciliationOrigin.CashAcceptanceInputCorrection,
                    Status = CashReconciliationStatus.Resolved,
                    Stage = CashReconciliationStage.Ready,
                    Resolution = CashReconciliationResolution.InputCorrection,
                    ExpectedAmount = amount,
                    ActualAmount = resolved,
                    Note = note.Trim(),
                    ResolvedAt = ClubClock.Current.LocalNow,
                    ResolvedBy = "Система",
                    ResolutionNote = "Идемпотентная запись исправления повторной приёмки."
                });
            }

            return resolved;
        }

        public static CashReconciliationItem Resolve(
            Guid id,
            string resolvedBy,
            string resolutionType,
            string note = "")
        {
            lock (Gate)
            {
                return MutateAndSave(() => ResolveCore(
                    id,
                    resolvedBy,
                    resolutionType,
                    note));
            }
        }

        private static CashReconciliationItem ResolveCore(
            Guid id,
            string resolvedBy,
            string resolutionType,
            string note)
        {
            var item = _items.FirstOrDefault(entry => entry.Id == id);

            if (item == null)
                throw new Exception("Сверочная запись не найдена.");

            if (item.Status == CashReconciliationStatus.Resolved)
                return item;

            NormalizeItem(item);

            string penaltyEmployee = !string.IsNullOrWhiteSpace(
                item.ResponsibleEmployeeName)
                    ? item.ResponsibleEmployeeName.Trim()
                    : item.SuspectedEmployeeName.Trim();
            if (resolutionType == "RealShortage" &&
                string.IsNullOrWhiteSpace(penaltyEmployee))
            {
                throw new Exception(
                    "У этой недостачи нет определённого сотрудника. " +
                    "Назначьте штраф за потери вручную выбранному сотруднику.");
            }

            if (item.Amount > 0)
            {
                if (resolutionType == "RealShortage")
                {
                    int formalizedAmount = item.Amount;
                    item.FormalizedAmount += formalizedAmount;
                    item.LossAllocations.Add(new CashLossAllocation
                    {
                        CreatedAt = ClubClock.Current.LocalNow,
                        EmployeeName = penaltyEmployee,
                        Amount = formalizedAmount,
                        Source = CashLossAllocationSource.OwnerManual,
                        Reason = string.IsNullOrWhiteSpace(note)
                            ? "Недостача подтверждена владельцем"
                            : note.Trim()
                    });
                }
                else
                    item.ResolvedAmount += item.Amount;

                item.Amount = 0;
            }
            if (IsExtraKind(item.Kind))
            {
                foreach (var contribution in item.ExtraContributions)
                {
                    contribution.ResolvedAmount += contribution.Amount;
                    contribution.Amount = 0;
                }
            }

            item.Status = CashReconciliationStatus.Resolved;
            item.Stage = CashReconciliationStage.Ready;
            item.Resolution = resolutionType switch
            {
                "PaymentTypeMistake" => CashReconciliationResolution.PairedTender,
                "RealShortage" => CashReconciliationResolution.FormalizedLoss,
                "ConfirmedExtra" => CashReconciliationResolution.OwnerBaseline,
                _ => CashReconciliationResolution.Legacy
            };
            item.ClosedAtCheckpointNumber = DateTime.UtcNow.Ticks;
                item.ResolvedAt = ClubClock.Current.LocalNow;
            item.ResolvedBy = string.IsNullOrWhiteSpace(resolvedBy)
                ? "Владелец"
                : resolvedBy.Trim();
            item.ResolutionNote = BuildResolutionNote(resolutionType, note);

            var period = BusinessCalendarService.GetBusinessMonth(item.CreatedAt);
            CashConstitutionEngine.ValidateConservation(
                _items,
                period.StartInclusive,
                period.EndExclusive);

            return item;
        }

        private static string BuildResolutionNote(string resolutionType, string note)
        {
            string title = resolutionType switch
            {
                "PaymentTypeMistake" => "Перепутали тип оплаты",
                "RealShortage" => "Реальная недостача",
                "ConfirmedExtra" => "Подтверждённый излишек",
                _ => "Закрыто владельцем"
            };

            if (string.IsNullOrWhiteSpace(note))
                return title;

            return $"{title}: {note.Trim()}";
        }

        public static void Clear()
        {
            _items.Clear();

            try
            {
                if (File.Exists(FilePath))
                    File.Delete(FilePath);
            }
            catch
            {
                // Ошибка очистки сверок не должна ломать работу кассы.
            }
        }

        private static (DateTime fromInclusive, DateTime toExclusive) LimitToSingleMonth(
            DateTime fromInclusive,
            DateTime toExclusive)
        {
            DateTime nextMonthStart = fromInclusive.AddMonths(1);

            if (toExclusive > nextMonthStart || toExclusive <= fromInclusive)
                toExclusive = nextMonthStart;

            return (fromInclusive, toExclusive);
        }

        private static bool IsExtraKind(CashReconciliationKind kind)
        {
            return kind == CashReconciliationKind.CashExtra ||
                   kind == CashReconciliationKind.CashlessExtra;
        }

        private static bool IsShortageKind(CashReconciliationKind kind)
        {
            return kind == CashReconciliationKind.CashShortage ||
                   kind == CashReconciliationKind.CashlessShortage;
        }

        private static void ResolveIfEmpty(
            CashReconciliationItem item,
            string resolvedBy,
            string note)
        {
            NormalizeItem(item);

            if (item.Amount > 0 ||
                item.Status == CashReconciliationStatus.Resolved)
            {
                return;
            }

            item.Status = CashReconciliationStatus.Resolved;
            item.ResolvedAt = ClubClock.Current.LocalNow;
            item.ResolvedBy = resolvedBy;
            item.ResolutionNote = note;
        }

        private static void AppendNote(CashReconciliationItem item, string note)
        {
            if (string.IsNullOrWhiteSpace(note))
                return;

            item.Note = string.IsNullOrWhiteSpace(item.Note)
                ? note.Trim()
                : $"{item.Note.Trim()}\n{note.Trim()}";
        }

        private static void ApplyAutomaticSettlement(CashReconciliationItem item, int amount)
        {
            NormalizeItem(item);

            amount = Math.Max(0, Math.Min(item.Amount, amount));
            if (amount <= 0)
                return;

            item.Amount -= amount;
            item.ResolvedAmount += amount;
        }

        private static void NormalizeItem(CashReconciliationItem item)
        {
            if (item == null)
                return;

            if (item.Amount < 0)
                item.Amount = 0;

            if (item.ResolvedAmount < 0)
                item.ResolvedAmount = 0;

            if (item.FormalizedAmount < 0)
                item.FormalizedAmount = 0;

            if (item.OriginalAmount <= 0)
            {
                int differenceAmount = Math.Abs(item.ActualAmount - item.ExpectedAmount);
                item.OriginalAmount = Math.Max(item.Amount, differenceAmount);
            }

            if (item.Status == CashReconciliationStatus.Resolved && item.Amount > 0)
            {
                if (LooksLikeFormalizedShortage(item))
                    item.FormalizedAmount += item.Amount;
                else
                    item.ResolvedAmount += item.Amount;

                item.Amount = 0;
            }

            int knownTotal = item.Amount + item.ResolvedAmount + item.FormalizedAmount;
            if (item.OriginalAmount < knownTotal)
                item.OriginalAmount = knownTotal;
        }

        private static bool NormalizeLegacyOrigin(CashReconciliationItem item)
        {
            if (item == null ||
                item.Origin != CashReconciliationOrigin.Unknown)
            {
                return false;
            }

            if (item.Kind == CashReconciliationKind.CashExtra ||
                item.Kind == CashReconciliationKind.CashShortage)
            {
                item.Origin = CashReconciliationOrigin.CashAcceptance;
                return true;
            }

            if (item.Kind != CashReconciliationKind.CashlessExtra &&
                item.Kind != CashReconciliationKind.CashlessShortage)
            {
                return false;
            }

            bool isRawDifference =
                item.Title.Contains("Сырые потери", StringComparison.OrdinalIgnoreCase) ||
                item.Title.Contains(
                    "Излишек после корректировки",
                    StringComparison.OrdinalIgnoreCase) ||
                item.Note.Contains(
                    "Итоговая сырая корректировка",
                    StringComparison.OrdinalIgnoreCase) ||
                item.Note.Contains(
                    "Восстановлен непокрытый остаток",
                    StringComparison.OrdinalIgnoreCase);

            item.Origin = isRawDifference
                ? CashReconciliationOrigin.BalanceRawDifference
                : CashReconciliationOrigin.CashlessVerification;
            return true;
        }

        private static bool LooksLikeFormalizedShortage(CashReconciliationItem item)
        {
            if (!IsShortageKind(item.Kind))
                return false;

            string text = $"{item.ResolutionNote} {item.Note}";
            return text.Contains("Реальная недостача", StringComparison.OrdinalIgnoreCase) ||
                   text.Contains("Оформлено", StringComparison.OrdinalIgnoreCase) ||
                   text.Contains("штраф", StringComparison.OrdinalIgnoreCase);
        }

        private static List<CashReconciliationItem> Load(out bool originMigrated)
        {
            originMigrated = false;

            try
            {
                if (!File.Exists(FilePath))
                    return new List<CashReconciliationItem>();

                string json = File.ReadAllText(FilePath);
                var items = JsonSerializer.Deserialize<List<CashReconciliationItem>>(json);

                items ??= new List<CashReconciliationItem>();

                foreach (var item in items)
                {
                    NormalizeItem(item);
                    originMigrated |= NormalizeLegacyOrigin(item);
                }

                return items;
            }
            catch
            {
                return new List<CashReconciliationItem>();
            }
        }

        private static void Save()
        {
            Directory.CreateDirectory(FolderPath);

            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            string json = JsonSerializer.Serialize(_items, options);
            string temporaryPath = FilePath + ".tmp";
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, FilePath, true);
        }

        private static T MutateAndSave<T>(Func<T> mutation)
        {
            string snapshot = JsonSerializer.Serialize(_items);
            try
            {
                T result = mutation();
                Save();
                return result;
            }
            catch
            {
                var restored = JsonSerializer.Deserialize<List<CashReconciliationItem>>(snapshot)
                    ?? new List<CashReconciliationItem>();
                _items.Clear();
                _items.AddRange(restored);
                throw;
            }
        }
    }
}
