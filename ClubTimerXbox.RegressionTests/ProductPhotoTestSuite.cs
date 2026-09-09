using ClubTimerXbox.Models;
using ClubTimerXbox.Services;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal sealed class ProductPhotoTestSuite
{
    private int _passed;
    public void Run()
    {
        Test("seed applies only to Salikhov", f =>
        {
            f.Store.EnsureSeed("club_1", [f.Item]);
            Equal("default", f.Store.Get("club_1", f.Item.Id).Kind);
            f.Seed();
            Equal("bundled", f.Store.Get("club_2", f.Item.Id).Kind);
        });
        Test("rename preserves stable photo identity", f =>
        {
            f.Seed(); f.Item.ProductName = "New name"; f.Seed();
            Equal("test-product", f.Store.Get("club_2", f.Item.Id).AssetKey);
        });
        Test("duplicate names are not guessed", f =>
        {
            f.Store.EnsureSeed("club_2", [f.Item, new ProductStockItem { ProductName = f.Item.ProductName }]);
            Equal("default", f.Store.Get("club_2", f.Item.Id).Kind);
        });
        Test("empty stock does not consume seed", f =>
        {
            f.Store.EnsureSeed("club_2", []); f.Seed();
            Equal("bundled", f.Store.Get("club_2", f.Item.Id).Kind);
        });
        Test("removed photo stays removed after restart and update", f =>
        {
            f.Seed(); f.Store.Apply("club_2", f.Item.Id, 1, "remove", "", "remove_1");
            var next = f.Reopen(); next.EnsureSeed("club_2", [f.Item]);
            Equal("removed", next.Get("club_2", f.Item.Id).Kind);
            Equal(next.DefaultPath, next.ResolvePath("club_2", f.Item.Id));
        });
        Test("custom photo wins over initial seed", f =>
        {
            var hash = f.SaveImage();
            f.Store.Apply("club_2", f.Item.Id, 0, "assign", hash, "assign_1"); f.Seed();
            Equal("custom", f.Store.Get("club_2", f.Item.Id).Kind);
        });
        Test("repeat command is idempotent", f =>
        {
            f.Seed();
            var first = f.Store.Apply("club_2", f.Item.Id, 1, "remove", "", "same");
            var repeated = f.Store.Apply("club_2", f.Item.Id, 1, "remove", "", "same");
            Equal(first, repeated);
        });
        Test("stale command cannot replace newer photo", f =>
        {
            f.Seed(); f.Store.Apply("club_2", f.Item.Id, 1, "remove", "", "new");
            Reject(() => f.Store.Apply("club_2", f.Item.Id, 1, "assign", f.SaveImage(), "old"));
            Equal("removed", f.Store.Get("club_2", f.Item.Id).Kind);
        });
        Test("missing custom image gives standard fallback", f =>
        {
            var hash = f.SaveImage();
            f.Store.Apply("club_2", f.Item.Id, 0, "assign", hash, "custom");
            var path = f.Store.ResolvePath("club_2", f.Item.Id);
            File.Delete(path);
            Equal(f.Store.DefaultPath, f.Store.ResolvePath("club_2", f.Item.Id));
        });
        Test("blob cannot cross club boundary", f =>
        {
            var hash = f.SaveImage();
            Reject(() => f.Store.Apply("club_1", f.Item.Id, 0, "assign", hash, "cross"));
        });
        Test("metadata changes leave stock and prices untouched", f =>
        {
            string original = JsonSerializer.Serialize(f.Item);
            f.Seed(); f.Store.Apply("club_2", f.Item.Id, 1, "remove", "", "remove");
            Equal(original, JsonSerializer.Serialize(f.Item));
        });
        Test("bad hash is rejected", f =>
            Reject(() => f.Store.SaveBlob("club_2", new string('0', 64), ImageBytes(8, 8))));
        Test("oversize and corrupt images are rejected", f =>
        {
            foreach (var bytes in new[] { new byte[ProductPhotoStore.MaxImageBytes + 1], new byte[] { 1, 2, 3 }, ImageBytes(1025, 8) })
                Reject(() => f.Store.SaveBlob("club_2", Hash(bytes), bytes));
        });
        Test("corrupt state cannot resurrect initial photos", f =>
        {
            f.Seed();
            File.WriteAllText(Path.Combine(f.Root, "state", "club_2", "state.json"), "not json");
            Reject(() => f.Reopen().EnsureSeed("club_2", [f.Item]));
        });
        Test("path traversal is rejected", f =>
        {
            Reject(() => f.Store.Get("../club_2", f.Item.Id));
            Reject(() => f.Store.HasBlob("club_2", "../../outside"));
        });
        Console.WriteLine($"Product photo tests passed: {_passed}");
    }

    private void Test(string name, Action<Fixture> test)
    {
        using var fixture = new Fixture(); test(fixture); _passed++;
        Console.WriteLine("PASS photo: " + name);
    }
    private static void Equal<T>(T expected, T actual)
    { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}"); }
    private static void Reject(Action action)
    {
        try { action(); } catch { return; }
        throw new Exception("Invalid photo operation was accepted.");
    }
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static byte[] ImageBytes(int width, int height)
    {
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr24, null,
            new byte[width * height * 3], width * 3);
        var encoder = new JpegBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream(); encoder.Save(stream); return stream.ToArray();
    }
    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "ClubTimerPhotoTests-" + Guid.NewGuid().ToString("N"));
        public ProductStockItem Item { get; } = new() { ProductName = "Test product", Quantity = 12, PurchasePrice = 45, SalePrice = 70 };
        public ProductPhotoStore Store { get; }
        public Fixture()
        {
            Directory.CreateDirectory(Path.Combine(Root, "assets"));
            File.WriteAllText(Path.Combine(Root, "assets", "catalogue.json"),
                """{"clubId":"club_2","seedId":"first","defaultAsset":"default","products":{"Test product":"test-product"}}""");
            File.WriteAllBytes(Path.Combine(Root, "assets", "default.png"), [0]);
            File.WriteAllBytes(Path.Combine(Root, "assets", "test-product.png"), [0]);
            Store = Reopen();
        }
        public ProductPhotoStore Reopen() => new(Path.Combine(Root, "assets"), Path.Combine(Root, "state"));
        public void Seed() => Store.EnsureSeed("club_2", [Item]);
        public string SaveImage()
        { var bytes = ImageBytes(8, 8); var hash = Hash(bytes); Store.SaveBlob("club_2", hash, bytes); return hash; }
        public void Dispose()
        {
            if (!Path.GetFullPath(Root).StartsWith(Path.Combine(Path.GetTempPath(), "ClubTimerPhotoTests-"), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Invalid test cleanup target.");
            Directory.Delete(Root, true);
        }
    }
}
