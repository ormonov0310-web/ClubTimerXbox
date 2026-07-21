using System;
using System.Collections.Generic;
using System.Linq;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public static class StockPurchaseService
    {
        private static readonly List<StockPurchase> _purchases = StockPurchaseStorageService.Load();

        public static IReadOnlyList<StockPurchase> Purchases => _purchases;

        public static int RenameEmployeeReferences(
            string oldEmployeeName,
            string newEmployeeName)
        {
            int changed = 0;

            foreach (var purchase in _purchases)
            {
                if (!EmployeeReferenceRenameService.Matches(purchase.AddedBy, oldEmployeeName))
                    continue;

                purchase.AddedBy = newEmployeeName;
                purchase.Note = EmployeeReferenceRenameService.RenameText(
                    purchase.Note,
                    oldEmployeeName,
                    newEmployeeName);
                changed++;
            }

            if (changed > 0)
                Save();

            return changed;
        }

        public static StockPurchase AddPurchase(
            List<StockPurchaseItem> items,
            string addedBy = "Владелец",
            string note = "")
        {
            var safeItems = new List<StockPurchaseItem>();

            foreach (var item in items)
            {
                string productName = item.ProductName.Trim();

                if (string.IsNullOrWhiteSpace(productName))
                    continue;

                if (item.Quantity <= 0)
                    continue;

                int purchasePrice = item.PurchasePrice;

                if (purchasePrice < 0)
                    purchasePrice = 0;

                int salePrice = item.SalePrice;

                if (salePrice < 0)
                    salePrice = 0;

                int minimumQuantity = item.MinimumQuantity;

                if (minimumQuantity < 0)
                    minimumQuantity = 0;

                safeItems.Add(new StockPurchaseItem
                {
                    ProductName = productName,
                    Quantity = item.Quantity,
                    PurchasePrice = purchasePrice,
                    SalePrice = salePrice,
                    MinimumQuantity = minimumQuantity
                });
            }

            if (safeItems.Count == 0)
                throw new Exception("В закупе нет товаров.");

            var purchase = new StockPurchase
            {
                Id = Guid.NewGuid(),
                CreatedAt = DateTime.Now,
                AddedBy = string.IsNullOrWhiteSpace(addedBy) ? "Владелец" : addedBy.Trim(),
                Note = note.Trim(),
                Items = safeItems
            };

            foreach (var item in purchase.Items)
            {
                var product = ProductStockService.FindByProductName(item.ProductName);

                if (product == null)
                {
                    ProductStockService.AddNewProduct(
                        productName: item.ProductName,
                        initialQuantity: item.Quantity,
                        purchasePrice: item.PurchasePrice,
                        salePrice: item.SalePrice,
                        minimumQuantity: item.MinimumQuantity
                    );
                }
                else
                {
                    ProductStockService.AddIncomingProduct(
                        productName: product.ProductName,
                        quantityToAdd: item.Quantity,
                        purchasePrice: item.PurchasePrice,
                        salePrice: item.SalePrice,
                        minimumQuantity: product.MinimumQuantity
                    );
                }
            }

            _purchases.Add(purchase);
            Save();

            return purchase;
        }

        public static List<StockPurchase> GetPurchasesByPeriod(
            DateTime fromInclusive,
            DateTime toExclusive)
        {
            return _purchases
                .Where(purchase =>
                    purchase.CreatedAt >= fromInclusive &&
                    purchase.CreatedAt < toExclusive)
                .OrderByDescending(purchase => purchase.CreatedAt)
                .ToList();
        }

        public static int GetTotalByPeriod(
            DateTime fromInclusive,
            DateTime toExclusive)
        {
            return _purchases
                .Where(purchase =>
                    purchase.CreatedAt >= fromInclusive &&
                    purchase.CreatedAt < toExclusive)
                .Sum(purchase => purchase.TotalAmount);
        }

        private static void Save()
        {
            StockPurchaseStorageService.Save(_purchases);
        }

        public static void Clear()
        {
            _purchases.Clear();
            StockPurchaseStorageService.Clear();
        }
    }
}
