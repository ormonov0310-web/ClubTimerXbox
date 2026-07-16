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

        private static readonly List<CashReconciliationItem> _items = Load();

        public static IReadOnlyList<CashReconciliationItem> Items => _items;

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
                    ExpectedAmount = expectedAmount,
                    ActualAmount = actualAmount
                };

            var item = new CashReconciliationItem
            {
                Id = Guid.NewGuid(),
                CreatedAt = DateTime.Now,
                Kind = difference > 0
                    ? CashReconciliationKind.CashExtra
                    : CashReconciliationKind.CashShortage,
                Status = CashReconciliationStatus.Open,
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
                CreatedAt = DateTime.Now,
                Kind = actualAmount >= expectedAmount
                    ? CashReconciliationKind.CashlessExtra
                    : CashReconciliationKind.CashlessShortage,
                Status = status,
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
                item.ResolvedAt = DateTime.Now;
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
                    item.CreatedAt >= fromInclusive &&
                    item.CreatedAt < toExclusive &&
                    item.ExpectedAmount == 0 &&
                    item.ActualAmount == actualAmount &&
                    item.Amount == actualAmount)
                .ToList())
            {
                item.Status = CashReconciliationStatus.Resolved;
                item.ResolvedAt = DateTime.Now;
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
                CreatedAt = DateTime.Now,
                Kind = isShortage
                    ? CashReconciliationKind.CashlessShortage
                    : CashReconciliationKind.CashlessExtra,
                Status = CashReconciliationStatus.Open,
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
                    item.CreatedAt >= fromInclusive &&
                    item.CreatedAt < toExclusive &&
                    IsAutoResolvablePaymentMistakeAmount(item.Amount))
                .OrderBy(item => item.CreatedAt)
                .ToList();

            var shortages = _items
                .Where(item =>
                    item.Status == CashReconciliationStatus.Open &&
                    item.Kind == shortageKind &&
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
                        extra.ResolvedAt = DateTime.Now;
                        extra.ResolvedBy = "Система";
                        extra.ResolutionNote = extraResolvedNote;
                    }

                    if (shortage.Amount == 0)
                    {
                        shortage.Status = CashReconciliationStatus.Resolved;
                        shortage.ResolvedAt = DateTime.Now;
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
                    item.ResolvedAt = DateTime.Now;
                    item.ResolvedBy = "Система";
                    item.ResolutionNote = "Зачтено как ошибка типа оплаты: безнал был принят наличными.";
                }
            }

            Save();

            return consumed;
        }

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
                    item.ResolvedAt = DateTime.Now;
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
                    item.CreatedAt >= fromInclusive &&
                    item.CreatedAt < toExclusive &&
                    IsAutoResolvablePaymentMistakeAmount(item.Amount))
                .OrderBy(item => item.CreatedAt)
                .ToList();
        }

        public static List<CashReconciliationItem> GetRecentItems(int count = 100)
        {
            return _items
                .OrderByDescending(item => item.CreatedAt)
                .Take(count)
                .ToList();
        }

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
                item.ResolvedAt = DateTime.Now;
                item.ResolvedBy = "Система";
                item.ResolutionNote = "Активная сумма стала 0, карточка закрыта.";
            }

            Save();

            return item;
        }

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
                item.ResolvedAt = DateTime.Now;
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
                    item.ResolvedAt = DateTime.Now;
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
            DateTime toExclusive)
        {
            checkedByEmployeeName = checkedByEmployeeName.Trim();

            if (string.IsNullOrWhiteSpace(checkedByEmployeeName) || amount <= 0)
                return 0;

            (fromInclusive, toExclusive) = LimitToSingleMonth(fromInclusive, toExclusive);

            DateTime fromTime = DateTime.Now.Subtract(correctionWindow);
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
                    item.ResolvedAt = DateTime.Now;
                    item.ResolvedBy = checkedByEmployeeName;
                    item.ResolutionNote = "Закрыто повторной приёмкой: сотрудник исправил свою ошибку ввода налички.";
                }
            }

            if (resolved > 0)
                Save();

            return resolved;
        }

        public static CashReconciliationItem Resolve(
            Guid id,
            string resolvedBy,
            string resolutionType,
            string note = "")
        {
            var item = _items.FirstOrDefault(entry => entry.Id == id);

            if (item == null)
                throw new Exception("Сверочная запись не найдена.");

            if (item.Status == CashReconciliationStatus.Resolved)
                return item;

            NormalizeItem(item);

            if (item.Amount > 0)
            {
                if (resolutionType == "RealShortage")
                    item.FormalizedAmount += item.Amount;
                else
                    item.ResolvedAmount += item.Amount;

                item.Amount = 0;
            }

            item.Status = CashReconciliationStatus.Resolved;
            item.ResolvedAt = DateTime.Now;
            item.ResolvedBy = string.IsNullOrWhiteSpace(resolvedBy)
                ? "Владелец"
                : resolvedBy.Trim();
            item.ResolutionNote = BuildResolutionNote(resolutionType, note);

            Save();

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
            var monthStart = new DateTime(fromInclusive.Year, fromInclusive.Month, 1);
            var nextMonthStart = monthStart.AddMonths(1);

            if (fromInclusive < monthStart)
                fromInclusive = monthStart;

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
            item.ResolvedAt = DateTime.Now;
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

        private static bool LooksLikeFormalizedShortage(CashReconciliationItem item)
        {
            if (!IsShortageKind(item.Kind))
                return false;

            string text = $"{item.ResolutionNote} {item.Note}";
            return text.Contains("Реальная недостача", StringComparison.OrdinalIgnoreCase) ||
                   text.Contains("Оформлено", StringComparison.OrdinalIgnoreCase) ||
                   text.Contains("штраф", StringComparison.OrdinalIgnoreCase);
        }

        private static List<CashReconciliationItem> Load()
        {
            try
            {
                if (!File.Exists(FilePath))
                    return new List<CashReconciliationItem>();

                string json = File.ReadAllText(FilePath);
                var items = JsonSerializer.Deserialize<List<CashReconciliationItem>>(json);

                items ??= new List<CashReconciliationItem>();

                foreach (var item in items)
                    NormalizeItem(item);

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
            File.WriteAllText(FilePath, json);
        }
    }
}
