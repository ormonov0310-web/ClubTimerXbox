using System;
using System.Collections.Generic;

namespace ClubTimerXbox.Models
{
    public sealed class ExpiredSessionViolationRecord
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid GameSessionId { get; set; }
        public string PlaceName { get; set; } = "";
        public string EmployeeName { get; set; } = "";
        public DateTime ExpiredAt { get; set; }
        public DateTime ViolationStartedAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime LastUpdatedAt { get; set; }
        public DateTime? AcknowledgedAt { get; set; }
        public int GraceMinutes { get; set; }
        public int ElapsedSeconds { get; set; }
        public int ChargedMinutes { get; set; }
        public int PenaltyAmount { get; set; }
        public string Status { get; set; } = "Active";
        public List<Guid> PenaltyLossIds { get; set; } = new();
        public List<Guid> CancelledPenaltyLossIds { get; set; } = new();
    }
}
