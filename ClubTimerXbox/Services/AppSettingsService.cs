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

                Normalize(settings);
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

        private static void Normalize(ClubSettings settings)
        {
            settings.NewBranchPromo ??= new NewBranchPromoSettings();

            if (settings.NewBranchPromo.StartDate == default)
                settings.NewBranchPromo.StartDate = DateTime.Today;

            if (settings.NewBranchPromo.LastDay == default)
                settings.NewBranchPromo.LastDay = settings.NewBranchPromo.StartDate;

            if (settings.NewBranchPromo.GraceEndHour < 0 || settings.NewBranchPromo.GraceEndHour > 23)
                settings.NewBranchPromo.GraceEndHour = 6;

            if (settings.NewBranchPromo.TvPromoMinutes <= 0)
                settings.NewBranchPromo.TvPromoMinutes = 120;

            if (settings.NewBranchPromo.TvPromoPrice <= 0)
                settings.NewBranchPromo.TvPromoPrice = 120;

            if (settings.NewBranchPromo.OpenModeDiscountPercent < 0 ||
                settings.NewBranchPromo.OpenModeDiscountPercent > 100)
            {
                settings.NewBranchPromo.OpenModeDiscountPercent = 50;
            }

            if (!settings.NewBranchPromo.IsOneMinuteEndTestEnabled)
                settings.NewBranchPromo.OneMinuteEndTestEndsAt = null;
        }
    }
}
