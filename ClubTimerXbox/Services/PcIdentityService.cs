using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public static class PcIdentityService
    {
        private static readonly object Sync = new object();

        private static readonly Regex ClubIdPattern = new Regex(
            @"^[a-z0-9][a-z0-9_-]{0,63}$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant
        );

        private static readonly Regex InstallationIdPattern = new Regex(
            @"^[a-zA-Z0-9_-]{16,128}$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant
        );

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        private static readonly string FolderPath =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ClubTimerXbox"
            );

        private static readonly string FilePath =
            Path.Combine(FolderPath, "pc_identity.json");

        private static readonly string BackupFilePath =
            Path.Combine(FolderPath, "pc_identity.backup.json");

        private static PcIdentity _current = LoadOrCreate();

        public static PcIdentity Current
        {
            get
            {
                lock (Sync)
                    return Clone(_current);
            }
        }

        public static bool HasAssignedClub
        {
            get
            {
                lock (Sync)
                    return IsAssignedIdentity(_current);
            }
        }

        public static bool WasRecoveredFromBackup { get; private set; }

        public static bool WasCreatedUnbound { get; private set; }

        public static bool TryNormalizeClubId(string value, out string clubId)
        {
            string normalized = value?.Trim().ToLowerInvariant() ?? "";
            Match numberedClub = Regex.Match(normalized, @"^(?:club[_-]?)?(\d+)$");

            if (numberedClub.Success &&
                int.TryParse(numberedClub.Groups[1].Value, out int clubNumber) &&
                clubNumber > 0)
            {
                normalized = $"club_{clubNumber}";
            }

            if (!ClubIdPattern.IsMatch(normalized))
            {
                clubId = "";
                return false;
            }

            clubId = normalized;
            return true;
        }

        public static void Save(PcIdentity identity)
        {
            var nextIdentity = Clone(identity);
            Normalize(nextIdentity);

            if (!IsValidIdentity(nextIdentity))
                throw new InvalidOperationException("Некорректная идентичность ПК.");

            lock (Sync)
            {
                WriteIdentity(nextIdentity, createBackup: true);
                _current = nextIdentity;
            }
        }

        public static void Activate(string clubId, string clubName)
        {
            PcIdentity identity = Current;
            identity.ClubId = clubId;
            identity.ClubName = clubName;
            identity.IsActivated = true;
            identity.ActivatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            Save(identity);
        }

        private static PcIdentity LoadOrCreate()
        {
            if (TryLoadIdentity(FilePath, out PcIdentity primaryIdentity))
            {
                TryPersistAfterLoad(primaryIdentity, createBackup: true);
                return primaryIdentity;
            }

            if (TryLoadIdentity(BackupFilePath, out PcIdentity backupIdentity))
            {
                WasRecoveredFromBackup = true;
                PreserveUnreadablePrimary();
                TryPersistAfterLoad(backupIdentity, createBackup: false);
                return backupIdentity;
            }

            WasCreatedUnbound = true;
            PreserveUnreadablePrimary();

            var newIdentity = new PcIdentity
            {
                InstallationId = Guid.NewGuid().ToString("N"),
                ClubId = "",
                ClubName = "",
                IsActivated = false,
                ActivatedAt = ""
            };

            TryPersistAfterLoad(newIdentity, createBackup: false);
            TryCreateInitialBackup(newIdentity);
            return newIdentity;
        }

        private static bool TryLoadIdentity(string path, out PcIdentity identity)
        {
            identity = new PcIdentity();

            try
            {
                if (!File.Exists(path))
                    return false;

                string json = File.ReadAllText(path, Encoding.UTF8);
                PcIdentity? loaded = JsonSerializer.Deserialize<PcIdentity>(json);

                if (loaded == null)
                    return false;

                Normalize(loaded);

                if (!IsValidIdentity(loaded))
                    return false;

                identity = loaded;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void Normalize(PcIdentity identity)
        {
            identity.InstallationId = identity.InstallationId?.Trim() ?? "";
            identity.ClubId = identity.ClubId?.Trim().ToLowerInvariant() ?? "";
            identity.ClubName = identity.ClubName?.Trim() ?? "";
            identity.ActivatedAt = identity.ActivatedAt?.Trim() ?? "";

            if (!identity.IsActivated)
            {
                identity.ClubId = "";
                identity.ClubName = "";
                identity.ActivatedAt = "";
                return;
            }

            if (string.IsNullOrWhiteSpace(identity.ClubName))
                identity.ClubName = identity.ClubId;

            if (string.IsNullOrWhiteSpace(identity.ActivatedAt))
                identity.ActivatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }

        private static bool IsValidIdentity(PcIdentity identity)
        {
            if (!InstallationIdPattern.IsMatch(identity.InstallationId))
                return false;

            if (!identity.IsActivated)
                return true;

            return IsAssignedIdentity(identity);
        }

        private static bool IsAssignedIdentity(PcIdentity identity)
        {
            return identity.IsActivated &&
                   InstallationIdPattern.IsMatch(identity.InstallationId) &&
                   ClubIdPattern.IsMatch(identity.ClubId);
        }

        private static PcIdentity Clone(PcIdentity identity)
        {
            return new PcIdentity
            {
                InstallationId = identity.InstallationId ?? "",
                ClubId = identity.ClubId ?? "",
                ClubName = identity.ClubName ?? "",
                IsActivated = identity.IsActivated,
                ActivatedAt = identity.ActivatedAt ?? ""
            };
        }

        private static void TryPersistAfterLoad(PcIdentity identity, bool createBackup)
        {
            try
            {
                WriteIdentity(identity, createBackup);
            }
            catch
            {
                // A valid identity remains usable in memory even if its backup cannot be refreshed.
            }
        }

        private static void WriteIdentity(PcIdentity identity, bool createBackup)
        {
            Directory.CreateDirectory(FolderPath);

            string json = JsonSerializer.Serialize(identity, JsonOptions);
            string temporaryPath = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            bool primaryCommitted = false;

            try
            {
                WriteTextThrough(temporaryPath, json);

                if (!File.Exists(FilePath))
                {
                    File.Move(temporaryPath, FilePath);
                    primaryCommitted = true;
                }
                else if (createBackup)
                {
                    try
                    {
                        File.Replace(
                            temporaryPath,
                            FilePath,
                            BackupFilePath,
                            ignoreMetadataErrors: true
                        );
                        primaryCommitted = true;
                    }
                    catch (PlatformNotSupportedException)
                    {
                        // Use the portable replacement path below.
                    }
                    catch (IOException)
                    {
                        // Use the portable replacement path below.
                    }

                    if (!primaryCommitted)
                    {
                        CopyFileAtomically(FilePath, BackupFilePath);
                        File.Move(temporaryPath, FilePath, overwrite: true);
                        primaryCommitted = true;
                    }
                }
                else
                {
                    File.Move(temporaryPath, FilePath, overwrite: true);
                    primaryCommitted = true;
                }

                if (createBackup && primaryCommitted)
                    TryWriteBackupSnapshot(json);
            }
            finally
            {
                TryDelete(temporaryPath);
            }
        }

        private static void TryCreateInitialBackup(PcIdentity identity)
        {
            try
            {
                string json = JsonSerializer.Serialize(identity, JsonOptions);
                TryWriteBackupSnapshot(json);
            }
            catch
            {
                // The primary unbound identity is still safe without a backup.
            }
        }

        private static void TryWriteBackupSnapshot(string json)
        {
            string temporaryPath = BackupFilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";

            try
            {
                Directory.CreateDirectory(FolderPath);
                WriteTextThrough(temporaryPath, json);
                File.Move(temporaryPath, BackupFilePath, overwrite: true);
            }
            catch
            {
                // The committed primary identity remains authoritative.
            }
            finally
            {
                TryDelete(temporaryPath);
            }
        }

        private static void CopyFileAtomically(string sourcePath, string destinationPath)
        {
            string temporaryPath = destinationPath + "." + Guid.NewGuid().ToString("N") + ".tmp";

            try
            {
                File.Copy(sourcePath, temporaryPath, overwrite: true);
                File.Move(temporaryPath, destinationPath, overwrite: true);
            }
            finally
            {
                TryDelete(temporaryPath);
            }
        }

        private static void WriteTextThrough(string path, string content)
        {
            using var stream = new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.WriteThrough
            );
            using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            writer.Write(content);
            writer.Flush();
            stream.Flush(flushToDisk: true);
        }

        private static void PreserveUnreadablePrimary()
        {
            try
            {
                if (!File.Exists(FilePath))
                    return;

                string timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
                string preservedPath = Path.Combine(
                    FolderPath,
                    $"pc_identity.corrupt-{timestamp}.json"
                );

                File.Move(FilePath, preservedPath);
            }
            catch
            {
                // The next atomic write can still replace the unreadable file.
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // Temporary cleanup must not replace the original persistence error.
            }
        }
    }
}
