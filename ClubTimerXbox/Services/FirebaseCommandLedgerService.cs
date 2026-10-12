using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace ClubTimerXbox.Services
{
    public sealed class FirebaseAppliedCommand
    {
        public string CommandId { get; set; } = "";

        public string CommandType { get; set; } = "";

        public string ResultMessage { get; set; } = "";

        public DateTime AppliedAt { get; set; } = DateTime.Now;
        public bool SnapshotPending { get; set; }
        public string SnapshotScope { get; set; } = "";
    }

    public static class FirebaseCommandLedgerService
    {
        private const int MaximumEntries = 5000;

        private static readonly string FolderPath =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ClubTimerXbox"
            );

        private static readonly string FilePath =
            Path.Combine(FolderPath, "firebase_applied_commands.json");

        private static readonly object Gate = new();

        private static readonly Dictionary<string, FirebaseAppliedCommand> Entries =
            Load()
                .Where(item => !string.IsNullOrWhiteSpace(item.CommandId))
                .GroupBy(item => item.CommandId, StringComparer.Ordinal)
                .Select(group => group.OrderByDescending(item => item.AppliedAt).First())
                .ToDictionary(
                    item => item.CommandId,
                    item => item,
                    StringComparer.Ordinal
                );

        public static bool TryGet(
            string commandId,
            out FirebaseAppliedCommand command)
        {
            lock (Gate)
            {
                return Entries.TryGetValue(commandId.Trim(), out command!);
            }
        }

        public static void MarkApplied(
            string commandId,
            string commandType,
            string resultMessage,
            string snapshotScope = "")
        {
            commandId = commandId.Trim();
            if (string.IsNullOrWhiteSpace(commandId))
                return;

            lock (Gate)
            {
                Entries[commandId] = new FirebaseAppliedCommand
                {
                    CommandId = commandId,
                    CommandType = commandType.Trim(),
                    ResultMessage = resultMessage,
                    SnapshotPending = !string.IsNullOrWhiteSpace(snapshotScope),
                    SnapshotScope = snapshotScope,
                    AppliedAt = DateTime.Now
                };

                foreach (string oldId in Entries.Values
                    .OrderByDescending(item => item.AppliedAt)
                    .Where(item => !item.SnapshotPending)
                    .Skip(MaximumEntries)
                    .Select(item => item.CommandId)
                    .ToList())
                {
                    Entries.Remove(oldId);
                }

                Save();
            }
        }

        public static List<FirebaseAppliedCommand> PendingSnapshots(string scope)
        {
            lock (Gate)
                return Entries.Values.Where(item => item.SnapshotPending &&
                    item.SnapshotScope == scope).ToList();
        }

        public static void MarkSnapshotPublished(string commandId)
        {
            lock (Gate)
            {
                if (!Entries.TryGetValue(commandId, out var item)) return;
                item.SnapshotPending = false;
                Save();
            }
        }

        private static List<FirebaseAppliedCommand> Load()
        {
            try
            {
                if (!File.Exists(FilePath))
                    return new List<FirebaseAppliedCommand>();

                return JsonSerializer.Deserialize<List<FirebaseAppliedCommand>>(
                           File.ReadAllText(FilePath))
                       ?? new List<FirebaseAppliedCommand>();
            }
            catch
            {
                return new List<FirebaseAppliedCommand>();
            }
        }

        private static void Save()
        {
            Directory.CreateDirectory(FolderPath);
            string temporaryPath = FilePath + ".tmp";
            string json = JsonSerializer.Serialize(
                Entries.Values.OrderBy(item => item.AppliedAt).ToList(),
                new JsonSerializerOptions { WriteIndented = true }
            );
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, FilePath, true);
        }
    }
}
