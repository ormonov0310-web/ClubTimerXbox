using System;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public static class StockVisibilityPolicy
    {
        public static readonly TimeSpan ZeroStockGracePeriod = TimeSpan.FromHours(24);

        public static void SetQuantity(ProductStockItem item, int quantity, DateTime utcNow)
        {
            if (quantity != item.Quantity)
                item.ZeroStockSinceUtc = null;
            item.Quantity = quantity;
            EnsureTracking(item, utcNow);
        }

        public static void EnsureTracking(ProductStockItem item, DateTime utcNow)
        {
            if (item.Quantity != 0)
                item.ZeroStockSinceUtc = null;
            else if (!FoldAfterUtc(item.Quantity, item.ZeroStockSinceUtc).HasValue)
                item.ZeroStockSinceUtc = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
        }

        public static DateTime? FoldAfterUtc(int? quantity, DateTime? zeroSinceUtc)
        {
            if (quantity != 0 || !zeroSinceUtc.HasValue || zeroSinceUtc.Value == default ||
                zeroSinceUtc.Value > DateTime.MaxValue - ZeroStockGracePeriod)
                return null;
            return DateTime.SpecifyKind(zeroSinceUtc.Value, DateTimeKind.Utc) + ZeroStockGracePeriod;
        }

        public static long? FoldAfterUnixMs(ProductStockItem item)
        {
            DateTime? deadline = FoldAfterUtc(item.Quantity, item.ZeroStockSinceUtc);
            return deadline.HasValue ? new DateTimeOffset(deadline.Value).ToUnixTimeMilliseconds() : null;
        }
    }
}
