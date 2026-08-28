using System;
using System.Collections.Generic;
using System.Linq;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public static class ProductStockService
    {
        private static readonly List<ProductStockItem> _defaultStockItems = new List<ProductStockItem>
        {
            new ProductStockItem
            {
                ProductName = "ТОРНАДО",
                Quantity = 0,
                MinimumQuantity = 0,
                PurchasePrice = 0,
                SalePrice = 80
            },
            new ProductStockItem
            {
                ProductName = "Летс Го 1 литр",
                Quantity = 0,
                MinimumQuantity = 0,
                PurchasePrice = 0,
                SalePrice = 90
            },
            new ProductStockItem
            {
                ProductName = "Яблоко 1 литр",
                Quantity = 0,
                MinimumQuantity = 0,
                PurchasePrice = 0,
                SalePrice = 80
            },
            new ProductStockItem
            {
                ProductName = "Султан Чай",
                Quantity = 0,
                MinimumQuantity = 0,
                PurchasePrice = 0,
                SalePrice = 70
            },
            new ProductStockItem
            {
                ProductName = "Пико 1 литр",
                Quantity = 0,
                MinimumQuantity = 0,
                PurchasePrice = 0,
                SalePrice = 150
            }
        };

        private static readonly List<ProductStockItem> _stockItems;

        static ProductStockService()
        {
            var savedItems = ProductStockStorageService.Load();

            if (savedItems.Count == 0)
            {
                _stockItems = CloneDefaultItems();
                Save();
                return;
            }

            _stockItems = savedItems;

            EnsureDefaultProductsExist();
            EnsureDefaultPricesForOldData();

            Save();
        }

        public static IReadOnlyList<ProductStockItem> StockItems => _stockItems;

        private static List<ProductStockItem> CloneDefaultItems()
        {
            return _defaultStockItems
                .Select(item => new ProductStockItem
                {
                    ProductName = item.ProductName,
                    Quantity = item.Quantity,
                    MinimumQuantity = item.MinimumQuantity,
                    PurchasePrice = item.PurchasePrice,
                    SalePrice = item.SalePrice,
                    UpdatedAt = ClubClock.Current.LocalNow
                })
                .ToList();
        }

        private static void EnsureDefaultProductsExist()
        {
            foreach (var defaultItem in _defaultStockItems)
            {
                bool exists = _stockItems.Any(item =>
                    item.ProductName.Equals(defaultItem.ProductName, StringComparison.OrdinalIgnoreCase)
                );

                if (exists)
                    continue;

                _stockItems.Add(new ProductStockItem
                {
                    ProductName = defaultItem.ProductName,
                    Quantity = defaultItem.Quantity,
                    MinimumQuantity = defaultItem.MinimumQuantity,
                    PurchasePrice = defaultItem.PurchasePrice,
                    SalePrice = defaultItem.SalePrice,
                    UpdatedAt = ClubClock.Current.LocalNow
                });
            }
        }

        private static void EnsureDefaultPricesForOldData()
        {
            foreach (var item in _stockItems)
            {
                var defaultItem = _defaultStockItems.FirstOrDefault(x =>
                    x.ProductName.Equals(item.ProductName, StringComparison.OrdinalIgnoreCase)
                );

                if (defaultItem == null)
                    continue;

                if (item.SalePrice <= 0)
                    item.SalePrice = defaultItem.SalePrice;

                if (item.PurchasePrice < 0)
                    item.PurchasePrice = 0;

                if (item.MinimumQuantity < 0)
                    item.MinimumQuantity = 0;
            }
        }

        private static void Save()
        {
            ProductStockStorageService.Save(_stockItems);
        }

        public static bool ExistsByProductName(string productName)
        {
            productName = productName.Trim();

            return _stockItems.Any(item =>
                item.ProductName.Equals(productName, StringComparison.OrdinalIgnoreCase)
            );
        }

        public static ProductStockItem? FindByProductName(string productName)
        {
            productName = productName.Trim();

            return _stockItems.FirstOrDefault(item =>
                item.ProductName.Equals(productName, StringComparison.OrdinalIgnoreCase)
            );
        }

        public static int GetQuantity(string productName)
        {
            var item = FindByProductName(productName);

            if (item == null)
                return 0;

            return item.Quantity;
        }

        public static int GetSalePrice(string productName)
        {
            var item = FindByProductName(productName);

            if (item == null)
                return 0;

            return item.SalePrice;
        }

        public static int GetPurchasePrice(string productName)
        {
            var item = FindByProductName(productName);

            if (item == null)
                return 0;

            return item.PurchasePrice;
        }

        public static void AddNewProduct(
            string productName,
            int initialQuantity,
            int purchasePrice,
            int salePrice,
            int minimumQuantity)
        {
            productName = productName.Trim();

            if (string.IsNullOrWhiteSpace(productName))
                return;

            if (ExistsByProductName(productName))
                return;

            if (initialQuantity < 0)
                initialQuantity = 0;

            if (purchasePrice < 0)
                purchasePrice = 0;

            if (salePrice < 0)
                salePrice = 0;

            if (minimumQuantity < 0)
                minimumQuantity = 0;

            _stockItems.Add(new ProductStockItem
            {
                ProductName = productName,
                Quantity = initialQuantity,
                PurchasePrice = purchasePrice,
                SalePrice = salePrice,
                MinimumQuantity = minimumQuantity,
                UpdatedAt = ClubClock.Current.LocalNow
            });

            Save();

            if (initialQuantity > 0)
            {
                ProductIncomingService.AddIncoming(
                    productName: productName,
                    quantityAdded: initialQuantity,
                    quantityBefore: 0,
                    quantityAfter: initialQuantity,
                    purchasePrice: purchasePrice,
                    salePrice: salePrice,
                    note: "Создание нового товара"
                );
            }
        }

        public static bool UpdateProductFull(
            string oldProductName,
            string newProductName,
            int quantity,
            int purchasePrice,
            int salePrice,
            int minimumQuantity)
        {
            oldProductName = oldProductName.Trim();
            newProductName = newProductName.Trim();

            if (string.IsNullOrWhiteSpace(oldProductName))
                return false;

            if (string.IsNullOrWhiteSpace(newProductName))
                return false;

            var item = FindByProductName(oldProductName);

            if (item == null)
                return false;

            bool nameChanged = !oldProductName.Equals(newProductName, StringComparison.OrdinalIgnoreCase);

            if (nameChanged)
            {
                bool newNameExists = _stockItems.Any(stockItem =>
                    stockItem.ProductName.Equals(newProductName, StringComparison.OrdinalIgnoreCase)
                );

                if (newNameExists)
                    return false;
            }

            if (quantity < 0)
                quantity = 0;

            if (purchasePrice < 0)
                purchasePrice = 0;

            if (salePrice < 0)
                salePrice = 0;

            if (minimumQuantity < 0)
                minimumQuantity = 0;

            item.ProductName = newProductName;
            item.Quantity = quantity;
            item.PurchasePrice = purchasePrice;
            item.SalePrice = salePrice;
            item.MinimumQuantity = minimumQuantity;
            item.UpdatedAt = ClubClock.Current.LocalNow;

            Save();

            return true;
        }

        public static bool DeleteProduct(string productName)
        {
            productName = productName.Trim();

            if (string.IsNullOrWhiteSpace(productName))
                return false;

            var item = FindByProductName(productName);

            if (item == null)
                return false;

            _stockItems.Remove(item);
            Save();

            return true;
        }

        public static void SetQuantity(string productName, int quantity)
        {
            var item = FindByProductName(productName);

            if (item == null)
                return;

            if (quantity < 0)
                quantity = 0;

            item.Quantity = quantity;
            item.UpdatedAt = ClubClock.Current.LocalNow;

            Save();
        }

        public static void Increase(string productName, int quantity)
        {
            if (quantity <= 0)
                return;

            var item = FindByProductName(productName);

            if (item == null)
                return;

            item.Quantity += quantity;
            item.UpdatedAt = ClubClock.Current.LocalNow;

            Save();
        }

        public static bool Decrease(string productName, int quantity)
        {
            if (quantity <= 0)
                return false;

            var item = FindByProductName(productName);

            if (item == null)
                return false;

            if (item.Quantity < quantity)
                return false;

            item.Quantity -= quantity;
            item.UpdatedAt = ClubClock.Current.LocalNow;

            Save();

            return true;
        }

        public static void ForceDecreaseAllowNegative(string productName, int quantity)
        {
            if (quantity <= 0)
                return;

            var item = FindByProductName(productName);

            if (item == null)
                return;

            item.Quantity -= quantity;
            item.UpdatedAt = ClubClock.Current.LocalNow;

            Save();
        }

        public static bool IsProductTracked(string productName)
        {
            return FindByProductName(productName) != null;
        }

        public static bool IsLowStock(string productName)
        {
            var item = FindByProductName(productName);

            if (item == null)
                return false;

            if (item.MinimumQuantity <= 0)
                return false;

            return item.Quantity <= item.MinimumQuantity;
        }

        public static void SetMinimumQuantity(string productName, int minimumQuantity)
        {
            var item = FindByProductName(productName);

            if (item == null)
                return;

            if (minimumQuantity < 0)
                minimumQuantity = 0;

            item.MinimumQuantity = minimumQuantity;
            item.UpdatedAt = ClubClock.Current.LocalNow;

            Save();
        }

        public static void SetPrices(
            string productName,
            int purchasePrice,
            int salePrice)
        {
            var item = FindByProductName(productName);

            if (item == null)
                return;

            if (purchasePrice < 0)
                purchasePrice = 0;

            if (salePrice < 0)
                salePrice = 0;

            item.PurchasePrice = purchasePrice;
            item.SalePrice = salePrice;
            item.UpdatedAt = ClubClock.Current.LocalNow;

            Save();
        }

        public static void UpdateProductSettings(
            string productName,
            int purchasePrice,
            int salePrice,
            int minimumQuantity)
        {
            var item = FindByProductName(productName);

            if (item == null)
                return;

            if (purchasePrice < 0)
                purchasePrice = 0;

            if (salePrice < 0)
                salePrice = 0;

            if (minimumQuantity < 0)
                minimumQuantity = 0;

            item.PurchasePrice = purchasePrice;
            item.SalePrice = salePrice;
            item.MinimumQuantity = minimumQuantity;
            item.UpdatedAt = ClubClock.Current.LocalNow;

            Save();
        }

        public static void AddIncomingProduct(
            string productName,
            int quantityToAdd,
            int purchasePrice,
            int salePrice,
            int minimumQuantity)
        {
            if (quantityToAdd < 0)
                quantityToAdd = 0;

            var item = FindByProductName(productName);

            if (item == null)
                return;

            if (purchasePrice < 0)
                purchasePrice = 0;

            if (salePrice < 0)
                salePrice = 0;

            if (minimumQuantity < 0)
                minimumQuantity = 0;

            int quantityBefore = item.Quantity;
            int quantityAfter = item.Quantity + quantityToAdd;
            int weightedPurchasePrice = InventoryCostService.CalculateWeightedAverageUnitCost(
                quantityBefore,
                item.PurchasePrice,
                quantityToAdd,
                purchasePrice);

            item.Quantity = quantityAfter;
            item.PurchasePrice = weightedPurchasePrice;
            item.SalePrice = salePrice;
            item.MinimumQuantity = minimumQuantity;
            item.UpdatedAt = ClubClock.Current.LocalNow;

            Save();

            if (quantityToAdd > 0)
            {
                ProductIncomingService.AddIncoming(
                    productName: item.ProductName,
                    quantityAdded: quantityToAdd,
                    quantityBefore: quantityBefore,
                    quantityAfter: quantityAfter,
                    purchasePrice: purchasePrice,
                    salePrice: salePrice,
                    note: "Приход товара"
                );
            }
        }
    }
}
