using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public static class PaymentStorageService
    {
        private static readonly string FolderPath =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ClubTimerXbox"
            );

        private static readonly string FilePath =
            Path.Combine(FolderPath, "payments.json");

        public static List<PaymentRecord> Load()
        {
            try
            {
                if (!File.Exists(FilePath))
                    return new List<PaymentRecord>();

                string json = File.ReadAllText(FilePath);

                var items = JsonSerializer.Deserialize<List<PaymentRecord>>(json);

                if (items == null)
                    return new List<PaymentRecord>();

                return items;
            }
            catch
            {
                return new List<PaymentRecord>();
            }
        }

        public static void Save(List<PaymentRecord> items)
        {
            Directory.CreateDirectory(FolderPath);

            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            string json = JsonSerializer.Serialize(items, options);

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