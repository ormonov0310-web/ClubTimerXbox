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

        private static string BaseUrl => FirebaseSettings.DatabaseUrl.TrimEnd('/');

        private static string UpdateManifestPath =>
            $"updates/channels/{AppVersionService.UpdateChannel}";

        private static string ClubUpdateStatusPath =>
            $"clubs/{PcIdentityService.Current.ClubId}/updateStatus";

        private static string OwnerClubUpdateStatusPath =>
            $"owner/clubs/{PcIdentityService.Current.ClubId}/updateStatus";

        public static async Task CheckAndReportAsync(IReadOnlyList<ClubPlace> places)
        {
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

        public static async Task<InstallUpdateResult> InstallLatestUpdateAsync(IReadOnlyList<ClubPlace> places)
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

                string packagePath = await DownloadPackageAsync(manifest);
                StartUpdater(packagePath);
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
            string json = await _httpClient.GetStringAsync($"{BaseUrl}/{UpdateManifestPath}.json");

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

        private static async Task<string> DownloadPackageAsync(UpdateManifest manifest)
        {
            string version = SafePathPart(manifest.LatestVersion);
            string updateDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ClubTimerXbox",
                "updates",
                version);

            Directory.CreateDirectory(updateDir);

            string packagePath = Path.Combine(updateDir, $"ClubTimerXbox-{version}.zip");
            byte[] bytes = await _httpClient.GetByteArrayAsync(manifest.DownloadUrl);

            if (!string.IsNullOrWhiteSpace(manifest.Sha256))
                VerifySha256(bytes, manifest.Sha256);

            await File.WriteAllBytesAsync(packagePath, bytes);
            return packagePath;
        }

        private static void StartUpdater(string packagePath)
        {
            string appDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            string runnerDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ClubTimerXbox",
                "updater-runner",
                Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(runnerDir);

            foreach (string file in Directory.GetFiles(appDir, "*", SearchOption.TopDirectoryOnly))
            {
                string target = Path.Combine(runnerDir, Path.GetFileName(file));
                File.Copy(file, target, overwrite: true);
            }

            string updaterExe = Path.Combine(runnerDir, "ClubTimerUpdater.exe");
            if (!File.Exists(updaterExe))
                throw new FileNotFoundException("ClubTimerUpdater.exe not found in application folder.", updaterExe);

            string backupRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ClubTimerXbox",
                "backups");

            string mainExe = Path.GetFileName(Process.GetCurrentProcess().MainModule?.FileName)
                ?? "ClubTimerXbox.exe";

            string[] args =
            {
                "--package", packagePath,
                "--target", appDir,
                "--backup-root", backupRoot,
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
                CreateNoWindow = false
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
            string json = JsonSerializer.Serialize(
                data,
                new JsonSerializerOptions
                {
                    WriteIndented = true
                });

            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            await _httpClient.PutAsync($"{BaseUrl}/{path}.json", content);
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
    }
}
