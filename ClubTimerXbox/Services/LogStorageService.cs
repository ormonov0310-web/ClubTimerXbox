using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
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
        private static readonly object Sync = new object();

        private static readonly string LogsFolderPath =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ClubTimerXbox"
            );

        private static readonly string LogsFilePath =
            Path.Combine(LogsFolderPath, "logs.json");

        private static readonly string BackupFilePath = LogsFilePath + ".bak";
        private static readonly string TemporaryFilePath = LogsFilePath + ".tmp";

        public static LogStorageData Load()
        {
            lock (Sync)
            {
                if (TryLoad(LogsFilePath, out var data))
                    return data;

                if (TryLoad(BackupFilePath, out data))
                {
                    RestorePrimaryFromBackup();
                    return data;
                }

                return new LogStorageData();
            }
        }

        public static void Save(
            List<ShiftLogItem> shifts,
            List<GameSessionLogItem> gameSessions)
        {
            lock (Sync)
            {
                Directory.CreateDirectory(LogsFolderPath);

                var data = new LogStorageData
                {
                    Shifts = shifts ?? new List<ShiftLogItem>(),
                    GameSessions = gameSessions ?? new List<GameSessionLogItem>()
                };

                var options = new JsonSerializerOptions
                {
                    WriteIndented = true
                };

                string json = JsonSerializer.Serialize(data, options);

                WriteDurable(TemporaryFilePath, json);

                if (TryLoad(LogsFilePath, out _))
                    File.Copy(LogsFilePath, BackupFilePath, true);

                File.Move(TemporaryFilePath, LogsFilePath, true);
            }
        }

        private static bool TryLoad(string path, out LogStorageData data)
        {
            data = new LogStorageData();

            try
            {
                if (!File.Exists(path))
                    return false;

                string json = File.ReadAllText(path);
                var loaded = JsonSerializer.Deserialize<LogStorageData>(json);
                if (loaded == null)
                    return false;

                loaded.Shifts ??= new List<ShiftLogItem>();
                loaded.GameSessions ??= new List<GameSessionLogItem>();
                data = loaded;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void RestorePrimaryFromBackup()
        {
            try
            {
                Directory.CreateDirectory(LogsFolderPath);
                File.Copy(BackupFilePath, LogsFilePath, true);
            }
            catch
            {
                // Данные уже загружены из резерва и будут записаны при следующем изменении.
            }
        }

        private static void WriteDurable(string path, string content)
        {
            byte[] bytes = new UTF8Encoding(false).GetBytes(content);

            using var stream = new FileStream(
                path,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.WriteThrough
            );

            stream.Write(bytes, 0, bytes.Length);
            stream.Flush(true);
        }
    }
}
