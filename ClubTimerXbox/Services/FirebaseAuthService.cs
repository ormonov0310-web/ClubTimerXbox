using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace ClubTimerXbox.Services
{
    public static class FirebaseAuthService
    {
        private static readonly HttpClient _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(8)
        };
        private static readonly object Sync = new object();
        private static readonly SemaphoreSlim TokenRefreshLock = new SemaphoreSlim(1, 1);

        private static readonly string FolderPath =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ClubTimerXbox"
            );

        private static readonly string SessionFilePath =
            Path.Combine(FolderPath, "firebase_auth.json");

        private static readonly string SessionBackupFilePath =
            Path.Combine(FolderPath, "firebase_auth.backup.json");

        private static FirebaseAuthSession? _session = LoadSession();

        public static bool IsConfigured =>
            !string.IsNullOrWhiteSpace(FirebaseSettings.WebApiKey);

        public static string CurrentEmail
        {
            get
            {
                lock (Sync)
                    return _session?.Email ?? "";
            }
        }

        public static string CurrentUserId
        {
            get
            {
                lock (Sync)
                    return _session?.UserId ?? "";
            }
        }

        public static bool HasSavedSession
        {
            get
            {
                if (!IsConfigured)
                    return true;

                lock (Sync)
                {
                    return _session != null &&
                           !string.IsNullOrWhiteSpace(_session.RefreshToken);
                }
            }
        }

        public static bool CanManageAllClubs
        {
            get
            {
                string email = CurrentEmail.Trim();
                return email.Equals(
                           "owner@clubtimer.local",
                           StringComparison.OrdinalIgnoreCase
                       ) ||
                       email.Equals(
                           "codex@clubtimer.local",
                           StringComparison.OrdinalIgnoreCase
                       );
            }
        }

        public static string SuggestedEmail
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(CurrentEmail))
                    return CurrentEmail;

                string clubId = PcIdentityService.Current.ClubId.Trim().ToLowerInvariant();
                Match match = Regex.Match(clubId, @"^club[_-]?(\d+)$");

                return match.Success
                    ? $"club{match.Groups[1].Value}@xbox.local"
                    : "club1@xbox.local";
            }
        }

        public static async Task<bool> TryRestoreAsync()
        {
            if (!IsConfigured)
                return true;

            if (_session == null || string.IsNullOrWhiteSpace(_session.RefreshToken))
                return false;

            try
            {
                await GetIdTokenAsync();
                return true;
            }
            catch
            {
                // Do not delete a saved refresh token on a single startup/network failure.
                // Firebase sync will retry token refresh in the background once the network is back.
                return true;
            }
        }

        public static async Task SignInAsync(string email, string password)
        {
            if (!IsConfigured)
                return;

            await TokenRefreshLock.WaitAsync();

            try
            {
                await SignInCoreAsync(email, password);
            }
            finally
            {
                TokenRefreshLock.Release();
            }
        }

        private static async Task SignInCoreAsync(string email, string password)
        {

            var payload = new
            {
                email = email.Trim(),
                password,
                returnSecureToken = true
            };

            using var content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json"
            );

            string url =
                $"https://identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key={Uri.EscapeDataString(FirebaseSettings.WebApiKey)}";

            using var response = await _httpClient.PostAsync(url, content);
            string json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(GetFirebaseError(json));

            var signIn = JsonSerializer.Deserialize<FirebaseSignInResponse>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            );

            if (signIn == null ||
                string.IsNullOrWhiteSpace(signIn.IdToken) ||
                string.IsNullOrWhiteSpace(signIn.RefreshToken))
            {
                throw new InvalidOperationException("Firebase не вернул токен входа.");
            }

            var nextSession = new FirebaseAuthSession
            {
                Email = signIn.Email,
                UserId = signIn.LocalId,
                IdToken = signIn.IdToken,
                RefreshToken = signIn.RefreshToken,
                ExpiresAtUtc = DateTime.UtcNow.AddSeconds(ParseExpiresIn(signIn.ExpiresIn))
            };

            lock (Sync)
            {
                _session = nextSession;
            }

            SaveSession(nextSession);
        }

        public static void SignOut()
        {
            lock (Sync)
            {
                _session = null;
            }

            try
            {
                if (File.Exists(SessionFilePath))
                    File.Delete(SessionFilePath);

                if (File.Exists(SessionBackupFilePath))
                    File.Delete(SessionBackupFilePath);
            }
            catch
            {
                // Local auth cache cleanup must not stop the app.
            }
        }

        public static async Task<string> BuildDatabaseUrlAsync(string path)
        {
            string cleanPath = path.Trim('/');
            string baseUrl =
                $"{FirebaseSettings.DatabaseUrl.TrimEnd('/')}/{cleanPath}.json";

            if (!IsConfigured)
                return baseUrl;

            string token = await GetIdTokenAsync();
            return $"{baseUrl}?auth={Uri.EscapeDataString(token)}";
        }

        private static async Task<string> GetIdTokenAsync()
        {
            if (!IsConfigured)
                return "";

            FirebaseAuthSession? session = GetSessionSnapshot();

            if (session == null || string.IsNullOrWhiteSpace(session.RefreshToken))
                throw new InvalidOperationException("Нужно войти в Firebase.");

            if (HasUsableIdToken(session))
                return session.IdToken;

            await TokenRefreshLock.WaitAsync();

            try
            {
                session = GetSessionSnapshot();

                if (session == null || string.IsNullOrWhiteSpace(session.RefreshToken))
                    throw new InvalidOperationException("Нужно войти в Firebase.");

                if (HasUsableIdToken(session))
                    return session.IdToken;

                return await RefreshTokenAsync(session.RefreshToken);
            }
            finally
            {
                TokenRefreshLock.Release();
            }
        }

        private static async Task<string> RefreshTokenAsync(string refreshToken)
        {
            string url =
                $"https://securetoken.googleapis.com/v1/token?key={Uri.EscapeDataString(FirebaseSettings.WebApiKey)}";

            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken
            });

            using var response = await _httpClient.PostAsync(url, content);
            string json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(GetFirebaseError(json));

            var refresh = JsonSerializer.Deserialize<FirebaseRefreshResponse>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            );

            if (refresh == null ||
                string.IsNullOrWhiteSpace(refresh.IdToken) ||
                string.IsNullOrWhiteSpace(refresh.RefreshToken))
            {
                throw new InvalidOperationException("Firebase не обновил токен.");
            }

            string email;
            lock (Sync)
                email = _session?.Email ?? "";

            var nextSession = new FirebaseAuthSession
            {
                Email = email,
                UserId = string.IsNullOrWhiteSpace(refresh.UserId)
                    ? GetSessionSnapshot()?.UserId ?? ""
                    : refresh.UserId,
                IdToken = refresh.IdToken,
                RefreshToken = refresh.RefreshToken,
                ExpiresAtUtc = DateTime.UtcNow.AddSeconds(ParseExpiresIn(refresh.ExpiresIn))
            };

            lock (Sync)
            {
                _session = nextSession;
            }

            SaveSession(nextSession);
            return nextSession.IdToken;
        }

        private static int ParseExpiresIn(string value)
        {
            return int.TryParse(value, out int seconds) && seconds > 0
                ? seconds
                : 3600;
        }

        private static string GetFirebaseError(string json)
        {
            try
            {
                using var document = JsonDocument.Parse(json);
                if (document.RootElement.TryGetProperty("error", out JsonElement error) &&
                    error.TryGetProperty("message", out JsonElement message))
                {
                    string code = message.GetString() ?? "";
                    return code switch
                    {
                        "EMAIL_NOT_FOUND" => "Firebase: пользователь не найден.",
                        "INVALID_PASSWORD" => "Firebase: неверный пароль.",
                        "USER_DISABLED" => "Firebase: пользователь отключён.",
                        "INVALID_LOGIN_CREDENTIALS" => "Firebase: неверный email или пароль.",
                        _ => $"Firebase: {code}"
                    };
                }
            }
            catch
            {
                // Fall through to a short generic message.
            }

            return "Firebase: не удалось выполнить вход.";
        }

        private static FirebaseAuthSession? LoadSession()
        {
            if (TryLoadSessionFile(SessionFilePath, out FirebaseAuthSession primary))
            {
                TrySaveSession(primary);
                return primary;
            }

            if (TryLoadSessionFile(SessionBackupFilePath, out FirebaseAuthSession backup))
            {
                TrySaveSession(backup);
                return backup;
            }

            return null;
        }

        private static bool TryLoadSessionFile(
            string path,
            out FirebaseAuthSession session)
        {
            session = new FirebaseAuthSession();

            try
            {
                if (!File.Exists(path))
                    return false;

                string json = File.ReadAllText(path, Encoding.UTF8);
                FirebaseAuthSession? loaded =
                    JsonSerializer.Deserialize<FirebaseAuthSession>(json);

                if (loaded == null || string.IsNullOrWhiteSpace(loaded.RefreshToken))
                    return false;

                if (string.IsNullOrWhiteSpace(loaded.UserId))
                    loaded.UserId = TryReadUserIdFromToken(loaded.IdToken);

                session = loaded;
                return true;
            }
            catch
            {
                session = new FirebaseAuthSession();
                return false;
            }
        }

        private static void SaveSession(FirebaseAuthSession session)
        {
            Directory.CreateDirectory(FolderPath);

            string json = JsonSerializer.Serialize(
                session,
                new JsonSerializerOptions { WriteIndented = true }
            );

            string temporaryPath = SessionFilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";

            try
            {
                WriteTextThrough(temporaryPath, json);

                if (File.Exists(SessionFilePath))
                {
                    try
                    {
                        File.Replace(
                            temporaryPath,
                            SessionFilePath,
                            SessionBackupFilePath,
                            ignoreMetadataErrors: true
                        );
                    }
                    catch (PlatformNotSupportedException)
                    {
                        File.Move(temporaryPath, SessionFilePath, overwrite: true);
                    }
                    catch (IOException)
                    {
                        File.Move(temporaryPath, SessionFilePath, overwrite: true);
                    }
                }
                else
                {
                    File.Move(temporaryPath, SessionFilePath);
                }

                TryWriteSessionBackup(json);
            }
            finally
            {
                TryDelete(temporaryPath);
            }
        }

        private static void TrySaveSession(FirebaseAuthSession session)
        {
            try
            {
                SaveSession(session);
            }
            catch
            {
                // A valid in-memory session can still be used for this launch.
            }
        }

        private static FirebaseAuthSession? GetSessionSnapshot()
        {
            lock (Sync)
            {
                if (_session == null)
                    return null;

                return new FirebaseAuthSession
                {
                    Email = _session.Email,
                    UserId = _session.UserId,
                    IdToken = _session.IdToken,
                    RefreshToken = _session.RefreshToken,
                    ExpiresAtUtc = _session.ExpiresAtUtc
                };
            }
        }

        private static bool HasUsableIdToken(FirebaseAuthSession session)
        {
            return !string.IsNullOrWhiteSpace(session.IdToken) &&
                   session.ExpiresAtUtc > DateTime.UtcNow.AddMinutes(5);
        }

        private static string TryReadUserIdFromToken(string idToken)
        {
            try
            {
                string[] parts = idToken.Split('.');
                if (parts.Length < 2)
                    return "";

                string payload = parts[1]
                    .Replace('-', '+')
                    .Replace('_', '/');
                payload = payload.PadRight(
                    payload.Length + ((4 - payload.Length % 4) % 4),
                    '='
                );

                byte[] bytes = Convert.FromBase64String(payload);
                using JsonDocument document = JsonDocument.Parse(bytes);

                if (document.RootElement.TryGetProperty("user_id", out JsonElement userId))
                    return userId.GetString() ?? "";

                if (document.RootElement.TryGetProperty("sub", out JsonElement subject))
                    return subject.GetString() ?? "";
            }
            catch
            {
                // A future token refresh will fill the UID if this token is unavailable.
            }

            return "";
        }

        private static void TryWriteSessionBackup(string json)
        {
            string temporaryPath =
                SessionBackupFilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";

            try
            {
                WriteTextThrough(temporaryPath, json);
                File.Move(temporaryPath, SessionBackupFilePath, overwrite: true);
            }
            catch
            {
                // The committed primary session remains authoritative.
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
            using var writer = new StreamWriter(
                stream,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
            );

            writer.Write(content);
            writer.Flush();
            stream.Flush(flushToDisk: true);
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

        private sealed class FirebaseAuthSession
        {
            public string Email { get; set; } = "";
            public string UserId { get; set; } = "";
            public string IdToken { get; set; } = "";
            public string RefreshToken { get; set; } = "";
            public DateTime ExpiresAtUtc { get; set; } = DateTime.MinValue;
        }

        private sealed class FirebaseSignInResponse
        {
            public string Email { get; set; } = "";
            public string LocalId { get; set; } = "";
            public string IdToken { get; set; } = "";
            public string RefreshToken { get; set; } = "";
            public string ExpiresIn { get; set; } = "";
        }

        private sealed class FirebaseRefreshResponse
        {
            [JsonPropertyName("id_token")]
            public string IdToken { get; set; } = "";

            [JsonPropertyName("refresh_token")]
            public string RefreshToken { get; set; } = "";

            [JsonPropertyName("user_id")]
            public string UserId { get; set; } = "";

            [JsonPropertyName("expires_in")]
            public string ExpiresIn { get; set; } = "";
        }
    }
}
