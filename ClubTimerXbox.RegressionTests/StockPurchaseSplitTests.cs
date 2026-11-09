using ClubTimerXbox.Models;
using ClubTimerXbox.Services;
using System.Text.Json;

internal static class StockPurchaseSplitTests
{
    public static void Run()
    {
        int passed = 0;
        void Check(bool condition, string name)
        {
            if (!condition) throw new Exception("Stock purchase: " + name);
            passed++;
        }
        void Reject(Action action, string name)
        {
            bool rejected = false;
            try { action(); } catch (Exception e) when (e is InvalidOperationException or OverflowException) { rejected = true; }
            Check(rejected, name);
        }
        var at = new DateTime(2026, 11, 9, 17, 0, 0);
        var id = Guid.NewGuid();
        var items = new List<StockPurchaseItem> { new() { ProductName = "Drink", Quantity = 1, PurchasePrice = 100, SalePrice = 120 } };
        var initial = new List<ProductStockItem> { new() { ProductName = "Drink", Quantity = 10, PurchasePrice = 80, SalePrice = 120, MinimumQuantity = 3 } };
        StockPurchase Make(int cash = 80, int bank = 20) => StockPurchasePostingPolicy.Create(id, items, cash, bank, "", at, initial);
        var purchase = Make();
        var movements = StockPurchasePostingPolicy.CashMovements(purchase).ToArray();
        Check(purchase.TotalAmount == 100 && purchase.Items.Single().Quantity == 1, "one bottle stays one purchase");
        Check(movements.Length == 2 && movements.Sum(row => row.Amount) == 100, "two legs conserve total");
        Check(movements.All(row => row.StockPurchaseId == purchase.Id), "both cash rows link to the single purchase");
        Check(movements.Single(row => row.PaymentMethod == "Наличные").Amount == 80 &&
            movements.Single(row => row.PaymentMethod == "Безнал").Amount == 20, "exact channels 80/20");
        Check(movements.All(row => row.ExpenseCategory == "Закупка" && !CashService.IsClubExpense(row)), "not an extra operating expense");
        Check(movements.Select(row => row.Id).SequenceEqual(StockPurchasePostingPolicy.CashMovements(purchase).Select(row => row.Id)), "stable cash IDs");
        Check(StockPurchasePostingPolicy.CashMovements(Make(100, 0)).Single().PaymentMethod == "Наличные", "cash-only no zero bank row");
        Check(StockPurchasePostingPolicy.CashMovements(Make(0, 100)).Single().PaymentMethod == "Безнал", "bank-only no zero cash row");
        var stock = StockPurchasePostingPolicy.ApplyInventory(purchase, initial, at.ToUniversalTime());
        Check(stock.Single().Quantity == 11 && initial.Single().Quantity == 10, "owned inventory change once, no source mutation");
        Check(stock.Single().PurchasePrice == InventoryCostService.CalculateWeightedAverageUnitCost(10, 80, 1, 100), "weighted cost retained");
        Check(stock.Single().MinimumQuantity == 3, "existing stock minimum preserved");
        Check(StockPurchasePostingPolicy.ApplyInventory(purchase, stock, at.ToUniversalTime()).Single().Quantity == 11, "inventory repeat ignored");
        stock.Single().Quantity = 9;
        Check(StockPurchasePostingPolicy.ApplyInventory(purchase, stock, at.ToUniversalTime()).Single().Quantity == 9, "retry cannot undo later sale");
        var added = StockPurchasePostingPolicy.ApplyInventory(purchase, Array.Empty<ProductStockItem>(), at.ToUniversalTime());
        Check(added.Single().Quantity == 1, "new product receives one unit");
        var old = JsonSerializer.Deserialize<StockPurchase>("{\"Items\":[{\"Quantity\":1,\"PurchasePrice\":100}]}")!;
        Check(old.CashAmount == null && old.CashlessAmount == null && !old.PaymentPending, "legacy payment remains unknown");
        Check(StockPurchasePostingPolicy.CashMovements(old).Count() == 0, "legacy history not reposted");
        Reject(() => Make(100, 20), "overpayment rejected");
        Reject(() => Make(80, 10), "underpayment rejected");
        Reject(() => Make(-1, 101), "negative leg rejected");
        Reject(() => StockPurchasePostingPolicy.Create(Guid.Empty, items, 80, 20, "", at, initial), "empty identity rejected");
        Reject(() => StockPurchasePostingPolicy.Create(id, new List<StockPurchaseItem>(), 0, 0, "", at, initial), "empty cart rejected");
        Reject(() => StockPurchasePostingPolicy.Create(id, items.Concat(items).ToArray(), 200, 0, "", at, initial), "duplicate goods rejected");
        var huge = new[] { new StockPurchaseItem { ProductName = "Huge", Quantity = int.MaxValue, PurchasePrice = 2 } };
        Reject(() => StockPurchasePostingPolicy.Create(id, huge, 1, 0, "", at, initial), "total overflow rejected");
        var changed = Make(70, 30);
        Reject(() => StockPurchasePostingPolicy.EnsureSame(purchase, changed), "same identity cannot change payment");
        StockPurchasePostingPolicy.EnsureSame(purchase, Make());
        Check(true, "same payload accepted");

        // Persist each step separately, then restart after every possible partial write.
        for (int failAfter = 0; failAfter < 6; failAfter++)
        {
            var pending = Make();
            string intent = JsonSerializer.Serialize(pending);
            string persistedStock = JsonSerializer.Serialize(initial);
            var receipts = new Dictionary<Guid, ProductIncomingItem>();
            var cash = new Dictionary<Guid, CashRecord>();
            int step = 0;
            void Crash() { if (step++ == failAfter) throw new IOException("synthetic failure"); }
            void Apply(StockPurchase value)
            {
                var state = JsonSerializer.Deserialize<List<ProductStockItem>>(persistedStock)!;
                persistedStock = JsonSerializer.Serialize(StockPurchasePostingPolicy.ApplyInventory(value, state, at.ToUniversalTime()));
                Crash();
            }
            void Receipt(ProductIncomingItem value) { receipts.TryAdd(value.Id, value); Crash(); }
            void Expense(CashRecord value) { cash.TryAdd(value.Id, value); Crash(); }
            void Save() { intent = JsonSerializer.Serialize(pending); Crash(); }
            try { StockPurchasePostingPolicy.Recover(pending, Apply, Receipt, Expense, Save); }
            catch (IOException) { }
            pending = JsonSerializer.Deserialize<StockPurchase>(intent)!;
            void Complete() => StockPurchasePostingPolicy.Recover(pending,
                value => persistedStock = JsonSerializer.Serialize(StockPurchasePostingPolicy.ApplyInventory(value,
                    JsonSerializer.Deserialize<List<ProductStockItem>>(persistedStock)!, at.ToUniversalTime())),
                value => receipts.TryAdd(value.Id, value), value => cash.TryAdd(value.Id, value),
                () => intent = JsonSerializer.Serialize(pending));
            Complete(); Complete();
            Check(!pending.PaymentPending && JsonSerializer.Deserialize<List<ProductStockItem>>(persistedStock)!.Single().Quantity == 11 &&
                receipts.Count == 1 && cash.Count == 2 && cash.Values.Sum(row => row.Amount) == 100,
                $"restart after step {failAfter} preserves one stock receipt and exact payments");
        }

        var balance = CashPhysicalBalancePolicy.Calculate(Array.Empty<CashAcceptanceItem>(),
            new[] { new CashBalanceCheckpointItem { CreatedAt = at.AddMinutes(-1), CashAmount = 1000 } },
            Array.Empty<PaymentRecord>(), movements, at.AddDays(-1), at.AddDays(1));
        Check(balance.Amount == 920, "cash expectation subtracts only 80, not total 100 twice");
        int commandBytes = JsonSerializer.SerializeToUtf8Bytes(new { purchaseOperationId = purchase.Id.ToString(), cashAmount = 80, cashlessAmount = 20 }).Length;
        int historyBytes = JsonSerializer.SerializeToUtf8Bytes(new { id = purchase.Id.ToString(), cashAmount = 80, cashlessAmount = 20 }).Length;
        Console.WriteLine($"Split contract sample: command fields {commandBytes} bytes, history fields {historyBytes} bytes/row (synthetic, including braces).");
        Console.WriteLine($"Stock purchase split: {passed} checks passed.");
    }
}
