using System;

namespace ClubTimerXbox.Services
{
    public interface IClubClock
    {
        DateTime UtcNow { get; }

        DateTime LocalNow { get; }
    }

    public sealed class SystemClubClock : IClubClock
    {
        public static SystemClubClock Instance { get; } = new();

        private SystemClubClock()
        {
        }

        public DateTime UtcNow => DateTime.UtcNow;

        public DateTime LocalNow => BusinessCalendarService.ToClubLocal(UtcNow);
    }

    public sealed class ManualClubClock : IClubClock
    {
        public ManualClubClock(DateTime localNow)
        {
            SetLocal(localNow);
        }

        public DateTime UtcNow { get; private set; }

        public DateTime LocalNow => BusinessCalendarService.ToClubLocal(UtcNow);

        public void SetLocal(DateTime localNow)
        {
            UtcNow = BusinessCalendarService.ToUtc(localNow);
        }

        public void Advance(TimeSpan value)
        {
            UtcNow = UtcNow.Add(value);
        }
    }

    public static class ClubClock
    {
        private static readonly object Gate = new();
        private static IClubClock _current = SystemClubClock.Instance;

        public static IClubClock Current
        {
            get
            {
                lock (Gate)
                    return _current;
            }
        }

        public static IDisposable UseForTesting(IClubClock clock)
        {
            ArgumentNullException.ThrowIfNull(clock);

            lock (Gate)
            {
                IClubClock previous = _current;
                _current = clock;
                return new RestoreClockScope(previous);
            }
        }

        private sealed class RestoreClockScope : IDisposable
        {
            private readonly IClubClock _previous;
            private bool _disposed;

            public RestoreClockScope(IClubClock previous)
            {
                _previous = previous;
            }

            public void Dispose()
            {
                lock (Gate)
                {
                    if (_disposed)
                        return;

                    _current = _previous;
                    _disposed = true;
                }
            }
        }
    }
}
