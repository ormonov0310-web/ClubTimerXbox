using System;
using System.Collections.Generic;
using System.Linq;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services;

public static class StockAcceptanceOrderPolicy
{
    public static List<Guid> Priority(IEnumerable<ProductStockItem> products, IEnumerable<Guid> saved,
        IReadOnlyDictionary<string, int> popularity)
    {
        var items = products.ToList();
        var present = items.Select(item => item.Id).ToHashSet();
        return saved.Where(present.Contains).Concat(ProductPopularityService.OrderPurchaseCatalog(items, popularity)
            .Select(item => item.Id)).Distinct().ToList();
    }

    public static List<ProductStockItem> Order(IEnumerable<ProductStockItem> products, IReadOnlyList<Guid> priority)
    {
        var ranks = priority.Select((id, index) => (id, index)).ToDictionary(item => item.id, item => item.index);
        return products.OrderBy(item => item.Quantity <= 0)
            .ThenBy(item => ranks.GetValueOrDefault(item.Id, int.MaxValue)).ToList();
    }

    public static List<Guid> MoveAvailable(IReadOnlyList<Guid> priority, IEnumerable<ProductStockItem> products,
        Guid source, Guid target, bool after)
    {
        var available = products.Where(item => item.Quantity > 0).Select(item => item.Id).ToHashSet();
        if (source == target || !available.Contains(source) || !available.Contains(target)) return priority.ToList();
        var movable = priority.Where(available.Contains).ToList();
        if (!movable.Remove(source)) return priority.ToList();
        int index = movable.IndexOf(target);
        if (index < 0) return priority.ToList();
        movable.Insert(index + (after ? 1 : 0), source);
        int cursor = 0;
        return priority.Select(id => available.Contains(id) ? movable[cursor++] : id).ToList();
    }
}
