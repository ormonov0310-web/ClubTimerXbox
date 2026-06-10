using System;
using System.IO;
using System.Text.Json;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public static class AppRuntimeStateStorageService
    {
        private static readonly string FolderPath =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ClubTimerXbox"
            );

        private static readonly string FilePath =
            Path.Combine(FolderPath, "app_runtime_state.json");

        public static AppRuntimeState Load()
        {
            try
            {
                if (!File.Exists(FilePath))
                    return new AppRuntimeState();

                string json = File.ReadAllText(FilePath);

                var state = JsonSerializer.Deserialize<AppRuntimeState>(json);

                if (state == null)
                    return new AppRuntimeState();

                return state;
            }
            catch
            {
                return new AppRuntimeState();
            }
        }

        public static void Save(AppRuntimeState state)
        {
            Directory.CreateDirectory(FolderPath);

            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            string json = JsonSerializer.Serialize(state, options);

            File.WriteAllText(FilePath, json);
        }

        public static void SaveOpenedNow()
        {
            var state = Load();
            state.LastOpenedAt = DateTime.Now;
            Save(state);
        }

        public static void SaveClosedNow()
        {
            var state = Load();
            state.LastClosedAt = DateTime.Now;
            Save(state);
        }
    }
}