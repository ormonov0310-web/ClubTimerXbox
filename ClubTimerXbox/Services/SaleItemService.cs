using System.Collections.Generic;
using System.Linq;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public static class SaleItemService
    {
        public static List<SaleItem> GetActiveItems()
        {
            var items = new List<SaleItem>();

            // Товары берём из склада.
            foreach (var stockItem in ProductStockService.StockItems)
            {
                items.Add(new SaleItem
                {
                    Name = stockItem.ProductName,
                    Type = SaleItemType.Product,
                    SalePrice = stockItem.SalePrice,
                    PurchasePrice = stockItem.PurchasePrice,
                    StockQuantity = stockItem.Quantity,
                    IsActive = true
                });
            }

            // Услуги берём из отдельного сервиса услуг.
            items.AddRange(CustomServiceService.GetActiveServices());

            return items
                .OrderBy(item => item.Type)
                .ThenBy(item => item.Name)
                .ToList();
        }

        public static List<SaleItem> GetProducts()
        {
            return GetActiveItems()
                .Where(item => item.Type == SaleItemType.Product)
                .ToList();
        }

        public static List<SaleItem> GetServices()
        {
            return GetActiveItems()
                .Where(item => item.Type == SaleItemType.Service)
                .ToList();
        }

        public static SaleItem? FindByName(string name)
        {
            return GetActiveItems()
                .FirstOrDefault(item => item.Name == name);
        }
    }
}