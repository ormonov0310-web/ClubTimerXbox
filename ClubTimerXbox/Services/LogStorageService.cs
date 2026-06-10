using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public class LogStorageData
    {
        public List<ShiftLogItem> Shifts { get; set; } = new List<ShiftLogItem>();
        public List<GameSessionLogItem> GameSessions { get; set; } = new List<GameSessionLogItem>();
    }

    public static class LogStorageService
    {
        private static readonly string LogsFolderPath =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ClubTimerXbox"
            );

        private static readonly string LogsFilePath =
            Path.Combine(LogsFolderPath, "logs.json");

        public static LogStorageData Load()
        {
            try
            {
                if (!File.Exists(LogsFilePath))
                    return new LogStorageData();

                string json = File.ReadAllText(LogsFilePath);

                var data = JsonSerializer.Deserialize<LogStorageData>(json);

                if (data == null)
                    return new LogStorageData();

                return data;
            }
            catch
            {
                return new LogStorageData();
            }
        }

        public static void Save(
            List<ShiftLogItem> shifts,
            List<GameSessionLogItem> gameSessions)
        {
            Directory.CreateDirectory(LogsFolderPath);

            var data = new LogStorageData
            {
                Shifts = shifts,
                GameSessions = gameSessions
            };

            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            string json = JsonSerializer.Serialize(data, options);

            File.WriteAllText(LogsFilePath, json);
        }
    }
}