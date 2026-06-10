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

        public static EmployeeLossItem AddLoss(
            string responsibleEmployeeName,
            string checkedByEmployeeName,
            string lossType,
            string title,
            string description,
            int amount,
            string note = "")
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
                Title = title.Trim(),
                Description = description.Trim(),
                Amount = amount,
                IsPaid = false,
                PaidAt = null,
                Note = note.Trim()
            };

            Items.Add(item);
            Save();

            return item;
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
                note: "Автоматически создано при приёмке товаров"
            );
        }

        public static EmployeeLossItem AddCashShortage(
            string responsibleEmployeeName,
            string checkedByEmployeeName,
            string description,
            int amount)
        {
            return AddLoss(
                responsibleEmployeeName: responsibleEmployeeName,
                checkedByEmployeeName: checkedByEmployeeName,
                lossType: "Недостача наличных",
                title: "Недостача наличных",
                description: description,
                amount: amount,
                note: "Автоматически создано при приёмке налички"
            );
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

            File.WriteAllText(FilePath, json);
        }
    }
}
