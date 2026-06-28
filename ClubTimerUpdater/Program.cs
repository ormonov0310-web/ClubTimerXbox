using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ClubTimerUpdater
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
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
            Width = 560;
            Height = 260;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Topmost = true;
            Background = Brush("#0F1117");
            Foreground = Brushes.White;

            Closing += (_, e) =>
            {
                if (!_allowClose)
                    e.Cancel = true;
            };

            var root = new StackPanel
            {
                Margin = new Thickness(28, 24, 28, 24)
            };

            root.Children.Add(new TextBlock
            {
                Text = "Идет обновление программы",
                FontSize = 28,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 10)
            });

            root.Children.Add(new TextBlock
            {
                Text =
                    "Пожалуйста, подождите. Не выключайте компьютер.\n" +
                    "Программа сама откроется после завершения обновления.",
                FontSize = 15,
                Foreground = Brush("#D1D5DB"),
                Margin = new Thickness(0, 0, 0, 20)
            });

            _progressBar = new ProgressBar
            {
                Minimum = 0,
                Maximum = 100,
                Height = 30,
                Value = 0,
                Margin = new Thickness(0, 0, 0, 14)
            };
            root.Children.Add(_progressBar);

            _statusText = new TextBlock
            {
                Text = "0% - Подготовка обновления",
                FontSize = 17,
                FontWeight = FontWeights.Bold,
                Foreground = Brush("#93A8FF"),
                Margin = new Thickness(0, 0, 0, 6)
            };
            root.Children.Add(_statusText);

            _detailsText = new TextBlock
            {
                Text = "",
                FontSize = 13,
                Foreground = Brush("#9CA3AF"),
                TextWrapping = TextWrapping.Wrap
            };
            root.Children.Add(_detailsText);

            Content = root;
            Loaded += async (_, _) => await RunUpdateAsync();
        }

        private async Task RunUpdateAsync()
        {
            try
            {
                await Task.Run(RunUpdate);
                SetProgress(100, "100% - Готово", "Новая версия запускается.");
                await Task.Delay(1500);
                CloseFromUi();
            }
            catch (Exception ex)
            {
                ExitCode = 1;
                UpdateLog.Write(ex.ToString());
                SetProgress(
                    Math.Max((int)_progressBar.Value, 1),
                    "Обновление не удалось. Позовите владельца.",
                    "Подробности записаны в updater.log. Пробуем открыть программу обратно.");
                TryStartApp();
                await Task.Delay(10000);
                CloseFromUi();
            }
        }

        private void RunUpdate()
        {
            UpdateLog.Write("Started");
            UpdateLog.Write($"Package path: {_options.PackagePath}");
            UpdateLog.Write($"Target dir: {_options.TargetDir}");

            SetProgress(0, "0% - Подготовка обновления");

            SetProgress(10, "10% - Закрываем программу");
            UpdateLog.Write("Waiting for process exit");
            WaitForProcessExit(
                _options.ProcessName,
                _options.TargetDir,
                TimeSpan.FromSeconds(_options.WaitSeconds));

            SetProgress(25, "25% - Создаем резервную копию");
            string backupPath = CreateBackup(_options.TargetDir, _options.BackupRoot);
            UpdateLog.Write($"Backup created: {backupPath}");

            SetProgress(50, "50% - Устанавливаем новую версию");
            InstallPackage(_options.PackagePath, _options.TargetDir);
            UpdateLog.Write("Package installed");

            SetProgress(80, "80% - Запускаем программу");
            TryStartApp();
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

        private void TryStartApp()
        {
            if (string.IsNullOrWhiteSpace(_options.MainExe))
                return;

            StartApp(Path.Combine(_options.TargetDir, _options.MainExe));
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

        private static void StartApp(string exePath)
        {
            if (!File.Exists(exePath))
            {
                UpdateLog.Write($"App exe not found: {exePath}");
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                WorkingDirectory = Path.GetDirectoryName(exePath) ?? "",
                UseShellExecute = true
            });
        }

        private static SolidColorBrush Brush(string hex)
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
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
        public string MainExe { get; private set; } = "ClubTimerXbox.exe";
        public string ProcessName { get; private set; } = "ClubTimerXbox";
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
                    case "--main-exe":
                        options.MainExe = value;
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

            error = "";
            return true;
        }
    }
}
