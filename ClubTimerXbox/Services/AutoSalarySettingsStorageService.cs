using System;
using System.IO;
using System.Text.Json;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public static class AutoSalarySettingsStorageService
    {
        private static readonly string FolderPath =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ClubTimerXbox"
            );

        private static readonly string FilePath =
            Path.Combine(FolderPath, "auto_salary_settings.json");

        public static AutoSalarySettings Load()
        {
            try
            {
                if (!File.Exists(FilePath))
                    return new AutoSalarySettings();

                string json = File.ReadAllText(FilePath);
                var settings = JsonSerializer.Deserialize<AutoSalarySettings>(json);

                return settings ?? new AutoSalarySettings();
            }
            catch
            {
                return new AutoSalarySettings();
            }
        }

        public static void Save(AutoSalarySettings settings)
        {
            Directory.CreateDirectory(FolderPath);

            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            string json = JsonSerializer.Serialize(settings, options);
            File.WriteAllText(FilePath, json);
        }
    }
}
