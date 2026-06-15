using System;
using System.Reflection;

namespace ClubTimerXbox.Services
{
    public static class AppVersionService
    {
        public const string UpdateChannel = "stable";

        public static string Version
        {
            get
            {
                var assembly = typeof(AppVersionService).Assembly;
                return assembly
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                    ?.InformationalVersion
                    ?? assembly.GetName().Version?.ToString()
                    ?? "0.0.0";
            }
        }

        public static object BuildPayload()
        {
            return new
            {
                name = "ClubTimerXbox",
                platform = "windows",
                version = Version,
                channel = UpdateChannel,
                updatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            };
        }
    }
}
