using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public static class CashAcceptanceService
    {
        private static readonly string FolderPath =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ClubTimerXbox"
            );

        private static readonly string FilePath =
            Path.Combine(FolderPath, "cash_acceptance_history.json");

        public static List<CashAcceptanceItem> Items { get; private set; } = LoadFromFile();

        public static List<CashAcceptanceItem> GetAll()
        {
            return Items
                .OrderByDescending(item => item.CreatedAt)
                .ToList();
        }

        public static CashAcceptanceItem? GetLastAcceptance()
        {
            return Items
                .OrderByDescending(item => item.CreatedAt)
                .FirstOrDefault();
        }

        public static DateTime? GetLastAcceptanceTime()
        {
            return GetLastAcceptance()?.CreatedAt;
        }

        public static int GetLastAcceptedActualCashAmount()
        {
            var last = GetLastAcceptance();

            if (last == null)
                return 0;

            return last.ActualCashAmount;
        }

        public static CashAcceptanceItem AddItem(
            string checkedByEmployeeName,
            string responsibleEmployeeName,
            int expectedCashAmount,
            int actualCashAmount,
            string note = "Приёмка налички")
        {
            if (expectedCashAmount < 0)
                expectedCashAmount = 0;

            if (actualCashAmount < 0)
                actualCashAmount = 0;

            int difference = actualCashAmount - expectedCashAmount;

            var item = new CashAcceptanceItem
            {
                Id = Guid.NewGuid(),
                CreatedAt = DateTime.Now,
                CheckedByEmployeeName = checkedByEmployeeName.Trim(),
                ResponsibleEmployeeName = responsibleEmployeeName.Trim(),
                ExpectedCashAmount = expectedCashAmount,
                ActualCashAmount = actualCashAmount,
                Difference = difference,
                Note = note.Trim()
            };

            Items.Add(item);
            Save();

            return item;
        }

        public static List<CashAcceptanceItem> GetByPeriod(DateTime fromInclusive, DateTime toExclusive)
        {
            return Items
                .Where(item =>
                    item.CreatedAt >= fromInclusive &&
                    item.CreatedAt < toExclusive)
                .OrderByDescending(item => item.CreatedAt)
                .ToList();
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
                // Если файл занят или недоступен, просто оставляем как есть.
            }
        }

        private static List<CashAcceptanceItem> LoadFromFile()
        {
            try
            {
                if (!File.Exists(FilePath))
                    return new List<CashAcceptanceItem>();

                string json = File.ReadAllText(FilePath);

                var items = JsonSerializer.Deserialize<List<CashAcceptanceItem>>(json);

                if (items == null)
                    return new List<CashAcceptanceItem>();

                return items;
            }
            catch
            {
                return new List<CashAcceptanceItem>();
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

            File.WriteAllText(FilePath, json);
        }
    }
}
