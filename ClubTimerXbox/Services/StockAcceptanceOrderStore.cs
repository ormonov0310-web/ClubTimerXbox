using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace ClubTimerXbox.Services;

public sealed class StockAcceptanceOrderStore
{
    private static readonly object Gate = new();
    private readonly string _path;
    public StockAcceptanceOrderStore(string path) => _path = path;
    public static StockAcceptanceOrderStore Local() => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClubTimerXbox", "stock_acceptance_order.json"));

    private sealed class Document
    {
        [System.Text.Json.Serialization.JsonRequired]
        public int SchemaVersion { get; set; } = 1;
        [System.Text.Json.Serialization.JsonRequired]
        public Dictionary<string, List<Guid>> Clubs { get; set; } = new();
    }

    private Document Read()
    {
        if (!File.Exists(_path)) return new();
        if (new FileInfo(_path).Length > 1024 * 1024) throw new InvalidDataException("Acceptance order file too large.");
        var document = JsonSerializer.Deserialize<Document>(File.ReadAllText(_path))
            ?? throw new InvalidDataException("Invalid acceptance order.");
        if (document.SchemaVersion != 1 || document.Clubs == null || document.Clubs.Count > 100 ||
            document.Clubs.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || pair.Value == null ||
                pair.Value.Count > 10000 || pair.Value.Any(id => id == Guid.Empty) || pair.Value.Distinct().Count() != pair.Value.Count))
            throw new InvalidDataException("Invalid acceptance order.");
        return document;
    }

    public List<Guid> Load(string clubId)
    {
        if (string.IsNullOrWhiteSpace(clubId)) throw new ArgumentException("Club is required.", nameof(clubId));
        lock (Gate) return Read().Clubs.TryGetValue(clubId, out var ids) ? new(ids) : new();
    }

    public void Save(string clubId, IReadOnlyList<Guid> order)
    {
        if (string.IsNullOrWhiteSpace(clubId) || order.Count > 10000 || order.Any(id => id == Guid.Empty) ||
            order.Distinct().Count() != order.Count) throw new ArgumentException("Invalid acceptance order.");
        lock (Gate)
        {
            var document = Read();
            if (document.Clubs.TryGetValue(clubId, out var old) && old.SequenceEqual(order)) return;
            document.Clubs[clubId] = order.ToList();
            if (document.Clubs.Count > 100) throw new InvalidDataException("Too many acceptance order scopes.");
            string json = JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true });
            if (System.Text.Encoding.UTF8.GetByteCount(json) > 1024 * 1024) throw new InvalidDataException("Acceptance order file too large.");
            AtomicFileStorageService.WriteAllText(_path, json);
        }
    }
}
