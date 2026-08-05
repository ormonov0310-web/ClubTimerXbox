using System;

namespace ClubTimerXbox.Models
{
    public class SavedActivePlace
    {
        public string Name { get; set; } = "";
        public PlaceType Type { get; set; }

        public bool IsBusy { get; set; }
        public bool IsOpenMode { get; set; }
        public bool IsNewBranchPromoSession { get; set; }
        public bool IsCalculating { get; set; }
        public bool IsTimeExpiredAwaitingAcknowledgement { get; set; }
        public DateTime? TimeExpiredAt { get; set; }
        public Guid? ExpiredGameSessionId { get; set; }
        public Guid? ExpiredPenaltyLossId { get; set; }
        public string ExpiredPenaltyLossMonthKey { get; set; } = "";
        public int ExpiredPenaltyLossBaseMinutes { get; set; }
        public string? ExpiredPenaltyEmployeeName { get; set; }
        public int ExpiredPenaltyChargedMinutes { get; set; }

        public int PaidAmount { get; set; }
        public int PrepaidCashAmount { get; set; }
        public int PrepaidMBankAmount { get; set; }

        public DateTime? StartTime { get; set; }

        public int TotalMinutes { get; set; }
        public int RemainingSeconds { get; set; }

        // Когда программа последний раз сохранила состояние.
        // При новом запуске мы сравним LastSavedAt с текущим временем.
        public DateTime LastSavedAt { get; set; } = DateTime.Now;

        public double PricePerMinute { get; set; }
        public double ActivePricePerMinute { get; set; }

        public double AccruedAmountBeforeCurrentSegment { get; set; }

        public string? StartedByEmployeeName { get; set; }
        public string? IncomeEmployeeName { get; set; }
    }
}
