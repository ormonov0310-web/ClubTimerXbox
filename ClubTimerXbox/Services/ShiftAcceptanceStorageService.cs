using System;
using System.IO;
using System.Text.Json;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public static class ShiftAcceptanceStorageService
    {
        private static readonly string FolderPath =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ClubTimerXbox"
            );

        private static readonly string FilePath =
            Path.Combine(FolderPath, "shift_acceptance_status.json");

        public static ShiftAcceptanceStatus Load()
        {
            try
            {
                if (!File.Exists(FilePath))
                    return new ShiftAcceptanceStatus();

                string json = File.ReadAllText(FilePath);

                var status = JsonSerializer.Deserialize<ShiftAcceptanceStatus>(json);

                if (status == null)
                    return new ShiftAcceptanceStatus();

                return status;
            }
            catch
            {
                return new ShiftAcceptanceStatus();
            }
        }

        public static void Save(ShiftAcceptanceStatus status)
        {
            Directory.CreateDirectory(FolderPath);

            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            string json = JsonSerializer.Serialize(status, options);

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