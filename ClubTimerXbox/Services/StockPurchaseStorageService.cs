using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public static class StockPurchaseStorageService
    {
        private static readonly string FolderPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ClubTimerXbox"
        );

        private static readonly string FilePath = Path.Combine(
            FolderPath,
            "stock-purchases.json"
        );

        public static List<StockPurchase> Load()
        {
            try
            {
                if (!File.Exists(FilePath))
                    return new List<StockPurchase>();

                string json = File.ReadAllText(FilePath);

                if (string.IsNullOrWhiteSpace(json))
                    return new List<StockPurchase>();

                var items = JsonSerializer.Deserialize<List<StockPurchase>>(
                    json,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    }
                );

                return items ?? new List<StockPurchase>();
            }
            catch
            {
                return new List<StockPurchase>();
            }
        }

        public static void Save(List<StockPurchase> purchases)
        {
            try
            {
                if (!Directory.Exists(FolderPath))
                {
                    Directory.CreateDirectory(FolderPath);
                }

                string json = JsonSerializer.Serialize(
                    purchases,
                    new JsonSerializerOptions
                    {
                        WriteIndented = true
                    }
                );

                File.WriteAllText(FilePath, json);
            }
            catch
            {
                // Не ломаем программу, если файл временно недоступен.
            }
        }

        public static void Clear()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    File.Delete(FilePath);
                }
            }
            catch
            {
                // Игнорируем.
            }
        }
    }
}