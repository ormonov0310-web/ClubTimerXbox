using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ClubTimerUpdater
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            string recoveryJournal = ReadArg(args, "--recover-journal");
            if (!string.IsNullOrWhiteSpace(recoveryJournal))
                return RunRecovery(recoveryJournal);

            var options = UpdateOptions.Parse(args);
            if (!options.IsValid(out string error))
            {
                UpdateLog.Write(error);
                MessageBox.Show(
                    error,
                    "ClubTimerXbox update",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return 2;
            }

            using var singleInstance = CreateSingleInstanceMutex(options, out bool ownsMutex);
            if (!ownsMutex)
            {
                UpdateLog.Write("Another updater instance is already running.");
                MessageBox.Show(
                    "Обновление уже запущено. Дождитесь завершения первого окна.",
                    "ClubTimerXbox update",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return 0;
            }

            var app = new Application
            {
                ShutdownMode = ShutdownMode.OnMainWindowClose
            };

            var window = new UpdateProgressWindow(options);
            app.Run(window);
            return window.ExitCode;
        }

        private static int RunRecovery(string journalPath)
        {
            try
            {
                UpdateLog.Write($"Automatic recovery started: {journalPath}");
                UpdateRecoveryResult result = UpdateTransactionEngine.RecoverFromJournal(journalPath);
                WriteRecoveryStatus(
                    result.StatusFile,
                    result.Result,
                    result.Result == "done"
                        ? "Установка была завершена до перезапуска Windows."
                        : "После прерванной установки восстановлена предыдущая версия.",
                    result.Version);
                StartRecoveredApp(result, result.Result);
                UpdateLog.Write("Automatic recovery completed.");
                return 0;
            }
            catch (Exception ex)
            {
                UpdateLog.Write($"Automatic recovery failed: {ex}");
                return 3;
            }
        }

        private static void StartRecoveredApp(UpdateRecoveryResult result, string updateResult)
        {
            if (!result.Restart)
                return;
            string exePath = Path.Combine(result.TargetDir, result.MainExe);
            if (!File.Exists(exePath))
                return;
            var args = new List<string>();
            if (!string.IsNullOrWhiteSpace(result.SessionToken))
            {
                args.Add("--update-session");
                args.Add(result.SessionToken);
                args.Add("--update-result");
                args.Add(updateResult);
                if (result.ReportOnly)
                    args.Add("--update-report-only");
            }
            Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = string.Join(" ", args.Select(QuoteArg)),
                WorkingDirectory = result.TargetDir,
                UseShellExecute = true
            });
        }

        private static void WriteRecoveryStatus(
            string path,
            string state,
            string message,
            string version)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, JsonSerializer.Serialize(new
                {
                    state,
                    message,
                    version,
                    updatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                }, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
            }
            catch
            {
            }
        }

        private static string ReadArg(string[] args, string key)
        {
            for (int index = 0; index < args.Length - 1; index++)
            {
                if (args[index].Equals(key, StringComparison.OrdinalIgnoreCase))
                    return args[index + 1];
            }
            return "";
        }

        private static string QuoteArg(string value) =>
            "\"" + value.Replace("\"", "\\\"") + "\"";

        private static Mutex CreateSingleInstanceMutex(UpdateOptions options, out bool ownsMutex)
        {
            string targetDir = Path.GetFullPath(options.TargetDir).TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
            byte[] hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(targetDir.ToLowerInvariant()));
            string hash = Convert.ToHexString(hashBytes).Substring(0, 16);

            return new Mutex(
                initiallyOwned: true,
                name: $"ClubTimerXboxUpdater_{hash}",
                createdNew: out ownsMutex);
        }
    }

    internal sealed class UpdateProgressWindow : Window
    {
        private readonly UpdateOptions _options;
        private readonly TextBlock _statusText;
        private readonly TextBlock _detailsText;
        private readonly ProgressBar _progressBar;
        private bool _allowClose;

        public int ExitCode { get; private set; }

        public UpdateProgressWindow(UpdateOptions options)
        {
            _options = options;

            Title = "Обновление ClubTimerXbox";
            Width = 620;
            Height = 390;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Topmost = true;
            WindowStyle = WindowStyle.None;
            Background = Brush("#070A0F");
            Foreground = Brushes.White;

            Closing += (_, e) =>
            {
                if (!_allowClose)
                    e.Cancel = true;
            };

            var root = new Grid
            {
                Background = CreateBackdropBrush() as System.Windows.Media.Brush
                    ?? Brush("#070A0F")
            };
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(38) });
            root.RowDefinitions.Add(new RowDefinition
            {
                Height = new GridLength(1, GridUnitType.Star)
            });

            var backdropShade = new Border
            {
                Background = Brush("#A6080C12")
            };
            Grid.SetRowSpan(backdropShade, 2);
            root.Children.Add(backdropShade);

            var titleBar = new Border
            {
                Background = Brush("#D0141B24"),
                BorderBrush = Brush("#48FFFFFF"),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Child = new Grid
                {
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "Обновление ClubTimerXbox",
                            FontSize = 13,
                            FontWeight = FontWeights.SemiBold,
                            Foreground = Brushes.White,
                            VerticalAlignment = VerticalAlignment.Center,
                            Margin = new Thickness(14, 0, 0, 0)
                        },
                        new TextBlock
                        {
                            Text = "УСТАНОВКА",
                            FontSize = 11,
                            FontWeight = FontWeights.Bold,
                            Foreground = Brush("#7DD3FC"),
                            HorizontalAlignment = HorizontalAlignment.Right,
                            VerticalAlignment = VerticalAlignment.Center,
                            Margin = new Thickness(0, 0, 14, 0)
                        }
                    }
                }
            };
            titleBar.MouseLeftButtonDown += (_, e) =>
            {
                if (e.LeftButton != MouseButtonState.Pressed)
                    return;

                try
                {
                    DragMove();
                }
                catch (InvalidOperationException)
                {
                    // The button may be released during a fast drag.
                }
            };
            Grid.SetRow(titleBar, 0);
            root.Children.Add(titleBar);

            var content = new StackPanel();

            content.Children.Add(new TextBlock
            {
                Text = "Идёт обновление программы",
                FontSize = 28,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 10)
            });

            content.Children.Add(new TextBlock
            {
                Text = _options.ReportOnly
                    ? "Пожалуйста, подождите. Не выключайте компьютер.\n" +
                      "После обновления клуб останется закрытым."
                    : "Пожалуйста, подождите. Не выключайте компьютер.\n" +
                      "Программа сама откроется после завершения обновления.",
                FontSize = 15,
                Foreground = Brush("#D7E1EC"),
                LineHeight = 22,
                Margin = new Thickness(0, 0, 0, 22)
            });

            _progressBar = new ProgressBar
            {
                Minimum = 0,
                Maximum = 100,
                Height = 24,
                Value = 0,
                Foreground = Brush("#38BDF8"),
                Background = Brush("#B817222E"),
                BorderBrush = Brush("#66FFFFFF"),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 0, 16)
            };
            content.Children.Add(_progressBar);

            _statusText = new TextBlock
            {
                Text = "0% - подготовка обновления",
                FontSize = 17,
                FontWeight = FontWeights.Bold,
                Foreground = Brush("#7DD3FC"),
                Margin = new Thickness(0, 0, 0, 6)
            };
            content.Children.Add(_statusText);

            _detailsText = new TextBlock
            {
                Text = "",
                FontSize = 13,
                Foreground = Brush("#B8C5D4"),
                TextWrapping = TextWrapping.Wrap
            };
            content.Children.Add(_detailsText);

            var glassPanel = new Border
            {
                Background = Brush("#C018222E"),
                BorderBrush = Brush("#70FFFFFF"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(28, 26, 28, 26),
                Margin = new Thickness(34, 30, 34, 32),
                Child = content
            };
            Grid.SetRow(glassPanel, 1);
            root.Children.Add(glassPanel);

            Content = root;
            Loaded += async (_, _) => await RunUpdateAsync();
        }

        private async Task RunUpdateAsync()
        {
            try
            {
                await Task.Run(RunUpdate);
                SetProgress(
                    100,
                    "100% - готово",
                    _options.ReportOnly
                        ? "Обновление завершено. Клуб остаётся закрытым."
                        : "Новая версия запускается.");
                WriteStatus("done", $"Обновление {_options.Version} установлено.");
                await Task.Delay(1500);
                CloseFromUi();
            }
            catch (UpdateRolledBackException ex)
            {
                ExitCode = 1;
                UpdateLog.Write(ex.ToString());
                WriteStatus("rolled_back", ex.Message);
                SetProgress(
                    Math.Max((int)_progressBar.Value, 1),
                    "Обновление отменено, предыдущая версия восстановлена.",
                    "Данные клуба не изменены. Программа откроется в прежней версии.");
                TryStartApp("rolled_back");
                await Task.Delay(7000);
                CloseFromUi();
            }
            catch (Exception ex)
            {
                ExitCode = 1;
                UpdateLog.Write(ex.ToString());
                bool recoveryRequired = File.Exists(_options.JournalFile);
                WriteStatus(recoveryRequired ? "recovery_required" : "failed", ex.Message);
                SetProgress(
                    Math.Max((int)_progressBar.Value, 1),
                    "Обновление не удалось. Позовите владельца.",
                    recoveryRequired
                        ? "Windows продолжит восстановление при следующем входе. Не удаляйте резервную копию."
                        : "Пакет не был установлен. Пробуем открыть программу обратно.");
                if (!recoveryRequired)
                    TryStartApp("failed");
                await Task.Delay(10000);
                CloseFromUi();
            }
        }

        private void RunUpdate()
        {
            UpdateLog.Write("Started");
            UpdateLog.Write($"Package path: {_options.PackagePath}");
            UpdateLog.Write($"Target dir: {_options.TargetDir}");

            SetProgress(0, "0% - проверяем пакет обновления");
            WriteStatus("starting", $"Начата установка обновления {_options.Version}.");

            var request = new UpdateTransactionRequest
            {
                PackagePath = _options.PackagePath,
                ExpectedSha256 = _options.Sha256,
                TargetDir = _options.TargetDir,
                BackupRoot = _options.BackupRoot,
                JournalPath = _options.JournalFile,
                MainExe = _options.MainExe,
                Version = _options.Version,
                SessionToken = _options.UpdateSession,
                InstallMode = _options.InstallMode,
                Restart = _options.Restart,
                ReportOnly = _options.ReportOnly,
                RecoveryUpdaterPath = Environment.ProcessPath ?? "",
                StatusFile = _options.StatusFile
            };

            using PreparedUpdatePackage prepared = UpdateTransactionEngine.PreparePackage(
                request,
                phase =>
                {
                    if (phase == UpdateTransactionPhase.PackagePrepared)
                        SetProgress(12, "12% - пакет проверен и распакован");
                });

            SetProgress(15, "15% - закрываем программу");
            WriteStatus("waiting_app", "Ждём закрытия программы.");
            UpdateLog.Write("Waiting for process exit");
            WaitForProcessExit(
                _options.ProcessName,
                _options.TargetDir,
                TimeSpan.FromSeconds(_options.WaitSeconds));

            SetProgress(25, "25% - создаём резервную копию");
            WriteStatus("backup", "Создаём резервную копию текущей версии.");
            string backupPath = UpdateTransactionEngine.InstallPrepared(
                prepared,
                request,
                phase =>
                {
                    switch (phase)
                    {
                        case UpdateTransactionPhase.BackupCreated:
                            SetProgress(42, "42% - резервная копия готова");
                            break;
                        case UpdateTransactionPhase.FilesPrepared:
                            SetProgress(62, "62% - новые файлы подготовлены");
                            break;
                        case UpdateTransactionPhase.Committing:
                            SetProgress(72, "72% - включаем новую версию");
                            WriteStatus("copying", "Атомарно заменяем файлы приложения.");
                            break;
                        case UpdateTransactionPhase.ValidatingInstall:
                            SetProgress(88, "88% - проверяем установленную версию");
                            break;
                    }
                });
            UpdateLog.Write($"Package installed. Backup: {backupPath}");

            CleanupOldLocalFiles(backupPath);

            SetProgress(96, "96% - завершаем обновление");
            WriteStatus("starting_app", "Проверка завершена. Запускаем программу.");
            TryStartApp("done");
            UpdateLog.Write("App restart requested");
        }

        private void CloseFromUi()
        {
            Dispatcher.Invoke(() =>
            {
                _allowClose = true;
                Close();
            });
        }

        private void SetProgress(int value, string status, string details = "")
        {
            Dispatcher.Invoke(() =>
            {
                int safeValue = Math.Max((int)_progressBar.Minimum, Math.Min((int)_progressBar.Maximum, value));
                _progressBar.Value = safeValue;
                _statusText.Text = status;
                _detailsText.Text = details;
            });
        }

        private void TryStartApp(string updateResult)
        {
            if (!_options.Restart || string.IsNullOrWhiteSpace(_options.MainExe))
                return;

            string[] args = string.IsNullOrWhiteSpace(_options.UpdateSession)
                ? Array.Empty<string>()
                : _options.ReportOnly
                    ? new[]
                    {
                        "--update-session", _options.UpdateSession,
                        "--update-result", updateResult,
                        "--update-report-only"
                    }
                    : new[]
                    {
                        "--update-session", _options.UpdateSession,
                        "--update-result", updateResult
                    };
            StartApp(Path.Combine(_options.TargetDir, _options.MainExe), args);
        }

        private void WriteStatus(string state, string message)
        {
            if (string.IsNullOrWhiteSpace(_options.StatusFile))
                return;

            try
            {
                string? directory = Path.GetDirectoryName(_options.StatusFile);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);

                var payload = new
                {
                    state,
                    message,
                    version = _options.Version,
                    updatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                };

                File.WriteAllText(
                    _options.StatusFile,
                    JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }),
                    Encoding.UTF8);
            }
            catch (Exception ex)
            {
                UpdateLog.Write($"Status write failed: {ex.Message}");
            }
        }

        private static void WaitForProcessExit(string processName, string targetDir, TimeSpan timeout)
        {
            if (string.IsNullOrWhiteSpace(processName))
                return;

            string cleanName = Path.GetFileNameWithoutExtension(processName);
            string targetRoot = Path.GetFullPath(targetDir).TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            DateTime deadline = DateTime.Now.Add(timeout);

            while (DateTime.Now < deadline)
            {
                var processes = Process
                    .GetProcessesByName(cleanName)
                    .Where(process => IsProcessFromTargetDir(process, targetRoot))
                    .ToArray();

                if (processes.Length == 0)
                    return;

                foreach (var process in processes)
                    process.Dispose();

                Thread.Sleep(500);
            }

            throw new InvalidOperationException(
                $"Process {cleanName} did not exit within {timeout.TotalSeconds:0} seconds.");
        }

        private static bool IsProcessFromTargetDir(Process process, string targetRoot)
        {
            try
            {
                string? processPath = process.MainModule?.FileName;
                if (string.IsNullOrWhiteSpace(processPath))
                    return true;

                string fullPath = Path.GetFullPath(processPath);
                return fullPath.StartsWith(targetRoot, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return true;
            }
        }

        private static string CreateBackup(string targetDir, string backupRoot)
        {
            Directory.CreateDirectory(backupRoot);

            string backupPath = Path.Combine(
                backupRoot,
                DateTime.Now.ToString("yyyyMMdd-HHmmss"));

            CopyDirectory(targetDir, backupPath);
            return backupPath;
        }

        private static void InstallPackage(string packagePath, string targetDir)
        {
            string extractDir = Path.Combine(
                Path.GetTempPath(),
                "ClubTimerUpdater",
                Guid.NewGuid().ToString("N"));

            try
            {
                Directory.CreateDirectory(extractDir);
                UpdateLog.Write("Extract started");
                ZipFile.ExtractToDirectory(packagePath, extractDir);

                string sourceDir = FindPackageRoot(extractDir);
                UpdateLog.Write($"Copy started: {sourceDir}");
                CopyDirectory(sourceDir, targetDir, overwrite: true);
            }
            finally
            {
                try
                {
                    if (Directory.Exists(extractDir))
                        Directory.Delete(extractDir, recursive: true);
                }
                catch
                {
                    // Temp cleanup must not hide update errors.
                }
            }
        }

        private static string FindPackageRoot(string extractDir)
        {
            string directExe = Path.Combine(extractDir, "ClubTimerXbox.exe");
            if (File.Exists(directExe))
                return extractDir;

            var candidates = Directory
                .GetDirectories(extractDir, "*", SearchOption.AllDirectories)
                .Where(dir => File.Exists(Path.Combine(dir, "ClubTimerXbox.exe")))
                .OrderBy(dir => dir.Length)
                .ToList();

            return candidates.FirstOrDefault() ?? extractDir;
        }

        private static void CopyDirectory(string sourceDir, string targetDir, bool overwrite = false)
        {
            Directory.CreateDirectory(targetDir);

            foreach (string directory in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(sourceDir, directory);
                Directory.CreateDirectory(Path.Combine(targetDir, relative));
            }

            foreach (string file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(sourceDir, file);
                string target = Path.Combine(targetDir, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target, overwrite);
            }
        }

        private void CleanupOldLocalFiles(string currentBackupPath)
        {
            CleanupOldDirectories(_options.BackupRoot, keepCount: 2, alsoKeep: currentBackupPath);

            string? versionDir = Path.GetDirectoryName(_options.PackagePath);
            string? updatesRoot = versionDir == null ? null : Path.GetDirectoryName(versionDir);
            if (!string.IsNullOrWhiteSpace(updatesRoot))
                CleanupOldDirectories(updatesRoot, keepCount: 2, alsoKeep: versionDir);
        }

        private static void CleanupOldDirectories(
            string root,
            int keepCount,
            string? alsoKeep = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
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
                        // Cleanup must not block installation.
                    }
                }
            }
            catch
            {
                // Cleanup must not block installation.
            }
        }

        private static void StartApp(string exePath, IReadOnlyList<string>? args = null)
        {
            if (!File.Exists(exePath))
            {
                UpdateLog.Write($"App exe not found: {exePath}");
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = args == null
                    ? ""
                    : string.Join(" ", args.Select(value => "\"" + value.Replace("\"", "\\\"") + "\"")),
                WorkingDirectory = Path.GetDirectoryName(exePath) ?? "",
                UseShellExecute = true
            });
        }

        private static SolidColorBrush Brush(string hex)
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        }

        private static ImageBrush? CreateBackdropBrush()
        {
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.UriSource = new Uri(
                    "pack://application:,,,/Assets/Themes/glass-club.png",
                    UriKind.Absolute);
                image.EndInit();
                image.Freeze();

                return new ImageBrush(image)
                {
                    Stretch = Stretch.UniformToFill,
                    AlignmentX = AlignmentX.Center,
                    AlignmentY = AlignmentY.Center
                };
            }
            catch
            {
                return null;
            }
        }
    }

    internal static class UpdateLog
    {
        private static readonly object Sync = new object();

        private static string LogPath
        {
            get
            {
                string backupRoot = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "ClubTimerXbox",
                    "backups");
                Directory.CreateDirectory(backupRoot);
                return Path.Combine(backupRoot, "updater.log");
            }
        }

        public static void Write(string message)
        {
            lock (Sync)
            {
                string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}";

                for (int attempt = 0; attempt < 5; attempt++)
                {
                    try
                    {
                        using var stream = new FileStream(
                            LogPath,
                            FileMode.Append,
                            FileAccess.Write,
                            FileShare.ReadWrite);
                        using var writer = new StreamWriter(stream, Encoding.UTF8);
                        writer.Write(line);
                        return;
                    }
                    catch (IOException) when (attempt < 4)
                    {
                        Thread.Sleep(100);
                    }
                    catch (UnauthorizedAccessException) when (attempt < 4)
                    {
                        Thread.Sleep(100);
                    }
                }
            }
        }
    }

    internal sealed class UpdateOptions
    {
        public string PackagePath { get; private set; } = "";
        public string TargetDir { get; private set; } = "";
        public string BackupRoot { get; private set; } = "";
        public string StatusFile { get; private set; } = "";
        public string Version { get; private set; } = "";
        public string MainExe { get; private set; } = "ClubTimerXbox.exe";
        public string ProcessName { get; private set; } = "ClubTimerXbox";
        public string JournalFile { get; private set; } = "";
        public string Sha256 { get; private set; } = "";
        public string UpdateSession { get; private set; } = "";
        public string InstallMode { get; private set; } = "";
        public bool Restart { get; private set; } = true;
        public bool ReportOnly { get; private set; }
        public int WaitSeconds { get; private set; } = 60;

        public static UpdateOptions Parse(string[] args)
        {
            var options = new UpdateOptions();

            for (int i = 0; i < args.Length; i++)
            {
                string key = args[i];
                string value = i + 1 < args.Length ? args[i + 1] : "";

                switch (key)
                {
                    case "--package":
                        options.PackagePath = value;
                        i++;
                        break;
                    case "--target":
                        options.TargetDir = value;
                        i++;
                        break;
                    case "--backup-root":
                        options.BackupRoot = value;
                        i++;
                        break;
                    case "--status-file":
                        options.StatusFile = value;
                        i++;
                        break;
                    case "--journal-file":
                        options.JournalFile = value;
                        i++;
                        break;
                    case "--version":
                        options.Version = value;
                        i++;
                        break;
                    case "--main-exe":
                        options.MainExe = value;
                        i++;
                        break;
                    case "--sha256":
                        options.Sha256 = value;
                        i++;
                        break;
                    case "--update-session":
                        options.UpdateSession = value;
                        i++;
                        break;
                    case "--install-mode":
                        options.InstallMode = value;
                        i++;
                        break;
                    case "--restart":
                        options.Restart = !value.Equals("false", StringComparison.OrdinalIgnoreCase);
                        i++;
                        break;
                    case "--report-only":
                        options.ReportOnly = value.Equals("true", StringComparison.OrdinalIgnoreCase);
                        i++;
                        break;
                    case "--process":
                        options.ProcessName = value;
                        i++;
                        break;
                    case "--wait-seconds":
                        if (int.TryParse(value, out int waitSeconds))
                            options.WaitSeconds = waitSeconds;
                        i++;
                        break;
                }
            }

            return options;
        }

        public bool IsValid(out string error)
        {
            if (string.IsNullOrWhiteSpace(PackagePath) || !File.Exists(PackagePath))
            {
                error = "Missing or invalid --package path.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(TargetDir) || !Directory.Exists(TargetDir))
            {
                error = "Missing or invalid --target directory.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(BackupRoot))
            {
                BackupRoot = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "ClubTimerXbox",
                    "backups");
            }

            if (string.IsNullOrWhiteSpace(JournalFile))
            {
                JournalFile = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "ClubTimerXbox",
                    "update-transaction.json");
            }

            if (string.IsNullOrWhiteSpace(Version))
                Version = Path.GetFileNameWithoutExtension(PackagePath).Replace("ClubTimerXbox-", "");

            error = "";
            return true;
        }
    }
}
