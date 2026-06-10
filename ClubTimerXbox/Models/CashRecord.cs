using System;

namespace ClubTimerXbox.Models
{
    public enum CashRecordType
    {
        GameSession,
        ProductOrService,
        Shortage,
        Correction,
        Expense
    }

    public class CashRecord
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // Кто сделал операцию.
        public string EmployeeName { get; set; } = "";

        // Кому относится выручка или ответственность.
        public string IncomeEmployeeName { get; set; } = "";

        // Кому относится операция:
        // Например зарплата Аргену / Ади / Сталбеку.
        // Позже это же поле можно использовать для вывода владельца.
        public string RelatedEmployeeName { get; set; } = "";

        public CashRecordType Type { get; set; }

        public string Title { get; set; } = "";

        public string Description { get; set; } = "";

        // Сумма операции.
        public int Amount { get; set; }

        // Главный раздел кассы:
        // "Игры"
        // "Товары и услуги"
        // "Недостачи"
        // "Коррекция"
        // "Расходы"
        public string Category { get; set; } = "";

        // Категория расхода:
        // "Аренда", "Ток", "Интернет", "Уборка",
        // "Ремонт", "Реклама", "Патент", "Мусор",
        // "Подписка", "Закупка", "Зарплата", "Другое"
        public string ExpenseCategory { get; set; } = "";

        // Наличные / Безнал / Не указано.
        public string PaymentMethod { get; set; } = "Не указано";

        public string PlaceName { get; set; } = "";

        public Guid? GameSessionId { get; set; }

        public bool IsAttachedToGameSession { get; set; }
    }
}