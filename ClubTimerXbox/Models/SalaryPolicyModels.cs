using System;
using System.Collections.Generic;

namespace ClubTimerXbox.Models
{
    public sealed class SalaryPolicyVersion
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public DateTime CreatedAt { get; set; }

        public DateTime EffectiveFrom { get; set; }

        public string CreatedBy { get; set; } = "";

        public AutoSalarySettings Settings { get; set; } = new();
    }

    public sealed class SalaryPolicyHistoryState
    {
        public int SchemaVersion { get; set; } = 1;

        public List<SalaryPolicyVersion> Versions { get; set; } = new();
    }

    public enum EmployeeRatingBranch
    {
        Time,
        Revenue
    }

    public enum EmployeeRatingEventStatus
    {
        Active,
        Forgiven,
        CancelledAsError
    }

    public enum EmployeeRatingEffectDirection
    {
        Penalty,
        Reward
    }

    public sealed class EmployeeRatingBaseVersion
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public DateTime CreatedAt { get; set; }

        public DateTime EffectiveFrom { get; set; }

        public int TimePercent { get; set; } = 100;

        public int RevenuePercent { get; set; } = 100;

        public string Reason { get; set; } = "";
    }

    public sealed class EmployeeRatingEvent
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public string EmployeeId { get; set; } = "";

        public string EmployeeName { get; set; } = "";

        public EmployeeRatingBranch Branch { get; set; }

        public string RuleCode { get; set; } = "";

        public int RuleVersion { get; set; } = 1;

        public EmployeeRatingEffectDirection Direction { get; set; }

        public int ChangePercent { get; set; }

        public int BasePercentAtCreation { get; set; } = 100;

        public string SourceId { get; set; } = "";

        public string SourceType { get; set; } = "";

        public string Title { get; set; } = "";

        public string Description { get; set; } = "";

        public DateTime CreatedAt { get; set; }

        public DateTime EffectiveFrom { get; set; }

        public DateTime ScheduledUntil { get; set; }

        public DateTime? EndedAt { get; set; }

        public int TargetPercent { get; set; } = 100;

        public EmployeeRatingEventStatus Status { get; set; }

        public int CompensationAmount { get; set; }

        public string ResolutionNote { get; set; } = "";

        public DateTime EffectiveUntil =>
            EndedAt.HasValue && EndedAt.Value < ScheduledUntil
                ? EndedAt.Value
                : ScheduledUntil;
    }

    public sealed class EmployeeRatingProfile
    {
        public string EmployeeId { get; set; } = "";

        public string EmployeeName { get; set; } = "";

        public List<EmployeeRatingBaseVersion> BaseVersions { get; set; } = new();
    }

    public sealed class EmployeeRatingState
    {
        public int SchemaVersion { get; set; } = 1;

        public DateTime ActivatedAt { get; set; }

        public DateTime CashExtraAcceptanceRewardsActivatedAt { get; set; }

        public List<EmployeeRatingProfile> Profiles { get; set; } = new();

        public List<EmployeeRatingEvent> Events { get; set; } = new();
    }

    public sealed class EmployeeRatingSnapshot
    {
        public string EmployeeId { get; set; } = "";

        public string EmployeeName { get; set; } = "";

        public int TimePercent { get; set; } = 100;

        public int RevenuePercent { get; set; } = 100;

        public int OverallPercent { get; set; } = 100;

        public bool HasWarning { get; set; }

        public List<EmployeeRatingEvent> ActiveEvents { get; set; } = new();

        public List<EmployeeRatingEvent> History { get; set; } = new();
    }
}
