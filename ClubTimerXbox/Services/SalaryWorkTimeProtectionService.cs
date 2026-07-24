using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace ClubTimerXbox.Services
{
    public class SalaryWorkTimeCheckpoint
    {
        public string MonthKey { get; set; } = "";

        public string EmployeeName { get; set; } = "";

        public double CarriedHours { get; set; }

        public double RawAnchorHours { get; set; }

        public double LastRawHours { get; set; }

        public double ProtectedHours { get; set; }

        public double LastSavedProtectedHours { get; set; }

        public DateTime UpdatedAt { get; set; }
    }

    public class SalaryWorkTimeProtectionData
    {
        public List<SalaryWorkTimeCheckpoint> Items { get; set; } =
            new List<SalaryWorkTimeCheckpoint>();
    }

    public static class SalaryWorkTimeProtectionService
    {
        private const double SaveStepHours = 1.0 / 60.0;
        private const double ResetToleranceHours = 1.0 / 120.0;

        private static readonly object Sync = new object();

        private static readonly string FolderPath =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ClubTimerXbox"
            );

        private static readonly string FilePath =
            Path.Combine(FolderPath, "salary_work_time.json");

        private static readonly string BackupPath = FilePath + ".bak";
        private static readonly string TemporaryPath = FilePath + ".tmp";

        private static readonly SalaryWorkTimeProtectionData Data = Load();

        public static double Protect(
            DateTime monthStart,
            string employeeName,
            double rawHours)
        {
            string cleanEmployeeName = employeeName.Trim();
            string monthKey = NormalizeMonthKey(monthStart);
            rawHours = NormalizeHours(rawHours);

            if (string.IsNullOrWhiteSpace(cleanEmployeeName))
                return rawHours;

            lock (Sync)
            {
                var item = Find(monthKey, cleanEmployeeName);
                bool shouldSave = false;

                if (item == null)
                {
                    item = new SalaryWorkTimeCheckpoint
                    {
                        MonthKey = monthKey,
                        EmployeeName = cleanEmployeeName,
                        LastRawHours = rawHours,
                        ProtectedHours = rawHours,
                        LastSavedProtectedHours = rawHours,
                        UpdatedAt = DateTime.Now
                    };

                    Data.Items.Add(item);
                    shouldSave = true;
                }
                else
                {
                    Normalize(item);

                    if (rawHours + ResetToleranceHours < item.LastRawHours)
                    {
                        item.CarriedHours = item.ProtectedHours;
                        item.RawAnchorHours = rawHours;
                        shouldSave = true;
                    }

                    double candidate =
                        item.CarriedHours +
                        Math.Max(0, rawHours - item.RawAnchorHours);

                    if (candidate > item.ProtectedHours)
                        item.ProtectedHours = candidate;

                    item.LastRawHours = rawHours;
                    item.UpdatedAt = DateTime.Now;

                    if (item.ProtectedHours - item.LastSavedProtectedHours >= SaveStepHours)
                    {
                        item.LastSavedProtectedHours = item.ProtectedHours;
                        shouldSave = true;
                    }
                }

                if (shouldSave)
                    SaveUnsafe();

                return item.ProtectedHours;
            }
        }

        public static void SetRecoveredHours(
            DateTime monthStart,
            string employeeName,
            double recoveredHours)
        {
            string cleanEmployeeName = employeeName.Trim();
            if (string.IsNullOrWhiteSpace(cleanEmployeeName))
                throw new InvalidOperationException("Сотрудник для восстановления часов не указан.");

            string monthKey = NormalizeMonthKey(monthStart);
            recoveredHours = NormalizeHours(recoveredHours);

            lock (Sync)
            {
                var item = Find(monthKey, cleanEmployeeName);
                if (item == null)
                {
                    item = new SalaryWorkTimeCheckpoint
                    {
                        MonthKey = monthKey,
                        EmployeeName = cleanEmployeeName
                    };
                    Data.Items.Add(item);
                }

                Normalize(item);

                item.CarriedHours = recoveredHours;
                item.RawAnchorHours = 0;
                item.ProtectedHours = Math.Max(
                    item.ProtectedHours,
                    recoveredHours + item.LastRawHours
                );
                item.LastSavedProtectedHours = item.ProtectedHours;
                item.UpdatedAt = DateTime.Now;

                SaveUnsafe();
            }
        }

        public static int RenameEmployeeReferences(
            string oldEmployeeName,
            string newEmployeeName)
        {
            lock (Sync)
            {
                int changed = 0;

                foreach (var item in Data.Items)
                {
                    if (!EmployeeReferenceRenameService.Matches(
                            item.EmployeeName,
                            oldEmployeeName))
                    {
                        continue;
                    }

                    item.EmployeeName = newEmployeeName.Trim();
                    item.UpdatedAt = DateTime.Now;
                    changed++;
                }

                if (changed > 0)
                {
                    MergeDuplicatesUnsafe();
                    SaveUnsafe();
                }

                return changed;
            }
        }

        public static void Clear()
        {
            lock (Sync)
            {
                Data.Items.Clear();
                SaveUnsafe();
            }
        }

        private static SalaryWorkTimeCheckpoint? Find(
            string monthKey,
            string employeeName)
        {
            return Data.Items.FirstOrDefault(item =>
                item.MonthKey.Equals(monthKey, StringComparison.OrdinalIgnoreCase) &&
                item.EmployeeName.Equals(employeeName, StringComparison.OrdinalIgnoreCase));
        }

        private static string NormalizeMonthKey(DateTime monthStart)
        {
            return new DateTime(monthStart.Year, monthStart.Month, 1)
                .ToString("yyyy-MM");
        }

        private static double NormalizeHours(double hours)
        {
            if (double.IsNaN(hours) || double.IsInfinity(hours) || hours < 0)
                return 0;

            return Math.Min(hours, 24 * 366);
        }

        private static void Normalize(SalaryWorkTimeCheckpoint item)
        {
            item.MonthKey = item.MonthKey.Trim();
            item.EmployeeName = item.EmployeeName.Trim();
            item.CarriedHours = NormalizeHours(item.CarriedHours);
            item.RawAnchorHours = NormalizeHours(item.RawAnchorHours);
            item.LastRawHours = NormalizeHours(item.LastRawHours);
            item.ProtectedHours = NormalizeHours(item.ProtectedHours);
            item.LastSavedProtectedHours = NormalizeHours(item.LastSavedProtectedHours);

            if (item.ProtectedHours < item.CarriedHours)
                item.ProtectedHours = item.CarriedHours;

            if (item.LastSavedProtectedHours > item.ProtectedHours)
                item.LastSavedProtectedHours = item.ProtectedHours;
        }

        private static SalaryWorkTimeProtectionData Load()
        {
            lock (Sync)
            {
                if (TryLoad(FilePath, out var data))
                    return data;

                if (TryLoad(BackupPath, out data))
                {
                    RestorePrimaryFromBackup();
                    return data;
                }

                return new SalaryWorkTimeProtectionData();
            }
        }

        private static bool TryLoad(
            string path,
            out SalaryWorkTimeProtectionData data)
        {
            data = new SalaryWorkTimeProtectionData();

            try
            {
                if (!File.Exists(path))
                    return false;

                string json = File.ReadAllText(path);
                var loaded = JsonSerializer.Deserialize<SalaryWorkTimeProtectionData>(json);
                if (loaded?.Items == null)
                    return false;

                loaded.Items = loaded.Items
                    .Where(item =>
                        item != null &&
                        !string.IsNullOrWhiteSpace(item.MonthKey) &&
                        !string.IsNullOrWhiteSpace(item.EmployeeName))
                    .ToList();

                foreach (var item in loaded.Items)
                    Normalize(item);

                data = loaded;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void MergeDuplicatesUnsafe()
        {
            Data.Items = Data.Items
                .GroupBy(
                    item => $"{item.MonthKey}\n{item.EmployeeName}",
                    StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                {
                    var items = group.ToList();
                    var target = items
                        .OrderByDescending(item => item.UpdatedAt)
                        .First();

                    target.CarriedHours = items.Max(item => item.CarriedHours);
                    target.ProtectedHours = items.Max(item => item.ProtectedHours);
                    target.LastRawHours = items.Max(item => item.LastRawHours);
                    target.LastSavedProtectedHours = target.ProtectedHours;
                    return target;
                })
                .ToList();
        }

        private static void SaveUnsafe()
        {
            Directory.CreateDirectory(FolderPath);
            MergeDuplicatesUnsafe();

            string json = JsonSerializer.Serialize(
                Data,
                new JsonSerializerOptions
                {
                    WriteIndented = true
                }
            );

            WriteDurable(TemporaryPath, json);

            if (TryLoad(FilePath, out _))
                File.Copy(FilePath, BackupPath, true);

            File.Move(TemporaryPath, FilePath, true);
        }

        private static void RestorePrimaryFromBackup()
        {
            try
            {
                Directory.CreateDirectory(FolderPath);
                File.Copy(BackupPath, FilePath, true);
            }
            catch
            {
                // Резерв уже загружен в память; основной файл восстановится при следующей записи.
            }
        }

        private static void WriteDurable(string path, string content)
        {
            byte[] bytes = new UTF8Encoding(false).GetBytes(content);

            using var stream = new FileStream(
                path,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.WriteThrough
            );

            stream.Write(bytes, 0, bytes.Length);
            stream.Flush(true);
        }
    }
}
