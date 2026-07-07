using System;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public static class NewBranchPromoService
    {
        public static NewBranchPromoSettings Current => AppSettingsService.Current.NewBranchPromo;

        public static bool IsActiveNow()
        {
            return IsActiveAt(DateTime.Now);
        }

        public static bool IsActiveAt(DateTime now)
        {
            var settings = Current;

            if (!settings.IsEnabled)
                return false;

            return TryGetActiveEndAt(now, out _);
        }

        public static bool TryGetActiveEndAt(DateTime now, out DateTime activeEndAt)
        {
            activeEndAt = default;
            var settings = Current;

            if (!settings.IsEnabled || now < settings.StartDate.Date)
                return false;

            activeEndAt = GetEffectiveEndAt(settings);

            if (settings.IsOneMinuteEndTestEnabled &&
                settings.OneMinuteEndTestEndsAt.HasValue &&
                settings.OneMinuteEndTestEndsAt.Value < activeEndAt)
            {
                activeEndAt = settings.OneMinuteEndTestEndsAt.Value;
            }

            return now < activeEndAt;
        }

        public static DateTime GetEffectiveEndAt(NewBranchPromoSettings settings)
        {
            int hour = Math.Clamp(settings.GraceEndHour, 0, 23);
            return settings.LastDay.Date.AddDays(1).AddHours(hour);
        }

        public static void Save(NewBranchPromoSettings promoSettings)
        {
            Normalize(promoSettings);

            var settings = AppSettingsService.Current;
            settings.NewBranchPromo = promoSettings;
            AppSettingsService.Save(settings);
        }

        private static void Normalize(NewBranchPromoSettings settings)
        {
            settings.StartDate = settings.StartDate.Date;
            settings.LastDay = settings.LastDay.Date;
            settings.GraceEndHour = Math.Clamp(settings.GraceEndHour, 0, 23);
            settings.TvPromoMinutes = Math.Max(1, settings.TvPromoMinutes);
            settings.TvPromoPrice = Math.Max(1, settings.TvPromoPrice);
            settings.OpenModeDiscountPercent = Math.Clamp(settings.OpenModeDiscountPercent, 0, 100);

            if (!settings.IsOneMinuteEndTestEnabled)
                settings.OneMinuteEndTestEndsAt = null;

            if (settings.LastDay < settings.StartDate)
                settings.LastDay = settings.StartDate;
        }
    }
}
