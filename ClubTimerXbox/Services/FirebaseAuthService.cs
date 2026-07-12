using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ClubTimerXbox.Services
{
    public static class FirebaseAuthService
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        private static readonly object Sync = new object();

        private static readonly string FolderPath =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ClubTimerXbox"
            );

        private static readonly string SessionFilePath =
            Path.Combine(FolderPath, "firebase_auth.json");

        private static FirebaseAuthSession? _session = LoadSession();

        public static bool IsConfigured =>
            !string.IsNullOrWhiteSpace(FirebaseSettings.WebApiKey);

        public static string CurrentEmail => _session?.Email ?? "";

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

            _session = new FirebaseAuthSession
            {
                Email = signIn.Email,
                IdToken = signIn.IdToken,
                RefreshToken = signIn.RefreshToken,
                ExpiresAtUtc = DateTime.UtcNow.AddSeconds(ParseExpiresIn(signIn.ExpiresIn))
            };

            SaveSession(_session);
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

            FirebaseAuthSession? session;
            lock (Sync)
            {
                session = _session;
            }

            if (session == null || string.IsNullOrWhiteSpace(session.RefreshToken))
                throw new InvalidOperationException("Нужно войти в Firebase.");

            if (!string.IsNullOrWhiteSpace(session.IdToken) &&
                session.ExpiresAtUtc > DateTime.UtcNow.AddMinutes(5))
            {
                return session.IdToken;
            }

            return await RefreshTokenAsync(session.RefreshToken);
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

            var nextSession = new FirebaseAuthSession
            {
                Email = _session?.Email ?? "",
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
            try
            {
                if (!File.Exists(SessionFilePath))
                    return null;

                string json = File.ReadAllText(SessionFilePath);
                return JsonSerializer.Deserialize<FirebaseAuthSession>(json);
            }
            catch
            {
                return null;
            }
        }

        private static void SaveSession(FirebaseAuthSession session)
        {
            Directory.CreateDirectory(FolderPath);

            string json = JsonSerializer.Serialize(
                session,
                new JsonSerializerOptions { WriteIndented = true }
            );

            File.WriteAllText(SessionFilePath, json);
        }

        private sealed class FirebaseAuthSession
        {
            public string Email { get; set; } = "";
            public string IdToken { get; set; } = "";
            public string RefreshToken { get; set; } = "";
            public DateTime ExpiresAtUtc { get; set; } = DateTime.MinValue;
        }

        private sealed class FirebaseSignInResponse
        {
            public string Email { get; set; } = "";
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

            [JsonPropertyName("expires_in")]
            public string ExpiresIn { get; set; } = "";
        }
    }
}
