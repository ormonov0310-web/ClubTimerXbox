using System;
using System.IO;
using System.Text.Json;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public static class PcIdentityService
    {
        private const string DefaultClubId = "club_1";
        private const string DefaultClubName = "XBOX CLUB";

        private static readonly string FolderPath =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ClubTimerXbox"
            );

        private static readonly string FilePath =
            Path.Combine(FolderPath, "pc_identity.json");

        public static PcIdentity Current { get; private set; } = LoadOrCreate();

        public static void Save(PcIdentity identity)
        {
            Normalize(identity);
            Directory.CreateDirectory(FolderPath);

            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            string json = JsonSerializer.Serialize(identity, options);
            File.WriteAllText(FilePath, json);

            Current = identity;
        }

        public static void Activate(string clubId, string clubName)
        {
            var identity = Current;
            identity.ClubId = clubId;
            identity.ClubName = clubName;
            identity.IsActivated = true;
            identity.ActivatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            Save(identity);
        }

        private static PcIdentity LoadOrCreate()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    string json = File.ReadAllText(FilePath);
                    var identity = JsonSerializer.Deserialize<PcIdentity>(json);

                    if (identity != null)
                    {
                        Normalize(identity);
                        Save(identity);
                        return identity;
                    }
                }
            }
            catch
            {
                // If identity is unreadable, continue with a non-activated identity.
            }

            var newIdentity = new PcIdentity
            {
                InstallationId = Guid.NewGuid().ToString("N"),
                ClubId = HasLegacyLocalData() ? DefaultClubId : "",
                ClubName = HasLegacyLocalData() ? DefaultClubName : "",
                IsActivated = HasLegacyLocalData(),
                ActivatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            };

            Save(newIdentity);
            return newIdentity;
        }

        private static void Normalize(PcIdentity identity)
        {
            identity.InstallationId = identity.InstallationId.Trim();
            identity.ClubId = identity.ClubId.Trim();
            identity.ClubName = identity.ClubName.Trim();
            identity.ActivatedAt = identity.ActivatedAt.Trim();

            if (string.IsNullOrWhiteSpace(identity.InstallationId))
                identity.InstallationId = Guid.NewGuid().ToString("N");

            if (identity.IsActivated && string.IsNullOrWhiteSpace(identity.ClubId))
                identity.ClubId = DefaultClubId;

            if (identity.IsActivated && string.IsNullOrWhiteSpace(identity.ClubName))
                identity.ClubName = DefaultClubName;

            if (identity.IsActivated && string.IsNullOrWhiteSpace(identity.ActivatedAt))
                identity.ActivatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }

        private static bool HasLegacyLocalData()
        {
            return File.Exists(Path.Combine(FolderPath, "employees.json")) ||
                   File.Exists(Path.Combine(FolderPath, "cash_records.json")) ||
                   File.Exists(Path.Combine(FolderPath, "payments.json")) ||
                   File.Exists(Path.Combine(FolderPath, "action_log.json")) ||
                   File.Exists(Path.Combine(FolderPath, "settings.json"));
        }
    }
}
