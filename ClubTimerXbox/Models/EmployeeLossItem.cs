using System;

namespace ClubTimerXbox.Models
{
    public class EmployeeLossItem
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // На кого записана потеря / недостача.
        public string ResponsibleEmployeeName { get; set; } = "";

        // Кто обнаружил / проверил.
        public string CheckedByEmployeeName { get; set; } = "";

        // Например: "Недостача товара", "Недостача наличных", "Поломка", "Ошибка", "Прочее".
        public string LossType { get; set; } = "";

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
