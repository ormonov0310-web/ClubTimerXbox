using System;

namespace ClubTimerXbox.Services
{
    public static class OwnerAccessService
    {
        private const string OwnerCode = "105103";

        public static bool IsValidCode(string value)
        {
            return string.Equals(value?.Trim(), OwnerCode, StringComparison.Ordinal);
        }
    }
}
