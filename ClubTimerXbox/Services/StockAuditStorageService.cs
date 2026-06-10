using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public static class StockAuditStorageService
    {
        private static readonly string AuditFolderPath =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ClubTimerXbox"
            );

        private static readonly string AuditFilePath =
            Path.Combine(AuditFolderPath, "stock_audit.json");

        public static List<StockAuditItem> Load()
        {
            try
            {
                if (!File.Exists(AuditFilePath))
                    return new List<StockAuditItem>();

                string json = File.ReadAllText(AuditFilePath);

                var items = JsonSerializer.Deserialize<List<StockAuditItem>>(json);

                if (items == null)
                    return new List<StockAuditItem>();

                return items;
            }
            catch
            {
                return new List<StockAuditItem>();
            }
        }

        public static void Save(List<StockAuditItem> items)
        {
            Directory.CreateDirectory(AuditFolderPath);

            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            string json = JsonSerializer.Serialize(items, options);

            File.WriteAllText(AuditFilePath, json);
        }

        public static void Clear()
        {
            try
            {
                if (File.Exists(AuditFilePath))
                    File.Delete(AuditFilePath);
            }
            catch
            {
                // Пока ничего не делаем.
            }
        }
    }
}