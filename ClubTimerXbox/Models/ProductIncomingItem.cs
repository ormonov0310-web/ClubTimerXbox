using System;

namespace ClubTimerXbox.Models
{
    public class ProductIncomingItem
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public string ProductName { get; set; } = "";

        // Сколько штук добавили.
        public int QuantityAdded { get; set; }

        // Остаток до прихода.
        public int QuantityBefore { get; set; }

        // Остаток после прихода.
        public int QuantityAfter { get; set; }

        // Цена прихода / закупки за 1 шт.
        public int PurchasePrice { get; set; }

        // Цена продажи за 1 шт.
        public int SalePrice { get; set; }

        // Общая закупочная сумма.
        public int TotalPurchaseAmount { get; set; }

        public string Note { get; set; } = "";
    }
}