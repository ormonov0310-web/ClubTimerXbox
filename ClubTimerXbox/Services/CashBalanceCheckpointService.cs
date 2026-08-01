using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public static class CashBalanceCheckpointService
    {
        private static readonly string FolderPath =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ClubTimerXbox"
            );

        private static readonly string FilePath =
            Path.Combine(FolderPath, "cash_balance_checkpoints.json");

        private static readonly List<CashBalanceCheckpointItem> _items = Load();

        public static IReadOnlyList<CashBalanceCheckpointItem> Items => _items;

        public static CashBalanceCheckpointItem AddCurrentMonthCheckpoint(
            int cashAmount,
            string note,
            string operationId = "")
        {
            if (cashAmount < 0)
                cashAmount = 0;

            operationId = operationId.Trim();
            if (!string.IsNullOrWhiteSpace(operationId))
            {
                var existing = _items.FirstOrDefault(item =>
                    item.OperationId.Equals(
                        operationId,
                        StringComparison.Ordinal));
                if (existing != null)
                    return existing;
            }

            var item = new CashBalanceCheckpointItem
            {
                Id = Guid.NewGuid(),
                CreatedAt = ClubClock.Current.LocalNow,
                MonthStart = BusinessCalendarService
                    .GetBusinessMonth(ClubClock.Current.LocalNow)
                    .StartInclusive,
                CashAmount = cashAmount,
                OperationId = operationId,
                Note = note.Trim()
            };

            _items.Add(item);
            Save();

            return item;
        }

        public static CashBalanceCheckpointItem? GetLatestByPeriod(
            DateTime fromInclusive,
            DateTime toExclusive)
        {
            return _items
                .Where(item =>
                    item.MonthStart >= fromInclusive.Date &&
                    item.MonthStart < toExclusive.Date)
                .OrderByDescending(item => item.CreatedAt)
                .FirstOrDefault();
        }

        public static DateTime GetCurrentCycleStart(
            DateTime fromInclusive,
            DateTime toExclusive)
        {
            var checkpoint = GetLatestByPeriod(fromInclusive, toExclusive);

            if (checkpoint == null || checkpoint.CreatedAt < fromInclusive)
                return fromInclusive;

            return checkpoint.CreatedAt < toExclusive
                ? checkpoint.CreatedAt
                : fromInclusive;
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
                // A balance checkpoint must not block history cleanup.
            }
        }

        private static List<CashBalanceCheckpointItem> Load()
        {
            try
            {
                if (!File.Exists(FilePath))
                    return new List<CashBalanceCheckpointItem>();

                string json = File.ReadAllText(FilePath);
                var items = JsonSerializer.Deserialize<List<CashBalanceCheckpointItem>>(json);

                return items ?? new List<CashBalanceCheckpointItem>();
            }
            catch
            {
                return new List<CashBalanceCheckpointItem>();
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
            AtomicFileStorageService.WriteAllText(FilePath, json);
        }
    }
}
