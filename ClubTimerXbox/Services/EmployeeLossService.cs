using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public static class EmployeeLossService
    {
        private static readonly string FolderPath =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ClubTimerXbox"
            );

        private static readonly string FilePath =
            Path.Combine(FolderPath, "employee_losses.json");

        public static List<EmployeeLossItem> Items { get; private set; } = Load();

        public static int RenameEmployeeReferences(
            string oldEmployeeName,
            string newEmployeeName)
        {
            int changed = 0;

            foreach (var item in Items)
            {
                bool itemChanged = false;

                if (EmployeeReferenceRenameService.Matches(
                        item.ResponsibleEmployeeName,
                        oldEmployeeName))
                {
                    item.ResponsibleEmployeeName = newEmployeeName;
                    itemChanged = true;
                }

                if (EmployeeReferenceRenameService.Matches(
                        item.CheckedByEmployeeName,
                        oldEmployeeName))
                {
                    item.CheckedByEmployeeName = newEmployeeName;
                    itemChanged = true;
                }

                if (!itemChanged)
                    continue;

                item.Title = EmployeeReferenceRenameService.RenameText(
                    item.Title,
                    oldEmployeeName,
                    newEmployeeName);
                item.Description = EmployeeReferenceRenameService.RenameText(
                    item.Description,
                    oldEmployeeName,
                    newEmployeeName);
                item.Note = EmployeeReferenceRenameService.RenameText(
                    item.Note,
                    oldEmployeeName,
                    newEmployeeName);
                changed++;
            }

            if (changed > 0)
                Save();

            return changed;
        }

        public static List<EmployeeLossItem> GetAll()
        {
            return Items
                .OrderByDescending(item => item.CreatedAt)
                .ToList();
        }

        public static List<EmployeeLossItem> GetByEmployee(string employeeName)
        {
            employeeName = employeeName.Trim();

            return Items
                .Where(item =>
                    item.ResponsibleEmployeeName.Equals(employeeName, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(item => item.CreatedAt)
                .ToList();
        }

        public static List<EmployeeLossItem> GetByPeriod(DateTime fromInclusive, DateTime toExclusive)
        {
            return Items
                .Where(item =>
                    item.CreatedAt >= fromInclusive &&
                    item.CreatedAt < toExclusive)
                .OrderByDescending(item => item.CreatedAt)
                .ToList();
        }

        public static bool HasNote(string note)
        {
            note = note.Trim();

            if (string.IsNullOrWhiteSpace(note))
                return false;

            return Items.Any(item =>
                item.Note.Equals(note, StringComparison.OrdinalIgnoreCase));
        }

        public static int GetUnpaidTotalByEmployee(string employeeName)
        {
            employeeName = employeeName.Trim();

            return Items
                .Where(item =>
                    !item.IsPaid &&
                    item.ResponsibleEmployeeName.Equals(employeeName, StringComparison.OrdinalIgnoreCase))
                .Sum(item => item.Amount);
        }

        public static int GetUnpaidTotal()
        {
            return Items
                .Where(item => !item.IsPaid)
                .Sum(item => item.Amount);
        }

        public static int GetUnpaidProductTotalByEmployee(
            string employeeName,
            DateTime fromInclusive,
            DateTime toExclusive)
        {
            employeeName = employeeName.Trim();

            return Items
                .Where(item =>
                    !item.IsPaid &&
                    item.CreatedAt >= fromInclusive &&
                    item.CreatedAt < toExclusive &&
                    item.ResponsibleEmployeeName.Equals(employeeName, StringComparison.OrdinalIgnoreCase) &&
                    IsProductLoss(item))
                .Sum(item => item.Amount);
        }

        public static int GetUnpaidViolationTotalByEmployee(
            string employeeName,
            DateTime fromInclusive,
            DateTime toExclusive)
        {
            employeeName = employeeName.Trim();

            return Items
                .Where(item =>
                    !item.IsPaid &&
                    item.CreatedAt >= fromInclusive &&
                    item.CreatedAt < toExclusive &&
                    item.IsFixed &&
                    item.ResponsibleEmployeeName.Equals(employeeName, StringComparison.OrdinalIgnoreCase) &&
                    IsViolationLoss(item))
                .Sum(item => item.Amount);
        }

        public static int FormalizeViolationRecommendationsForEmployee(
            string employeeName,
            DateTime fromInclusive,
            DateTime toExclusive,
            int amount,
            string note)
        {
            employeeName = employeeName.Trim();

            if (amount <= 0 || string.IsNullOrWhiteSpace(employeeName))
                return 0;

            int remaining = amount;
            int formalized = 0;

            var recommendations = Items
                .Where(item =>
                    !item.IsPaid &&
                    !item.IsFixed &&
                    item.Amount > 0 &&
                    item.CreatedAt >= fromInclusive &&
                    item.CreatedAt < toExclusive &&
                    item.ResponsibleEmployeeName.Equals(employeeName, StringComparison.OrdinalIgnoreCase) &&
                    IsViolationLoss(item))
                .OrderBy(item => item.CreatedAt)
                .ToList();

            foreach (var item in recommendations)
            {
                if (remaining <= 0)
                    break;

                int useAmount = Math.Min(item.Amount, remaining);
                item.Amount -= useAmount;
                formalized += useAmount;
                remaining -= useAmount;

                if (!string.IsNullOrWhiteSpace(note))
                {
                    item.Note = string.IsNullOrWhiteSpace(item.Note)
                        ? note.Trim()
                        : $"{item.Note.Trim()}\n{note.Trim()}";
                }

                if (item.Amount == 0)
                    item.IsPaid = true;
            }

            if (formalized > 0)
                Save();

            return formalized;
        }

        public static int GetRawUnpaidMoneyTotalByEmployee(
            string employeeName,
            DateTime fromInclusive,
            DateTime toExclusive)
        {
            employeeName = employeeName.Trim();

            return Items
                .Where(item =>
                    !item.IsPaid &&
                    item.CreatedAt >= fromInclusive &&
                    item.CreatedAt < toExclusive &&
                    item.ResponsibleEmployeeName.Equals(employeeName, StringComparison.OrdinalIgnoreCase) &&
                    IsMoneyLoss(item))
                .Sum(item => item.Amount);
        }

        public static int GetCappedUnpaidMoneyTotalByEmployee(
            string employeeName,
            DateTime fromInclusive,
            DateTime toExclusive,
            int? moneyShortageCap)
        {
            employeeName = employeeName.Trim();

            var totals = GetCappedUnpaidMoneyTotalsByEmployee(
                fromInclusive,
                toExclusive,
                moneyShortageCap
            );

            return totals.TryGetValue(employeeName, out int amount)
                ? amount
                : 0;
        }

        public static Dictionary<string, int> GetCappedUnpaidMoneyTotalsByEmployee(
            DateTime fromInclusive,
            DateTime toExclusive,
            int? moneyShortageCap)
        {
            var fixedTotals = Items
                .Where(item =>
                    !item.IsPaid &&
                    item.CreatedAt >= fromInclusive &&
                    item.CreatedAt < toExclusive &&
                    item.Amount > 0 &&
                    IsMoneyLoss(item) &&
                    item.IsFixed)
                .GroupBy(item => item.ResponsibleEmployeeName.Trim(), StringComparer.OrdinalIgnoreCase)
                .Where(group => !string.IsNullOrWhiteSpace(group.Key))
                .ToDictionary(
                    group => group.Key,
                    group => group.Sum(item => item.Amount),
                    StringComparer.OrdinalIgnoreCase
                );

            var automaticTotals = Items
                .Where(item =>
                    !item.IsPaid &&
                    item.CreatedAt >= fromInclusive &&
                    item.CreatedAt < toExclusive &&
                    item.Amount > 0 &&
                    IsMoneyLoss(item) &&
                    !item.IsFixed)
                .GroupBy(item => item.ResponsibleEmployeeName.Trim(), StringComparer.OrdinalIgnoreCase)
                .Where(group => !string.IsNullOrWhiteSpace(group.Key))
                .ToDictionary(
                    group => group.Key,
                    group => group.Sum(item => item.Amount),
                    StringComparer.OrdinalIgnoreCase
                );

            var rawTotals = MergeMoneyTotals(fixedTotals, automaticTotals);

            if (!moneyShortageCap.HasValue)
                return rawTotals;

            int cap = Math.Max(0, moneyShortageCap.Value);
            int fixedTotal = fixedTotals.Values.Sum();
            int automaticCap = Math.Max(0, cap - fixedTotal);
            int rawTotal = automaticTotals.Values.Sum();

            var result = new Dictionary<string, int>(fixedTotals, StringComparer.OrdinalIgnoreCase);

            if (rawTotal <= automaticCap)
                return rawTotals;

            if (automaticCap == 0 || rawTotal == 0)
            {
                foreach (string employeeName in automaticTotals.Keys)
                {
                    if (!result.ContainsKey(employeeName))
                        result[employeeName] = 0;
                }

                return result;
            }

            int distributed = 0;
            var ordered = automaticTotals
                .OrderByDescending(pair => pair.Value)
                .ThenBy(pair => pair.Key)
                .ToList();

            for (int index = 0; index < ordered.Count; index++)
            {
                var pair = ordered[index];
                int amount = index == ordered.Count - 1
                    ? automaticCap - distributed
                    : (int)Math.Round(automaticCap * (pair.Value / (double)rawTotal));

                amount = Math.Max(0, Math.Min(amount, pair.Value));
                distributed += amount;
                result[pair.Key] = result.TryGetValue(pair.Key, out int fixedAmount)
                    ? fixedAmount + amount
                    : amount;
            }

            return result;
        }

        private static Dictionary<string, int> MergeMoneyTotals(
            Dictionary<string, int> first,
            Dictionary<string, int> second)
        {
            var result = new Dictionary<string, int>(first, StringComparer.OrdinalIgnoreCase);

            foreach (var pair in second)
            {
                result[pair.Key] = result.TryGetValue(pair.Key, out int amount)
                    ? amount + pair.Value
                    : pair.Value;
            }

            return result;
        }

        public static EmployeeLossItem AddLoss(
            string responsibleEmployeeName,
            string checkedByEmployeeName,
            string lossType,
            string title,
            string description,
            int amount,
            string note = "",
            string lossKind = "",
            bool isFixed = false)
        {
            if (amount < 0)
                amount = 0;

            var item = new EmployeeLossItem
            {
                Id = Guid.NewGuid(),
                CreatedAt = DateTime.Now,
                ResponsibleEmployeeName = responsibleEmployeeName.Trim(),
                CheckedByEmployeeName = checkedByEmployeeName.Trim(),
                LossType = lossType.Trim(),
                LossKind = NormalizeLossKind(lossKind, lossType, title, description, note),
                Title = title.Trim(),
                Description = description.Trim(),
                Amount = amount,
                IsPaid = false,
                IsFixed = isFixed,
                PaidAt = null,
                Note = note.Trim()
            };

            Items.Add(item);
            Save();

            return item;
        }

        public static bool TryCorrectKnownFixedLoss(
            Guid id,
            int incorrectAmount,
            int correctedAmount,
            string responsibleEmployeeName)
        {
            var item = Items.FirstOrDefault(loss => loss.Id == id);

            if (item == null ||
                !item.IsFixed ||
                item.Amount != incorrectAmount ||
                !item.ResponsibleEmployeeName.Equals(
                    responsibleEmployeeName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            item.Amount = correctedAmount;
            item.Description = item.Description.Replace(
                $"{incorrectAmount} сом",
                $"{correctedAmount} сом",
                StringComparison.Ordinal);
            item.Note = string.IsNullOrWhiteSpace(item.Note)
                ? "Исправлена ошибочная повторная месячная автокоррекция."
                : item.Note.Trim() + "\nИсправлена ошибочная повторная месячная автокоррекция.";
            Save();
            return true;
        }

        public static bool TryDeleteKnownFixedMoneyLoss(
            Guid id,
            int expectedAmount,
            string responsibleEmployeeName)
        {
            var item = Items.FirstOrDefault(loss => loss.Id == id);

            if (item == null)
                return false;

            if (!item.IsFixed ||
                item.IsPaid ||
                !IsMoneyLoss(item) ||
                item.Amount != expectedAmount ||
                !item.ResponsibleEmployeeName.Equals(
                    responsibleEmployeeName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            Items.Remove(item);
            Save();
            return true;
        }

        public static EmployeeLossItem AddProductShortage(
            string responsibleEmployeeName,
            string checkedByEmployeeName,
            string description,
            int amount)
        {
            return AddLoss(
                responsibleEmployeeName: responsibleEmployeeName,
                checkedByEmployeeName: checkedByEmployeeName,
                lossType: "Недостача товара",
                title: "Недостача товара",
                description: description,
                amount: amount,
                note: "Автоматически создано при приёмке товаров",
                lossKind: "product"
            );
        }

        public static EmployeeLossItem AddCashShortage(
            string responsibleEmployeeName,
            string checkedByEmployeeName,
            string description,
            int amount,
            bool isFixed = false)
        {
            return AddLoss(
                responsibleEmployeeName: responsibleEmployeeName,
                checkedByEmployeeName: checkedByEmployeeName,
                lossType: "Недостача наличных",
                title: "Недостача наличных",
                description: description,
                amount: amount,
                note: "Автоматически создано при приёмке налички",
                lossKind: "money",
                isFixed: isFixed
            );
        }

        public static bool IsProductLoss(EmployeeLossItem item)
        {
            if (item.LossKind.Equals("product", StringComparison.OrdinalIgnoreCase))
                return true;

            if (item.LossKind.Equals("money", StringComparison.OrdinalIgnoreCase))
                return false;

            string text = $"{item.LossType} {item.Title} {item.Description} {item.Note}";

            return text.Contains("товар", StringComparison.OrdinalIgnoreCase) ||
                   text.Contains("склад", StringComparison.OrdinalIgnoreCase) ||
                   text.Contains("приёмка товаров", StringComparison.OrdinalIgnoreCase) ||
                   text.Contains("приемка товаров", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsMoneyLoss(EmployeeLossItem item)
        {
            if (IsViolationLoss(item))
                return false;

            if (item.LossKind.Equals("money", StringComparison.OrdinalIgnoreCase))
                return true;

            if (item.LossKind.Equals("product", StringComparison.OrdinalIgnoreCase) ||
                item.LossKind.Equals("violation", StringComparison.OrdinalIgnoreCase))
                return false;

            return !IsProductLoss(item);
        }

        public static bool IsViolationLoss(EmployeeLossItem item)
        {
            if (item.LossKind.Equals("violation", StringComparison.OrdinalIgnoreCase))
                return true;

            if (item.LossKind.Equals("product", StringComparison.OrdinalIgnoreCase) ||
                item.LossKind.Equals("money", StringComparison.OrdinalIgnoreCase))
            {
                string explicitText = $"{item.LossType} {item.Title} {item.Description} {item.Note}";
                return LooksLikeViolationLoss(explicitText);
            }

            string text = $"{item.LossType} {item.Title} {item.Description} {item.Note}";
            return LooksLikeViolationLoss(text);
        }

        public static string GetLossKind(EmployeeLossItem item)
        {
            if (IsProductLoss(item))
                return "product";

            if (IsViolationLoss(item))
                return "violation";

            return "money";
        }

        private static string NormalizeLossKind(
            string lossKind,
            string lossType,
            string title,
            string description,
            string note)
        {
            lossKind = (lossKind ?? "").Trim().ToLowerInvariant();

            if (lossKind == "product" || lossKind == "money" || lossKind == "violation")
                return lossKind;

            string text = $"{lossType} {title} {description} {note}";

            if (LooksLikeViolationLoss(text))
                return "violation";

            if (text.Contains("товар", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("склад", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("приёмка товаров", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("приемка товаров", StringComparison.OrdinalIgnoreCase))
            {
                return "product";
            }

            return "money";
        }

        private static bool LooksLikeViolationLoss(string text)
        {
            return text.Contains("AutoLateOpeningPenalty", StringComparison.OrdinalIgnoreCase) ||
                   text.Contains("LateOpeningRecommendation", StringComparison.OrdinalIgnoreCase) ||
                   text.Contains("опоздан", StringComparison.OrdinalIgnoreCase) ||
                   text.Contains("позднее открытие", StringComparison.OrdinalIgnoreCase) ||
                   text.Contains("нарушен", StringComparison.OrdinalIgnoreCase) ||
                   text.Contains("нарушение", StringComparison.OrdinalIgnoreCase);
        }

        public static int ForgiveCashShortagesByPaymentMistake(
            int amount,
            DateTime fromInclusive,
            DateTime toExclusive)
        {
            if (amount <= 0)
                return 0;

            int remaining = amount;
            int forgiven = 0;

            var losses = Items
                .Where(item =>
                    !item.IsPaid &&
                    item.CreatedAt >= fromInclusive &&
                    item.CreatedAt < toExclusive &&
                    item.Amount > 0 &&
                    item.LossType.Contains("налич", StringComparison.OrdinalIgnoreCase))
                .OrderBy(item => item.CreatedAt)
                .ToList();

            foreach (var loss in losses)
            {
                if (remaining <= 0)
                    break;

                int useAmount = Math.Min(loss.Amount, remaining);

                loss.Amount -= useAmount;
                forgiven += useAmount;
                remaining -= useAmount;

                string note = $"Зачтено излишком безнала как ошибка типа оплаты: {useAmount} сом.";
                loss.Description = string.IsNullOrWhiteSpace(loss.Description)
                    ? note
                    : $"{loss.Description}\n{note}";
                loss.Note = string.IsNullOrWhiteSpace(loss.Note)
                    ? note
                    : $"{loss.Note}\n{note}";

                if (loss.Amount == 0)
                {
                    loss.IsPaid = true;
                    loss.PaidAt = DateTime.Now;
                }
            }

            if (forgiven > 0)
                Save();

            return forgiven;
        }

        public static void MarkPaid(Guid id)
        {
            var item = Items.FirstOrDefault(loss => loss.Id == id);

            if (item == null)
                return;

            item.IsPaid = true;
            item.PaidAt = DateTime.Now;

            Save();
        }

        public static void MarkUnpaid(Guid id)
        {
            var item = Items.FirstOrDefault(loss => loss.Id == id);

            if (item == null)
                return;

            item.IsPaid = false;
            item.PaidAt = null;

            Save();
        }

        public static void Delete(Guid id)
        {
            var item = Items.FirstOrDefault(loss => loss.Id == id);

            if (item == null)
                return;

            Items.Remove(item);
            Save();
        }

        public static bool DeleteFixedViolation(Guid id)
        {
            var item = Items.FirstOrDefault(loss => loss.Id == id);

            if (item == null)
                return false;

            if (!item.IsFixed || item.IsPaid || !IsViolationLoss(item))
                return false;

            Items.Remove(item);
            Save();

            return true;
        }

        public static void Clear()
        {
            Items.Clear();

            try
            {
                if (File.Exists(FilePath))
                    File.Delete(FilePath);
            }
            catch
            {
                // Если файл занят или недоступен, просто сохраняем пустой список.
                Save();
            }
        }

        private static List<EmployeeLossItem> Load()
        {
            try
            {
                if (!File.Exists(FilePath))
                    return new List<EmployeeLossItem>();

                string json = File.ReadAllText(FilePath);

                var items = JsonSerializer.Deserialize<List<EmployeeLossItem>>(json);

                if (items == null)
                    return new List<EmployeeLossItem>();

                return items;
            }
            catch
            {
                return new List<EmployeeLossItem>();
            }
        }

        private static void Save()
        {
            Directory.CreateDirectory(FolderPath);

            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            string json = JsonSerializer.Serialize(Items, options);

            AtomicFileStorageService.WriteAllText(FilePath, json);
        }
    }
}
