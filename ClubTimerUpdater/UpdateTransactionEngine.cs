using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Win32;

namespace ClubTimerUpdater
{
    public enum UpdateTransactionPhase
    {
        ValidatingPackage,
        PackagePrepared,
        BackupCreated,
        FilesPrepared,
        Committing,
        ValidatingInstall,
        Completed,
        RollingBack,
        RolledBack
    }

    public sealed class UpdateTransactionRequest
    {
        public string PackagePath { get; set; } = "";
        public string ExpectedSha256 { get; set; } = "";
        public string TargetDir { get; set; } = "";
        public string BackupRoot { get; set; } = "";
        public string JournalPath { get; set; } = "";
        public string MainExe { get; set; } = "ClubTimerXbox.exe";
        public string Version { get; set; } = "";
        public string SessionToken { get; set; } = "";
        public string InstallMode { get; set; } = "";
        public bool Restart { get; set; } = true;
        public bool ReportOnly { get; set; }
        public string RecoveryUpdaterPath { get; set; } = "";
        public string StatusFile { get; set; } = "";
    }

    public sealed class PreparedUpdatePackage : IDisposable
    {
        public string StagingDir { get; init; } = "";
        public string SourceDir { get; init; } = "";

        public void Dispose()
        {
            TryDeleteDirectory(StagingDir);
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, recursive: true);
            }
            catch
            {
            }
        }
    }

    public sealed class UpdateRolledBackException : Exception
    {
        public UpdateRolledBackException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }

    public static class UpdateTransactionEngine
    {
        private const string RunOnceValueName = "ClubTimerXboxUpdateRecovery";
        private const string TemporarySuffixMarker = ".ctupdate-";
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        public static PreparedUpdatePackage PreparePackage(
            UpdateTransactionRequest request,
            Action<UpdateTransactionPhase>? phaseChanged = null)
        {
            ValidateRequest(request);
            phaseChanged?.Invoke(UpdateTransactionPhase.ValidatingPackage);
            VerifyFileHash(request.PackagePath, request.ExpectedSha256);

            string stagingRoot = Path.Combine(
                Path.GetDirectoryName(request.JournalPath) ?? Path.GetTempPath(),
                "update-staging");
            string stagingDir = Path.Combine(stagingRoot, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stagingDir);

            try
            {
                ExtractArchiveSafely(request.PackagePath, stagingDir);
                string sourceDir = FindPackageRoot(stagingDir, request.MainExe);
                ValidatePreparedTree(sourceDir, request.MainExe);
                phaseChanged?.Invoke(UpdateTransactionPhase.PackagePrepared);
                return new PreparedUpdatePackage
                {
                    StagingDir = stagingDir,
                    SourceDir = sourceDir
                };
            }
            catch
            {
                TryDeleteDirectory(stagingDir);
                throw;
            }
        }

        public static string InstallPrepared(
            PreparedUpdatePackage prepared,
            UpdateTransactionRequest request,
            Action<UpdateTransactionPhase>? phaseChanged = null,
            Action<UpdateTransactionPhase>? failureHook = null)
        {
            ValidateRequest(request);
            ValidatePreparedTree(prepared.SourceDir, request.MainExe);

            string backupPath = CreateBackup(request.TargetDir, request.BackupRoot);
            phaseChanged?.Invoke(UpdateTransactionPhase.BackupCreated);
            failureHook?.Invoke(UpdateTransactionPhase.BackupCreated);

            var journal = UpdateTransactionJournal.FromRequest(request, backupPath, prepared.StagingDir);
            journal.Phase = UpdateTransactionPhase.BackupCreated.ToString();
            SaveJournal(request.JournalPath, journal);
            RegisterRecovery(request);

            try
            {
                string transactionId = Guid.NewGuid().ToString("N");
                List<PreparedFile> files = PrepareTargetFiles(
                    prepared.SourceDir,
                    request.TargetDir,
                    transactionId);
                journal.Phase = UpdateTransactionPhase.FilesPrepared.ToString();
                SaveJournal(request.JournalPath, journal);
                phaseChanged?.Invoke(UpdateTransactionPhase.FilesPrepared);
                failureHook?.Invoke(UpdateTransactionPhase.FilesPrepared);

                journal.Phase = UpdateTransactionPhase.Committing.ToString();
                SaveJournal(request.JournalPath, journal);
                phaseChanged?.Invoke(UpdateTransactionPhase.Committing);

                foreach (PreparedFile file in OrderForCommit(files, request.MainExe))
                {
                    File.Move(file.TemporaryPath, file.TargetPath, overwrite: true);
                    failureHook?.Invoke(UpdateTransactionPhase.Committing);
                }

                journal.Phase = UpdateTransactionPhase.ValidatingInstall.ToString();
                SaveJournal(request.JournalPath, journal);
                phaseChanged?.Invoke(UpdateTransactionPhase.ValidatingInstall);
                ValidateInstalledTree(prepared.SourceDir, request.TargetDir);
                failureHook?.Invoke(UpdateTransactionPhase.ValidatingInstall);

                journal.Phase = UpdateTransactionPhase.Completed.ToString();
                SaveJournal(request.JournalPath, journal);
                phaseChanged?.Invoke(UpdateTransactionPhase.Completed);
                ClearRecoveryRegistration();
                TryDeleteFile(request.JournalPath);
                CleanupTemporaryFiles(request.TargetDir);
                return backupPath;
            }
            catch (Exception installError)
            {
                phaseChanged?.Invoke(UpdateTransactionPhase.RollingBack);
                journal.Phase = UpdateTransactionPhase.RollingBack.ToString();
                journal.Error = installError.Message;
                SaveJournal(request.JournalPath, journal);

                try
                {
                    RestoreBackup(request.TargetDir, backupPath);
                    ValidateInstalledTree(backupPath, request.TargetDir);
                    journal.Phase = UpdateTransactionPhase.RolledBack.ToString();
                    SaveJournal(request.JournalPath, journal);
                    phaseChanged?.Invoke(UpdateTransactionPhase.RolledBack);
                    ClearRecoveryRegistration();
                    CleanupTemporaryFiles(request.TargetDir);
                    throw new UpdateRolledBackException(
                        "Установка не завершилась. Предыдущая версия восстановлена.",
                        installError);
                }
                catch (UpdateRolledBackException)
                {
                    throw;
                }
                catch (Exception rollbackError)
                {
                    journal.Phase = "RecoveryRequired";
                    journal.Error = $"{installError.Message} | rollback: {rollbackError.Message}";
                    SaveJournal(request.JournalPath, journal);
                    throw new AggregateException(
                        "Установка и автоматический откат не завершились. Восстановление продолжится при запуске Windows.",
                        installError,
                        rollbackError);
                }
            }
        }

        public static UpdateRecoveryResult RecoverFromJournal(string journalPath)
        {
            UpdateTransactionJournal journal = LoadJournal(journalPath);
            if (string.IsNullOrWhiteSpace(journal.TargetDir) ||
                string.IsNullOrWhiteSpace(journal.BackupPath) ||
                !Directory.Exists(journal.BackupPath))
            {
                throw new InvalidOperationException("Журнал восстановления не содержит резервную копию.");
            }

            bool installationCompleted = journal.Phase.Equals(
                UpdateTransactionPhase.Completed.ToString(),
                StringComparison.OrdinalIgnoreCase);
            bool rollbackCompleted = journal.Phase.Equals(
                UpdateTransactionPhase.RolledBack.ToString(),
                StringComparison.OrdinalIgnoreCase);

            if (installationCompleted)
            {
                if (!File.Exists(Path.Combine(journal.TargetDir, journal.MainExe)))
                    throw new InvalidOperationException("Подтверждённая версия не найдена в папке приложения.");
            }
            else if (!rollbackCompleted)
            {
                RestoreBackup(journal.TargetDir, journal.BackupPath);
                ValidateInstalledTree(journal.BackupPath, journal.TargetDir);
                journal.Phase = UpdateTransactionPhase.RolledBack.ToString();
                SaveJournal(journalPath, journal);
            }
            else
            {
                ValidateInstalledTree(journal.BackupPath, journal.TargetDir);
            }

            CleanupTemporaryFiles(journal.TargetDir);
            TryDeleteDirectory(journal.StagingDir);
            ClearRecoveryRegistration();
            TryDeleteFile(journalPath);

            return new UpdateRecoveryResult
            {
                TargetDir = journal.TargetDir,
                MainExe = journal.MainExe,
                StatusFile = journal.StatusFile,
                Version = journal.Version,
                SessionToken = journal.SessionToken,
                InstallMode = journal.InstallMode,
                Restart = journal.Restart,
                ReportOnly = journal.ReportOnly,
                Result = installationCompleted ? "done" : "rolled_back"
            };
        }

        private static void ValidateRequest(UpdateTransactionRequest request)
        {
            if (!File.Exists(request.PackagePath))
                throw new FileNotFoundException("Пакет обновления не найден.", request.PackagePath);
            if (!Directory.Exists(request.TargetDir))
                throw new DirectoryNotFoundException("Папка приложения не найдена.");
            if (string.IsNullOrWhiteSpace(request.JournalPath))
                throw new InvalidOperationException("Не задан путь журнала обновления.");

            string target = Path.GetFullPath(request.TargetDir).TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
            string? root = Path.GetPathRoot(target)?.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
            if (string.IsNullOrWhiteSpace(root) ||
                target.Equals(root, StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(Path.Combine(target, request.MainExe)))
            {
                throw new InvalidOperationException("Небезопасная папка назначения обновления.");
            }
        }

        private static void VerifyFileHash(string path, string expected)
        {
            if (string.IsNullOrWhiteSpace(expected))
                throw new InvalidOperationException("SHA-256 пакета не указан.");
            using FileStream stream = File.OpenRead(path);
            string actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            string cleanExpected = expected.Replace(" ", "").ToLowerInvariant();
            if (!actual.Equals(cleanExpected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("SHA-256 пакета не совпадает.");
        }

        private static void ExtractArchiveSafely(string packagePath, string destinationRoot)
        {
            string root = Path.GetFullPath(destinationRoot).TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            using ZipArchive archive = ZipFile.OpenRead(packagePath);
            if (archive.Entries.Count == 0)
                throw new InvalidOperationException("Архив обновления пуст.");

            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                string destination = Path.GetFullPath(Path.Combine(destinationRoot, entry.FullName));
                if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Архив содержит небезопасный путь.");

                if (string.IsNullOrEmpty(entry.Name))
                {
                    Directory.CreateDirectory(destination);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                entry.ExtractToFile(destination, overwrite: true);
            }
        }

        private static string FindPackageRoot(string extractDir, string mainExe)
        {
            if (File.Exists(Path.Combine(extractDir, mainExe)))
                return extractDir;

            string? candidate = Directory.GetDirectories(extractDir, "*", SearchOption.AllDirectories)
                .Where(dir => File.Exists(Path.Combine(dir, mainExe)))
                .OrderBy(dir => dir.Length)
                .FirstOrDefault();
            return candidate ?? throw new InvalidOperationException(
                $"В пакете отсутствует {mainExe}.");
        }

        private static void ValidatePreparedTree(string sourceDir, string mainExe)
        {
            if (!File.Exists(Path.Combine(sourceDir, mainExe)))
                throw new InvalidOperationException($"В пакете отсутствует {mainExe}.");
            if (Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories).Length < 2)
                throw new InvalidOperationException("Пакет обновления неполный.");
        }

        private static string CreateBackup(string targetDir, string backupRoot)
        {
            Directory.CreateDirectory(backupRoot);
            string backupPath = Path.Combine(
                backupRoot,
                DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N")[..8]);
            CopyDirectory(targetDir, backupPath, overwrite: false, skipTemporaryFiles: true);
            ValidateInstalledTree(targetDir, backupPath);
            return backupPath;
        }

        private static List<PreparedFile> PrepareTargetFiles(
            string sourceDir,
            string targetDir,
            string transactionId)
        {
            var files = new List<PreparedFile>();
            foreach (string sourcePath in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                string relativePath = Path.GetRelativePath(sourceDir, sourcePath);
                string targetPath = Path.Combine(targetDir, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                string temporaryPath = targetPath + TemporarySuffixMarker + transactionId + ".new";
                File.Copy(sourcePath, temporaryPath, overwrite: true);
                files.Add(new PreparedFile(sourcePath, targetPath, temporaryPath, relativePath));
            }
            return files;
        }

        private static IEnumerable<PreparedFile> OrderForCommit(
            IEnumerable<PreparedFile> files,
            string mainExe)
        {
            return files.OrderBy(file => file.RelativePath.Equals(
                mainExe,
                StringComparison.OrdinalIgnoreCase) ? 1 : 0);
        }

        private static void RestoreBackup(string targetDir, string backupPath)
        {
            ClearDirectory(targetDir);
            CopyDirectory(backupPath, targetDir, overwrite: true, skipTemporaryFiles: false);
        }

        private static void ClearDirectory(string directory)
        {
            foreach (string file in Directory.GetFiles(directory, "*", SearchOption.TopDirectoryOnly))
                File.Delete(file);
            foreach (string child in Directory.GetDirectories(directory, "*", SearchOption.TopDirectoryOnly))
                Directory.Delete(child, recursive: true);
        }

        private static void CopyDirectory(
            string sourceDir,
            string targetDir,
            bool overwrite,
            bool skipTemporaryFiles)
        {
            Directory.CreateDirectory(targetDir);
            foreach (string directory in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(sourceDir, directory);
                Directory.CreateDirectory(Path.Combine(targetDir, relative));
            }

            foreach (string file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                if (skipTemporaryFiles && file.Contains(TemporarySuffixMarker, StringComparison.OrdinalIgnoreCase))
                    continue;
                string relative = Path.GetRelativePath(sourceDir, file);
                string target = Path.Combine(targetDir, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target, overwrite);
            }
        }

        private static void ValidateInstalledTree(string expectedDir, string actualDir)
        {
            foreach (string expectedPath in Directory.GetFiles(expectedDir, "*", SearchOption.AllDirectories))
            {
                if (expectedPath.Contains(TemporarySuffixMarker, StringComparison.OrdinalIgnoreCase))
                    continue;
                string relative = Path.GetRelativePath(expectedDir, expectedPath);
                string actualPath = Path.Combine(actualDir, relative);
                if (!File.Exists(actualPath))
                    throw new InvalidOperationException($"После установки отсутствует файл {relative}.");
                var expectedInfo = new FileInfo(expectedPath);
                var actualInfo = new FileInfo(actualPath);
                if (expectedInfo.Length != actualInfo.Length)
                    throw new InvalidOperationException($"Размер файла {relative} не совпадает.");
            }
        }

        private static void CleanupTemporaryFiles(string targetDir)
        {
            try
            {
                foreach (string file in Directory.GetFiles(targetDir, "*", SearchOption.AllDirectories)
                             .Where(path => path.Contains(TemporarySuffixMarker, StringComparison.OrdinalIgnoreCase)))
                    TryDeleteFile(file);
            }
            catch
            {
            }
        }

        private static void RegisterRecovery(UpdateTransactionRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.RecoveryUpdaterPath) ||
                !File.Exists(request.RecoveryUpdaterPath))
                return;
            string command = $"\"{request.RecoveryUpdaterPath}\" --recover-journal \"{request.JournalPath}\"";
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\RunOnce");
            key.SetValue(RunOnceValueName, command, RegistryValueKind.String);
        }

        private static void ClearRecoveryRegistration()
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\RunOnce",
                    writable: true);
                key?.DeleteValue(RunOnceValueName, throwOnMissingValue: false);
            }
            catch
            {
            }
        }

        private static void SaveJournal(string path, UpdateTransactionJournal journal)
        {
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
            journal.UpdatedAtUtc = DateTime.UtcNow;
            string temporaryPath = path + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(journal, JsonOptions));
            File.Move(temporaryPath, path, true);
        }

        private static UpdateTransactionJournal LoadJournal(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("Журнал восстановления не найден.", path);
            return JsonSerializer.Deserialize<UpdateTransactionJournal>(
                File.ReadAllText(path), JsonOptions)
                ?? throw new InvalidOperationException("Журнал восстановления повреждён.");
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
            }
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, recursive: true);
            }
            catch
            {
            }
        }

        private sealed record PreparedFile(
            string SourcePath,
            string TargetPath,
            string TemporaryPath,
            string RelativePath);

        private sealed class UpdateTransactionJournal
        {
            public string Phase { get; set; } = "";
            public string TargetDir { get; set; } = "";
            public string BackupPath { get; set; } = "";
            public string StagingDir { get; set; } = "";
            public string MainExe { get; set; } = "";
            public string StatusFile { get; set; } = "";
            public string Version { get; set; } = "";
            public string SessionToken { get; set; } = "";
            public string InstallMode { get; set; } = "";
            public bool Restart { get; set; }
            public bool ReportOnly { get; set; }
            public string Error { get; set; } = "";
            public DateTime UpdatedAtUtc { get; set; }

            public static UpdateTransactionJournal FromRequest(
                UpdateTransactionRequest request,
                string backupPath,
                string stagingDir) => new UpdateTransactionJournal
                {
                    TargetDir = request.TargetDir,
                    BackupPath = backupPath,
                    StagingDir = stagingDir,
                    MainExe = request.MainExe,
                    StatusFile = request.StatusFile,
                    Version = request.Version,
                    SessionToken = request.SessionToken,
                    InstallMode = request.InstallMode,
                    Restart = request.Restart,
                    ReportOnly = request.ReportOnly
                };
        }
    }

    public sealed class UpdateRecoveryResult
    {
        public string TargetDir { get; set; } = "";
        public string MainExe { get; set; } = "";
        public string StatusFile { get; set; } = "";
        public string Version { get; set; } = "";
        public string SessionToken { get; set; } = "";
        public string InstallMode { get; set; } = "";
        public bool Restart { get; set; }
        public bool ReportOnly { get; set; }
        public string Result { get; set; } = "rolled_back";
    }
}
