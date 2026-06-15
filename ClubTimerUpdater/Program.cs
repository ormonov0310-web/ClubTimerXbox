using System.Diagnostics;
using System.IO.Compression;

namespace ClubTimerUpdater
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            try
            {
                var options = UpdateOptions.Parse(args);
                if (!options.IsValid(out string error))
                {
                    Console.Error.WriteLine(error);
                    return 2;
                }

                WaitForProcessExit(options.ProcessName, TimeSpan.FromSeconds(options.WaitSeconds));

                string backupPath = CreateBackup(options.TargetDir, options.BackupRoot);
                InstallPackage(options.PackagePath, options.TargetDir);

                if (!string.IsNullOrWhiteSpace(options.MainExe))
                    StartApp(Path.Combine(options.TargetDir, options.MainExe));

                Console.WriteLine($"Update installed. Backup: {backupPath}");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 1;
            }
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

            Directory.CreateDirectory(extractDir);
            ZipFile.ExtractToDirectory(packagePath, extractDir);

            string sourceDir = FindPackageRoot(extractDir);
            CopyDirectory(sourceDir, targetDir, overwrite: true);
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
                return;

            Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                WorkingDirectory = Path.GetDirectoryName(exePath) ?? "",
                UseShellExecute = true
            });
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
