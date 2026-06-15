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
        public const int AutoResolveLimit = 1000;

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
            AutoResolveSmallPaymentMistakes();
            Save();

            return item;
        }

        public static CashReconciliationItem AddCashlessVerification(
            int expectedAmount,
            int actualAmount,
            int amount,
            CashReconciliationStatus status,
            string note)
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
                ExpectedAmount = expectedAmount,
                ActualAmount = actualAmount,
                CheckedByEmployeeName = "Владелец",
                ResponsibleEmployeeName = "",
                Title = actualAmount >= expectedAmount
                    ? "Излишек безнала"
                    : "Недостача безнала",
                Note = note.Trim()
            };

            if (status == CashReconciliationStatus.Resolved)
            {
                item.ResolvedAt = DateTime.Now;
                item.ResolvedBy = "Система";
                item.ResolutionNote = note.Trim();
            }

            _items.Add(item);
            AutoResolveSmallPaymentMistakes();
            Save();

            return item;
        }

        public static void AutoResolveSmallPaymentMistakes()
        {
            var cashExtras = _items
                .Where(item =>
                    item.Status == CashReconciliationStatus.Open &&
                    item.Kind == CashReconciliationKind.CashExtra &&
                    item.Amount > 0 &&
                    item.Amount < AutoResolveLimit)
                .OrderBy(item => item.CreatedAt)
                .ToList();

            var cashlessShortages = _items
                .Where(item =>
                    item.Status == CashReconciliationStatus.Open &&
                    item.Kind == CashReconciliationKind.CashlessShortage &&
                    item.Amount > 0 &&
                    item.Amount < AutoResolveLimit)
                .OrderBy(item => item.CreatedAt)
                .ToList();

            foreach (var shortage in cashlessShortages)
            {
                foreach (var extra in cashExtras)
                {
                    if (shortage.Amount <= 0)
                        break;

                    if (extra.Amount <= 0)
                        continue;

                    int amount = Math.Min(shortage.Amount, extra.Amount);

                    shortage.Amount -= amount;
                    extra.Amount -= amount;

                    if (extra.Amount == 0)
                    {
                        extra.Status = CashReconciliationStatus.Resolved;
                        extra.ResolvedAt = DateTime.Now;
                        extra.ResolvedBy = "Система";
                        extra.ResolutionNote = "Автоматически закрыто: безнал указали в программе, а деньги оказались наличными.";
                    }

                    if (shortage.Amount == 0)
                    {
                        shortage.Status = CashReconciliationStatus.Resolved;
                        shortage.ResolvedAt = DateTime.Now;
                        shortage.ResolvedBy = "Система";
                        shortage.ResolutionNote = "Автоматически закрыто излишком налички: ошибка типа оплаты.";
                    }
                }
            }
        }

        public static int ConsumeOpenCashExtra(int amount)
        {
            if (amount <= 0)
                return 0;

            int remaining = amount;
            int consumed = 0;

            var extras = _items
                .Where(item =>
                    item.Status == CashReconciliationStatus.Open &&
                    item.Kind == CashReconciliationKind.CashExtra &&
                    item.Amount > 0)
                .OrderBy(item => item.CreatedAt)
                .ToList();

            foreach (var item in extras)
            {
                if (remaining <= 0)
                    break;

                int useAmount = Math.Min(item.Amount, remaining);

                item.Amount -= useAmount;
                consumed += useAmount;
                remaining -= useAmount;

                if (item.Amount == 0)
                {
                    item.Status = CashReconciliationStatus.Resolved;
                    item.ResolvedAt = DateTime.Now;
                    item.ResolvedBy = "Система";
                    item.ResolutionNote = "Закрыто как ошибка типа оплаты: безнал был принят наличными.";
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

        public static List<CashReconciliationItem> GetOpenSmallCashlessShortages()
        {
            return _items
                .Where(item =>
                    item.Status == CashReconciliationStatus.Open &&
                    item.Kind == CashReconciliationKind.CashlessShortage &&
                    item.Amount > 0 &&
                    item.Amount < AutoResolveLimit)
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
                // История сверок не должна ломать работу кассы.
            }
        }

        private static List<CashReconciliationItem> Load()
        {
            try
            {
                if (!File.Exists(FilePath))
                    return new List<CashReconciliationItem>();

                string json = File.ReadAllText(FilePath);
                var items = JsonSerializer.Deserialize<List<CashReconciliationItem>>(json);

                return items ?? new List<CashReconciliationItem>();
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
