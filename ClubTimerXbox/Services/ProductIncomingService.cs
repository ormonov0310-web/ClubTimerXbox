using System;
using System.Collections.Generic;
using System.Linq;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public static class ProductIncomingService
    {
        private static readonly List<ProductIncomingItem> _items =
            ProductIncomingStorageService.Load();

        public static IReadOnlyList<ProductIncomingItem> Items => _items;

        public static void AddIncoming(
            string productName,
            int quantityAdded,
            int quantityBefore,
            int quantityAfter,
            int purchasePrice,
            int salePrice,
            string note = "")
        {
            if (quantityAdded <= 0)
                return;

            if (purchasePrice < 0)
                purchasePrice = 0;

            if (salePrice < 0)
                salePrice = 0;

            var item = new ProductIncomingItem
            {
                CreatedAt = ClubClock.Current.LocalNow,
                ProductName = productName,
                QuantityAdded = quantityAdded,
                QuantityBefore = quantityBefore,
                QuantityAfter = quantityAfter,
                PurchasePrice = purchasePrice,
                SalePrice = salePrice,
                TotalPurchaseAmount = quantityAdded * purchasePrice,
                Note = note
            };

            _items.Add(item);
            Save();
        }

        public static List<ProductIncomingItem> GetAll()
        {
            return _items
                .OrderByDescending(item => item.CreatedAt)
                .ToList();
        }

        public static List<ProductIncomingItem> GetToday()
        {
            var day = BusinessCalendarService.GetBusinessDay(ClubClock.Current.LocalNow);
            return GetByPeriod(day.StartInclusive, day.EndExclusive);
        }

        public static List<ProductIncomingItem> GetByPeriod(
            DateTime fromInclusive,
            DateTime toExclusive)
        {
            return _items
                .Where(item =>
                    item.CreatedAt >= fromInclusive &&
                    item.CreatedAt < toExclusive)
                .OrderByDescending(item => item.CreatedAt)
                .ToList();
        }

        public static int GetTodayPurchaseTotal()
        {
            return GetToday()
                .Sum(item => item.TotalPurchaseAmount);
        }

        public static int GetTodayQuantityTotal()
        {
            return GetToday()
                .Sum(item => item.QuantityAdded);
        }

        public static void Clear()
        {
            _items.Clear();
            ProductIncomingStorageService.Clear();
        }

        private static void Save()
        {
            ProductIncomingStorageService.Save(_items);
        }
    }
}
