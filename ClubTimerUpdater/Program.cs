using System.Diagnostics;
using System.Drawing;
using System.IO.Compression;
using System.Windows.Forms;

namespace ClubTimerUpdater
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var options = UpdateOptions.Parse(args);
            if (!options.IsValid(out string error))
            {
                UpdateLog.Write(error);
                MessageBox.Show(
                    error,
                    "ClubTimerXbox update",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return 2;
            }

            using var form = new UpdateProgressForm(options);
            Application.Run(form);
            return form.ExitCode;
        }
    }

    internal sealed class UpdateProgressForm : Form
    {
        private readonly UpdateOptions _options;
        private readonly Label _statusLabel;
        private readonly Label _detailsLabel;
        private readonly ProgressBar _progressBar;

        public int ExitCode { get; private set; }

        public UpdateProgressForm(UpdateOptions options)
        {
            _options = options;

            Text = "Обновление ClubTimerXbox";
            StartPosition = FormStartPosition.CenterScreen;
            Width = 560;
            Height = 260;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ControlBox = false;
            TopMost = true;
            BackColor = Color.FromArgb(15, 17, 23);
            ForeColor = Color.White;

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 5,
                Padding = new Padding(28, 24, 28, 24),
                BackColor = BackColor
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var title = new Label
            {
                AutoSize = true,
                Text = "Идёт обновление программы",
                Font = new Font("Segoe UI", 20, FontStyle.Bold),
                ForeColor = Color.White,
                Margin = new Padding(0, 0, 0, 10)
            };

            var message = new Label
            {
                AutoSize = true,
                Text = "Пожалуйста, подождите. Не выключайте компьютер.\nПрограмма сама откроется после завершения обновления.",
                Font = new Font("Segoe UI", 11, FontStyle.Regular),
                ForeColor = Color.FromArgb(209, 213, 219),
                Margin = new Padding(0, 0, 0, 20)
            };

            _progressBar = new ProgressBar
            {
                Dock = DockStyle.Fill,
                Minimum = 0,
                Maximum = 100,
                Value = 0,
                Style = ProgressBarStyle.Continuous,
                Margin = new Padding(0, 5, 0, 14)
            };

            _statusLabel = new Label
            {
                AutoSize = true,
                Text = "0% - Подготовка обновления",
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                ForeColor = Color.FromArgb(147, 168, 255),
                Margin = new Padding(0, 0, 0, 4)
            };

            _detailsLabel = new Label
            {
                AutoSize = true,
                Text = "",
                Font = new Font("Segoe UI", 9, FontStyle.Regular),
                ForeColor = Color.FromArgb(156, 163, 175)
            };

            root.Controls.Add(title, 0, 0);
            root.Controls.Add(message, 0, 1);
            root.Controls.Add(_progressBar, 0, 2);
            root.Controls.Add(_statusLabel, 0, 3);
            root.Controls.Add(_detailsLabel, 0, 4);
            Controls.Add(root);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _ = RunUpdateAsync();
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
                    Math.Max(_progressBar.Value, 1),
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
            WaitForProcessExit(_options.ProcessName, TimeSpan.FromSeconds(_options.WaitSeconds));

            SetProgress(25, "25% - Создаём резервную копию");
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
            if (InvokeRequired)
            {
                BeginInvoke(new Action(CloseFromUi));
                return;
            }

            Close();
        }

        private void SetProgress(int value, string status, string details = "")
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => SetProgress(value, status, details)));
                return;
            }

            int safeValue = Math.Max(_progressBar.Minimum, Math.Min(_progressBar.Maximum, value));
            _progressBar.Value = safeValue;
            _statusLabel.Text = status;
            _detailsLabel.Text = details;
        }

        private void TryStartApp()
        {
            if (string.IsNullOrWhiteSpace(_options.MainExe))
                return;

            StartApp(Path.Combine(_options.TargetDir, _options.MainExe));
        }

        private static void WaitForProcessExit(string processName, TimeSpan timeout)
        {
            if (string.IsNullOrWhiteSpace(processName))
                return;

            string cleanName = Path.GetFileNameWithoutExtension(processName);
            DateTime deadline = DateTime.Now.Add(timeout);

            while (DateTime.Now < deadline)
            {
                var processes = Process.GetProcessesByName(cleanName);
                if (processes.Length == 0)
                    return;

                foreach (var process in processes)
                    process.Dispose();

                Thread.Sleep(500);
            }

            throw new InvalidOperationException(
                $"Process {cleanName} did not exit within {timeout.TotalSeconds:0} seconds.");
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
                File.AppendAllText(
                    LogPath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
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
