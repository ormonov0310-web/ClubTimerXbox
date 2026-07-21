using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public static class EmployeeBonusService
    {
        private static readonly string FolderPath =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ClubTimerXbox"
            );

        private static readonly string FilePath =
            Path.Combine(FolderPath, "employee_bonuses.json");

        public static List<EmployeeBonusItem> Items { get; private set; } = Load();

        public static int RenameEmployeeReferences(
            string oldEmployeeName,
            string newEmployeeName)
        {
            int changed = 0;

            foreach (var item in Items)
            {
                bool itemChanged = false;

                if (EmployeeReferenceRenameService.Matches(item.EmployeeName, oldEmployeeName))
                {
                    item.EmployeeName = newEmployeeName;
                    itemChanged = true;
                }

                if (EmployeeReferenceRenameService.Matches(item.CreatedBy, oldEmployeeName))
                {
                    item.CreatedBy = newEmployeeName;
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
                changed++;
            }

            if (changed > 0)
                Save();

            return changed;
        }

        public static EmployeeBonusItem AddOwnerBonus(
            string employeeName,
            int amount,
            DateTime salaryMonth,
            string description = "")
        {
            if (amount < 0)
                amount = 0;

            var monthStart = new DateTime(salaryMonth.Year, salaryMonth.Month, 1);
            var item = new EmployeeBonusItem
            {
                Id = Guid.NewGuid(),
                CreatedAt = DateTime.Now,
                EmployeeName = employeeName.Trim(),
                CreatedBy = "Владелец",
                BonusType = "OwnerBonus",
                Title = "Премия от владельца",
                Description = string.IsNullOrWhiteSpace(description)
                    ? "Премия от владельца"
                    : description.Trim(),
                Amount = amount,
                SalaryMonthKey = monthStart.ToString("yyyy-MM")
            };

            Items.Add(item);
            Save();

            return item;
        }

        public static List<EmployeeBonusItem> GetSalaryMonthBonuses(
            DateTime monthStart,
            DateTime nextMonthStart)
        {
            string monthKey = new DateTime(monthStart.Year, monthStart.Month, 1)
                .ToString("yyyy-MM");

            return Items
                .Where(item =>
                    item.SalaryMonthKey.Equals(monthKey, StringComparison.OrdinalIgnoreCase) ||
                    (string.IsNullOrWhiteSpace(item.SalaryMonthKey) &&
                     item.CreatedAt >= monthStart &&
                     item.CreatedAt < nextMonthStart))
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
                Save();
            }
        }

        private static List<EmployeeBonusItem> Load()
        {
            try
            {
                if (!File.Exists(FilePath))
                    return new List<EmployeeBonusItem>();

                string json = File.ReadAllText(FilePath);
                var items = JsonSerializer.Deserialize<List<EmployeeBonusItem>>(json);

                return items ?? new List<EmployeeBonusItem>();
            }
            catch
            {
                return new List<EmployeeBonusItem>();
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
