using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public static class TuyaSettingsStorageService
    {
        private static readonly string FolderPath =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ClubTimerXbox"
            );

        private static readonly string FilePath =
            Path.Combine(FolderPath, "tuya_settings.json");

        public static TuyaSettings Current { get; private set; } = Load();

        public static TuyaSettings Load()
        {
            try
            {
                if (!File.Exists(FilePath))
                    return new TuyaSettings();

                string json = File.ReadAllText(FilePath);
                var settings = JsonSerializer.Deserialize<TuyaSettings>(json);

                if (settings == null)
                    return new TuyaSettings();

                Normalize(settings);
                return settings;
            }
            catch
            {
                return new TuyaSettings();
            }
        }

        public static void Save(TuyaSettings settings)
        {
            Normalize(settings);

            Directory.CreateDirectory(FolderPath);

            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            string json = JsonSerializer.Serialize(settings, options);
            File.WriteAllText(FilePath, json);

            Current = settings;
        }

        public static TuyaDevicePreference GetOrCreateDevicePreference(
            TuyaSettings settings,
            string deviceId,
            string cloudName)
        {
            if (settings.DevicePreferences == null)
                settings.DevicePreferences = new System.Collections.Generic.List<TuyaDevicePreference>();

            if (settings.WorkModes == null)
                settings.WorkModes = new System.Collections.Generic.List<TuyaWorkMode>();

            if (settings.ActiveWorkModes == null)
                settings.ActiveWorkModes = new System.Collections.Generic.List<TuyaActiveWorkMode>();

            var preference = settings.DevicePreferences.FirstOrDefault(item =>
                item.DeviceId.Equals(deviceId, StringComparison.OrdinalIgnoreCase));

            if (preference != null)
            {
                if (!string.IsNullOrWhiteSpace(cloudName))
                    preference.CloudName = cloudName.Trim();

                return preference;
            }

            preference = new TuyaDevicePreference
            {
                DeviceId = deviceId.Trim(),
                CloudName = cloudName.Trim()
            };

            settings.DevicePreferences.Add(preference);
            return preference;
        }

        public static string GetDeviceDisplayName(TuyaSettings settings, TuyaDevice device)
        {
            var preference = GetDevicePreference(settings, device.Id);

            if (preference != null && !string.IsNullOrWhiteSpace(preference.DisplayName))
                return preference.DisplayName;

            if (!string.IsNullOrWhiteSpace(device.Name))
                return device.Name;

            return device.Id;
        }

        public static bool IsDeviceHidden(TuyaSettings settings, string deviceId)
        {
            return GetDevicePreference(settings, deviceId)?.IsHidden == true;
        }

        public static string GetDeviceType(TuyaSettings settings, string deviceId)
        {
            string type = GetDevicePreference(settings, deviceId)?.DeviceType ?? TuyaDeviceTypes.Appliance;
            return IsKnownDeviceType(type) ? type : TuyaDeviceTypes.Appliance;
        }

        public static string GetDeviceTypeTitle(TuyaSettings settings, string deviceId)
        {
            return GetDeviceType(settings, deviceId) == TuyaDeviceTypes.TvSocket
                ? "ТВ розетка"
                : "Прибор";
        }

        private static TuyaDevicePreference? GetDevicePreference(TuyaSettings settings, string deviceId)
        {
            if (settings.DevicePreferences == null)
                return null;

            return settings.DevicePreferences.FirstOrDefault(item =>
                item.DeviceId.Equals(deviceId, StringComparison.OrdinalIgnoreCase));
        }

        private static void Normalize(TuyaSettings settings)
        {
            if (string.IsNullOrWhiteSpace(settings.Endpoint))
                settings.Endpoint = "https://openapi.tuyaeu.com";

            settings.Endpoint = settings.Endpoint.Trim().TrimEnd('/');
            settings.AccessId = settings.AccessId.Trim();
            settings.AccessSecret = settings.AccessSecret.Trim();

            if (settings.PlaceMappings == null)
                settings.PlaceMappings = new System.Collections.Generic.List<TuyaPlaceDeviceMapping>();

            if (settings.DevicePreferences == null)
                settings.DevicePreferences = new System.Collections.Generic.List<TuyaDevicePreference>();

            foreach (var mapping in settings.PlaceMappings)
            {
                mapping.PlaceName = mapping.PlaceName.Trim();
                mapping.DeviceId = mapping.DeviceId.Trim();
                mapping.DeviceName = mapping.DeviceName.Trim();

                if (string.IsNullOrWhiteSpace(mapping.SwitchCode))
                    mapping.SwitchCode = "switch_1";

                mapping.SwitchCode = mapping.SwitchCode.Trim();
            }

            foreach (var preference in settings.DevicePreferences)
            {
                preference.DeviceId = preference.DeviceId.Trim();
                preference.CloudName = preference.CloudName.Trim();
                preference.DisplayName = preference.DisplayName.Trim();
                preference.DeviceType = NormalizeDeviceType(preference.DeviceType);
            }

            settings.DevicePreferences = settings.DevicePreferences
                .Where(item => !string.IsNullOrWhiteSpace(item.DeviceId))
                .GroupBy(item => item.DeviceId, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Last())
                .ToList();

            if (!settings.WorkModesInitialized)
            {
                settings.WorkModes.Add(new TuyaWorkMode
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = "Выключить через 10 минут",
                    ModeType = TuyaWorkModeTypes.TurnOffAfterMinutes,
                    Minutes = 10
                });

                settings.WorkModes.Add(new TuyaWorkMode
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = "Выключить через 30 минут",
                    ModeType = TuyaWorkModeTypes.TurnOffAfterMinutes,
                    Minutes = 30
                });

                settings.WorkModes.Add(new TuyaWorkMode
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = "Включить на 30 минут",
                    ModeType = TuyaWorkModeTypes.TurnOnForMinutes,
                    Minutes = 30
                });

                settings.WorkModesInitialized = true;
            }

            foreach (var mode in settings.WorkModes)
            {
                if (string.IsNullOrWhiteSpace(mode.Id))
                    mode.Id = Guid.NewGuid().ToString("N");

                mode.Name = mode.Name.Trim();
                mode.ModeType = mode.ModeType.Trim();

                if (!IsKnownWorkModeType(mode.ModeType))
                    mode.ModeType = TuyaWorkModeTypes.TurnOffAfterMinutes;

                if (mode.Minutes < 1)
                    mode.Minutes = 1;

                if (mode.Minutes > 1440)
                    mode.Minutes = 1440;

                if (string.IsNullOrWhiteSpace(mode.Name))
                {
                    mode.Name = GetDefaultWorkModeName(mode.ModeType, mode.Minutes);
                }
            }

            settings.WorkModes = settings.WorkModes
                .GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Last())
                .ToList();

            foreach (var activeMode in settings.ActiveWorkModes)
            {
                activeMode.DeviceId = activeMode.DeviceId.Trim();
                activeMode.WorkModeId = activeMode.WorkModeId.Trim();

                if (activeMode.DurationSeconds < 0)
                    activeMode.DurationSeconds = 0;
            }

            settings.ActiveWorkModes = settings.ActiveWorkModes
                .Where(item =>
                    !string.IsNullOrWhiteSpace(item.DeviceId) &&
                    !string.IsNullOrWhiteSpace(item.WorkModeId))
                .GroupBy(item => item.DeviceId, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Last())
                .ToList();
        }

        private static bool IsKnownWorkModeType(string modeType)
        {
            return modeType == TuyaWorkModeTypes.TurnOnAfterMinutes ||
                   modeType == TuyaWorkModeTypes.TurnOffAfterMinutes ||
                   modeType == TuyaWorkModeTypes.TurnOnForMinutes ||
                   modeType == TuyaWorkModeTypes.TurnOffForMinutes;
        }

        private static bool IsKnownDeviceType(string deviceType)
        {
            return deviceType == TuyaDeviceTypes.TvSocket ||
                   deviceType == TuyaDeviceTypes.Appliance;
        }

        private static string NormalizeDeviceType(string deviceType)
        {
            if (string.IsNullOrWhiteSpace(deviceType))
                return TuyaDeviceTypes.Appliance;

            deviceType = deviceType.Trim();
            return IsKnownDeviceType(deviceType) ? deviceType : TuyaDeviceTypes.Appliance;
        }

        private static string GetDefaultWorkModeName(string modeType, int minutes)
        {
            return modeType switch
            {
                TuyaWorkModeTypes.TurnOnAfterMinutes => $"Включить через {minutes} минут",
                TuyaWorkModeTypes.TurnOnForMinutes => $"Включить на {minutes} минут",
                TuyaWorkModeTypes.TurnOffForMinutes => $"Выключить на {minutes} минут",
                _ => $"Выключить через {minutes} минут"
            };
        }
    }
}
