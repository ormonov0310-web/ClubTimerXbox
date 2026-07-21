using System;
using System.Collections.Generic;
using System.Linq;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public static class StockAuditService
    {
        private static readonly List<StockAuditItem> _items = StockAuditStorageService.Load();

        public static IReadOnlyList<StockAuditItem> Items => _items;

        public static int RenameEmployeeReferences(
            string oldEmployeeName,
            string newEmployeeName)
        {
            int changed = 0;

            foreach (var item in _items)
            {
                bool itemChanged = false;

                if (EmployeeReferenceRenameService.Matches(
                        item.CheckedByEmployeeName,
                        oldEmployeeName))
                {
                    item.CheckedByEmployeeName = newEmployeeName;
                    itemChanged = true;
                }

                if (EmployeeReferenceRenameService.Matches(
                        item.ResponsibleEmployeeName,
                        oldEmployeeName))
                {
                    item.ResponsibleEmployeeName = newEmployeeName;
                    itemChanged = true;
                }

                if (!itemChanged)
                    continue;

                item.Note = EmployeeReferenceRenameService.RenameText(
                    item.Note,
                    oldEmployeeName,
                    newEmployeeName);
                changed++;
            }

            if (changed > 0)
                Save();

            return changed;
        }

        public static void AddAuditItem(
            Guid batchId,
            string checkedByEmployeeName,
            string responsibleEmployeeName,
            string productName,
            int expectedQuantity,
            int actualQuantity,
            int salePrice,
            string note = "",
            string acceptanceKey = "")
        {
            int difference = actualQuantity - expectedQuantity;
            int differenceAmount = Math.Abs(difference) * salePrice;

            var item = new StockAuditItem
            {
                BatchId = batchId,
                CreatedAt = DateTime.Now,
                CheckedByEmployeeName = checkedByEmployeeName,
                ResponsibleEmployeeName = responsibleEmployeeName,
                AcceptanceKey = acceptanceKey.Trim(),
                ProductName = productName,
                ExpectedQuantity = expectedQuantity,
                ActualQuantity = actualQuantity,
                Difference = difference,
                SalePrice = salePrice,
                DifferenceAmount = differenceAmount,
                Note = note
            };

            _items.Add(item);
            Save();
        }

        public static List<StockAuditItem> GetAll()
        {
            return _items
                .OrderByDescending(item => item.CreatedAt)
                .ToList();
        }

        public static List<StockAuditItem> GetByPeriod(
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

        public static List<StockAuditItem> GetToday()
        {
            return GetByPeriod(DateTime.Today, DateTime.Today.AddDays(1));
        }

        public static List<IGrouping<Guid, StockAuditItem>> GetTodayBatches()
        {
            return GetToday()
                .GroupBy(item => item.BatchId)
                .OrderByDescending(group => group.Max(item => item.CreatedAt))
                .ToList();
        }

        public static List<IGrouping<Guid, StockAuditItem>> GetAllBatches()
        {
            return _items
                .GroupBy(item => item.BatchId)
                .OrderByDescending(group => group.Max(item => item.CreatedAt))
                .ToList();
        }

        public static bool HasAcceptanceKey(string acceptanceKey)
        {
            acceptanceKey = acceptanceKey.Trim();

            if (string.IsNullOrWhiteSpace(acceptanceKey))
                return false;

            return _items.Any(item =>
                item.AcceptanceKey.Equals(acceptanceKey, StringComparison.OrdinalIgnoreCase));
        }

        public static int GetTodayShortageAmount()
        {
            return GetToday()
                .Where(item => item.Difference < 0)
                .Sum(item => item.DifferenceAmount);
        }

        public static void Clear()
        {
            _items.Clear();
            StockAuditStorageService.Clear();
        }

        private static void Save()
        {
            StockAuditStorageService.Save(_items);
        }
    }
}
