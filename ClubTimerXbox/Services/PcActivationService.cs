using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public static class PcActivationService
    {
        private static readonly HttpClient _httpClient = new HttpClient();

        public static async Task<PcActivationResult> ActivateAsync(string code)
        {
            code = code.Trim();

            if (string.IsNullOrWhiteSpace(code))
                return PcActivationResult.Fail("Введите код активации.");

            var activation = await GetJsonAsync($"activationCodes/{code}");

            if (activation == null)
                return PcActivationResult.Fail("Код не найден. Проверьте код на телефоне.");

            if (GetBool(activation.Value, "used"))
                return PcActivationResult.Fail("Этот код уже использован.");

            string action = GetString(activation.Value, "action");
            if (action != "activateNewClub" && action != "restorePc")
                return PcActivationResult.Fail("Этот код не подходит для активации ПК.");

            string clubId = GetString(activation.Value, "clubId");
            string clubName = GetString(activation.Value, "clubName");

            if (string.IsNullOrWhiteSpace(clubId))
                return PcActivationResult.Fail("В коде нет ID клуба.");

            if (string.IsNullOrWhiteSpace(clubName))
                clubName = clubId;

            var settingsJson = await GetJsonAsync($"clubs/{clubId}/settings");
            var employeesJson = await GetJsonAsync($"clubs/{clubId}/employees");

            if (settingsJson != null)
                AppSettingsService.Save(ParseClubSettings(settingsJson.Value));

            var employees = ParseEmployees(employeesJson);
            if (employees.Count == 0)
                return PcActivationResult.Fail("В клубе нет сотрудников. Добавьте сотрудника на телефоне.");

            EmployeeService.ReplaceAll(employees);
            PcIdentityService.Activate(clubId, clubName);

            string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string installationId = PcIdentityService.Current.InstallationId;
            string pcName = Environment.MachineName;

            await PatchAsync($"activationCodes/{code}", new
            {
                used = true,
                usedAt = now,
                installationId,
                pcName
            });

            var meta = new
            {
                id = clubId,
                name = clubName,
                isActivated = true,
                installationId,
                pcName,
                activatedAt = PcIdentityService.Current.ActivatedAt,
                updatedAt = now
            };

            await PatchAsync($"clubs/{clubId}/meta", meta);
            await PatchAsync($"owner/clubs/{clubId}", meta);

            return PcActivationResult.Ok(clubId, clubName);
        }

        private static ClubSettings ParseClubSettings(JsonElement root)
        {
            var current = AppSettingsService.Current;
            var settings = new ClubSettings
            {
                TvCount = GetInt(root, "tvCount", current.TvCount),
                WheelCount = GetInt(root, "wheelCount", current.WheelCount),
                VipRoomCount = GetInt(root, "vipRoomCount", current.VipRoomCount),
                TvTariff = current.TvTariff,
                WheelTariff = current.WheelTariff,
                VipTariff = current.VipTariff
            };

            if (root.TryGetProperty("tariffs", out JsonElement tariffs))
            {
                if (tariffs.TryGetProperty("tv", out JsonElement tv))
                    settings.TvTariff = ParseTariff(tv, current.TvTariff);

                if (tariffs.TryGetProperty("wheel", out JsonElement wheel))
                    settings.WheelTariff = ParseTariff(wheel, current.WheelTariff);

                if (tariffs.TryGetProperty("vip", out JsonElement vip))
                    settings.VipTariff = ParseTariff(vip, current.VipTariff);
            }

            return settings;
        }

        private static TariffSettings ParseTariff(JsonElement root, TariffSettings fallback)
        {
            int oneHour = GetInt(root, "oneHourPrice", fallback.OneHourPrice);
            int halfHour = GetInt(root, "halfHourPrice", fallback.HalfHourPrice);
            int fiveMinutes = GetInt(root, "fiveMinutesPrice", fallback.FiveMinutesPrice);

            return new TariffSettings
            {
                OneHourPrice = oneHour,
                HalfHourPrice = halfHour,
                FiveMinutesPrice = fiveMinutes,
                PricePerMinute = oneHour > 0 ? oneHour / 60.0 : fallback.PricePerMinute
            };
        }

        private static List<Employee> ParseEmployees(JsonElement? root)
        {
            var employees = new List<Employee>();

            if (root == null || root.Value.ValueKind != JsonValueKind.Object)
                return employees;

            foreach (var property in root.Value.EnumerateObject())
            {
                var item = property.Value;
                if (item.ValueKind != JsonValueKind.Object)
                    continue;

                string name = GetString(item, "name");
                string pinCode = GetString(item, "pinCode");

                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(pinCode))
                    continue;

                employees.Add(new Employee
                {
                    EmployeeId = GetString(item, "employeeId").Trim(),
                    Name = name.Trim(),
                    PinCode = pinCode.Trim(),
                    IsActive = GetBool(item, "isActive", true)
                });
            }

            return employees
                .GroupBy(employee => employee.EmployeeId, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Last())
                .ToList();
        }

        private static async Task<JsonElement?> GetJsonAsync(string path)
        {
            string url = await FirebaseAuthService.BuildDatabaseUrlAsync(path);
            string json = await _httpClient.GetStringAsync(url);

            if (string.IsNullOrWhiteSpace(json) || json == "null")
                return null;

            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }

        private static async Task PatchAsync(string path, object data)
        {
            string url = await FirebaseAuthService.BuildDatabaseUrlAsync(path);
            string json = JsonSerializer.Serialize(data);

            using var request = new HttpRequestMessage(new HttpMethod("PATCH"), url)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            await _httpClient.SendAsync(request);
        }

        private static string GetString(JsonElement root, string name)
        {
            if (!root.TryGetProperty(name, out JsonElement value))
                return "";

            return value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? ""
                : value.ToString();
        }

        private static int GetInt(JsonElement root, string name, int fallback)
        {
            if (!root.TryGetProperty(name, out JsonElement value))
                return fallback;

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number))
                return number;

            return int.TryParse(value.ToString(), out number) ? number : fallback;
        }

        private static bool GetBool(JsonElement root, string name, bool fallback = false)
        {
            if (!root.TryGetProperty(name, out JsonElement value))
                return fallback;

            if (value.ValueKind == JsonValueKind.True)
                return true;

            if (value.ValueKind == JsonValueKind.False)
                return false;

            return bool.TryParse(value.ToString(), out bool result) ? result : fallback;
        }
    }

    public class PcActivationResult
    {
        public bool Success { get; set; }

        public string ClubId { get; set; } = "";

        public string ClubName { get; set; } = "";

        public string ErrorMessage { get; set; } = "";

        public static PcActivationResult Ok(string clubId, string clubName)
        {
            return new PcActivationResult
            {
                Success = true,
                ClubId = clubId,
                ClubName = clubName
            };
        }

        public static PcActivationResult Fail(string message)
        {
            return new PcActivationResult
            {
                Success = false,
                ErrorMessage = message
            };
        }
    }
}
