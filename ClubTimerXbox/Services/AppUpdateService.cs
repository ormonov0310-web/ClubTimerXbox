using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public static class AppUpdateService
    {
        private static readonly HttpClient HttpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(15)
        };
        private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(1);
        private static readonly SemaphoreSlim PreparationLock = new SemaphoreSlim(1, 1);
        private static readonly object PreparationIntentSync = new object();
        private static readonly object InstallSync = new object();
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };
        private static DateTime _lastCheck = DateTime.MinValue;
        private static AppUpdateInfo? _lastInfo;
        private static bool _installInProgress;
        private static UpdateManifest? _desiredManifest;
        private static CancellationTokenSource? _preparationCancellation;
        private static int _preparationGeneration;
        private static bool _backgroundPreparationActive;
        private const int KeepLocalUpdateCopies = 2;

        public static event EventHandler? StateChanged;

        private static string UpdateManifestPath =>
            $"updates/channels/{AppVersionService.UpdateChannel}";

        private static string ClubUpdateStatusPath =>
            $"clubs/{PcIdentityService.Current.ClubId}/updateStatus";

        private static string OwnerClubUpdateStatusPath =>
            $"owner/clubs/{PcIdentityService.Current.ClubId}/updateStatus";

        private static string LocalAppDataRoot => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ClubTimerXbox");

        private static string UpdatesRoot => Path.Combine(LocalAppDataRoot, "updates");
        private static string BackupsRoot => Path.Combine(LocalAppDataRoot, "backups");
        private static string UpdaterRunnerRoot => Path.Combine(LocalAppDataRoot, "updater-runner");
        private static string PreparationStatePath => Path.Combine(LocalAppDataRoot, "update-state.json");
        private static string UpdaterStatusFilePath => Path.Combine(LocalAppDataRoot, "updater-status.json");
        private static string TransactionJournalPath => Path.Combine(LocalAppDataRoot, "update-transaction.json");

        public static async Task CheckAndReportAsync(IReadOnlyList<ClubPlace> places)
        {
            try
            {
                await ReportLocalUpdaterStatusIfAnyAsync(places);
            }
            catch
            {
                // A local updater status must never interrupt club work.
            }

            if (!FirebaseConnectionService.CanSync || DateTime.Now - _lastCheck < CheckInterval)
                return;

            _lastCheck = DateTime.Now;

            try
            {
                await GetLatestUpdateInfoAsync(places, forceRefresh: true);
            }
            catch
            {
                // Update checks retry independently of the club workflow.
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
                return ApplyPreparationState(_lastInfo.WithPlaces(places));
            }

            UpdateManifest? manifest = await ReadManifestAsync();
            AppUpdateInfo info = BuildUpdateInfo(manifest, places);
            _lastInfo = info;

            if (manifest != null && info.HasUpdate)
                StartBackgroundPreparation(manifest);
            else if (!info.HasUpdate)
                ClearPreparationForInstalledVersion();

            await ReportStatusAsync(info, "checked", "");
            return ApplyPreparationState(info);
        }

        public static AppUpdateInfo GetLocalUpdateInfo(IReadOnlyList<ClubPlace> places)
        {
            if (_lastInfo != null)
                return ApplyPreparationState(_lastInfo.WithPlaces(places));

            UpdatePreparationState state = LoadPreparationState();
            var info = new AppUpdateInfo
            {
                CurrentVersion = AppVersionService.Version,
                LatestVersion = state.Version,
                DisplayLatestVersion = FormatDisplayVersion(state.Version),
                HasUpdate = IsNewerVersion(state.Version, AppVersionService.Version),
                SafeToInstall = places.All(place => !place.IsBusy),
                ActivePlaces = places.Count(place => place.IsBusy),
                Notes = state.Notes,
                DownloadUrl = state.DownloadUrl,
                CheckedAt = state.UpdatedAtUtc == default
                    ? DateTime.Now
                    : state.UpdatedAtUtc.ToLocalTime()
            };
            return ApplyPreparationState(info);
        }

        public static async Task<string> PrepareLatestUpdateAsync()
        {
            UpdateManifest? manifest = await ReadManifestAsync();

            if (manifest == null || string.IsNullOrWhiteSpace(manifest.DownloadUrl))
            {
                await ReportStatusAsync(
                    manifest,
                    Array.Empty<ClubPlace>(),
                    "no_update",
                    "Пакет обновления не настроен.");
                return "Пакет обновления не настроен.";
            }

            if (!IsNewerVersion(manifest.LatestVersion, AppVersionService.Version))
            {
                await ReportStatusAsync(
                    manifest,
                    Array.Empty<ClubPlace>(),
                    "current",
                    "Установлена актуальная версия.");
                return "Установлена актуальная версия.";
            }

            string packagePath = await EnsurePreparedUpdateAsync(manifest);
            await ReportStatusAsync(
                manifest,
                Array.Empty<ClubPlace>(),
                "downloaded",
                $"Пакет {manifest.LatestVersion} проверен и готов: {packagePath}");

            return $"Обновление {manifest.LatestVersion} скачано и готово к установке.";
        }

        public static async Task<InstallUpdateResult> InstallLatestUpdateAsync(
            IReadOnlyList<ClubPlace> places,
            IProgress<AppUpdateProgress>? progress = null,
            AppUpdateInstallMode mode = AppUpdateInstallMode.SettingsResume)
        {
            if (!TryBeginInstall())
                return InstallUpdateResult.Blocked("Обновление уже запускается. Дождитесь окна установки.");

            bool updaterStarted = false;

            try
            {
                int activePlaces = places.Count(place => place.IsBusy);
                if (activePlaces > 0)
                {
                    string message = $"Обновление отложено. Активных мест: {activePlaces}.";
                    await ReportStatusAsync(
                        GetLocalUpdateInfo(places),
                        "waiting_free_club",
                        message);
                    return InstallUpdateResult.Blocked(message);
                }

                UpdateManifest? manifest = await ResolveManifestForInstallAsync();
                if (manifest == null || string.IsNullOrWhiteSpace(manifest.DownloadUrl))
                    return InstallUpdateResult.Blocked("Нет проверенного пакета обновления.");

                if (!IsNewerVersion(manifest.LatestVersion, AppVersionService.Version))
                    return InstallUpdateResult.Blocked("Установлена актуальная версия.");

                string packagePath = await EnsurePreparedUpdateAsync(manifest, progress);

                // A newer release may appear while the previous package is downloading.
                // Re-read once and never start a stale prepared package.
                UpdateManifest? latestManifest = await TryReadManifestAsync();
                if (latestManifest != null && IsManifestNewerOrDifferent(latestManifest, manifest))
                {
                    manifest = latestManifest;
                    packagePath = await EnsurePreparedUpdateAsync(manifest, progress);
                }

                progress?.Report(AppUpdateProgress.Preparing(
                    100,
                    "Пакет проверен. Открываем безопасный установщик..."));

                UpdateSessionTicket ticket = AppUpdateSessionService.Create(
                    mode,
                    manifest.LatestVersion);
                SavePreparationState(UpdatePreparationState.FromManifest(
                    manifest,
                    AppUpdateStage.Installing,
                    packagePath,
                    100,
                    "Установка началась."));

                await ReportStatusAsync(
                    BuildUpdateInfo(manifest, places),
                    "installing",
                    $"Устанавливается версия {manifest.LatestVersion}.",
                    ticket);

                StartUpdater(packagePath, manifest, ticket);
                updaterStarted = true;
                AppUpdateShutdownCoordinator.Begin(mode);

                return InstallUpdateResult.ReadyToShutdown(
                    mode == AppUpdateInstallMode.ExitAndClose
                        ? $"Версия {manifest.LatestVersion} устанавливается. После установки клуб останется закрытым."
                        : $"Версия {manifest.LatestVersion} устанавливается. Программа откроется автоматически.");
            }
            catch (Exception ex)
            {
                UpdatePreparationState state = LoadPreparationState();
                state.Stage = AppUpdateStage.Failed;
                state.Message = ex.Message;
                state.UpdatedAtUtc = DateTime.UtcNow;
                SavePreparationState(state);
                throw;
            }
            finally
            {
                if (!updaterStarted)
                    EndInstall();
            }
        }

        public static async Task<bool> TryInstallPreparedUpdateAtStartupAsync()
        {
            if (!AppUpdateRuntimeGuard.WasLastShutdownClean() ||
                ActiveSessionStorageService.Load().Any(item => item.IsBusy))
            {
                return false;
            }

            UpdatePreparationState state = LoadPreparationState();
            if (state.Stage != AppUpdateStage.Ready ||
                !IsNewerVersion(state.Version, AppVersionService.Version) ||
                !File.Exists(state.PackagePath))
            {
                return false;
            }

            UpdateManifest manifest = state.ToManifest();
            UpdateManifest? onlineManifest = await TryReadManifestAsync();
            if (onlineManifest != null && IsManifestNewerOrDifferent(onlineManifest, manifest))
            {
                SavePreparationState(UpdatePreparationState.FromManifest(
                    onlineManifest,
                    AppUpdateStage.Available,
                    "",
                    0,
                    "Найдена более новая версия. Она будет скачана после входа."));
                return false;
            }

            if (!await VerifyPreparedFileAsync(state, deleteInvalid: true))
                return false;

            if (!TryBeginInstall())
                return false;

            try
            {
                UpdateSessionTicket ticket = AppUpdateSessionService.Create(
                    AppUpdateInstallMode.StartupBeforeLogin,
                    state.Version);
                state.Stage = AppUpdateStage.Installing;
                state.Message = "Установка при запуске.";
                state.UpdatedAtUtc = DateTime.UtcNow;
                SavePreparationState(state);
                StartUpdater(state.PackagePath, manifest, ticket);
                AppUpdateShutdownCoordinator.Begin(AppUpdateInstallMode.StartupBeforeLogin);
                return true;
            }
            catch
            {
                EndInstall();
                throw;
            }
        }

        public static void ApplyLaunchResult(string result, string targetVersion)
        {
            UpdatePreparationState state = LoadPreparationState();
            if (result.Equals("done", StringComparison.OrdinalIgnoreCase) &&
                !IsNewerVersion(targetVersion, AppVersionService.Version))
            {
                TryDelete(PreparationStatePath);
                StateChanged?.Invoke(null, EventArgs.Empty);
                return;
            }

            if (result.Equals("rolled_back", StringComparison.OrdinalIgnoreCase) ||
                result.Equals("failed", StringComparison.OrdinalIgnoreCase))
            {
                state.Stage = AppUpdateStage.Failed;
                state.Message = result.Equals("rolled_back", StringComparison.OrdinalIgnoreCase)
                    ? "Установка отменена. Предыдущая версия восстановлена."
                    : "Установка не завершилась. Пакет можно скачать заново.";
                state.UpdatedAtUtc = DateTime.UtcNow;
                SavePreparationState(state);
            }
        }

        public static async Task FinalizeUpdateSessionAsync(
            UpdateSessionTicket ticket,
            string result)
        {
            if (!FirebaseConnectionService.CanSync)
                return;

            string state = result.Equals("done", StringComparison.OrdinalIgnoreCase)
                ? "done"
                : result.Equals("rolled_back", StringComparison.OrdinalIgnoreCase)
                    ? "rolled_back"
                    : "failed";
            string message = state switch
            {
                "done" => $"Обновление {ticket.TargetVersion} установлено.",
                "rolled_back" => "Предыдущая версия восстановлена после ошибки обновления.",
                _ => $"Обновление {ticket.TargetVersion} не установлено."
            };
            AppUpdateInfo info = GetLocalUpdateInfo(Array.Empty<ClubPlace>());
            await ReportStatusAsync(info, state, message, ticket, maintenanceActive: false);
        }

        private static void StartBackgroundPreparation(UpdateManifest manifest)
        {
            CancellationToken token;
            int generation;
            lock (PreparationIntentSync)
            {
                bool replacesDesired = _desiredManifest == null ||
                    IsManifestNewerOrDifferent(manifest, _desiredManifest);
                if (_backgroundPreparationActive && !replacesDesired)
                    return;

                if (replacesDesired)
                {
                    _desiredManifest = manifest;
                    _preparationCancellation?.Cancel();
                }

                _preparationCancellation = new CancellationTokenSource();
                token = _preparationCancellation.Token;
                generation = ++_preparationGeneration;
                _backgroundPreparationActive = true;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await EnsurePreparedUpdateAsync(manifest, cancellationToken: token);
                    if (IsCurrentPreparationGeneration(generation))
                    {
                        await ReportStatusAsync(
                            manifest,
                            Array.Empty<ClubPlace>(),
                            "downloaded",
                            $"Пакет {manifest.LatestVersion} скачан и проверен.");
                    }
                }
                catch (OperationCanceledException)
                {
                    // A newer manifest replaced this in-progress package.
                }
                catch (Exception ex)
                {
                    if (!IsCurrentPreparationGeneration(generation))
                        return;

                    UpdatePreparationState state = LoadPreparationState();
                    if (!state.Version.Equals(
                            manifest.LatestVersion,
                            StringComparison.OrdinalIgnoreCase))
                        return;
                    state.Stage = AppUpdateStage.Failed;
                    state.Message = ex.Message;
                    state.UpdatedAtUtc = DateTime.UtcNow;
                    SavePreparationState(state);
                }
                finally
                {
                    lock (PreparationIntentSync)
                    {
                        if (generation == _preparationGeneration)
                            _backgroundPreparationActive = false;
                    }
                }
            }, token);
        }

        private static bool IsCurrentPreparationGeneration(int generation)
        {
            lock (PreparationIntentSync)
                return generation == _preparationGeneration;
        }

        private static async Task<string> EnsurePreparedUpdateAsync(
            UpdateManifest manifest,
            IProgress<AppUpdateProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            await PreparationLock.WaitAsync(cancellationToken);

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                UpdatePreparationState existing = LoadPreparationState();
                if (existing.Matches(manifest) &&
                    existing.Stage == AppUpdateStage.Ready &&
                    await VerifyPreparedFileAsync(existing, deleteInvalid: false))
                {
                    progress?.Report(AppUpdateProgress.Preparing(100, "Пакет уже скачан и проверен."));
                    return existing.PackagePath;
                }

                string version = SafePathPart(manifest.LatestVersion);
                string updateDir = Path.Combine(UpdatesRoot, version);
                Directory.CreateDirectory(updateDir);
                string packagePath = Path.Combine(updateDir, $"ClubTimerXbox-{version}.zip");
                string partialPath = packagePath + ".partial";

                SavePreparationState(UpdatePreparationState.FromManifest(
                    manifest,
                    AppUpdateStage.Downloading,
                    packagePath,
                    0,
                    "Скачивание обновления."));

                TryDelete(partialPath);
                using HttpResponseMessage response = await HttpClient.GetAsync(
                    manifest.DownloadUrl,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
                response.EnsureSuccessStatusCode();

                long? totalBytes = response.Content.Headers.ContentLength;
                await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var destination = new FileStream(
                    partialPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.Read,
                    128 * 1024,
                    useAsync: true);

                byte[] buffer = new byte[128 * 1024];
                long receivedBytes = 0;
                int lastSavedPercent = -1;

                while (true)
                {
                    int read = await source.ReadAsync(
                        buffer.AsMemory(0, buffer.Length),
                        cancellationToken);
                    if (read == 0)
                        break;

                    await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    receivedBytes += read;
                    int percent = totalBytes.HasValue && totalBytes.Value > 0
                        ? Math.Min(99, (int)(receivedBytes * 100 / totalBytes.Value))
                        : 0;

                    progress?.Report(AppUpdateProgress.Downloading(
                        percent,
                        totalBytes.HasValue
                            ? $"Скачиваем: {FormatBytes(receivedBytes)} / {FormatBytes(totalBytes.Value)}"
                            : $"Скачиваем: {FormatBytes(receivedBytes)}"));

                    if (percent != lastSavedPercent && (percent == 0 || percent % 2 == 0))
                    {
                        lastSavedPercent = percent;
                        SavePreparationState(UpdatePreparationState.FromManifest(
                            manifest,
                            AppUpdateStage.Downloading,
                            packagePath,
                            percent,
                            "Скачивание обновления."));
                    }
                }

                await destination.FlushAsync(cancellationToken);
                destination.Flush(flushToDisk: true);

                if (manifest.SizeBytes > 0 && receivedBytes != manifest.SizeBytes)
                    throw new InvalidOperationException("Размер скачанного пакета не совпадает с манифестом.");

                SavePreparationState(UpdatePreparationState.FromManifest(
                    manifest,
                    AppUpdateStage.Verifying,
                    packagePath,
                    100,
                    "Проверяем целостность пакета."));
                progress?.Report(AppUpdateProgress.Preparing(60, "Проверяем SHA-256 пакета..."));

                await VerifySha256FileAsync(partialPath, manifest.Sha256);
                File.Move(partialPath, packagePath, true);

                UpdatePreparationState ready = UpdatePreparationState.FromManifest(
                    manifest,
                    AppUpdateStage.Ready,
                    packagePath,
                    100,
                    "Пакет скачан, проверен и готов к установке.");
                ready.SizeBytes = receivedBytes;
                SavePreparationState(ready);
                progress?.Report(AppUpdateProgress.Preparing(100, ready.Message));
                CleanupOldDirectories(UpdatesRoot, KeepLocalUpdateCopies, updateDir);
                return packagePath;
            }
            catch
            {
                UpdatePreparationState failed = LoadPreparationState();
                TryDelete(failed.PackagePath + ".partial");
                throw;
            }
            finally
            {
                PreparationLock.Release();
            }
        }

        private static async Task<UpdateManifest?> ResolveManifestForInstallAsync()
        {
            UpdateManifest? online = await TryReadManifestAsync();
            if (online != null)
                return online;

            UpdatePreparationState state = LoadPreparationState();
            return state.Stage == AppUpdateStage.Ready && File.Exists(state.PackagePath)
                ? state.ToManifest()
                : null;
        }

        private static async Task<UpdateManifest?> TryReadManifestAsync()
        {
            try
            {
                return await ReadManifestAsync();
            }
            catch
            {
                return null;
            }
        }

        private static async Task<UpdateManifest?> ReadManifestAsync()
        {
            string url = await FirebaseAuthService.BuildDatabaseUrlAsync(UpdateManifestPath);
            string json = await HttpClient.GetStringAsync(url);
            if (string.IsNullOrWhiteSpace(json) || json == "null")
                return null;

            UpdateManifest? manifest = JsonSerializer.Deserialize<UpdateManifest>(json, JsonOptions);
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

        private static AppUpdateInfo BuildUpdateInfo(
            UpdateManifest? manifest,
            IReadOnlyList<ClubPlace> places)
        {
            int activePlaces = places.Count(place => place.IsBusy);
            var info = new AppUpdateInfo
            {
                CurrentVersion = AppVersionService.Version,
                LatestVersion = manifest?.LatestVersion ?? "",
                DisplayLatestVersion = FormatDisplayVersion(manifest?.LatestVersion ?? ""),
                HasUpdate = manifest != null &&
                    IsNewerVersion(manifest.LatestVersion, AppVersionService.Version),
                SafeToInstall = activePlaces == 0,
                ActivePlaces = activePlaces,
                Notes = manifest?.Notes ?? "",
                DownloadUrl = manifest?.DownloadUrl ?? "",
                CheckedAt = DateTime.Now
            };
            return ApplyPreparationState(info);
        }

        private static AppUpdateInfo ApplyPreparationState(AppUpdateInfo info)
        {
            if (!info.HasUpdate)
            {
                info.Stage = AppUpdateStage.None;
                return info;
            }

            UpdatePreparationState state = LoadPreparationState();
            bool sameVersion = state.Version.Equals(
                info.LatestVersion,
                StringComparison.OrdinalIgnoreCase);

            info.Stage = sameVersion ? state.Stage : AppUpdateStage.Available;
            info.DownloadPercent = sameVersion ? state.DownloadPercent : 0;
            info.StateMessage = sameVersion ? state.Message : "Найдена новая версия.";
            info.IsPackageReady = sameVersion &&
                state.Stage == AppUpdateStage.Ready &&
                File.Exists(state.PackagePath);

            if (info.IsPackageReady)
                info.Stage = info.SafeToInstall
                    ? AppUpdateStage.Ready
                    : AppUpdateStage.DownloadedBlocked;

            return info;
        }

        private static async Task ReportStatusAsync(
            AppUpdateInfo info,
            string state,
            string message,
            UpdateSessionTicket? ticket = null,
            bool maintenanceActive = true)
        {
            if (!FirebaseConnectionService.CanSync)
                return;

            long maintenanceUntil = ticket == null || !maintenanceActive
                ? 0
                : new DateTimeOffset(ticket.ExpiresAtUtc).ToUnixTimeMilliseconds();
            var payload = new
            {
                currentVersion = AppVersionService.Version,
                latestVersion = info.LatestVersion,
                channel = AppVersionService.UpdateChannel,
                hasUpdate = info.HasUpdate,
                state,
                message,
                preparationStage = info.Stage.ToString(),
                downloadPercent = info.DownloadPercent,
                packageReady = info.IsPackageReady,
                safeToInstall = info.SafeToInstall,
                activePlaces = info.ActivePlaces,
                notes = info.Notes,
                url = info.DownloadUrl,
                updateSessionId = ticket?.SessionId ?? "",
                installMode = ticket?.Mode.ToString() ?? "",
                maintenanceUntilUnixMs = maintenanceUntil,
                employeeName = ticket?.EmployeeName ?? "",
                checkedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                checkedAtUnixMs = DateTimeOffset.Now.ToUnixTimeMilliseconds()
            };

            await PutAsync(ClubUpdateStatusPath, payload);
            await PutAsync(OwnerClubUpdateStatusPath, payload);
        }

        private static Task ReportStatusAsync(
            UpdateManifest? manifest,
            IReadOnlyList<ClubPlace> places,
            string state,
            string message)
        {
            return ReportStatusAsync(BuildUpdateInfo(manifest, places), state, message);
        }

        private static void StartUpdater(
            string packagePath,
            UpdateManifest manifest,
            UpdateSessionTicket ticket)
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
                throw new FileNotFoundException("ClubTimerUpdater.exe не найден.", updaterExe);

            string mainExe = Path.GetFileName(Process.GetCurrentProcess().MainModule?.FileName)
                ?? "ClubTimerXbox.exe";
            bool reportOnly = ticket.Mode == AppUpdateInstallMode.ExitAndClose;

            string[] args =
            {
                "--package", packagePath,
                "--target", appDir,
                "--backup-root", BackupsRoot,
                "--status-file", UpdaterStatusFilePath,
                "--journal-file", TransactionJournalPath,
                "--version", manifest.LatestVersion,
                "--sha256", manifest.Sha256,
                "--main-exe", mainExe,
                "--process", Path.GetFileNameWithoutExtension(mainExe),
                "--wait-seconds", "90",
                "--update-session", ticket.Token,
                "--install-mode", ticket.Mode.ToString(),
                "--restart", "true",
                "--report-only", reportOnly ? "true" : "false"
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

        private static async Task<bool> VerifyPreparedFileAsync(
            UpdatePreparationState state,
            bool deleteInvalid)
        {
            try
            {
                if (!File.Exists(state.PackagePath))
                    return false;
                if (state.SizeBytes > 0 && new FileInfo(state.PackagePath).Length != state.SizeBytes)
                    throw new InvalidOperationException("Размер локального пакета изменился.");
                await VerifySha256FileAsync(state.PackagePath, state.Sha256);
                return true;
            }
            catch
            {
                if (deleteInvalid)
                    TryDelete(state.PackagePath);
                state.Stage = AppUpdateStage.Failed;
                state.Message = "Проверенный пакет повреждён. Он будет скачан заново.";
                state.UpdatedAtUtc = DateTime.UtcNow;
                SavePreparationState(state);
                return false;
            }
        }

        private static async Task VerifySha256FileAsync(string path, string expected)
        {
            if (string.IsNullOrWhiteSpace(expected))
                throw new InvalidOperationException("В манифесте отсутствует SHA-256 пакета.");

            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                128 * 1024,
                useAsync: true);
            byte[] hash = await SHA256.HashDataAsync(stream);
            string actual = Convert.ToHexString(hash).ToLowerInvariant();
            string cleanExpected = expected.Replace(" ", "").ToLowerInvariant();
            if (!string.Equals(actual, cleanExpected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("SHA-256 пакета не совпадает. Установка отменена.");
        }

        private static bool IsManifestNewerOrDifferent(
            UpdateManifest candidate,
            UpdateManifest current)
        {
            return ShouldReplacePreparedPackage(
                candidate.LatestVersion,
                candidate.Sha256,
                candidate.DownloadUrl,
                current.LatestVersion,
                current.Sha256,
                current.DownloadUrl);
        }

        public static bool ShouldReplacePreparedPackage(
            string candidateVersion,
            string candidateSha256,
            string candidateUrl,
            string preparedVersion,
            string preparedSha256,
            string preparedUrl)
        {
            if (IsNewerVersion(candidateVersion, preparedVersion))
                return true;

            return candidateVersion.Equals(preparedVersion, StringComparison.OrdinalIgnoreCase) &&
                   (!candidateSha256.Equals(preparedSha256, StringComparison.OrdinalIgnoreCase) ||
                    !candidateUrl.Equals(preparedUrl, StringComparison.OrdinalIgnoreCase));
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
                _installInProgress = false;
        }

        private static void ClearPreparationForInstalledVersion()
        {
            UpdatePreparationState state = LoadPreparationState();
            if (string.IsNullOrWhiteSpace(state.Version) ||
                IsNewerVersion(state.Version, AppVersionService.Version))
            {
                return;
            }

            TryDelete(PreparationStatePath);
            StateChanged?.Invoke(null, EventArgs.Empty);
        }

        private static UpdatePreparationState LoadPreparationState()
        {
            try
            {
                if (!File.Exists(PreparationStatePath))
                    return new UpdatePreparationState();
                return JsonSerializer.Deserialize<UpdatePreparationState>(
                    File.ReadAllText(PreparationStatePath), JsonOptions)
                    ?? new UpdatePreparationState();
            }
            catch
            {
                return new UpdatePreparationState();
            }
        }

        private static void SavePreparationState(UpdatePreparationState state)
        {
            Directory.CreateDirectory(LocalAppDataRoot);
            state.UpdatedAtUtc = DateTime.UtcNow;
            string temporaryPath = PreparationStatePath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(state, JsonOptions));
            File.Move(temporaryPath, PreparationStatePath, true);
            StateChanged?.Invoke(null, EventArgs.Empty);
        }

        private static async Task PutAsync(string path, object data)
        {
            string url = await FirebaseAuthService.BuildDatabaseUrlAsync(path);
            string json = JsonSerializer.Serialize(data, JsonOptions);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await HttpClient.PutAsync(url, content);
            response.EnsureSuccessStatusCode();
        }

        private static async Task ReportLocalUpdaterStatusIfAnyAsync(
            IReadOnlyList<ClubPlace> places)
        {
            if (!File.Exists(UpdaterStatusFilePath) || !FirebaseConnectionService.CanSync)
                return;

            LocalUpdaterStatus? status;
            try
            {
                status = JsonSerializer.Deserialize<LocalUpdaterStatus>(
                    await File.ReadAllTextAsync(UpdaterStatusFilePath), JsonOptions);
            }
            catch
            {
                return;
            }

            if (status == null || string.IsNullOrWhiteSpace(status.State))
                return;

            UpdateManifest? manifest = await TryReadManifestAsync();
            await ReportStatusAsync(
                BuildUpdateInfo(manifest, places),
                status.State,
                string.IsNullOrWhiteSpace(status.Message)
                    ? $"Состояние установщика: {status.State}"
                    : status.Message);

            if (IsTerminalUpdaterState(status.State))
                TryDelete(UpdaterStatusFilePath);
        }

        private static bool IsTerminalUpdaterState(string state)
        {
            return state.Equals("done", StringComparison.OrdinalIgnoreCase) ||
                   state.Equals("rolled_back", StringComparison.OrdinalIgnoreCase) ||
                   state.Equals("failed", StringComparison.OrdinalIgnoreCase) ||
                   state.Equals("recovery_required", StringComparison.OrdinalIgnoreCase);
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
                var directories = Directory.GetDirectories(root)
                    .Select(path => new DirectoryInfo(path))
                    .OrderByDescending(info => info.LastWriteTimeUtc)
                    .ToList();
                foreach (DirectoryInfo directory in directories.Skip(Math.Max(0, keepCount)))
                {
                    if (keepFullPath != null && directory.FullName.Equals(
                            keepFullPath,
                            StringComparison.OrdinalIgnoreCase))
                        continue;
                    try
                    {
                        directory.Delete(recursive: true);
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
            }
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
            return unitIndex == 0 ? $"{value} {units[unitIndex]}" : $"{size:0.0} {units[unitIndex]}";
        }

        private static bool IsNewerVersion(string latest, string current)
        {
            if (string.IsNullOrWhiteSpace(latest))
                return false;
            if (Version.TryParse(NormalizeVersion(latest), out Version? latestVersion) &&
                Version.TryParse(NormalizeVersion(current), out Version? currentVersion))
                return latestVersion > currentVersion;
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
            char[] invalid = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(value.Length);
            foreach (char ch in value)
                builder.Append(invalid.Contains(ch) ? '_' : ch);
            return builder.ToString();
        }

        private static string QuoteArg(string value)
        {
            return string.IsNullOrEmpty(value)
                ? "\"\""
                : "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        public enum AppUpdateStage
        {
            None,
            Available,
            Downloading,
            Verifying,
            DownloadedBlocked,
            Ready,
            Installing,
            Recovering,
            Failed
        }

        public sealed class InstallUpdateResult
        {
            public bool ShouldShutdown { get; private set; }
            public string Message { get; private set; } = "";

            public static InstallUpdateResult Blocked(string message) => new InstallUpdateResult
            {
                Message = message
            };

            public static InstallUpdateResult ReadyToShutdown(string message) => new InstallUpdateResult
            {
                ShouldShutdown = true,
                Message = message
            };
        }

        public sealed class AppUpdateProgress
        {
            public int DownloadPercent { get; private set; }
            public int ReadyPercent { get; private set; }
            public string Message { get; private set; } = "";

            public static AppUpdateProgress Downloading(int percent, string message) => new AppUpdateProgress
            {
                DownloadPercent = Math.Clamp(percent, 0, 100),
                Message = message
            };

            public static AppUpdateProgress Preparing(int percent, string message) => new AppUpdateProgress
            {
                DownloadPercent = 100,
                ReadyPercent = Math.Clamp(percent, 0, 100),
                Message = message
            };
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
            public AppUpdateStage Stage { get; set; }
            public int DownloadPercent { get; set; }
            public bool IsPackageReady { get; set; }
            public string StateMessage { get; set; } = "";

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
                    CheckedAt = CheckedAt,
                    Stage = Stage,
                    DownloadPercent = DownloadPercent,
                    IsPackageReady = IsPackageReady,
                    StateMessage = StateMessage
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
            public long SizeBytes { get; set; }
            public string Notes { get; set; } = "";
            public string Channel { get; set; } = "";
        }

        private sealed class UpdatePreparationState
        {
            public string Version { get; set; } = "";
            public string DownloadUrl { get; set; } = "";
            public string Sha256 { get; set; } = "";
            public long SizeBytes { get; set; }
            public string Notes { get; set; } = "";
            public string Channel { get; set; } = "";
            public string PackagePath { get; set; } = "";
            public AppUpdateStage Stage { get; set; }
            public int DownloadPercent { get; set; }
            public string Message { get; set; } = "";
            public DateTime UpdatedAtUtc { get; set; }

            public bool Matches(UpdateManifest manifest)
            {
                return Version.Equals(manifest.LatestVersion, StringComparison.OrdinalIgnoreCase) &&
                       DownloadUrl.Equals(manifest.DownloadUrl, StringComparison.OrdinalIgnoreCase) &&
                       Sha256.Equals(manifest.Sha256, StringComparison.OrdinalIgnoreCase);
            }

            public UpdateManifest ToManifest() => new UpdateManifest
            {
                LatestVersion = Version,
                Version = Version,
                DownloadUrl = DownloadUrl,
                Url = DownloadUrl,
                Sha256 = Sha256,
                SizeBytes = SizeBytes,
                Notes = Notes,
                Channel = Channel
            };

            public static UpdatePreparationState FromManifest(
                UpdateManifest manifest,
                AppUpdateStage stage,
                string packagePath,
                int downloadPercent,
                string message) => new UpdatePreparationState
                {
                    Version = manifest.LatestVersion,
                    DownloadUrl = manifest.DownloadUrl,
                    Sha256 = manifest.Sha256,
                    SizeBytes = manifest.SizeBytes,
                    Notes = manifest.Notes,
                    Channel = manifest.Channel,
                    PackagePath = packagePath,
                    Stage = stage,
                    DownloadPercent = downloadPercent,
                    Message = message,
                    UpdatedAtUtc = DateTime.UtcNow
                };
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
