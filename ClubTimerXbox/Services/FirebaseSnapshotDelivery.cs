using System;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace ClubTimerXbox.Services;

// Business data stays in its existing durable stores. This file records delivery,
// not a second copy of payroll or a financial operation to replay.
internal sealed class FirebaseSnapshotDelivery
{
    private readonly string _path;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _publishGate = new(1, 1);
    private JsonObject? _published;
    private long _generation;
    private long _scopeEpoch;
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
        public double BuildMilliseconds { get; set; }
        public double CaptureWorkflowMilliseconds { get; set; }
        public BackgroundFinancialCalculation.Profile? FinancialBuild { get; set; }
        public BackgroundFinancialCalculation.Profile? LastSlowFinancialBuild { get; set; }
        public double PrepareMilliseconds { get; set; }
        public double BackgroundBuildMilliseconds { get; set; }
        public int BackgroundBuildThreadId { get; set; }
        public double SendMilliseconds { get; set; }
        public bool? BuildOnUiThread { get; set; }
        public int BuildThreadId { get; set; }
        public int PrepareThreadId { get; set; }
        public Dictionary<string, SnapshotBuildMeasurements.Stage> BuildStages { get; set; } = new();
        public long LastSlowBuildAtUnixMs { get; set; }
        public double LastSlowBuildMilliseconds { get; set; }
        public Dictionary<string, SnapshotBuildMeasurements.Stage> LastSlowBuildStages { get; set; } = new();
        public BuildProfile? OverviewBuild { get; set; }
        public BuildProfile? LastSlowOverviewBuild { get; set; }
        public double CheckpointMilliseconds { get; set; }
        public long LastSlowCheckpointAtUnixMs { get; set; }
        public double LastSlowCheckpointMilliseconds { get; set; }
    }

    internal sealed record BuildProfile(long AtUnixMs, double Milliseconds, bool? OnUiThread,
        Dictionary<string, SnapshotBuildMeasurements.Stage> Stages);

    internal sealed record PreparedPatch(JsonObject Patch, byte[] Utf8, long Revision, int ThreadId)
    {
        public bool HasChanges => Patch.Count > 0;
    }

    internal sealed record CapturedSnapshot(JsonObject Document, Action<CancellationToken>? Complete = null,
        Func<CancellationToken, JsonObject>? BuildDocument = null, double? OwnerBuildMilliseconds = null);

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

    public void RecordFinancialBuild(BackgroundFinancialCalculation.Profile profile)
    {
        lock (_gate)
        {
            _state.FinancialBuild = profile;
            if (profile.CaptureMilliseconds + profile.ApplyMilliseconds >= 250 || profile.SynchronousFallback)
                _state.LastSlowFinancialBuild = profile;
        }
    }

    public void RecordOverviewBuild(BuildProfile profile)
    {
        lock (_gate)
        {
            _state.OverviewBuild = profile;
            if (profile.Milliseconds >= 250) _state.LastSlowOverviewBuild = profile;
            // Persist with the next existing delivery checkpoint, not an extra diagnostic write.
        }
    }

    public void Request(string scope, long now, bool urgent)
    {
        lock (_gate)
        {
            if (_state.Scope != scope)
            {
                _scopeEpoch++;
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

    public Task<bool> PublishAsync(Func<JsonObject> build, Func<JsonObject, Task> send, long now)
        => PublishPreparedAsync(build, prepared => send(prepared.Patch), now);

    public Task<bool> PublishPreparedAsync(Func<JsonObject> build,
        Func<PreparedPatch, Task> send, long now, CancellationToken token = default)
        => PublishCapturedAsync(() => new CapturedSnapshot(build()), send, now, token);

    public async Task<bool> PublishCapturedAsync(Func<CapturedSnapshot> capture,
        Func<PreparedPatch, Task> send, long now, CancellationToken token = default)
        => await PublishCapturedAsync(() => Task.FromResult(capture()), send, now, token);

    public async Task<bool> PublishCapturedAsync(Func<Task<CapturedSnapshot>> capture,
        Func<PreparedPatch, Task> send, long now, CancellationToken token = default)
    {
        if (!await _publishGate.WaitAsync(0)) return false;
        try { return await PublishCapturedCoreAsync(capture, send, now, token); }
        finally { _publishGate.Release(); }
    }

    private async Task<bool> PublishCapturedCoreAsync(Func<Task<CapturedSnapshot>> capture,
        Func<PreparedPatch, Task> send, long now, CancellationToken token)
    {
        long generation;
        long scopeEpoch;
        long previousRevision;
        JsonObject? previous;
        JsonObject? snapshot = null;
        lock (_gate)
        {
            if (!CanAttempt(now)) return false;
            generation = _generation;
            scopeEpoch = _scopeEpoch;
            previousRevision = _state.Revision;
            previous = _published;
            _state.LastAttemptAt = now;
            _state.BuildMilliseconds = 0;
            _state.PrepareMilliseconds = 0;
            _state.BackgroundBuildMilliseconds = 0;
            _state.BackgroundBuildThreadId = 0;
            _state.SendMilliseconds = 0;
            _state.BuildStages = new();
            _state.BuildOnUiThread = System.Windows.Application.Current?.Dispatcher.CheckAccess();
            _state.BuildThreadId = Environment.CurrentManagedThreadId;
            _state.PrepareThreadId = 0;
            Save();
        }
        try
        {
            token.ThrowIfCancellationRequested();
            CapturedSnapshot? captured = null;
            var buildWatch = Stopwatch.StartNew();
            using (var measurements = SnapshotBuildMeasurements.Begin())
            {
                try { captured = await capture(); snapshot = captured.Document; }
                finally
                {
                    lock (_gate)
                    {
                        if (_scopeEpoch == scopeEpoch)
                        {
                            _state.CaptureWorkflowMilliseconds = Math.Round(buildWatch.Elapsed.TotalMilliseconds, 3);
                            _state.BuildMilliseconds = captured?.OwnerBuildMilliseconds ?? _state.CaptureWorkflowMilliseconds;
                            _state.BuildStages = measurements.Export();
                            if (_state.BuildMilliseconds >= 250)
                            {
                                _state.LastSlowBuildAtUnixMs = now;
                                _state.LastSlowBuildMilliseconds = _state.BuildMilliseconds;
                                _state.LastSlowBuildStages = measurements.Export();
                            }
                        }
                    }
                }
            }
            double backgroundMilliseconds = 0;
            int backgroundThreadId = 0;
            var prepareWatch = Stopwatch.StartNew();
            // Completion owns its inputs; accounting services remain on the capture thread.
            PreparedPatch prepared = await Task.Run(
                () =>
                {
                    backgroundThreadId = Environment.CurrentManagedThreadId;
                    var watch = Stopwatch.StartNew();
                    snapshot = captured.BuildDocument?.Invoke(token) ?? snapshot;
                    captured.Complete?.Invoke(token);
                    backgroundMilliseconds = Math.Round(watch.Elapsed.TotalMilliseconds, 3);
                    token.ThrowIfCancellationRequested();
                    return PreparePatch(previous, snapshot, now, previousRevision);
                }, token);
            lock (_gate)
            {
                if (_scopeEpoch != scopeEpoch) return false;
                _state.BackgroundBuildMilliseconds = backgroundMilliseconds;
                _state.BackgroundBuildThreadId = backgroundThreadId;
                _state.PrepareMilliseconds = Math.Round(prepareWatch.Elapsed.TotalMilliseconds, 3);
                _state.PrepareThreadId = prepared.ThreadId;
            }
            bool changed = prepared.HasChanges;
            long revision = prepared.Revision;
            if (changed)
            {
                lock (_gate)
                {
                    _state.PayloadBytes = prepared.Utf8.Length;
                    Save();
                }
                token.ThrowIfCancellationRequested();
                var sendWatch = Stopwatch.StartNew();
                try { await send(prepared); }
                finally
                {
                    lock (_gate)
                        if (_scopeEpoch == scopeEpoch)
                            _state.SendMilliseconds = Math.Round(sendWatch.Elapsed.TotalMilliseconds, 3);
                }
            }
            lock (_gate)
            {
                if (_scopeEpoch != scopeEpoch) return false;
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
                if (_scopeEpoch != scopeEpoch) return false;
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

    internal static PreparedPatch PreparePatch(JsonObject? previous, JsonObject next,
        long now, long previousRevision)
    {
        JsonObject patch = BuildPatch(previous, next);
        if (patch.Count == 0) return new PreparedPatch(patch, Array.Empty<byte>(), previousRevision,
            Environment.CurrentManagedThreadId);
        long revision = Math.Max(now, previousRevision + 1);
        patch["updatedAtUnixMs"] = revision;
        patch["updatedAt"] = DateTimeOffset.FromUnixTimeMilliseconds(now)
            .ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        return new PreparedPatch(patch, JsonSerializer.SerializeToUtf8Bytes(patch), revision,
            Environment.CurrentManagedThreadId);
    }

    private static string ErrorKind(Exception ex) => ex switch
    {
        HttpRequestException { StatusCode: { } status } => "http_" + (int)status,
        OperationCanceledException => "timeout",
        HttpRequestException => "network",
        _ => ex.GetType().Name
    };

    private void Save()
    {
        var watch = Stopwatch.StartNew();
        try { AtomicFileStorageService.WriteAllText(_path, JsonSerializer.Serialize(_state)); }
        finally
        {
            // This completed duration is persisted by the next ordinary checkpoint.
            _state.CheckpointMilliseconds = Math.Round(watch.Elapsed.TotalMilliseconds, 3);
            if (_state.CheckpointMilliseconds >= 250)
            {
                _state.LastSlowCheckpointAtUnixMs = _clock();
                _state.LastSlowCheckpointMilliseconds = _state.CheckpointMilliseconds;
            }
        }
    }

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
            if (before is JsonArray oldDays && pair.Value is JsonArray newDays &&
                IsPaceDaysPath(path) && PaceDaysEqual(oldDays, newDays)) continue;
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

    private static bool IsPaceDaysPath(string path)
    {
        if (path == "financialPace/days") return true;
        if (!path.StartsWith("reportsByMonth/", StringComparison.Ordinal) ||
            !path.EndsWith("/financialPace/days", StringComparison.Ordinal)) return false;
        string[] parts = path.Split('/');
        return parts.Length == 4 && BusinessCalendarService.TryParseMonthKey(parts[1], out _);
    }

    private static bool PaceDaysEqual(JsonArray before, JsonArray after)
    {
        if (before.Count != after.Count) return false;
        for (int index = 0; index < before.Count; index++)
        {
            if (before[index] is not JsonObject oldDay || after[index] is not JsonObject newDay)
            {
                if (!JsonNode.DeepEquals(before[index], after[index])) return false;
                continue;
            }
            if (oldDay.Count != newDay.Count) return false;
            foreach (var (key, value) in newDay)
            {
                if (!oldDay.TryGetPropertyValue(key, out var oldValue)) return false;
                // Keep deletions/schema changes meaningful; only a changed as-of string is metadata.
                if (key == "calculatedAt" && oldValue is JsonValue oldTime && value is JsonValue newTime &&
                    oldTime.TryGetValue<string>(out _) && newTime.TryGetValue<string>(out _)) continue;
                if (!JsonNode.DeepEquals(oldValue, value)) return false;
            }
        }
        return true;
    }
}
