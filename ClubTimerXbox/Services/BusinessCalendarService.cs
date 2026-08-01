using System;
using System.Collections.Generic;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public static class BusinessCalendarService
    {
        public const int BusinessDayStartHour = 6;
        public static readonly TimeSpan ClubUtcOffset = TimeSpan.FromHours(6);

        public static DateTime ToClubLocal(DateTime utcTime)
        {
            DateTime normalizedUtc = utcTime.Kind == DateTimeKind.Utc
                ? utcTime
                : DateTime.SpecifyKind(utcTime, DateTimeKind.Utc);
            return DateTime.SpecifyKind(
                normalizedUtc.Add(ClubUtcOffset),
                DateTimeKind.Unspecified);
        }

        public static DateTime ToUtc(DateTime clubLocalTime)
        {
            return DateTime.SpecifyKind(
                clubLocalTime.Subtract(ClubUtcOffset),
                DateTimeKind.Utc);
        }

        public static DateTime GetBusinessDate(DateTime clubLocalTime)
        {
            DateTime date = clubLocalTime.Date;
            return clubLocalTime.TimeOfDay < TimeSpan.FromHours(BusinessDayStartHour)
                ? date.AddDays(-1)
                : date;
        }

        public static BusinessPeriodRange GetBusinessDay(DateTime clubLocalTime)
        {
            DateTime businessDate = GetBusinessDate(clubLocalTime);
            DateTime start = businessDate.AddHours(BusinessDayStartHour);
            return new BusinessPeriodRange(
                start,
                start.AddDays(1),
                businessDate.ToString("yyyy-MM-dd"));
        }

        public static BusinessPeriodRange GetBusinessMonth(DateTime clubLocalTime)
        {
            DateTime businessDate = GetBusinessDate(clubLocalTime);
            return GetBusinessMonthByKey(businessDate.ToString("yyyy-MM"));
        }

        public static BusinessPeriodRange GetBusinessMonthByAnchor(DateTime monthAnchor)
        {
            DateTime month = new(monthAnchor.Year, monthAnchor.Month, 1);
            return GetBusinessMonthByKey(month.ToString("yyyy-MM"));
        }

        public static BusinessPeriodRange GetBusinessMonthByKey(string monthKey)
        {
            if (!TryParseMonthKey(monthKey, out DateTime month))
                throw new ArgumentException("Business month key must use yyyy-MM.", nameof(monthKey));

            DateTime start = month.AddHours(BusinessDayStartHour);
            return new BusinessPeriodRange(
                start,
                month.AddMonths(1).AddHours(BusinessDayStartHour),
                month.ToString("yyyy-MM"));
        }

        public static string GetBusinessMonthKey(DateTime clubLocalTime)
        {
            return GetBusinessMonth(clubLocalTime).Key;
        }

        public static bool TryParseMonthKey(string value, out DateTime monthStart)
        {
            monthStart = default;
            if (string.IsNullOrWhiteSpace(value) ||
                value.Length != 7 ||
                value[4] != '-' ||
                !int.TryParse(value[..4], out int year) ||
                !int.TryParse(value[5..], out int month) ||
                year < 2000 ||
                month is < 1 or > 12)
            {
                return false;
            }

            monthStart = new DateTime(year, month, 1);
            return true;
        }

        public static IReadOnlyDictionary<string, TimeSpan> SplitByBusinessMonth(
            DateTime fromInclusive,
            DateTime toExclusive)
        {
            var result = new Dictionary<string, TimeSpan>(StringComparer.OrdinalIgnoreCase);
            if (toExclusive <= fromInclusive)
                return result;

            DateTime cursor = fromInclusive;
            while (cursor < toExclusive)
            {
                BusinessPeriodRange month = GetBusinessMonth(cursor);
                DateTime segmentEnd = toExclusive < month.EndExclusive
                    ? toExclusive
                    : month.EndExclusive;
                TimeSpan duration = segmentEnd - cursor;

                result[month.Key] = result.TryGetValue(month.Key, out TimeSpan current)
                    ? current + duration
                    : duration;
                cursor = segmentEnd;
            }

            return result;
        }

        public static string FormatBusinessDate(DateTime clubLocalTime)
        {
            BusinessPeriodRange day = GetBusinessDay(clubLocalTime);
            return $"Рабочий день: {day.Key} • 06:00–05:59";
        }
    }
}
