using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public static class CashStorageService
    {
        private static readonly string CashFolderPath =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ClubTimerXbox"
            );

        private static readonly string CashFilePath =
            Path.Combine(CashFolderPath, "cash_records.json");

        public static List<CashRecord> Load()
        {
            try
            {
                if (!File.Exists(CashFilePath))
                    return new List<CashRecord>();

                string json = File.ReadAllText(CashFilePath);

                var records = JsonSerializer.Deserialize<List<CashRecord>>(json);

                if (records == null)
                    return new List<CashRecord>();

                return records;
            }
            catch
            {
                return new List<CashRecord>();
            }
        }

        public static void Save(List<CashRecord> records)
        {
            Directory.CreateDirectory(CashFolderPath);

            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            string json = JsonSerializer.Serialize(records, options);

            File.WriteAllText(CashFilePath, json);
        }

        public static void Clear()
        {
            try
            {
                if (File.Exists(CashFilePath))
                    File.Delete(CashFilePath);
            }
            catch
            {
                // Пока ничего не делаем.
            }
        }
    }
}