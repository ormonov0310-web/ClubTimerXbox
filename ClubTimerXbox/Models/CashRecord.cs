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

        // Business recognition time. Legacy records fall back to CreatedAt.
        // It may differ for a prepaid session that is physically closed later.
        public DateTime BusinessOccurredAt { get; set; }

        // Кто сделал операцию.
        public string EmployeeName { get; set; } = "";

        // Кому относится выручка или ответственность.
        public string IncomeEmployeeName { get; set; } = "";

        // Кому относится операция:
        // Например зарплата Аргену / Ади / Сталбеку.
        // Позже это же поле можно использовать для вывода владельца.
        public string RelatedEmployeeName { get; set; } = "";

        // Для операций владельца: месяц, из прибыли которого забрали деньги.
        // CreatedAt остается фактической датой движения денег.
        public string AccountingMonthKey { get; set; } = "";

        // Для зарплаты: месяц, за который сделана выплата, в формате yyyy-MM.
        // CreatedAt при этом остается фактической датой движения денег.
        public string SalaryMonthKey { get; set; } = "";

        // Stable 06:00-based period keys for new records.
        public string BusinessDateKey { get; set; } = "";

        public string BusinessMonthKey { get; set; } = "";

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
