using System;

namespace ClubTimerXbox.Models
{
    public class ClubPlace
    {
        public string Name { get; set; } = "";
        public PlaceType Type { get; set; }

        public double PricePerMinute { get; set; }
        public double ActivePricePerMinute { get; set; }

        public double AccruedAmountBeforeCurrentSegment { get; set; }

        public bool IsBusy { get; set; }
        public bool IsOpenMode { get; set; }

        // Место уже остановлено, но окно расчёта ещё открыто.
        // После нажатия OK место станет свободным.
        public bool IsCalculating { get; set; }

        public int PaidAmount { get; set; }
        public int PrepaidCashAmount { get; set; }
        public int PrepaidMBankAmount { get; set; }
        public DateTime? StartTime { get; set; }

        public int TotalMinutes { get; set; }
        public int RemainingSeconds { get; set; }

        // Кто запустил место.
        // Например: Сталбек нажал "60 мин — 120 сом".
        public string? StartedByEmployeeName { get; set; }

        // Кому должна относиться выручка.
        // Для предоплаты — сотрудник, который запустил тариф.
        // Для открытого режима — сотрудник, который остановил и принял оплату.
        public string? IncomeEmployeeName { get; set; }
    }
}
