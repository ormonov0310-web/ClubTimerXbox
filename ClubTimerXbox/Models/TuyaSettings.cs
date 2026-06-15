using System.Collections.Generic;

namespace ClubTimerXbox.Models
{
    public class TuyaSettings
    {
        public bool IsEnabled { get; set; }

        public bool DryRunMode { get; set; } = true;

        public string Endpoint { get; set; } = "https://openapi.tuyaeu.com";

        public string AccessId { get; set; } = "";

        public string AccessSecret { get; set; } = "";

        public List<TuyaPlaceDeviceMapping> PlaceMappings { get; set; } = new List<TuyaPlaceDeviceMapping>();

        public List<TuyaDevicePreference> DevicePreferences { get; set; } = new List<TuyaDevicePreference>();

        public List<TuyaWorkMode> WorkModes { get; set; } = new List<TuyaWorkMode>();

        public bool WorkModesInitialized { get; set; }

        public List<TuyaActiveWorkMode> ActiveWorkModes { get; set; } = new List<TuyaActiveWorkMode>();
    }

    public class TuyaPlaceDeviceMapping
    {
        public string PlaceName { get; set; } = "";

        public string DeviceId { get; set; } = "";

        public string DeviceName { get; set; } = "";

        public string SwitchCode { get; set; } = "switch_1";
    }

    public class TuyaDevice
    {
        public string Id { get; set; } = "";

        public string Name { get; set; } = "";

        public string Category { get; set; } = "";

        public string ProductName { get; set; } = "";

        public bool Online { get; set; }

        public bool? IsOn { get; set; }

        public int CountdownSeconds { get; set; }

        public override string ToString()
        {
            string online = Online ? "онлайн" : "офлайн";
            string state = IsOn.HasValue ? (IsOn.Value ? "включено" : "выключено") : "состояние неизвестно";

            return $"{Name} - {Id} - {online} - {state}";
        }
    }

    public class TuyaDevicePreference
    {
        public string DeviceId { get; set; } = "";

        public string CloudName { get; set; } = "";

        public string DisplayName { get; set; } = "";

        public bool IsHidden { get; set; }

        public string DeviceType { get; set; } = TuyaDeviceTypes.Appliance;
    }

    public static class TuyaDeviceTypes
    {
        public const string TvSocket = "TvSocket";

        public const string Appliance = "Appliance";
    }

    public class TuyaScheduleTask
    {
        public string TimerId { get; set; } = "";

        public string AliasName { get; set; } = "";

        public string Time { get; set; } = "10:30";

        public string Date { get; set; } = "";

        public string Loops { get; set; } = "1111111";

        public string TimezoneId { get; set; } = "Asia/Bishkek";

        public bool Enable { get; set; } = true;

        public bool TurnOn { get; set; } = true;

        public bool IsEveryDay => Loops == "1111111";

        public TuyaScheduleTask Clone()
        {
            return new TuyaScheduleTask
            {
                TimerId = TimerId,
                AliasName = AliasName,
                Time = Time,
                Date = Date,
                Loops = Loops,
                TimezoneId = TimezoneId,
                Enable = Enable,
                TurnOn = TurnOn
            };
        }
    }

    public class TuyaWorkMode
    {
        public string Id { get; set; } = "";

        public string Name { get; set; } = "";

        public string ModeType { get; set; } = TuyaWorkModeTypes.TurnOffAfterMinutes;

        public int Minutes { get; set; } = 10;

        public TuyaWorkMode Clone()
        {
            return new TuyaWorkMode
            {
                Id = Id,
                Name = Name,
                ModeType = ModeType,
                Minutes = Minutes
            };
        }
    }

    public static class TuyaWorkModeTypes
    {
        public const string TurnOnAfterMinutes = "TurnOnAfterMinutes";

        public const string TurnOffAfterMinutes = "TurnOffAfterMinutes";

        public const string TurnOnForMinutes = "TurnOnForMinutes";

        public const string TurnOffForMinutes = "TurnOffForMinutes";
    }

    public class TuyaActiveWorkMode
    {
        public string DeviceId { get; set; } = "";

        public string WorkModeId { get; set; } = "";

        public int DurationSeconds { get; set; }

        public string StartedAt { get; set; } = "";
    }
}
