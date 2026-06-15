using System;
using System.IO;
using System.Text.Json;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public static class AlarmSettingsService
    {
        private static readonly string FolderPath =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ClubTimerXbox"
            );

        private static readonly string FilePath =
            Path.Combine(FolderPath, "alarm_settings.json");

        public static AlarmSettings Current { get; private set; } = Load();

        public static AlarmSettings Load()
        {
            try
            {
                if (!File.Exists(FilePath))
                    return new AlarmSettings();

                string json = File.ReadAllText(FilePath);

                var settings = JsonSerializer.Deserialize<AlarmSettings>(json);

                if (settings == null)
                    return new AlarmSettings();

                Normalize(settings);

                return settings;
            }
            catch
            {
                return new AlarmSettings();
            }
        }

        public static void Save(AlarmSettings settings)
        {
            Directory.CreateDirectory(FolderPath);

            Normalize(settings);

            Current = settings;

            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            string json = JsonSerializer.Serialize(settings, options);

            File.WriteAllText(FilePath, json);
        }

        private static void Normalize(AlarmSettings settings)
        {
            if (settings.TriggerBeforeEndSeconds < 10)
                settings.TriggerBeforeEndSeconds = 10;

            if (settings.TriggerBeforeEndSeconds > 600)
                settings.TriggerBeforeEndSeconds = 600;

            if (settings.SoundDurationSeconds < 0)
                settings.SoundDurationSeconds = 0;

            if (settings.SoundDurationSeconds > 120)
                settings.SoundDurationSeconds = 120;

            settings.SoundName = AlarmSoundService.NormalizeSoundName(settings.SoundName);
        }

        public static void ResetToDefault()
        {
            Save(new AlarmSettings());
        }
    }
}
