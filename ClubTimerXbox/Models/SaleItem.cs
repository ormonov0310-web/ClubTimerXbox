namespace ClubTimerXbox.Models
{
    public enum SaleItemType
    {
        Product,
        Service
    }

    public class SaleItem
    {
        public string Name { get; set; } = "";

        public SaleItemType Type { get; set; }

        // Цена продажи клиенту
        public int SalePrice { get; set; }

        // Закупочная цена. Пока 0, позже добавим бухгалтерию прибыли.
        public int PurchasePrice { get; set; }

        // Остаток. Пока 0, позже добавим склад/холодильник.
        public int StockQuantity { get; set; }

        public bool IsActive { get; set; } = true;
    }
}