using System;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public static class TariffService
    {
        public static double GetPricePerSecond(TariffSettings tariff)
        {
            if (tariff.OneHourPrice <= 0)
                return 0;

            return tariff.OneHourPrice / 3600.0;
        }

        public static double GetPricePerMinute(TariffSettings tariff)
        {
            if (tariff.OneHourPrice <= 0)
                return 0;

            return tariff.OneHourPrice / 60.0;
        }

        public static int CalculateSecondsByAmount(TariffSettings tariff, int amount)
        {
            if (amount <= 0 || tariff.OneHourPrice <= 0)
                return 0;

            double pricePerSecond = GetPricePerSecond(tariff);

            if (pricePerSecond <= 0)
                return 0;

            return (int)Math.Floor(amount / pricePerSecond);
        }

        public static int CalculatePriceBySeconds(TariffSettings tariff, int seconds)
        {
            if (seconds <= 0 || tariff.OneHourPrice <= 0)
                return 0;

            double pricePerSecond = GetPricePerSecond(tariff);
            double price = seconds * pricePerSecond;

            return (int)Math.Ceiling(price);
        }

        public static int CalculatePriceByMinutes(TariffSettings tariff, int minutes)
        {
            if (minutes <= 0)
                return 0;

            return CalculatePriceBySeconds(tariff, minutes * 60);
        }

        public static string FormatTime(int totalSeconds)
        {
            if (totalSeconds <= 0)
                return "00:00";

            int hours = totalSeconds / 3600;
            int minutes = (totalSeconds % 3600) / 60;
            int seconds = totalSeconds % 60;

            if (hours > 0)
                return $"{hours}:{minutes:00}:{seconds:00}";

            return $"{minutes:00}:{seconds:00}";
        }

        public static string FormatMenuTime(int totalSeconds)
        {
            if (totalSeconds <= 0)
                return "0 мин";

            int minutes = totalSeconds / 60;
            int seconds = totalSeconds % 60;

            if (seconds == 0)
                return $"{minutes} мин";

            return $"{minutes} мин {seconds} сек";
        }
    }
}