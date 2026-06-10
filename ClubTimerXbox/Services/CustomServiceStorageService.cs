using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public static class CustomServiceStorageService
    {
        private static readonly string FolderPath =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ClubTimerXbox"
            );

        private static readonly string FilePath =
            Path.Combine(FolderPath, "custom_services.json");

        public static List<SaleItem> Load()
        {
            try
            {
                if (!File.Exists(FilePath))
                    return new List<SaleItem>();

                string json = File.ReadAllText(FilePath);

                var items = JsonSerializer.Deserialize<List<SaleItem>>(json);

                if (items == null)
                    return new List<SaleItem>();

                return items;
            }
            catch
            {
                return new List<SaleItem>();
            }
        }

        public static void Save(List<SaleItem> items)
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