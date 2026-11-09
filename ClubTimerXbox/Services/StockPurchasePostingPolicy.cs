using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services;

public static class StockPurchasePostingPolicy
{
    public static StockPurchase Create(Guid operationId, IReadOnlyList<StockPurchaseItem> items,
        int cash, int cashless, string note, DateTime at, IReadOnlyList<ProductStockItem> stock)
    {
        if (operationId == Guid.Empty || items.Count is < 1 or > 100 || cash < 0 || cashless < 0)
            throw new InvalidOperationException("Некорректная оплата закупки.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.ProductName) || !names.Add(item.ProductName.Trim()) ||
                item.Quantity <= 0 || item.PurchasePrice < 0 || item.SalePrice < 0 || item.MinimumQuantity < 0)
                throw new InvalidOperationException("Проверьте товары закупки.");
            total = checked(total + (long)item.Quantity * item.PurchasePrice);
        }
        if (total <= 0 || total > int.MaxValue || (long)cash + cashless != total)
            throw new InvalidOperationException("Наличные и безнал должны равняться итогу закупки.");
        var purchase = new StockPurchase
        {
            Id = operationId, OperationId = operationId.ToString("D"), CreatedAt = at,
            AddedBy = "Владелец", Note = note.Trim(), CashAmount = cash, CashlessAmount = cashless,
            PaymentPending = true,
            Items = items.Select(item => new StockPurchaseItem
            {
                ProductName = item.ProductName.Trim(), Quantity = item.Quantity,
                PurchasePrice = item.PurchasePrice, SalePrice = item.SalePrice, MinimumQuantity = item.MinimumQuantity
            }).ToList()
        };
        foreach (var item in purchase.Items)
        {
            var old = stock.FirstOrDefault(row => row.ProductName.Equals(item.ProductName, StringComparison.OrdinalIgnoreCase));
            int before = old?.Quantity ?? 0;
            purchase.IncomingRecords.Add(new ProductIncomingItem
            {
                Id = MovementId(purchase.Id, item.ProductName.ToUpperInvariant()), CreatedAt = at,
                ProductName = item.ProductName, QuantityAdded = item.Quantity, QuantityBefore = before,
                QuantityAfter = checked(before + item.Quantity), PurchasePrice = item.PurchasePrice,
                SalePrice = item.SalePrice, TotalPurchaseAmount = checked(item.Quantity * item.PurchasePrice),
                Note = "Приход товара"
            });
        }
        return purchase;
    }

    public static void EnsureSame(StockPurchase saved, StockPurchase requested)
    {
        bool same = saved.CashAmount == requested.CashAmount && saved.CashlessAmount == requested.CashlessAmount &&
            saved.Note == requested.Note && saved.Items.Count == requested.Items.Count &&
            saved.Items.Zip(requested.Items).All(pair => pair.First.ProductName == pair.Second.ProductName &&
                pair.First.Quantity == pair.Second.Quantity && pair.First.PurchasePrice == pair.Second.PurchasePrice &&
                pair.First.SalePrice == pair.Second.SalePrice && pair.First.MinimumQuantity == pair.Second.MinimumQuantity);
        if (!same) throw new InvalidOperationException("Эта закупка уже сохранена с другими данными.");
    }

    public static List<ProductStockItem> ApplyInventory(StockPurchase purchase,
        IReadOnlyList<ProductStockItem> stock, DateTime utcNow)
    {
        var result = stock.Select(row => new ProductStockItem
        {
            Id = row.Id, ProductName = row.ProductName, Quantity = row.Quantity,
            PurchasePrice = row.PurchasePrice, SalePrice = row.SalePrice, MinimumQuantity = row.MinimumQuantity,
            UpdatedAt = row.UpdatedAt, ZeroStockSinceUtc = row.ZeroStockSinceUtc,
            AppliedPurchaseIds = new(row.AppliedPurchaseIds ?? new())
        }).ToList();
        foreach (var item in purchase.Items)
        {
            var product = result.FirstOrDefault(row => row.ProductName.Equals(item.ProductName, StringComparison.OrdinalIgnoreCase));
            if (product?.AppliedPurchaseIds.Contains(purchase.Id) == true) continue;
            if (product == null)
            {
                product = new ProductStockItem { ProductName = item.ProductName, MinimumQuantity = item.MinimumQuantity };
                result.Add(product);
            }
            int quantity = checked(product.Quantity + item.Quantity);
            product.PurchasePrice = InventoryCostService.CalculateWeightedAverageUnitCost(
                product.Quantity, product.PurchasePrice, item.Quantity, item.PurchasePrice);
            StockVisibilityPolicy.SetQuantity(product, quantity, utcNow);
            product.SalePrice = item.SalePrice;
            product.UpdatedAt = purchase.CreatedAt;
            product.AppliedPurchaseIds.Add(purchase.Id);
        }
        return result;
    }

    public static void CommitIncomingInventory(IList<ProductStockItem> live, IReadOnlyList<ProductStockItem> persisted)
    {
        foreach (var item in persisted)
        {
            var current = live.FirstOrDefault(row => row.Id == item.Id);
            if (current == null) { live.Add(item); continue; }
            current.Quantity = item.Quantity; current.PurchasePrice = item.PurchasePrice;
            current.SalePrice = item.SalePrice; current.MinimumQuantity = item.MinimumQuantity;
            current.UpdatedAt = item.UpdatedAt; current.ZeroStockSinceUtc = item.ZeroStockSinceUtc;
            current.AppliedPurchaseIds = new(item.AppliedPurchaseIds);
        }
    }

    public static IEnumerable<CashRecord> CashMovements(StockPurchase purchase)
    {
        foreach (var leg in new[] { ("Наличные", purchase.CashAmount), ("Безнал", purchase.CashlessAmount) })
        {
            if (leg.Item2 is not > 0) continue;
            yield return new CashRecord
            {
                Id = MovementId(purchase.Id, leg.Item1), StockPurchaseId = purchase.Id, CreatedAt = purchase.CreatedAt,
                BusinessOccurredAt = purchase.CreatedAt, EmployeeName = purchase.AddedBy,
                IncomeEmployeeName = purchase.AddedBy, Type = CashRecordType.Expense,
                Category = "Расходы", ExpenseCategory = "Закупка", Title = "Закуп товаров",
                Description = string.Join("\n", purchase.Items.Select(item =>
                    $"{item.ProductName}: {item.Quantity} шт × {item.PurchasePrice} сом = {item.TotalAmount} сом")) +
                    (string.IsNullOrWhiteSpace(purchase.Note) ? "" : "\n" + purchase.Note),
                PaymentMethod = leg.Item1, Amount = leg.Item2.Value
            };
        }
    }

    public static void Recover(StockPurchase purchase, Action<StockPurchase> applyInventory,
        Action<ProductIncomingItem> ensureIncoming, Action<CashRecord> ensureCash, Action save)
    {
        if (!purchase.PaymentPending) return;
        applyInventory(purchase);
        foreach (var incoming in purchase.IncomingRecords) ensureIncoming(incoming);
        foreach (var row in CashMovements(purchase)) ensureCash(row);
        purchase.PaymentPending = false;
        try { save(); }
        catch { purchase.PaymentPending = true; throw; }
    }

    private static Guid MovementId(Guid purchaseId, string leg) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes($"stock-purchase:{purchaseId:D}:{leg}"))[..16]);
}
