using System;

namespace ClubTimerXbox.Models
{
    public class ProductStockItem
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        // Название товара должно совпадать с SaleItem.Name
        public string ProductName { get; set; } = "";

        // Текущий остаток товара
        public int Quantity { get; set; }

        // Минимальный остаток для предупреждения владельца
        public int MinimumQuantity { get; set; }

        // Цена прихода / закупочная цена
        public int PurchasePrice { get; set; }

        // Цена продажи клиенту
        public int SalePrice { get; set; }

        // Когда последний раз изменяли остаток или настройки товара
        public DateTime UpdatedAt { get; set; } = DateTime.Now;
    }
}