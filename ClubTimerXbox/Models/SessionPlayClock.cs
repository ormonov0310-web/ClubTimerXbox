using System;

namespace ClubTimerXbox.Models;

internal enum SessionPlayPhase
{
    Playing,
    Paused,
    Stopped
}

internal sealed record SessionPlayClock(
    Guid GameSessionId,
    SessionPlayPhase Phase,
    DateTimeOffset ChangedAt,
    TimeSpan PlayedAtChange,
    TimeSpan? PrepaidDuration)
{
    public static SessionPlayClock Start(Guid gameSessionId, DateTimeOffset now, TimeSpan? prepaidDuration = null)
    {
        if (gameSessionId == Guid.Empty)
            throw new ArgumentException("A game session ID is required.", nameof(gameSessionId));
        if (prepaidDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(prepaidDuration));

        return new(gameSessionId, SessionPlayPhase.Playing, now, TimeSpan.Zero, prepaidDuration);
    }

    public TimeSpan PlayedAt(DateTimeOffset now)
    {
        TimeSpan played = Phase == SessionPlayPhase.Playing && now > ChangedAt
            ? PlayedAtChange + (now - ChangedAt)
            : PlayedAtChange;
        return PrepaidDuration is { } duration && played > duration ? duration : played;
    }

    public TimeSpan? RemainingAt(DateTimeOffset now) =>
        PrepaidDuration is { } duration ? duration - PlayedAt(now) : null;

    public SessionPlayClock Pause(DateTimeOffset now) =>
        Phase == SessionPlayPhase.Playing && RemainingAt(now) != TimeSpan.Zero
            ? ChangePhase(SessionPlayPhase.Paused, now)
            : this;

    public SessionPlayClock Resume(DateTimeOffset now) =>
        Phase == SessionPlayPhase.Paused ? ChangePhase(SessionPlayPhase.Playing, now) : this;

    public SessionPlayClock Stop(DateTimeOffset now) =>
        Phase != SessionPlayPhase.Stopped ? ChangePhase(SessionPlayPhase.Stopped, now) : this;

    private SessionPlayClock ChangePhase(SessionPlayPhase phase, DateTimeOffset now) =>
        now < ChangedAt ? this : this with
        {
            Phase = phase,
            ChangedAt = now,
            PlayedAtChange = PlayedAt(now)
        };
}
