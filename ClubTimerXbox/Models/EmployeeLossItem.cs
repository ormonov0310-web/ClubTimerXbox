using System;

namespace ClubTimerXbox.Models
{
    public class EmployeeLossItem
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // Source salary period for penalties posted after the 06:00 close.
        public string SalaryMonthKey { get; set; } = "";

        // На кого записана потеря / недостача.
        public string ResponsibleEmployeeName { get; set; } = "";

        // Кто обнаружил / проверил.
        public string CheckedByEmployeeName { get; set; } = "";

        // Например: "Недостача товара", "Недостача наличных", "Поломка", "Ошибка", "Прочее".
        public string LossType { get; set; } = "";

        // Stable machine-readable category: "money" or "product".
        public string LossKind { get; set; } = "";

        // Owner-confirmed losses should not be changed by later automatic reconciliation caps.
        public bool IsFixed { get; set; }

        // Some domain events, such as opening lateness, create their rating event separately.
        public bool SuppressAutomaticRating { get; set; }

        public string SourceCode { get; set; } = "";

        public string ResolutionStatus { get; set; } = "";

        public DateTime? DecisionDueAt { get; set; }

        public DateTime? ResolvedAt { get; set; }

        public string Title { get; set; } = "";

        public string Description { get; set; } = "";

        public int Amount { get; set; }

        // Позже пригодится для зарплаты:
        // если удержали из зарплаты или сотрудник оплатил, ставим true.
        public bool IsPaid { get; set; }

        public DateTime? PaidAt { get; set; }

        public string Note { get; set; } = "";
    }
}
