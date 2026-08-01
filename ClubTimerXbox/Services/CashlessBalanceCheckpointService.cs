using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public static class CashlessBalanceCheckpointService
    {
        private static readonly string FolderPath =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ClubTimerXbox"
            );

        private static readonly string FilePath =
            Path.Combine(FolderPath, "cashless_balance_checkpoints.json");

        private static readonly List<CashlessBalanceCheckpointItem> _items = Load();

        public static IReadOnlyList<CashlessBalanceCheckpointItem> Items => _items;

        public static CashlessBalanceCheckpointItem AddCurrentMonthCheckpoint(
            int cashlessAmount,
            string note)
        {
            if (cashlessAmount < 0)
                cashlessAmount = 0;

            var item = new CashlessBalanceCheckpointItem
            {
                Id = Guid.NewGuid(),
                CreatedAt = ClubClock.Current.LocalNow,
                MonthStart = BusinessCalendarService
                    .GetBusinessMonth(ClubClock.Current.LocalNow)
                    .StartInclusive,
                CashlessAmount = cashlessAmount,
                Note = note.Trim()
            };

            _items.Add(item);
            Save();

            return item;
        }

        public static CashlessBalanceCheckpointItem? GetLatestByPeriod(
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

        private static List<CashlessBalanceCheckpointItem> Load()
        {
            try
            {
                if (!File.Exists(FilePath))
                    return new List<CashlessBalanceCheckpointItem>();

                string json = File.ReadAllText(FilePath);
                var items = JsonSerializer.Deserialize<List<CashlessBalanceCheckpointItem>>(json);

                return items ?? new List<CashlessBalanceCheckpointItem>();
            }
            catch
            {
                return new List<CashlessBalanceCheckpointItem>();
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
