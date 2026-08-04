using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace ClubTimerXbox.Services
{
    public enum AppUpdateInstallMode
    {
        SettingsResume,
        ExitAndClose,
        StartupBeforeLogin,
        RemoteResume
    }

    public static class AppUpdateShutdownCoordinator
    {
        public static bool IsPlannedUpdate { get; private set; }
        public static AppUpdateInstallMode Mode { get; private set; }

        public static void Begin(AppUpdateInstallMode mode)
        {
            IsPlannedUpdate = true;
            Mode = mode;
        }
    }

    public static class AppUpdateRuntimeGuard
    {
        private static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ClubTimerXbox",
            "app-run-state.json");

        public static bool WasLastShutdownClean()
        {
            try
            {
                if (!File.Exists(FilePath))
                    return false;

                AppRunState? state = JsonSerializer.Deserialize<AppRunState>(
                    File.ReadAllText(FilePath));
                return state != null && !state.IsRunning && state.ClosedAtUtc.HasValue;
            }
            catch
            {
                return false;
            }
        }

        public static void MarkRunning()
        {
            Save(new AppRunState
            {
                IsRunning = true,
                StartedAtUtc = DateTime.UtcNow
            });
        }

        public static void MarkCleanShutdown()
        {
            AppRunState state = Load();
            state.IsRunning = false;
            state.ClosedAtUtc = DateTime.UtcNow;
            Save(state);
        }

        private static AppRunState Load()
        {
            try
            {
                if (!File.Exists(FilePath))
                    return new AppRunState();

                return JsonSerializer.Deserialize<AppRunState>(File.ReadAllText(FilePath))
                    ?? new AppRunState();
            }
            catch
            {
                return new AppRunState();
            }
        }

        private static void Save(AppRunState state)
        {
            string? directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            WriteJsonAtomically(FilePath, state);
        }

        private sealed class AppRunState
        {
            public bool IsRunning { get; set; }
            public DateTime? StartedAtUtc { get; set; }
            public DateTime? ClosedAtUtc { get; set; }
        }

        private static void WriteJsonAtomically<T>(string path, T value)
        {
            string temporaryPath = path + ".tmp";
            File.WriteAllText(
                temporaryPath,
                JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporaryPath, path, true);
        }
    }

    public static class AppUpdateSessionService
    {
        private static readonly string RootPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ClubTimerXbox");

        private static readonly string SessionsPath = Path.Combine(RootPath, "update-sessions");
        private static readonly string HistoryPath = Path.Combine(RootPath, "update-history.json");
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        public static UpdateSessionTicket Create(
            AppUpdateInstallMode mode,
            string targetVersion)
        {
            Directory.CreateDirectory(SessionsPath);

            string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32))
                .ToLowerInvariant();
            var employee = EmployeeService.CurrentEmployee;
            var identity = PcIdentityService.Current;
            var ticket = new UpdateSessionTicket
            {
                Token = token,
                SessionId = Guid.NewGuid().ToString("N"),
                Mode = mode,
                TargetVersion = targetVersion,
                ClubId = identity.ClubId,
                InstallationId = identity.InstallationId,
                EmployeeId = employee?.EmployeeId ?? "",
                EmployeeName = employee?.Name ?? "",
                CreatedAtUtc = DateTime.UtcNow,
                ExpiresAtUtc = DateTime.UtcNow.AddMinutes(15)
            };

            WriteJsonAtomically(GetTicketPath(token), ticket);
            CleanupExpiredTickets();
            return ticket;
        }

        public static bool TryLoad(string token, out UpdateSessionTicket ticket)
        {
            ticket = new UpdateSessionTicket();
            if (!IsValidToken(token))
                return false;

            try
            {
                string path = GetTicketPath(token);
                if (!File.Exists(path))
                    return false;

                UpdateSessionTicket? loaded = JsonSerializer.Deserialize<UpdateSessionTicket>(
                    File.ReadAllText(path), JsonOptions);
                if (loaded == null ||
                    !CryptographicOperations.FixedTimeEquals(
                        Convert.FromHexString(loaded.Token),
                        Convert.FromHexString(token)) ||
                    loaded.ExpiresAtUtc < DateTime.UtcNow)
                {
                    TryDelete(path);
                    return false;
                }

                var identity = PcIdentityService.Current;
                if (!loaded.ClubId.Equals(identity.ClubId, StringComparison.OrdinalIgnoreCase) ||
                    !loaded.InstallationId.Equals(
                        identity.InstallationId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                ticket = loaded;
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static void Complete(UpdateSessionTicket ticket, string result, string message)
        {
            var history = LoadHistory();
            history.Insert(0, new UpdateHistoryItem
            {
                SessionId = ticket.SessionId,
                Version = ticket.TargetVersion,
                Mode = ticket.Mode.ToString(),
                EmployeeName = ticket.EmployeeName,
                Result = result,
                Message = message,
                CompletedAt = DateTime.Now
            });
            SaveHistory(history.Take(20).ToList());
            TryDelete(GetTicketPath(ticket.Token));
        }

        public static IReadOnlyList<UpdateHistoryItem> GetHistory()
        {
            return LoadHistory();
        }

        private static List<UpdateHistoryItem> LoadHistory()
        {
            try
            {
                if (!File.Exists(HistoryPath))
                    return new List<UpdateHistoryItem>();

                return JsonSerializer.Deserialize<List<UpdateHistoryItem>>(
                    File.ReadAllText(HistoryPath), JsonOptions)
                    ?? new List<UpdateHistoryItem>();
            }
            catch
            {
                return new List<UpdateHistoryItem>();
            }
        }

        private static void SaveHistory(List<UpdateHistoryItem> history)
        {
            Directory.CreateDirectory(RootPath);
            WriteJsonAtomically(HistoryPath, history);
        }

        private static void CleanupExpiredTickets()
        {
            try
            {
                if (!Directory.Exists(SessionsPath))
                    return;

                foreach (string path in Directory.GetFiles(SessionsPath, "*.json"))
                {
                    if (File.GetLastWriteTimeUtc(path) < DateTime.UtcNow.AddDays(-1))
                        TryDelete(path);
                }
            }
            catch
            {
                // Old tickets do not affect the active update.
            }
        }

        private static bool IsValidToken(string token)
        {
            return token.Length == 64 && token.All(Uri.IsHexDigit);
        }

        private static string GetTicketPath(string token)
        {
            return Path.Combine(SessionsPath, token + ".json");
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
                // A stale ticket expires independently and cannot reveal a PIN.
            }
        }

        private static void WriteJsonAtomically<T>(string path, T value)
        {
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            string temporaryPath = path + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(value, JsonOptions));
            File.Move(temporaryPath, path, true);
        }
    }

    public sealed class UpdateSessionTicket
    {
        public string Token { get; set; } = "";
        public string SessionId { get; set; } = "";
        public AppUpdateInstallMode Mode { get; set; }
        public string TargetVersion { get; set; } = "";
        public string ClubId { get; set; } = "";
        public string InstallationId { get; set; } = "";
        public string EmployeeId { get; set; } = "";
        public string EmployeeName { get; set; } = "";
        public DateTime CreatedAtUtc { get; set; }
        public DateTime ExpiresAtUtc { get; set; }
    }

    public sealed class UpdateHistoryItem
    {
        public string SessionId { get; set; } = "";
        public string Version { get; set; } = "";
        public string Mode { get; set; } = "";
        public string EmployeeName { get; set; } = "";
        public string Result { get; set; } = "";
        public string Message { get; set; } = "";
        public DateTime CompletedAt { get; set; }
    }

    public static class AppUpdateLaunchContext
    {
        public static UpdateSessionTicket? Ticket { get; private set; }
        public static string Result { get; private set; } = "";
        public static bool ReportOnly { get; private set; }
        public static bool WasUpdateLaunch { get; private set; }
        public static AppUpdateInstallMode LaunchMode { get; private set; }
        public static bool SuppressOpenedNotification =>
            WasUpdateLaunch &&
            (LaunchMode == AppUpdateInstallMode.SettingsResume ||
             LaunchMode == AppUpdateInstallMode.RemoteResume);
        public static bool IsUpdateLaunch => Ticket != null;
        public static bool IsResumeLaunch =>
            Ticket != null &&
            (Result.Equals("done", StringComparison.OrdinalIgnoreCase) ||
             Result.Equals("rolled_back", StringComparison.OrdinalIgnoreCase)) &&
            (Ticket.Mode == AppUpdateInstallMode.SettingsResume ||
             Ticket.Mode == AppUpdateInstallMode.RemoteResume);

        public static void Initialize(string[] args)
        {
            string token = ReadArg(args, "--update-session");
            Result = ReadArg(args, "--update-result");
            ReportOnly = args.Any(arg => arg.Equals(
                "--update-report-only",
                StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(token) &&
                AppUpdateSessionService.TryLoad(token, out UpdateSessionTicket ticket))
            {
                Ticket = ticket;
                WasUpdateLaunch = true;
                LaunchMode = ticket.Mode;
            }
        }

        public static void Complete(string message)
        {
            if (Ticket == null)
                return;

            AppUpdateSessionService.Complete(Ticket, Result, message);
            Ticket = null;
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
    }
}
