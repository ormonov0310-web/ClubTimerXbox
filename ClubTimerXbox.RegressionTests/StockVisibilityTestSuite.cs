using ClubTimerXbox.Models;
using ClubTimerXbox.Services;
using System.Text.Json;

internal sealed class StockVisibilityTestSuite
{
    private static readonly DateTime Now = new(2026, 9, 9, 18, 30, 0, DateTimeKind.Utc);
    private int _passed;

    public void Run()
    {
        Test("new zero gets full day", NewZero);
        Test("legacy zero gets full day without guessing updated time", LegacyZero);
        Test("positive stock has no deadline", Positive);
        Test("sale of last unit starts grace period", LastSale);
        Test("second zero does not extend grace", RepeatedZero);
        Test("metadata changes do not extend grace", Metadata);
        Test("acceptance of one unit cancels timer", AcceptOne);
        Test("new zero after restock starts a new day", NewCycle);
        Test("negative stock remains visible", NegativeStock);
        Test("restart keeps zero timestamp", Restart);
        Test("restart keeps cancellation", RestartAfterRestock);
        Test("unknown date remains visible until migration", UnknownDate);
        Test("corrupt date does not overflow", InvalidDate);
        Test("UTC deadline matches phone contract", PhoneDeadline);
        Test("clock rollback does not reset original zero", ClockRollback);
        Console.WriteLine($"Stock visibility lifecycle: {_passed} scenarios passed.");
    }

    private static void NewZero()
    {
        var item = new ProductStockItem();
        StockVisibilityPolicy.EnsureTracking(item, Now);
        Equal(Now, item.ZeroStockSinceUtc);
        Equal(Now.AddHours(24), StockVisibilityPolicy.FoldAfterUtc(0, item.ZeroStockSinceUtc));
    }

    private static void LegacyZero()
    {
        var item = new ProductStockItem { UpdatedAt = Now.AddMonths(-2), PurchasePrice = 30, SalePrice = 50 };
        var id = item.Id;
        StockVisibilityPolicy.EnsureTracking(item, Now);
        Equal(Now, item.ZeroStockSinceUtc);
        Equal(Now.AddMonths(-2), item.UpdatedAt);
        Equal(0, item.Quantity);
        Equal(30, item.PurchasePrice);
        Equal(50, item.SalePrice);
        Equal(id, item.Id);
    }

    private static void Positive()
    {
        var item = new ProductStockItem { Quantity = 2, ZeroStockSinceUtc = Now.AddDays(-3) };
        StockVisibilityPolicy.EnsureTracking(item, Now);
        Equal<DateTime?>(null, item.ZeroStockSinceUtc);
        Equal<long?>(null, StockVisibilityPolicy.FoldAfterUnixMs(item));
    }

    private static void LastSale()
    {
        var item = new ProductStockItem { Quantity = 1 };
        StockVisibilityPolicy.SetQuantity(item, 0, Now);
        Equal(Now, item.ZeroStockSinceUtc);
    }

    private static ProductStockItem Zero() => new() { ZeroStockSinceUtc = Now };

    private static void RepeatedZero()
    {
        var item = Zero();
        StockVisibilityPolicy.SetQuantity(item, 0, Now.AddHours(20));
        Equal(Now, item.ZeroStockSinceUtc);
    }

    private static void Metadata()
    {
        var item = Zero();
        item.ProductName = "Renamed";
        item.SalePrice = 100;
        item.MinimumQuantity = 5;
        item.UpdatedAt = Now.AddHours(12);
        StockVisibilityPolicy.EnsureTracking(item, Now.AddHours(12));
        Equal(Now, item.ZeroStockSinceUtc);
    }

    private static void AcceptOne()
    {
        var item = Zero();
        StockVisibilityPolicy.SetQuantity(item, 1, Now.AddMinutes(10));
        Equal<DateTime?>(null, item.ZeroStockSinceUtc);
        Equal<long?>(null, StockVisibilityPolicy.FoldAfterUnixMs(item));
    }

    private static void NewCycle()
    {
        var item = Zero();
        StockVisibilityPolicy.SetQuantity(item, 1, Now.AddHours(2));
        StockVisibilityPolicy.SetQuantity(item, 0, Now.AddHours(5));
        Equal(Now.AddHours(5), item.ZeroStockSinceUtc);
        Equal(Now.AddHours(29), StockVisibilityPolicy.FoldAfterUtc(0, item.ZeroStockSinceUtc));
    }

    private static void NegativeStock()
    {
        var item = Zero();
        StockVisibilityPolicy.SetQuantity(item, -1, Now.AddHours(2));
        Equal<DateTime?>(null, item.ZeroStockSinceUtc);
        Equal<long?>(null, StockVisibilityPolicy.FoldAfterUnixMs(item));
        StockVisibilityPolicy.SetQuantity(item, 0, Now.AddHours(3));
        Equal(Now.AddHours(3), item.ZeroStockSinceUtc);
    }

    private static ProductStockItem Roundtrip(ProductStockItem item) =>
        JsonSerializer.Deserialize<ProductStockItem>(JsonSerializer.Serialize(item))!;

    private static void Restart()
    {
        var item = Roundtrip(Zero());
        StockVisibilityPolicy.EnsureTracking(item, Now.AddDays(4));
        Equal(Now, item.ZeroStockSinceUtc);
        Equal(DateTimeKind.Utc, item.ZeroStockSinceUtc!.Value.Kind);
    }

    private static void RestartAfterRestock()
    {
        var item = Zero();
        StockVisibilityPolicy.SetQuantity(item, 1, Now.AddHours(2));
        item = Roundtrip(item);
        StockVisibilityPolicy.EnsureTracking(item, Now.AddDays(4));
        Equal<DateTime?>(null, item.ZeroStockSinceUtc);
    }

    private static void UnknownDate() => Equal<DateTime?>(null, StockVisibilityPolicy.FoldAfterUtc(0, null));

    private static void InvalidDate()
    {
        Equal<DateTime?>(null, StockVisibilityPolicy.FoldAfterUtc(0, DateTime.MaxValue));
        var item = new ProductStockItem { ZeroStockSinceUtc = DateTime.MaxValue };
        StockVisibilityPolicy.EnsureTracking(item, Now);
        Equal(Now, item.ZeroStockSinceUtc);
    }

    private static void PhoneDeadline() => Equal(
        (long?)new DateTimeOffset(Now.AddHours(24)).ToUnixTimeMilliseconds(),
        StockVisibilityPolicy.FoldAfterUnixMs(Zero()));

    private static void ClockRollback()
    {
        var item = Zero();
        StockVisibilityPolicy.EnsureTracking(item, Now.AddHours(-2));
        Equal(Now, item.ZeroStockSinceUtc);
    }

    private void Test(string name, Action test) { test(); _passed++; Console.WriteLine("PASS stock lifecycle: " + name); }
    private static void Equal<T>(T expected, T actual)
    {
        if (!Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}");
    }
}
