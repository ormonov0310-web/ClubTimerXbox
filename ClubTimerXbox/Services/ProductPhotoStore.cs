using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services;

public sealed record ProductPhoto
{
    public string Kind { get; init; } = "default";
    public string AssetKey { get; init; } = "";
    public string ContentHash { get; init; } = "";
    public long Revision { get; init; }
    public string LastCommandId { get; init; } = "";
}

internal sealed class ProductPhotoCatalogue
{
    public string SeedId { get; set; } = "";
    public string ClubId { get; set; } = "";
    public string DefaultAsset { get; set; } = "";
    public Dictionary<string, string> Products { get; set; } = new();
}

internal sealed class ProductPhotoState
{
    public string AppliedSeed { get; set; } = "";
    public Dictionary<Guid, ProductPhoto> Products { get; set; } = new();
}

// Photo state is independent of stock/accounting and names are used only during the seed.
internal sealed class ProductPhotoStore
{
    public const int MaxImageBytes = 300 * 1024;
    public const int MaxImageEdge = 1024;
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
    private readonly string _assets;
    private readonly string _root;
    private readonly ProductPhotoCatalogue _catalogue;
    private readonly object _sync = new();

    public ProductPhotoStore(string assets, string root)
    {
        _assets = assets;
        _root = root;
        _catalogue = JsonSerializer.Deserialize<ProductPhotoCatalogue>(
            File.ReadAllText(Path.Combine(assets, "catalogue.json")), Json)
            ?? throw new InvalidDataException("Photo catalogue is missing.");
    }

    public static bool IsHash(string hash) => Regex.IsMatch(hash, @"\A[a-f0-9]{64}\z");
    public static bool IsClub(string club) => Regex.IsMatch(club, @"\A[a-z0-9][a-z0-9_-]{0,63}\z");

    private string ClubFolder(string club)
    {
        if (!IsClub(club)) throw new InvalidDataException("Invalid photo club.");
        return Path.Combine(_root, club);
    }

    private ProductPhotoState Load(string club)
    {
        string path = Path.Combine(ClubFolder(club), "state.json");
        // Never treat corrupt state as empty: doing so could resurrect removed photos.
        return File.Exists(path)
            ? JsonSerializer.Deserialize<ProductPhotoState>(File.ReadAllText(path), Json)
                ?? throw new InvalidDataException("Invalid photo state.")
            : new ProductPhotoState();
    }

    private void Save(string club, ProductPhotoState state) =>
        AtomicFileStorageService.WriteAllText(Path.Combine(ClubFolder(club), "state.json"),
            JsonSerializer.Serialize(state, Json));

    public void EnsureSeed(string club, IReadOnlyList<ProductStockItem> stock)
    {
        if (club != _catalogue.ClubId || stock.Count == 0) return;
        lock (_sync)
        {
            var state = Load(club);
            // An earlier seed is also final. New updates must not implicitly re-seed.
            if (state.AppliedSeed.Length > 0) return;
            foreach (var entry in _catalogue.Products)
            {
                var matches = stock.Where(p => p.ProductName.Trim().Equals(entry.Key,
                    StringComparison.OrdinalIgnoreCase)).ToArray();
                if (matches.Length != 1 || matches[0].Id == Guid.Empty) continue;
                state.Products.TryAdd(matches[0].Id, new ProductPhoto
                {
                    Kind = "bundled", AssetKey = entry.Value, Revision = 1
                });
            }
            state.AppliedSeed = _catalogue.SeedId;
            Save(club, state);
        }
    }

    public ProductPhoto Get(string club, Guid productId)
    {
        lock (_sync)
            return Load(club).Products.GetValueOrDefault(productId) ?? new ProductPhoto();
    }

