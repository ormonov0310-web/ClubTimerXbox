using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public static class ProductPopularityService
    {
        public static IReadOnlyDictionary<string, int> GetLifetimePaidQuantities()
        {
            return CalculateLifetimePaidQuantities(
                PaymentService.Records,
                ActionLogService.GetAllGameSessions(),
                ProductStockService.IsProductTracked,
                CashService.Records);
        }

        public static IReadOnlyDictionary<string, int> CalculateLifetimePaidQuantities(
            IEnumerable<PaymentRecord> payments,
            IEnumerable<GameSessionLogItem> sessions,
            Func<string, bool>? isProductTracked = null,
            IEnumerable<CashRecord>? legacyCashRecords = null)
        {
            var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            isProductTracked ??= _ => false;
            legacyCashRecords ??= Array.Empty<CashRecord>();

            foreach (var payment in payments.Where(item => item.GameSessionId == null))
            {
                foreach (var item in payment.Items ?? new List<CheckoutItem>())
                {
                    if (!IsProduct(item, isProductTracked) || item.Quantity <= 0)
                        continue;

                    Add(result, item.Name, item.Quantity);
                }
            }

            foreach (var line in sessions
                         .SelectMany(session => session.SaleLines)
                         .Where(line =>
                             line.ItemType == SaleItemType.Product &&
                             SessionSaleSettlementService.IsFinanciallyPaid(line) &&
                             line.Quantity > 0))
            {
                Add(result, line.ItemName, line.Quantity);
            }

            foreach (var record in legacyCashRecords.Where(record =>
                         record.Category == "Товары и услуги" &&
                         record.GameSessionId == null &&
                         !record.PaymentRecordId.HasValue &&
                         record.Amount > 0 &&
                         isProductTracked(record.Title ?? "")))
            {
                Add(result, record.Title ?? string.Empty, ReadLegacyQuantity(record.Description));
            }

            return result;
        }

        public static int GetLifetimePaidQuantity(string productName)
        {
            var quantities = GetLifetimePaidQuantities();
            return quantities.TryGetValue(productName.Trim(), out int value) ? value : 0;
        }

        public static IEnumerable<ProductStockItem> OrderStock(IEnumerable<ProductStockItem> items)
        {
            return OrderStock(items, GetLifetimePaidQuantities());
        }

        public static IEnumerable<ProductStockItem> OrderStock(
            IEnumerable<ProductStockItem> items,
            IReadOnlyDictionary<string, int> popularity)
        {
            return items
                .OrderBy(item => item.Quantity <= 0)
                .ThenByDescending(item => Get(popularity, item.ProductName))
                .ThenBy(item => item.ProductName, StringComparer.CurrentCultureIgnoreCase);
        }

        public static IEnumerable<ProductStockItem> OrderPurchaseCatalog(IEnumerable<ProductStockItem> items)
        {
            return OrderPurchaseCatalog(items, GetLifetimePaidQuantities());
        }

        public static IEnumerable<ProductStockItem> OrderPurchaseCatalog(
            IEnumerable<ProductStockItem> items,
            IReadOnlyDictionary<string, int> popularity)
        {
            return items
                .OrderByDescending(item => Get(popularity, item.ProductName))
                .ThenBy(item => item.ProductName, StringComparer.CurrentCultureIgnoreCase);
        }

        public static IEnumerable<SaleItem> OrderSalesCatalog(
            IEnumerable<SaleItem> items,
            IReadOnlyDictionary<string, int> popularity)
        {
            return items
                .OrderBy(item => item.Type)
                .ThenBy(item => item.Type == SaleItemType.Product && item.StockQuantity <= 0)
                .ThenByDescending(item => Get(popularity, item.Name))
                .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase);
        }

        private static bool IsProduct(
            CheckoutItem item,
            Func<string, bool> isProductTracked)
        {
            return item.ItemType == SaleItemType.Product.ToString() ||
                   item.Category == "Товар" ||
                   isProductTracked(item.Name);
        }

        private static int Get(IReadOnlyDictionary<string, int> values, string key)
        {
            return values.TryGetValue(key.Trim(), out int value) ? value : 0;
        }

        private static void Add(IDictionary<string, int> values, string key, int quantity)
        {
            key = key.Trim();
            if (key.Length == 0)
                return;

            values[key] = values.TryGetValue(key, out int current)
                ? current + quantity
                : quantity;
        }

        private static int ReadLegacyQuantity(string? description)
        {
            var match = Regex.Match(
                description ?? "",
                @"Количество:\s*(\d+)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            return match.Success && int.TryParse(match.Groups[1].Value, out int quantity)
                ? Math.Max(1, quantity)
                : 1;
        }
    }
}
