namespace ClubTimerXbox.Models
{
    public class ClubSettings
    {
        public int TvCount { get; set; } = 8;
        public int WheelCount { get; set; } = 4;
        public int VipRoomCount { get; set; } = 0;

        public TariffSettings TvTariff { get; set; } = new TariffSettings
        {
            OneHourPrice = 120,
            HalfHourPrice = 60,
            FiveMinutesPrice = 10,
            PricePerMinute = 2.0
        };

        public TariffSettings WheelTariff { get; set; } = new TariffSettings
        {
            OneHourPrice = 150,

            // Для руля это НЕ обязательно 30 минут.
            // Это просто круглая сумма, а система сама считает, сколько времени дать.
            HalfHourPrice = 80,

            // Предварительный маленький тариф руля.
            // Потом можно изменить в настройках.
            FiveMinutesPrice = 10,

            PricePerMinute = 2.5
        };

        public TariffSettings VipTariff { get; set; } = new TariffSettings
        {
            OneHourPrice = 200,
            HalfHourPrice = 100,
            FiveMinutesPrice = 20,
            PricePerMinute = 3.33
        };
    }

    public class TariffSettings
    {
        public int OneHourPrice { get; set; }

        // Название пока старое, но смысл теперь такой:
        // это средняя кнопка тарифа, например для руля 80 сом.
        public int HalfHourPrice { get; set; }

        // Это маленькая кнопка тарифа, например 10 сом.
        public int FiveMinutesPrice { get; set; }

        // Пока оставляем для совместимости.
        // Позже уберём и будем считать только от OneHourPrice.
        public double PricePerMinute { get; set; }
    }
}