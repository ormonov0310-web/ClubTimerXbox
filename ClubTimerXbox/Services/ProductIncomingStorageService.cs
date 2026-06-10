using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public static class ProductIncomingStorageService
    {
        private static readonly string IncomingFolderPath =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ClubTimerXbox"
            );

        private static readonly string IncomingFilePath =
            Path.Combine(IncomingFolderPath, "product_incoming.json");

        public static List<ProductIncomingItem> Load()
        {
            try
            {
                if (!File.Exists(IncomingFilePath))
                    return new List<ProductIncomingItem>();

                string json = File.ReadAllText(IncomingFilePath);

                var items = JsonSerializer.Deserialize<List<ProductIncomingItem>>(json);

                if (items == null)
                    return new List<ProductIncomingItem>();

                return items;
            }
            catch
            {
                return new List<ProductIncomingItem>();
            }
        }

        public static void Save(List<ProductIncomingItem> items)
        {
            Directory.CreateDirectory(IncomingFolderPath);

            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            string json = JsonSerializer.Serialize(items, options);

            File.WriteAllText(IncomingFilePath, json);
        }

        public static void Clear()
        {
            try
            {
                if (File.Exists(IncomingFilePath))
                    File.Delete(IncomingFilePath);
            }
            catch
            {
                // Пока ничего не делаем.
            }
        }
    }
}