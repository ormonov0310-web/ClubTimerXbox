using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public static class AppUpdateService
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(1);
        private static DateTime _lastCheck = DateTime.MinValue;
        private static AppUpdateInfo? _lastInfo;
        private static readonly object InstallSync = new object();
        private static bool _installInProgress;
        private const int KeepLocalUpdateCopies = 2;

        private static string UpdateManifestPath =>
            $"updates/channels/{AppVersionService.UpdateChannel}";

        private static string ClubUpdateStatusPath =>
            $"clubs/{PcIdentityService.Current.ClubId}/updateStatus";

        private static string OwnerClubUpdateStatusPath =>
            $"owner/clubs/{PcIdentityService.Current.ClubId}/updateStatus";

        private static string LocalAppDataRoot =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ClubTimerXbox");

        private static string UpdatesRoot => Path.Combine(LocalAppDataRoot, "updates");

        private static string BackupsRoot => Path.Combine(LocalAppDataRoot, "backups");

        private static string UpdaterRunnerRoot => Path.Combine(LocalAppDataRoot, "updater-runner");

        private static string UpdaterStatusFilePath => Path.Combine(LocalAppDataRoot, "updater-status.json");

        public static async Task CheckAndReportAsync(IReadOnlyList<ClubPlace> places)
        {
            if (!FirebaseConnectionService.CanSync)
                return;

            try
            {
                await ReportLocalUpdaterStatusIfAnyAsync(places);
            }
            catch
            {
                // Local updater status must never interrupt the club workflow.
            }

            if (DateTime.Now - _lastCheck < CheckInterval)
                return;

            _lastCheck = DateTime.Now;

            try
            {
                await GetLatestUpdateInfoAsync(places, forceRefresh: true);
            }
            catch
            {
                // Update checks must never interrupt the club workflow.
            }
        }

        public static async Task<AppUpdateInfo> GetLatestUpdateInfoAsync(
            IReadOnlyList<ClubPlace> places,
            bool forceRefresh = false)
        {
            if (!FirebaseConnectionService.CanSync)
                return BuildUpdateInfo(null, places);

            if (!forceRefresh &&
                _lastInfo != null &&
                DateTime.Now - _lastInfo.CheckedAt < CheckInterval)
            {
                return _lastInfo.WithPlaces(places);
            }

            var manifest = await ReadManifestAsync();
            var info = BuildUpdateInfo(manifest, places);
            _lastInfo = info;
            await ReportStatusAsync(info, "checked", "");
            return info;
        }

        public static async Task<string> PrepareLatestUpdateAsync()
        {
            var manifest = await ReadManifestAsync();

            if (manifest == null || string.IsNullOrWhiteSpace(manifest.DownloadUrl))
            {
                await ReportStatusAsync(manifest, Array.Empty<ClubPlace>(), "no_update", "Release update is not configured.");
                return "Release update is not configured.";
            }

            if (!IsNewerVersion(manifest.LatestVersion, AppVersionService.Version))
            {
                await ReportStatusAsync(manifest, Array.Empty<ClubPlace>(), "current", "Current version is already installed.");
                return "Current version is already installed.";
            }

            string packagePath = await DownloadPackageAsync(manifest);
            await ReportStatusAsync(
                manifest,
                Array.Empty<ClubPlace>(),
                "downloaded",
                $"Update package downloaded: {packagePath}");

            return $"Update {manifest.LatestVersion} downloaded. Installation can be started when the club is free.";
        }

        public static async Task<InstallUpdateResult> InstallLatestUpdateAsync(
            IReadOnlyList<ClubPlace> places,
            IProgress<AppUpdateProgress>? progress = null)
        {
            if (!TryBeginInstall())
                return InstallUpdateResult.Blocked("Обновление уже запускается. Дождитесь окна обновления.");

            bool updaterStarted = false;

            try
            {
                var manifest = await ReadManifestAsync();

                if (manifest == null || string.IsNullOrWhiteSpace(manifest.DownloadUrl))
                {
                    await ReportStatusAsync(manifest, places, "no_update", "Release update is not configured.");
                    return InstallUpdateResult.Blocked("Release update is not configured.");
                }

                if (!IsNewerVersion(manifest.LatestVersion, AppVersionService.Version))
                {
                    await ReportStatusAsync(manifest, places, "current", "Current version is already installed.");
                    return InstallUpdateResult.Blocked("Current version is already installed.");
                }

                int activePlaces = places.Count(place => place.IsBusy);
                if (activePlaces > 0)
                {
                    string message = $"Club is busy. Active places: {activePlaces}.";
                    await ReportStatusAsync(manifest, places, "waiting_free_club", message);
                    return InstallUpdateResult.Blocked(message);
                }

                progress?.Report(AppUpdateProgress.Downloading(0, "Начинаем скачивание пакета обновления..."));
                string packagePath = await DownloadPackageAsync(manifest, progress);

                for (int step = 0; step <= 100; step += 4)
                {
                    progress?.Report(AppUpdateProgress.Preparing(
                        step,
                        step < 100
                            ? "Проверяем готовность и готовим окно установки..."
                            : "Готово. Открываем установщик..."
                    ));
                    await Task.Delay(100);
                }

                StartUpdater(packagePath, manifest.LatestVersion);
                updaterStarted = true;

                await ReportStatusAsync(
                    manifest,
                    places,
                    "installing",
                    $"Installing update {manifest.LatestVersion}.");

                return InstallUpdateResult.ReadyToShutdown(
                    $"Installing update {manifest.LatestVersion}. The app will restart.");
            }
            finally
            {
                if (!updaterStarted)
                    EndInstall();
            }
        }

        private static bool TryBeginInstall()
        {
            lock (InstallSync)
            {
                if (_installInProgress)
                    return false;

                _installInProgress = true;
                return true;
            }
        }

        private static void EndInstall()
        {
            lock (InstallSync)
            {
                _installInProgress = false;
            }
        }

        private static async Task<UpdateManifest?> ReadManifestAsync()
        {
            string url = await FirebaseAuthService.BuildDatabaseUrlAsync(UpdateManifestPath);
            string json = await _httpClient.GetStringAsync(url);

            if (string.IsNullOrWhiteSpace(json) || json == "null")
                return null;

            var manifest = JsonSerializer.Deserialize<UpdateManifest>(
                json,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

            if (manifest == null)
                return null;

            if (string.IsNullOrWhiteSpace(manifest.LatestVersion))
                manifest.LatestVersion = manifest.Version;

            if (string.IsNullOrWhiteSpace(manifest.DownloadUrl))
                manifest.DownloadUrl = manifest.Url;

            if (string.IsNullOrWhiteSpace(manifest.Channel))
                manifest.Channel = AppVersionService.UpdateChannel;

            return manifest;
        }

        private static async Task ReportStatusAsync(
            AppUpdateInfo info,
            string state,
            string message)
        {
            if (!FirebaseConnectionService.CanSync)
                return;

            var payload = new
            {
                currentVersion = AppVersionService.Version,
                latestVersion = info.LatestVersion,
                channel = AppVersionService.UpdateChannel,
                hasUpdate = info.HasUpdate,
                state,
                message,
                safeToInstall = info.SafeToInstall,
                activePlaces = info.ActivePlaces,
                notes = info.Notes,
                url = info.DownloadUrl,
                checkedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            };

            await PutAsync(ClubUpdateStatusPath, payload);
            await PutAsync(OwnerClubUpdateStatusPath, payload);
        }

        private static async Task ReportStatusAsync(
            UpdateManifest? manifest,
            IReadOnlyList<ClubPlace> places,
            string state,
            string message)
        {
            await ReportStatusAsync(BuildUpdateInfo(manifest, places), state, message);
        }

        private static AppUpdateInfo BuildUpdateInfo(
            UpdateManifest? manifest,
            IReadOnlyList<ClubPlace> places)
        {
            bool hasUpdate = manifest != null &&
                             IsNewerVersion(manifest.LatestVersion, AppVersionService.Version);
            int activePlaces = places.Count(place => place.IsBusy);

            return new AppUpdateInfo
            {
                CurrentVersion = AppVersionService.Version,
                LatestVersion = manifest?.LatestVersion ?? "",
                DisplayLatestVersion = FormatDisplayVersion(manifest?.LatestVersion ?? ""),
                HasUpdate = hasUpdate,
                SafeToInstall = activePlaces == 0,
                ActivePlaces = activePlaces,
                Notes = manifest?.Notes ?? "",
                DownloadUrl = manifest?.DownloadUrl ?? "",
                CheckedAt = DateTime.Now
            };
        }

        private static async Task<string> DownloadPackageAsync(
            UpdateManifest manifest,
            IProgress<AppUpdateProgress>? progress = null)
        {
            string version = SafePathPart(manifest.LatestVersion);
            string updateDir = Path.Combine(UpdatesRoot, version);
            Directory.CreateDirectory(updateDir);

            string packagePath = Path.Combine(updateDir, $"ClubTimerXbox-{version}.zip");
            using var response = await _httpClient.GetAsync(
                manifest.DownloadUrl,
                HttpCompletionOption.ResponseHeadersRead
            );
            response.EnsureSuccessStatusCode();

            long? totalBytes = response.Content.Headers.ContentLength;
            await using var source = await response.Content.ReadAsStreamAsync();
            await using var destination = new MemoryStream();
            byte[] buffer = new byte[128 * 1024];
            long receivedBytes = 0;

            while (true)
            {
                int read = await source.ReadAsync(buffer);
                if (read == 0)
                    break;

                await destination.WriteAsync(buffer.AsMemory(0, read));
                receivedBytes += read;

                int percent = totalBytes.HasValue && totalBytes.Value > 0
                    ? Math.Min(100, (int)Math.Round(receivedBytes * 100.0 / totalBytes.Value))
                    : 0;

                string sizeText = totalBytes.HasValue && totalBytes.Value > 0
                    ? $"{FormatBytes(receivedBytes)} / {FormatBytes(totalBytes.Value)}"
                    : FormatBytes(receivedBytes);

                progress?.Report(AppUpdateProgress.Downloading(
                    percent,
                    $"Скачиваем пакет обновления: {sizeText}"
                ));
            }

            byte[] bytes = destination.ToArray();
            progress?.Report(AppUpdateProgress.Downloading(100, "Скачивание завершено."));

            if (!string.IsNullOrWhiteSpace(manifest.Sha256))
                VerifySha256(bytes, manifest.Sha256);

            await File.WriteAllBytesAsync(packagePath, bytes);
            CleanupOldDirectories(UpdatesRoot, KeepLocalUpdateCopies, updateDir);
            return packagePath;
        }

        private static string FormatBytes(long value)
        {
            string[] units = { "Б", "КБ", "МБ", "ГБ" };
            double size = value;
            int unitIndex = 0;

            while (size >= 1024 && unitIndex < units.Length - 1)
            {
                size /= 1024;
                unitIndex++;
            }

            return unitIndex == 0
                ? $"{value} {units[unitIndex]}"
                : $"{size:0.0} {units[unitIndex]}";
        }

        private static void StartUpdater(string packagePath, string version)
        {
            string appDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            CleanupOldDirectories(UpdaterRunnerRoot, KeepLocalUpdateCopies);
            string runnerDir = Path.Combine(UpdaterRunnerRoot, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(runnerDir);

            foreach (string file in Directory.GetFiles(appDir, "*", SearchOption.TopDirectoryOnly))
            {
                string target = Path.Combine(runnerDir, Path.GetFileName(file));
                File.Copy(file, target, overwrite: true);
            }

            string updaterExe = Path.Combine(runnerDir, "ClubTimerUpdater.exe");
            if (!File.Exists(updaterExe))
                throw new FileNotFoundException("ClubTimerUpdater.exe not found in application folder.", updaterExe);

            string mainExe = Path.GetFileName(Process.GetCurrentProcess().MainModule?.FileName)
                ?? "ClubTimerXbox.exe";

            string[] args =
            {
                "--package", packagePath,
                "--target", appDir,
                "--backup-root", BackupsRoot,
                "--status-file", UpdaterStatusFilePath,
                "--version", version,
                "--main-exe", mainExe,
                "--process", Path.GetFileNameWithoutExtension(mainExe),
                "--wait-seconds", "90"
            };

            Process.Start(new ProcessStartInfo
            {
                FileName = updaterExe,
                Arguments = string.Join(" ", args.Select(QuoteArg)),
                WorkingDirectory = runnerDir,
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }

        private static void VerifySha256(byte[] bytes, string expected)
        {
            byte[] hash = SHA256.HashData(bytes);
            string actual = Convert.ToHexString(hash).ToLowerInvariant();
            string cleanExpected = expected.Replace(" ", "").ToLowerInvariant();

            if (!string.Equals(actual, cleanExpected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Update SHA256 mismatch. Package was not installed.");
        }

        private static bool IsNewerVersion(string latest, string current)
        {
            if (string.IsNullOrWhiteSpace(latest))
                return false;

            if (Version.TryParse(NormalizeVersion(latest), out var latestVersion) &&
                Version.TryParse(NormalizeVersion(current), out var currentVersion))
            {
                return latestVersion > currentVersion;
            }

            return !string.Equals(latest, current, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeVersion(string value)
        {
            string clean = value.Split('+')[0].Split('-')[0].Trim();
            int parts = clean.Count(ch => ch == '.') + 1;

            if (parts == 1)
                return $"{clean}.0.0";

            if (parts == 2)
                return $"{clean}.0";

            return clean;
        }

        public static string FormatDisplayVersion(string value)
        {
            return value.Split('+')[0].Split('-')[0].Trim();
        }

        private static string SafePathPart(string value)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(value.Length);

            foreach (char ch in value)
                builder.Append(invalid.Contains(ch) ? '_' : ch);

            return builder.ToString();
        }

        private static string QuoteArg(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "\"\"";

            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        private static async Task PutAsync(string path, object data)
        {
            string url = await FirebaseAuthService.BuildDatabaseUrlAsync(path);
            string json = JsonSerializer.Serialize(
                data,
                new JsonSerializerOptions
                {
                    WriteIndented = true
                });

            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            await _httpClient.PutAsync(url, content);
        }

        private static async Task ReportLocalUpdaterStatusIfAnyAsync(IReadOnlyList<ClubPlace> places)
        {
            if (!File.Exists(UpdaterStatusFilePath))
                return;

            LocalUpdaterStatus? status = null;

            try
            {
                string json = await File.ReadAllTextAsync(UpdaterStatusFilePath);
                status = JsonSerializer.Deserialize<LocalUpdaterStatus>(
                    json,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
            }
            catch
            {
                return;
            }

            if (status == null || string.IsNullOrWhiteSpace(status.State))
                return;

            var manifest = await ReadManifestAsync();
            var info = BuildUpdateInfo(manifest, places);
            string message = string.IsNullOrWhiteSpace(status.Message)
                ? $"Updater state: {status.State}"
                : status.Message;

            await ReportStatusAsync(info, status.State, message);

            if (status.State.Equals("done", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    File.Delete(UpdaterStatusFilePath);
                }
                catch
                {
                    // A stale status file is not critical.
                }
            }
        }

        private static void CleanupOldDirectories(
            string root,
            int keepCount,
            string? alsoKeep = null)
        {
            try
            {
                if (!Directory.Exists(root))
                    return;

                string? keepFullPath = string.IsNullOrWhiteSpace(alsoKeep)
                    ? null
                    : Path.GetFullPath(alsoKeep);

                var directories = Directory
                    .GetDirectories(root)
                    .Select(path => new DirectoryInfo(path))
                    .OrderByDescending(info => info.LastWriteTimeUtc)
                    .ToList();

                foreach (var directory in directories.Skip(Math.Max(0, keepCount)))
                {
                    if (keepFullPath != null &&
                        directory.FullName.Equals(keepFullPath, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    try
                    {
                        directory.Delete(recursive: true);
                    }
                    catch
                    {
                        // Cleanup must not block updates.
                    }
                }
            }
            catch
            {
                // Cleanup must not block updates.
            }
        }

        public sealed class InstallUpdateResult
        {
            public bool ShouldShutdown { get; private set; }
            public string Message { get; private set; } = "";

            public static InstallUpdateResult Blocked(string message)
            {
                return new InstallUpdateResult
                {
                    ShouldShutdown = false,
                    Message = message
                };
            }

            public static InstallUpdateResult ReadyToShutdown(string message)
            {
                return new InstallUpdateResult
                {
                    ShouldShutdown = true,
                    Message = message
                };
            }
        }

        public sealed class AppUpdateProgress
        {
            public int DownloadPercent { get; private set; }
            public int ReadyPercent { get; private set; }
            public string Message { get; private set; } = "";

            public static AppUpdateProgress Downloading(int downloadPercent, string message)
            {
                return new AppUpdateProgress
                {
                    DownloadPercent = Math.Clamp(downloadPercent, 0, 100),
                    ReadyPercent = 0,
                    Message = message
                };
            }

            public static AppUpdateProgress Preparing(int readyPercent, string message)
            {
                return new AppUpdateProgress
                {
                    DownloadPercent = 100,
                    ReadyPercent = Math.Clamp(readyPercent, 0, 100),
                    Message = message
                };
            }
        }

        public sealed class AppUpdateInfo
        {
            public string CurrentVersion { get; set; } = "";
            public string LatestVersion { get; set; } = "";
            public string DisplayLatestVersion { get; set; } = "";
            public bool HasUpdate { get; set; }
            public bool SafeToInstall { get; set; }
            public int ActivePlaces { get; set; }
            public string Notes { get; set; } = "";
            public string DownloadUrl { get; set; } = "";
            public DateTime CheckedAt { get; set; } = DateTime.Now;

            public AppUpdateInfo WithPlaces(IReadOnlyList<ClubPlace> places)
            {
                int activePlaces = places.Count(place => place.IsBusy);

                return new AppUpdateInfo
                {
                    CurrentVersion = CurrentVersion,
                    LatestVersion = LatestVersion,
                    DisplayLatestVersion = DisplayLatestVersion,
                    HasUpdate = HasUpdate,
                    SafeToInstall = activePlaces == 0,
                    ActivePlaces = activePlaces,
                    Notes = Notes,
                    DownloadUrl = DownloadUrl,
                    CheckedAt = CheckedAt
                };
            }
        }

        private sealed class UpdateManifest
        {
            public string LatestVersion { get; set; } = "";
            public string Version { get; set; } = "";
            public string Url { get; set; } = "";
            public string DownloadUrl { get; set; } = "";
            public string Sha256 { get; set; } = "";
            public string Notes { get; set; } = "";
            public string Channel { get; set; } = "";
        }

        private sealed class LocalUpdaterStatus
        {
            public string State { get; set; } = "";
            public string Message { get; set; } = "";
            public string Version { get; set; } = "";
            public string UpdatedAt { get; set; } = "";
        }
    }
}