    public ProductPhoto Apply(string club, Guid productId, long expectedRevision,
        string action, string hash, string commandId)
    {
        if (productId == Guid.Empty || string.IsNullOrWhiteSpace(commandId))
            throw new InvalidDataException("Invalid photo identity.");
        lock (_sync)
        {
            var state = Load(club);
            var old = state.Products.GetValueOrDefault(productId) ?? new ProductPhoto();
            if (old.LastCommandId == commandId) return old;
            if (old.Revision != expectedRevision)
                throw new InvalidOperationException("Фото уже изменилось. Обновите карточку и повторите.");
            if (action != "remove" && action != "assign")
                throw new InvalidDataException("Unknown photo action.");
            if (action == "assign" && (!IsHash(hash) || !File.Exists(BlobPath(club, hash))))
                throw new InvalidDataException("Фотография ещё не загружена на ПК.");
            var next = new ProductPhoto
            {
                Kind = action == "remove" ? "removed" : "custom",
                ContentHash = action == "remove" ? "" : hash,
                Revision = checked(old.Revision + 1),
                LastCommandId = commandId
            };
            state.Products[productId] = next;
            Save(club, state);
            return next;
        }
    }

    private string BlobPath(string club, string hash)
    {
        if (!IsHash(hash)) throw new InvalidDataException("Invalid photo hash.");
        return Path.Combine(ClubFolder(club), hash + ".jpg");
    }

    public bool HasBlob(string club, string hash) => File.Exists(BlobPath(club, hash));

    public void SaveBlob(string club, string hash, byte[] bytes)
    {
        string target = BlobPath(club, hash);
        if (bytes.Length == 0 || bytes.Length > MaxImageBytes ||
            !Convert.ToHexStringLower(SHA256.HashData(bytes)).Equals(hash, StringComparison.Ordinal))
            throw new InvalidDataException("Фотография повреждена или слишком большая.");
        using var stream = new MemoryStream(bytes, writable: false);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation,
            BitmapCacheOption.None);
        if (decoder is not JpegBitmapDecoder || decoder.Frames.Count != 1 ||
            decoder.Frames[0].PixelWidth is <= 0 or > MaxImageEdge ||
            decoder.Frames[0].PixelHeight is <= 0 or > MaxImageEdge)
            throw new InvalidDataException("Неподдерживаемый формат фотографии.");
        var frame = decoder.Frames[0];
        int stride = (frame.PixelWidth * frame.Format.BitsPerPixel + 7) / 8;
        frame.CopyPixels(new byte[checked(stride * frame.PixelHeight)], stride, 0);
        Directory.CreateDirectory(ClubFolder(club));
        string temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, target, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public string DefaultPath => Path.Combine(_assets, _catalogue.DefaultAsset + ".png");

    public string ResolvePath(string club, Guid productId)
    {
        var photo = Get(club, productId);
        string path = photo.Kind switch
        {
            "bundled" when _catalogue.Products.Values.Contains(photo.AssetKey) =>
                Path.Combine(_assets, photo.AssetKey + ".png"),
            "custom" when IsHash(photo.ContentHash) => BlobPath(club, photo.ContentHash),
            _ => DefaultPath
        };
        return File.Exists(path) ? path : DefaultPath;
    }
}

internal static class ProductPhotoService
{
    public static string DefaultPath => Path.Combine(AppContext.BaseDirectory, "Assets",
        "ProductPhotos", "clubtimer-water-default-v1.png");
    private static readonly Lazy<ProductPhotoStore> Store = new(() => new ProductPhotoStore(
        Path.Combine(AppContext.BaseDirectory, "Assets", "ProductPhotos"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ClubTimerXbox", "ProductPhotos")));
    public static ProductPhotoStore Current => Store.Value;

    public static void EnsureSeed()
    {
        try { Current.EnsureSeed(PcIdentityService.Current.ClubId, ProductStockService.StockItems); }
        catch (Exception ex) { System.Diagnostics.Trace.WriteLine("Product photo seed: " + ex.GetType().Name); }
    }

    public static object Snapshot(Guid id)
    {
        ProductPhoto photo;
        try { photo = Current.Get(PcIdentityService.Current.ClubId, id); }
        catch { photo = new ProductPhoto(); }
        return new { kind = photo.Kind, assetKey = photo.AssetKey,
            contentHash = photo.ContentHash, revision = photo.Revision };
    }

    public static string? Resolve(string productName)
    {
        try
        {
            EnsureSeed();
            var item = ProductStockService.FindByProductName(productName);
            return item == null ? Current.DefaultPath :
                Current.ResolvePath(PcIdentityService.Current.ClubId, item.Id);
        }
        catch
        {
            return DefaultPath;
        }
    }
}
