using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public static class ProductStockStorageService
    {
        private static readonly string StockFolderPath =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ClubTimerXbox"
            );

        private static readonly string StockFilePath =
            Path.Combine(StockFolderPath, "product_stock.json");

        public static List<ProductStockItem> Load()
        {
            try
            {
                if (!File.Exists(StockFilePath))
                    return new List<ProductStockItem>();

                string json = File.ReadAllText(StockFilePath);

                var items = JsonSerializer.Deserialize<List<ProductStockItem>>(json);

                if (items == null)
                    return new List<ProductStockItem>();

                return items;
            }
            catch
            {
                return new List<ProductStockItem>();
            }
        }

        public static void Save(List<ProductStockItem> items)
        {
            Directory.CreateDirectory(StockFolderPath);

            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            string json = JsonSerializer.Serialize(items, options);

            File.WriteAllText(StockFilePath, json);
        }

        public static void Clear()
        {
            try
            {
                if (File.Exists(StockFilePath))
                    File.Delete(StockFilePath);
            }
            catch
            {
                // Пока ничего не делаем.
            }
        }
    }
}