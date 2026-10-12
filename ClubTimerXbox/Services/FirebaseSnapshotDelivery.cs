using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ClubTimerXbox.Services;

// Business data stays in its existing durable stores. This file records delivery,
// not a second copy of payroll or a financial operation to replay.
internal sealed class FirebaseSnapshotDelivery
{
    private readonly string _path;
    private readonly object _gate = new();
    private JsonObject? _published;
    private long _generation;
    private State _state;
    private readonly Func<long> _clock;

    internal sealed class State
    {
        public string Scope { get; set; } = "";
        public bool Pending { get; set; }
        public bool Blocked { get; set; }
        public int Failures { get; set; }
        public long NextAttemptAt { get; set; }
        public long LastAttemptAt { get; set; }
        public long LastSuccessAt { get; set; }
        public long Revision { get; set; }
        public int PayloadBytes { get; set; }
        public string ErrorKind { get; set; } = "";
    }

    public FirebaseSnapshotDelivery(string path, Func<long>? clock = null)
    {
        _path = path;
        _clock = clock ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        try { _state = JsonSerializer.Deserialize<State>(File.ReadAllText(path)) ?? new(); }
        catch { _state = new(); }
        // A new process must re-establish its acknowledged baseline and binding.
        _state.Blocked = false;
        _state.NextAttemptAt = 0;
    }

    public long Revision { get { lock (_gate) return _state.Revision; } }
    public bool Pending { get { lock (_gate) return _state.Pending; } }

    public void Request(string scope, long now, bool urgent)
    {
        lock (_gate)
        {
            if (_state.Scope != scope)
            {
                _state = new State { Scope = scope };
                _published = null;
            }
            if (urgent && _state.Blocked)
            {
                _state.Blocked = false;
                _state.Failures = 0;
            }
            if (!_state.Pending)
                _state.NextAttemptAt = urgent ? now : now + 2000;
            else if (urgent && _state.Failures == 0)
                _state.NextAttemptAt = now;
            _state.Pending = true;
            _generation++;
            Save();
        }
    }

    public bool CanAttempt(long now)
    {
        lock (_gate)
            return _state.Pending && !_state.Blocked && now >= _state.NextAttemptAt;
    }

    public async System.Threading.Tasks.Task<bool> PublishAsync(
        Func<JsonObject> build, Func<JsonObject, System.Threading.Tasks.Task> send,
        long now)
    {
        long generation;
        string scope;
        JsonObject? snapshot = null;
        lock (_gate)
        {
            if (!CanAttempt(now)) return false;
            generation = _generation;
            scope = _state.Scope;
            _state.LastAttemptAt = now;
            Save();
        }
        try
        {
            snapshot = build();
            JsonObject patch = BuildPatch(_published, snapshot);
            // A generated clock is not a business change.
            bool changed = patch.Count > 0;
            long revision = changed ? Math.Max(now, Revision + 1) : Revision;
            if (changed)
            {
                patch["updatedAtUnixMs"] = revision;
                patch["updatedAt"] = DateTimeOffset.FromUnixTimeMilliseconds(now)
                    .ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
                lock (_gate)
                {
                    _state.PayloadBytes = Encoding.UTF8.GetByteCount(patch.ToJsonString());
                    Save();
                }
                await send(patch);
            }
            lock (_gate)
            {
                if (_state.Scope != scope) return false;
                _published = snapshot;
                _state.Revision = revision;
                long completedAt = Math.Max(now, _clock());
                if (changed) _state.LastSuccessAt = completedAt;
                _state.Failures = 0;
                _state.ErrorKind = "";
                _state.Pending = generation != _generation;
                _state.NextAttemptAt = completedAt + 2000;
                Save();
            }
            return true;
        }
        catch (Exception ex)
        {
            lock (_gate)
            {
                if (_state.Scope != scope) return false;
                _state.Pending = true;
                _state.Failures++;
                _state.ErrorKind = ErrorKind(ex);
                _state.Blocked = ex is HttpRequestException { StatusCode: { } status }
                    && status is >= HttpStatusCode.BadRequest and < HttpStatusCode.InternalServerError
                    && status != HttpStatusCode.RequestTimeout && (int)status != 429;
                long delay = Math.Min(120000, 5000L << Math.Min(5, _state.Failures - 1));
                _state.NextAttemptAt = Math.Max(now, _clock()) + delay + Random.Shared.Next(0, 1000);
                Save();
            }
            return false;
        }
    }

    private static string ErrorKind(Exception ex) => ex switch
    {
        HttpRequestException { StatusCode: { } status } => "http_" + (int)status,
        OperationCanceledException => "timeout",
        HttpRequestException => "network",
        _ => ex.GetType().Name
    };

    private void Save() => AtomicFileStorageService.WriteAllText(
        _path, JsonSerializer.Serialize(_state));

    internal static JsonObject BuildPatch(JsonObject? previous, JsonObject next)
    {
        var patch = new JsonObject();
        Diff(previous, next, "", patch);
        patch.Remove("updatedAt");
        patch.Remove("updatedAtUnixMs");
        return patch;
    }

    private static void Diff(JsonObject? old, JsonObject next, string prefix, JsonObject patch)
    {
        foreach (var pair in next)
        {
            string path = prefix + pair.Key;
            JsonNode? before = old?[pair.Key];
            if (JsonNode.DeepEquals(before, pair.Value)) continue;
            if (before is JsonObject oldObject && pair.Value is JsonObject newObject
                && newObject.Count > 0)
                Diff(oldObject, newObject, path + "/", patch);
            else
                patch[path] = pair.Value?.DeepClone();
        }
        if (old == null) return;
        foreach (var pair in old)
            if (!next.ContainsKey(pair.Key)) patch[prefix + pair.Key] = null;
    }
}
