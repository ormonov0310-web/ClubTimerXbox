using System;

namespace ClubTimerXbox.Models
{
    public class StockAuditItem
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        // Один BatchId объединяет несколько товаров в одну приёмку смены.
        public Guid BatchId { get; set; } = Guid.NewGuid();

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // Кто проверил и принял фактический остаток.
        // Например: Арген.
        public string CheckedByEmployeeName { get; set; } = "";

        // Чья смена считается ответственной за разницу.
        // Обычно это предыдущий сотрудник.
        public string ResponsibleEmployeeName { get; set; } = "";

        public string ProductName { get; set; } = "";

        // Сколько было по программе до приёмки.
        public int ExpectedQuantity { get; set; }

        // Сколько новый админ фактически посчитал.
        public int ActualQuantity { get; set; }

        // Разница: ActualQuantity - ExpectedQuantity.
        // было 10, фактически 9 => -1
        // было 10, фактически 11 => +1
        public int Difference { get; set; }

        // Цена продажи на момент проверки.
        public int SalePrice { get; set; }

        // Сумма разницы.
        public int DifferenceAmount { get; set; }

        public string Note { get; set; } = "";
    }
}