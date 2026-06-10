using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public static class CashlessStorageService
    {
        private static readonly string FolderPath =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ClubTimerXbox"
            );

        private static readonly string FilePath =
            Path.Combine(FolderPath, "cashless_days.json");

        public static List<CashlessDayRecord> Load()
        {
            try
            {
                if (!File.Exists(FilePath))
                    return new List<CashlessDayRecord>();

                string json = File.ReadAllText(FilePath);

                var records = JsonSerializer.Deserialize<List<CashlessDayRecord>>(json);

                if (records == null)
                    return new List<CashlessDayRecord>();

                return records;
            }
            catch
            {
                return new List<CashlessDayRecord>();
            }
        }

        public static void Save(List<CashlessDayRecord> records)
        {
            Directory.CreateDirectory(FolderPath);

            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            string json = JsonSerializer.Serialize(records, options);

            File.WriteAllText(FilePath, json);
        }

        public static void Clear()
        {
            try
            {
                if (File.Exists(FilePath))
                    File.Delete(FilePath);
            }
            catch
            {
                // Пока ничего не делаем.
            }
        }
    }
}