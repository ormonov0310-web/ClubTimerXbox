using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace ClubTimerXbox.Services;

internal sealed class SnapshotBuildMeasurements : IDisposable
{
    private static readonly AsyncLocal<SnapshotBuildMeasurements?> Current = new();
    private readonly SnapshotBuildMeasurements? _previous;
    private readonly Dictionary<string, Stage> _stages = new(StringComparer.Ordinal);
    private readonly int _threadId = Environment.CurrentManagedThreadId;
    private int _active = 1;

    internal sealed record Stage(int Calls, double Milliseconds, long AllocatedBytes);

    private SnapshotBuildMeasurements()
    {
        _previous = Current.Value;
        Current.Value = this;
    }

    public static SnapshotBuildMeasurements Begin() => new();
    public static Measurement Measure(string name)
    {
        var owner = Current.Value;
        return new Measurement(owner?.CanRecord == true ? owner : null, name);
    }
    public Dictionary<string, Stage> Export() => new(_stages, StringComparer.Ordinal);
    public void Dispose()
    {
        Volatile.Write(ref _active, 0);
        Current.Value = _previous;
    }

    private bool CanRecord => Volatile.Read(ref _active) != 0 &&
        Environment.CurrentManagedThreadId == _threadId;

    private void Record(string name, long start, long allocated)
    {
        if (!CanRecord) return;
        if (!_stages.TryGetValue(name, out var old))
        {
            if (_stages.Count >= 16) return;
            old = new Stage(0, 0, 0);
        }
        _stages[name] = new Stage(old.Calls + 1,
            Math.Round(old.Milliseconds + Stopwatch.GetElapsedTime(start).TotalMilliseconds, 3),
            old.AllocatedBytes + Math.Max(0, GC.GetAllocatedBytesForCurrentThread() - allocated));
    }

    internal readonly struct Measurement : IDisposable
    {
        private readonly SnapshotBuildMeasurements? _owner;
        private readonly string _name;
        private readonly long _start;
        private readonly long _allocated;

        internal Measurement(SnapshotBuildMeasurements? owner, string name)
        {
            _owner = owner;
            _name = name;
            _start = owner == null ? 0 : Stopwatch.GetTimestamp();
            _allocated = owner == null ? 0 : GC.GetAllocatedBytesForCurrentThread();
        }

        public void Dispose() => _owner?.Record(_name, _start, _allocated);
    }
}
