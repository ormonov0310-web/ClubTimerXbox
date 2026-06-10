using System;
using System.IO;
using System.Text.Json;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public static class AppSettingsService
    {
        private static readonly string SettingsFolderPath =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ClubTimerXbox"
            );

        private static readonly string SettingsFilePath =
            Path.Combine(SettingsFolderPath, "settings.json");

        public static ClubSettings Current { get; private set; } = Load();

        public static void Save(ClubSettings settings)
        {
            Current = settings;
            SaveToFile(settings);
        }

        private static ClubSettings Load()
        {
            try
            {
                if (!File.Exists(SettingsFilePath))
                    return new ClubSettings();

                string json = File.ReadAllText(SettingsFilePath);

                var settings = JsonSerializer.Deserialize<ClubSettings>(json);

                if (settings == null)
                    return new ClubSettings();

                return settings;
            }
            catch
            {
                return new ClubSettings();
            }
        }

        private static void SaveToFile(ClubSettings settings)
        {
            Directory.CreateDirectory(SettingsFolderPath);

            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            string json = JsonSerializer.Serialize(settings, options);

            File.WriteAllText(SettingsFilePath, json);
        }
    }
}