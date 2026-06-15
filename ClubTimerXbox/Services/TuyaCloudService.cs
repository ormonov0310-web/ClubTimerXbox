using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public static class TuyaCloudService
    {
        public const string ClubTimerCategory = "ClubTimerXbox";

        private static readonly HttpClient _httpClient = new HttpClient();

        public static async Task<TuyaConnectionResult> TestConnectionAsync(TuyaSettings settings)
        {
            var token = await GetTokenAsync(settings);
            var devices = await GetDevicesAsync(settings, token);

            return new TuyaConnectionResult
            {
                Success = true,
                Message = $"Tuya подключена. Устройств найдено: {devices.Count}.",
                Devices = devices
            };
        }

        public static async Task<List<TuyaDevice>> GetDevicesAsync(TuyaSettings settings)
        {
            var token = await GetTokenAsync(settings);
            return await GetDevicesAsync(settings, token);
        }

        public static async Task<bool> SetSwitchAsync(
            TuyaSettings settings,
            string deviceId,
            bool turnOn,
            string switchCode = "switch_1")
        {
            if (string.IsNullOrWhiteSpace(deviceId))
                throw new InvalidOperationException("Не указан Tuya device id.");

            if (string.IsNullOrWhiteSpace(switchCode))
                switchCode = "switch_1";

            var token = await GetTokenAsync(settings);

            string path = $"/v1.0/iot-03/devices/{deviceId.Trim()}/commands";
            string body = JsonSerializer.Serialize(new
            {
                commands = new[]
                {
                    new
                    {
                        code = switchCode.Trim(),
                        value = turnOn
                    }
                }
            });

            using var document = await SendAsync(settings, HttpMethod.Post, path, body, token.AccessToken);

            if (!IsSuccess(document.RootElement))
                throw new InvalidOperationException(GetErrorMessage(document.RootElement));

            return document.RootElement.TryGetProperty("result", out var result) &&
                   result.ValueKind == JsonValueKind.True;
        }

        public static async Task SetSwitchCountdownAsync(
            TuyaSettings settings,
            string deviceId,
            bool finalTurnOn,
            int seconds,
            string switchCode = "switch_1",
            string countdownCode = "countdown_1")
        {
            if (seconds <= 0)
                throw new InvalidOperationException("Время countdown должно быть больше 0 секунд.");

            // Countdown on these Tuya sockets toggles the switch after the specified seconds.
            // Put the socket into the opposite state first, then let the device finish the action.
            await SetSwitchAsync(settings, deviceId, !finalTurnOn, switchCode);
            await SendCommandAsync(settings, deviceId, countdownCode, seconds);
        }

        public static async Task StartSwitchCountdownOnlyAsync(
            TuyaSettings settings,
            string deviceId,
            int seconds,
            string countdownCode = "countdown_1")
        {
            if (seconds <= 0)
                throw new InvalidOperationException("Время countdown должно быть больше 0 секунд.");

            await SendCommandAsync(settings, deviceId, countdownCode, seconds);
        }

        public static async Task CancelSwitchCountdownAsync(
            TuyaSettings settings,
            string deviceId,
            string countdownCode = "countdown_1")
        {
            await SendCommandAsync(settings, deviceId, countdownCode, 0);
        }

        public static async Task ApplyOfflineDailyScheduleAsync(
            TuyaSettings settings,
            string deviceId,
            string onTime,
            string offTime,
            string timezoneId = "Asia/Bishkek",
            string switchCode = "switch_1")
        {
            ValidateScheduleTime(onTime, "включения");
            ValidateScheduleTime(offTime, "выключения");

            var token = await GetTokenAsync(settings);

            await DeleteClubTimerSchedulesAsync(settings, token, deviceId);

            await AddScheduleTaskAsync(
                settings,
                token,
                deviceId,
                aliasName: "ClubTimerXbox ON",
                time: onTime,
                turnOn: true,
                timezoneId: timezoneId,
                switchCode: switchCode);

            await AddScheduleTaskAsync(
                settings,
                token,
                deviceId,
                aliasName: "ClubTimerXbox OFF",
                time: offTime,
                turnOn: false,
                timezoneId: timezoneId,
                switchCode: switchCode);
        }

        public static async Task<List<TuyaScheduleTask>> GetClubTimerSchedulesAsync(
            TuyaSettings settings,
            string deviceId)
        {
            if (string.IsNullOrWhiteSpace(deviceId))
                throw new InvalidOperationException("Не указан Tuya device id.");

            var token = await GetTokenAsync(settings);
            string path = $"/v2.0/cloud/timer/device/{deviceId.Trim()}?category={Uri.EscapeDataString(ClubTimerCategory)}";

            using var document = await SendAsync(settings, HttpMethod.Get, path, "", token.AccessToken);

            if (!IsSuccess(document.RootElement))
                throw new InvalidOperationException(GetErrorMessage(document.RootElement));

            var schedules = new List<TuyaScheduleTask>();

            if (document.RootElement.TryGetProperty("result", out var result))
                CollectClubTimerSchedules(result, schedules);

            return schedules
                .Where(schedule => !string.IsNullOrWhiteSpace(schedule.TimerId))
                .GroupBy(schedule => schedule.TimerId, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(schedule => schedule.Time, StringComparer.OrdinalIgnoreCase)
                .ThenBy(schedule => schedule.TurnOn ? 1 : 0)
                .ToList();
        }

        public static async Task SaveClubTimerScheduleAsync(
            TuyaSettings settings,
            string deviceId,
            TuyaScheduleTask schedule,
            string switchCode = "switch_1")
        {
            if (string.IsNullOrWhiteSpace(deviceId))
                throw new InvalidOperationException("Не указан Tuya device id.");

            ValidateScheduleTime(schedule.Time, "таймера");

            if (string.IsNullOrWhiteSpace(schedule.TimezoneId))
                schedule.TimezoneId = "Asia/Bishkek";

            if (string.IsNullOrWhiteSpace(schedule.Loops))
                schedule.Loops = "1111111";

            var token = await GetTokenAsync(settings);
            string path = $"/v2.0/cloud/timer/device/{deviceId.Trim()}";
            string body = JsonSerializer.Serialize(BuildScheduleBody(schedule, switchCode));
            var method = string.IsNullOrWhiteSpace(schedule.TimerId)
                ? HttpMethod.Post
                : HttpMethod.Put;

            using var document = await SendAsync(settings, method, path, body, token.AccessToken);

            if (!IsSuccess(document.RootElement))
                throw new InvalidOperationException(GetErrorMessage(document.RootElement));

            if (string.IsNullOrWhiteSpace(schedule.TimerId) &&
                document.RootElement.TryGetProperty("result", out var result))
            {
                schedule.TimerId = GetFirstString(result, "timer_id", "time_id", "id");
            }
        }

        public static async Task DeleteClubTimerScheduleAsync(
            TuyaSettings settings,
            string deviceId,
            string timerId)
        {
            if (string.IsNullOrWhiteSpace(deviceId))
                throw new InvalidOperationException("Не указан Tuya device id.");

            if (string.IsNullOrWhiteSpace(timerId))
                throw new InvalidOperationException("Не указан ID таймера.");

            var token = await GetTokenAsync(settings);
            string path = $"/v2.0/cloud/timer/device/{deviceId.Trim()}/batch?timer_ids={Uri.EscapeDataString(timerId.Trim())}";

            using var document = await SendAsync(settings, HttpMethod.Delete, path, "", token.AccessToken);

            if (!IsSuccess(document.RootElement))
                throw new InvalidOperationException(GetErrorMessage(document.RootElement));
        }

        private static Dictionary<string, object> BuildScheduleBody(
            TuyaScheduleTask schedule,
            string switchCode)
        {
            string aliasName = BuildTuyaScheduleAlias(schedule);

            var body = new Dictionary<string, object>
            {
                ["alias_name"] = aliasName,
                ["time"] = schedule.Time.Trim(),
                ["timezone_id"] = schedule.TimezoneId.Trim(),
                ["loops"] = schedule.Loops.Trim(),
                ["category"] = ClubTimerCategory,
                ["functions"] = new[]
                {
                    new Dictionary<string, object>
                    {
                        ["code"] = string.IsNullOrWhiteSpace(switchCode) ? "switch_1" : switchCode.Trim(),
                        ["value"] = schedule.TurnOn
                    }
                }
            };

            if (!string.IsNullOrWhiteSpace(schedule.TimerId))
                body["timer_id"] = schedule.TimerId.Trim();

            if (schedule.Loops.Trim() == "0000000" && !string.IsNullOrWhiteSpace(schedule.Date))
                body["date"] = schedule.Date.Trim();

            return body;
        }

        private static string BuildTuyaScheduleAlias(TuyaScheduleTask schedule)
        {
            string compactTime = schedule.Time
                .Replace(":", "", StringComparison.Ordinal)
                .Replace(" ", "", StringComparison.Ordinal);

            if (compactTime.Length != 4 || !compactTime.All(char.IsDigit))
                compactTime = DateTime.Now.ToString("HHmm", CultureInfo.InvariantCulture);

            return schedule.TurnOn
                ? $"ct_on_{compactTime}"
                : $"ct_off_{compactTime}";
        }

        private static void ValidateScheduleTime(string time, string label)
        {
            if (!TimeSpan.TryParseExact(time, @"hh\:mm", CultureInfo.InvariantCulture, out _))
                throw new InvalidOperationException($"Время {label} должно быть в формате ЧЧ:ММ, например 10:30.");
        }

        private static async Task AddScheduleTaskAsync(
            TuyaSettings settings,
            TuyaToken token,
            string deviceId,
            string aliasName,
            string time,
            bool turnOn,
            string timezoneId,
            string switchCode)
        {
            string path = $"/v2.0/cloud/timer/device/{deviceId.Trim()}";
            string body = JsonSerializer.Serialize(new
            {
                alias_name = aliasName,
                time,
                timezone_id = timezoneId,
                loops = "1111111",
                category = ClubTimerCategory,
                functions = new[]
                {
                    new
                    {
                        code = switchCode,
                        value = turnOn
                    }
                }
            });

            using var document = await SendAsync(settings, HttpMethod.Post, path, body, token.AccessToken);

            if (!IsSuccess(document.RootElement))
                throw new InvalidOperationException(GetErrorMessage(document.RootElement));
        }

        private static async Task DeleteClubTimerSchedulesAsync(
            TuyaSettings settings,
            TuyaToken token,
            string deviceId)
        {
            var timerIds = await QueryClubTimerScheduleIdsAsync(settings, token, deviceId);

            if (timerIds.Count == 0)
                return;

            string joinedIds = string.Join(",", timerIds);
            string path = $"/v2.0/cloud/timer/device/{deviceId.Trim()}/batch?timer_ids={Uri.EscapeDataString(joinedIds)}";

            using var document = await SendAsync(settings, HttpMethod.Delete, path, "", token.AccessToken);

            if (!IsSuccess(document.RootElement))
                throw new InvalidOperationException(GetErrorMessage(document.RootElement));
        }

        private static async Task<List<string>> QueryClubTimerScheduleIdsAsync(
            TuyaSettings settings,
            TuyaToken token,
            string deviceId)
        {
            string path = $"/v2.0/cloud/timer/device/{deviceId.Trim()}?category={ClubTimerCategory}";

            using var document = await SendAsync(settings, HttpMethod.Get, path, "", token.AccessToken);

            if (!IsSuccess(document.RootElement))
                return new List<string>();

            if (!document.RootElement.TryGetProperty("result", out var result))
                return new List<string>();

            var ids = new List<string>();
            CollectClubTimerIds(result, ids);

            return ids
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static void CollectClubTimerIds(JsonElement element, List<string> ids)
        {
            if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray())
                    CollectClubTimerIds(item, ids);

                return;
            }

            if (element.ValueKind != JsonValueKind.Object)
                return;

            bool isClubTimer =
                GetString(element, "category").Equals(ClubTimerCategory, StringComparison.OrdinalIgnoreCase) ||
                GetString(element, "alias_name").StartsWith(ClubTimerCategory, StringComparison.OrdinalIgnoreCase);

            if (isClubTimer)
            {
                string id =
                    GetFirstString(element, "timer_id", "time_id", "id");

                if (!string.IsNullOrWhiteSpace(id))
                    ids.Add(id);
            }

            foreach (var property in element.EnumerateObject())
                CollectClubTimerIds(property.Value, ids);
        }

        private static void CollectClubTimerSchedules(JsonElement element, List<TuyaScheduleTask> schedules)
        {
            if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray())
                    CollectClubTimerSchedules(item, schedules);

                return;
            }

            if (element.ValueKind != JsonValueKind.Object)
                return;

            string id = GetFirstString(element, "timer_id", "time_id", "id");
            string time = GetString(element, "time");
            string aliasName = GetString(element, "alias_name");
            string category = GetString(element, "category");
            bool isClubTimer =
                category.Equals(ClubTimerCategory, StringComparison.OrdinalIgnoreCase) ||
                aliasName.StartsWith(ClubTimerCategory, StringComparison.OrdinalIgnoreCase);

            if (isClubTimer &&
                !string.IsNullOrWhiteSpace(id) &&
                !string.IsNullOrWhiteSpace(time) &&
                TryReadScheduleSwitchAction(element, out bool turnOn))
            {
                string loops = GetString(element, "loops");
                string date = GetString(element, "date");

                if (string.IsNullOrWhiteSpace(loops))
                    loops = string.IsNullOrWhiteSpace(date) ? "1111111" : "0000000";

                bool enable = true;

                if (element.TryGetProperty("enable", out var enableElement) &&
                    (enableElement.ValueKind == JsonValueKind.True || enableElement.ValueKind == JsonValueKind.False))
                {
                    enable = enableElement.GetBoolean();
                }

                schedules.Add(new TuyaScheduleTask
                {
                    TimerId = id,
                    AliasName = aliasName,
                    Time = time,
                    Date = date,
                    Loops = loops,
                    TimezoneId = GetString(element, "timezone_id"),
                    Enable = enable,
                    TurnOn = turnOn
                });
            }

            foreach (var property in element.EnumerateObject())
                CollectClubTimerSchedules(property.Value, schedules);
        }

        private static bool TryReadScheduleSwitchAction(JsonElement element, out bool turnOn)
        {
            turnOn = false;

            if (!element.TryGetProperty("functions", out var functions) ||
                functions.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (var function in functions.EnumerateArray())
            {
                string code = GetString(function, "code");

                if (!code.StartsWith("switch", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!function.TryGetProperty("value", out var value))
                    return false;

                if (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False)
                {
                    turnOn = value.GetBoolean();
                    return true;
                }

                if (value.ValueKind == JsonValueKind.String &&
                    bool.TryParse(value.GetString(), out bool parsed))
                {
                    turnOn = parsed;
                    return true;
                }
            }

            return false;
        }

        private static async Task<bool> SendCommandAsync(
            TuyaSettings settings,
            string deviceId,
            string code,
            object value)
        {
            if (string.IsNullOrWhiteSpace(deviceId))
                throw new InvalidOperationException("Не указан Tuya device id.");

            if (string.IsNullOrWhiteSpace(code))
                throw new InvalidOperationException("Не указан код команды Tuya.");

            var token = await GetTokenAsync(settings);

            string path = $"/v1.0/iot-03/devices/{deviceId.Trim()}/commands";
            string body = JsonSerializer.Serialize(new
            {
                commands = new[]
                {
                    new
                    {
                        code = code.Trim(),
                        value
                    }
                }
            });

            using var document = await SendAsync(settings, HttpMethod.Post, path, body, token.AccessToken);

            if (!IsSuccess(document.RootElement))
                throw new InvalidOperationException(GetErrorMessage(document.RootElement));

            return document.RootElement.TryGetProperty("result", out var result) &&
                   result.ValueKind == JsonValueKind.True;
        }

        private static async Task<TuyaToken> GetTokenAsync(TuyaSettings settings)
        {
            EnsureConfigured(settings);

            using var document = await SendAsync(
                settings,
                HttpMethod.Get,
                "/v1.0/token?grant_type=1",
                "",
                ""
            );

            if (!IsSuccess(document.RootElement))
                throw new InvalidOperationException(GetErrorMessage(document.RootElement));

            var result = document.RootElement.GetProperty("result");

            return new TuyaToken
            {
                AccessToken = GetString(result, "access_token"),
                Uid = GetString(result, "uid")
            };
        }

        private static async Task<List<TuyaDevice>> GetDevicesAsync(
            TuyaSettings settings,
            TuyaToken token)
        {
            var paths = new List<string>
            {
                "/v2.0/cloud/thing/device?page_size=20"
            };

            if (!string.IsNullOrWhiteSpace(token.Uid))
                paths.Add($"/v1.0/users/{token.Uid}/devices");

            paths.Add("/v1.0/devices");

            Exception? lastError = null;

            foreach (string path in paths.Distinct())
            {
                try
                {
                    using var document = await SendAsync(settings, HttpMethod.Get, path, "", token.AccessToken);

                    if (!IsSuccess(document.RootElement))
                        throw new InvalidOperationException(GetErrorMessage(document.RootElement));

                    var devices = ParseDevices(document.RootElement);
                    await FillSwitchStatusesAsync(settings, token, devices);
                    return devices;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                }
            }

            throw new InvalidOperationException(
                lastError == null
                    ? "Не удалось получить список устройств Tuya."
                    : lastError.Message
            );
        }

        private static async Task<JsonDocument> SendAsync(
            TuyaSettings settings,
            HttpMethod method,
            string path,
            string body,
            string accessToken)
        {
            string endpoint = settings.Endpoint.TrimEnd('/');
            string url = endpoint + path;
            string timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
            string nonce = Guid.NewGuid().ToString("N");
            string sign = BuildSign(settings, method.Method, path, body, accessToken, timestamp, nonce);

            var request = new HttpRequestMessage(method, url);
            request.Headers.TryAddWithoutValidation("client_id", settings.AccessId);
            request.Headers.TryAddWithoutValidation("sign", sign);
            request.Headers.TryAddWithoutValidation("sign_method", "HMAC-SHA256");
            request.Headers.TryAddWithoutValidation("t", timestamp);
            request.Headers.TryAddWithoutValidation("nonce", nonce);
            request.Headers.TryAddWithoutValidation("lang", "en");

            if (!string.IsNullOrWhiteSpace(accessToken))
                request.Headers.TryAddWithoutValidation("access_token", accessToken);

            if (!string.IsNullOrEmpty(body))
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request);
            string responseText = await response.Content.ReadAsStringAsync();

            if (string.IsNullOrWhiteSpace(responseText))
                responseText = "{}";

            return JsonDocument.Parse(responseText);
        }

        private static string BuildSign(
            TuyaSettings settings,
            string method,
            string path,
            string body,
            string accessToken,
            string timestamp,
            string nonce)
        {
            string bodyHash = Sha256Hex(body ?? "");
            string stringToSign = method.ToUpperInvariant() + "\n" +
                                  bodyHash + "\n" +
                                  "\n" +
                                  path;

            string source = string.IsNullOrWhiteSpace(accessToken)
                ? settings.AccessId + timestamp + nonce + stringToSign
                : settings.AccessId + accessToken + timestamp + nonce + stringToSign;

            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(settings.AccessSecret));
            byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(source));

            return Convert.ToHexString(hash).ToUpperInvariant();
        }

        private static string Sha256Hex(string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            byte[] hash = SHA256.HashData(bytes);

            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private static void EnsureConfigured(TuyaSettings settings)
        {
            if (string.IsNullOrWhiteSpace(settings.Endpoint))
                throw new InvalidOperationException("Не указан Tuya endpoint.");

            if (string.IsNullOrWhiteSpace(settings.AccessId))
                throw new InvalidOperationException("Не указан Tuya Access ID.");

            if (string.IsNullOrWhiteSpace(settings.AccessSecret))
                throw new InvalidOperationException("Не указан Tuya Access Secret.");
        }

        private static bool IsSuccess(JsonElement root)
        {
            return root.TryGetProperty("success", out var success) &&
                   success.ValueKind == JsonValueKind.True;
        }

        private static string GetErrorMessage(JsonElement root)
        {
            string code = root.TryGetProperty("code", out var codeElement)
                ? codeElement.ToString()
                : "";

            string message = root.TryGetProperty("msg", out var msgElement)
                ? msgElement.ToString()
                : "Tuya вернула ошибку.";

            if (!string.IsNullOrWhiteSpace(code))
                return $"Tuya ошибка {code}: {message}";

            return message;
        }

        private static List<TuyaDevice> ParseDevices(JsonElement root)
        {
            if (!root.TryGetProperty("result", out var result))
                return new List<TuyaDevice>();

            JsonElement devicesElement = result;

            if (result.ValueKind == JsonValueKind.Object &&
                result.TryGetProperty("devices", out var nestedDevices))
            {
                devicesElement = nestedDevices;
            }

            if (result.ValueKind == JsonValueKind.Object &&
                result.TryGetProperty("list", out var listedDevices))
            {
                devicesElement = listedDevices;
            }

            if (devicesElement.ValueKind != JsonValueKind.Array)
                return new List<TuyaDevice>();

            var devices = new List<TuyaDevice>();

            foreach (var item in devicesElement.EnumerateArray())
            {
                devices.Add(new TuyaDevice
                {
                    Id = GetString(item, "id"),
                    Name = GetFirstString(item, "customName", "name"),
                    Category = GetString(item, "category"),
                    ProductName = GetFirstString(item, "product_name", "productName"),
                    Online = GetFirstBool(item, "online", "isOnline"),
                    IsOn = GetSwitchStatus(item),
                    CountdownSeconds = GetCountdownSeconds(item)
                });
            }

            return devices;
        }

        private static bool? GetSwitchStatus(JsonElement item)
        {
            if (!item.TryGetProperty("status", out var status) ||
                status.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            return GetSwitchStatusFromArray(status);
        }

        private static int GetCountdownSeconds(JsonElement item)
        {
            if (!item.TryGetProperty("status", out var status) ||
                status.ValueKind != JsonValueKind.Array)
            {
                return 0;
            }

            return GetCountdownSecondsFromArray(status);
        }

        private static async Task FillSwitchStatusesAsync(
            TuyaSettings settings,
            TuyaToken token,
            List<TuyaDevice> devices)
        {
            foreach (var device in devices)
            {
                if (string.IsNullOrWhiteSpace(device.Id))
                    continue;

                try
                {
                    string path = $"/v1.0/iot-03/devices/{device.Id}/status";
                    using var document = await SendAsync(settings, HttpMethod.Get, path, "", token.AccessToken);

                    if (!IsSuccess(document.RootElement))
                        continue;

                    if (!document.RootElement.TryGetProperty("result", out var result) ||
                        result.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    device.IsOn = GetSwitchStatusFromArray(result);
                    device.CountdownSeconds = GetCountdownSecondsFromArray(result);
                }
                catch
                {
                    // Список устройств важнее статуса одной розетки.
                }
            }
        }

        private static bool? GetSwitchStatusFromArray(JsonElement status)
        {
            foreach (var statusItem in status.EnumerateArray())
            {
                string code = GetString(statusItem, "code");

                if (!code.StartsWith("switch", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (statusItem.TryGetProperty("value", out var value) &&
                    (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False))
                {
                    return value.GetBoolean();
                }
            }

            return null;
        }

        private static int GetCountdownSecondsFromArray(JsonElement status)
        {
            foreach (var statusItem in status.EnumerateArray())
            {
                string code = GetString(statusItem, "code");

                if (!code.Equals("countdown_1", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!statusItem.TryGetProperty("value", out var value))
                    return 0;

                if (value.ValueKind == JsonValueKind.Number &&
                    value.TryGetInt32(out int seconds))
                {
                    return Math.Max(0, seconds);
                }

                if (value.ValueKind == JsonValueKind.String &&
                    int.TryParse(value.GetString(), out int parsed))
                {
                    return Math.Max(0, parsed);
                }
            }

            return 0;
        }

        private static string GetString(JsonElement element, string propertyName)
        {
            return element.TryGetProperty(propertyName, out var property) &&
                   property.ValueKind == JsonValueKind.String
                ? property.GetString() ?? ""
                : "";
        }

        private static string GetFirstString(JsonElement element, params string[] propertyNames)
        {
            foreach (string propertyName in propertyNames)
            {
                string value = GetString(element, propertyName);

                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }

            return "";
        }

        private static bool GetFirstBool(JsonElement element, params string[] propertyNames)
        {
            foreach (string propertyName in propertyNames)
            {
                if (element.TryGetProperty(propertyName, out var property) &&
                    (property.ValueKind == JsonValueKind.True || property.ValueKind == JsonValueKind.False))
                {
                    return property.GetBoolean();
                }
            }

            return false;
        }

        private class TuyaToken
        {
            public string AccessToken { get; set; } = "";

            public string Uid { get; set; } = "";
        }
    }

    public class TuyaConnectionResult
    {
        public bool Success { get; set; }

        public string Message { get; set; } = "";

        public List<TuyaDevice> Devices { get; set; } = new List<TuyaDevice>();
    }
}
