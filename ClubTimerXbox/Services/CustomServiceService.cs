using System;
using System.Collections.Generic;
using System.Linq;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public static class CustomServiceService
    {
        private static readonly List<SaleItem> _services = CustomServiceStorageService.Load();

        static CustomServiceService()
        {
            EnsureDefaultServicesExist();
            Save();
        }

        public static IReadOnlyList<SaleItem> Services => _services;

        private static void EnsureDefaultServicesExist()
        {
            bool joystickExists = _services.Any(item =>
                item.Name.Equals("Джойстик", StringComparison.OrdinalIgnoreCase)
            );

            if (!joystickExists)
            {
                _services.Add(new SaleItem
                {
                    Name = "Джойстик",
                    Type = SaleItemType.Service,
                    SalePrice = 50,
                    PurchasePrice = 0,
                    StockQuantity = 0,
                    IsActive = true
                });
            }
        }

        public static List<SaleItem> GetActiveServices()
        {
            return _services
                .Where(item => item.IsActive)
                .OrderBy(item => item.Name)
                .ToList();
        }

        public static List<SaleItem> GetAllServices()
        {
            return _services
                .OrderBy(item => item.Name)
                .ToList();
        }

        public static bool ExistsByName(string name)
        {
            name = name.Trim();

            return _services.Any(item =>
                item.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
            );
        }

        public static SaleItem? FindByName(string name)
        {
            name = name.Trim();

            return _services.FirstOrDefault(item =>
                item.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
            );
        }

        public static void AddService(string name, int salePrice)
        {
            name = name.Trim();

            if (string.IsNullOrWhiteSpace(name))
                return;

            if (salePrice < 0)
                salePrice = 0;

            if (ExistsByName(name))
                return;

            _services.Add(new SaleItem
            {
                Name = name,
                Type = SaleItemType.Service,
                SalePrice = salePrice,
                PurchasePrice = 0,
                StockQuantity = 0,
                IsActive = true
            });

            Save();
        }

        public static void UpdateService(string name, int salePrice, bool isActive)
        {
            var item = FindByName(name);

            if (item == null)
                return;

            if (salePrice < 0)
                salePrice = 0;

            item.SalePrice = salePrice;
            item.IsActive = isActive;

            Save();
        }

        public static bool DeleteService(string name)
        {
            var item = FindByName(name);

            if (item == null)
                return false;

            _services.Remove(item);
            Save();

            return true;
        }

        public static void Save()
        {
            CustomServiceStorageService.Save(_services);
        }

        public static void Clear()
        {
            _services.Clear();
            CustomServiceStorageService.Clear();

            EnsureDefaultServicesExist();
            Save();
        }
    }
}