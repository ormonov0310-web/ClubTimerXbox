using System;

namespace ClubTimerXbox.Services
{
    public static class EmployeeCashPrivacyService
    {
        public static string GetAvailableCashBand(
            int currentCash,
            int reserveAmount)
        {
            int availableCash = Math.Max(0, currentCash - reserveAmount);
            if (availableCash <= 0)
                return "нет свободной налички";

            if (availableCash < 500)
                return "менее 500 сом";

            if (availableCash < 1000)
                return "500+ сом";

            if (availableCash < 3000)
                return "1000+ сом";

            if (availableCash < 5000)
                return "3000+ сом";

            if (availableCash < 10000)
                return "5000+ сом";

            return "10000+ сом";
        }

        public static string GetAcceptanceResult(int difference)
        {
            if (difference < 0)
                return "Есть недостача";

            if (difference > 0)
                return "Есть излишек";

            return "Расхождений нет";
        }
    }
}
