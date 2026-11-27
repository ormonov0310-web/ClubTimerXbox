using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services;

internal static class SessionPausePolicy
{
    public static bool CanChange(ClubPlace place) =>
        place.IsBusy && !place.IsCalculating && !place.IsTimeExpiredAwaitingAcknowledgement;

    public static bool TryPause(ClubPlace place, Guid gameId, DateTime now)
    {
        if (!CanChange(place) || place.IsPaused || place.StartTime is not { } start ||
            gameId == Guid.Empty || now < start || (!place.IsOpenMode && place.RemainingSeconds <= 0))
            return false;

        var duration = place.IsOpenMode ? (TimeSpan?)null : TimeSpan.FromSeconds(place.RemainingSeconds);
        var frozen = SessionPlayClock.Start(gameId, new DateTimeOffset(start), duration).Pause(new DateTimeOffset(now));
        if (frozen.Phase != SessionPlayPhase.Paused)
            return false;

        SetPaused(place, new SessionPauseState(frozen, null));
        return true;
    }

    public static bool TryResume(ClubPlace place, Guid gameId, DateTime now)
    {
        if (!CanChange(place) || place.PauseState is not { ResumedAt: null } state ||
            state.FrozenClock.GameSessionId != gameId)
            return false;

        var resumed = state.FrozenClock.Resume(new DateTimeOffset(now));
        if (resumed.Phase != SessionPlayPhase.Playing)
            return false;

        // Move the legacy segment origin, not its price or prepaid balance.
        place.StartTime = now - resumed.PlayedAtChange;
        if (resumed.PrepaidDuration is { } duration)
            place.RemainingSeconds = (int)duration.TotalSeconds;
        place.PauseState = state with { ResumedAt = resumed.ChangedAt };
        return true;
    }

    public static void Restore(ClubPlace place, SavedActivePlace saved, Guid? currentGameId)
    {
        if (saved.PauseState is not { } state)
            return;
        var clock = state.FrozenClock;
        if (!place.IsBusy || clock is null || clock.GameSessionId == Guid.Empty ||
            currentGameId != clock.GameSessionId || saved.GameSessionId != currentGameId ||
            clock.Phase != SessionPlayPhase.Paused || clock.PlayedAtChange < TimeSpan.Zero ||
            (place.IsOpenMode ? clock.PrepaidDuration is not null :
                clock.PrepaidDuration is not { } duration || duration <= clock.PlayedAtChange ||
                duration > TimeSpan.FromSeconds(int.MaxValue)) ||
            state.ResumedAt < clock.ChangedAt)
            throw new InvalidDataException("Saved pause does not match its active game.");

        place.PauseState = state;
        if (state.ResumedAt is null)
        {
            SetPaused(place, state);
            // If Stop was displaying its modal at shutdown, keep the existing pause.
            place.IsCalculating = false;
        }
    }

    public static void RestorePrepaidCountdown(ClubPlace place, DateTime lastSavedAt, DateTime now)
    {
        if (!place.IsBusy || place.IsOpenMode || place.IsPaused || place.IsCalculating ||
            place.IsTimeExpiredAwaitingAcknowledgement)
            return;

        int offlineSeconds = (int)Math.Clamp((now - lastSavedAt).TotalSeconds, 0, int.MaxValue);
        place.RemainingSeconds = Math.Max(0, Math.Max(0, place.RemainingSeconds) - offlineSeconds);
        place.StartTime = place.RemainingSeconds > 0 ? now : null;
    }

    public static TimeSpan OpenSegmentTime(ClubPlace place, DateTime now) =>
        place.PauseState is { ResumedAt: null } state
            ? state.FrozenClock.PlayedAtChange
            : place.StartTime is { } start && now > start ? now - start : TimeSpan.Zero;

    public static int OpenPrice(ClubPlace place, DateTime now) =>
        TariffService.CalculateOpenModePrice(place.AccruedAmountBeforeCurrentSegment,
            place.ActivePricePerMinute, OpenSegmentTime(place, now));

    public static int GetFrozenPrepaidSeconds(SessionPauseState state)
    {
        var clock = state.FrozenClock;
        return (int)Math.Ceiling((clock.PrepaidDuration.GetValueOrDefault() - clock.PlayedAtChange).TotalSeconds);
    }

    private static void SetPaused(ClubPlace place, SessionPauseState state)
    {
        place.PauseState = state;
        place.StartTime = null;
        if (!place.IsOpenMode)
            place.RemainingSeconds = GetFrozenPrepaidSeconds(state);
    }

    public static bool RecordInterval(GameSessionLogItem game, SessionPauseState state)
    {
        if (game.Id != state.FrozenClock.GameSessionId)
            throw new InvalidOperationException("Pause belongs to a different game.");
        var interval = new GameSessionPauseInterval(
            BusinessCalendarService.ToClubLocal(state.FrozenClock.ChangedAt.UtcDateTime),
            state.ResumedAt is { } end ? BusinessCalendarService.ToClubLocal(end.UtcDateTime) : null);

        var intervals = game.PlayPauses ??= [];
        if (intervals.Count > 0 && intervals[^1].StartedAt == interval.StartedAt)
        {
            if (intervals[^1] == interval || intervals[^1].EndedAt.HasValue && interval.EndedAt is null)
                return false;
            intervals[^1] = interval;
            return true;
        }
        if (intervals.Count > 0 && (intervals[^1].EndedAt is not { } previousEnd || previousEnd > interval.StartedAt))
            throw new InvalidDataException("Overlapping game pauses.");
        intervals.Add(interval);
        return true;
    }

    public static IEnumerable<(DateTime Start, DateTime End)> ActiveIntervals(
        DateTime start, DateTime end, IReadOnlyList<GameSessionPauseInterval>? pauses)
    {
        if (end <= start)
            yield break;
        if (pauses is not null)
        {
            foreach (var pause in pauses.OrderBy(item => item.StartedAt))
            {
                if (pause.StartedAt >= end)
                    break;
                if (pause.StartedAt > start)
                    yield return (start, pause.StartedAt);
                if (pause.EndedAt is not { } resume || resume >= end)
                    yield break;
                if (resume > start)
                    start = resume;
            }
        }
        if (end > start)
            yield return (start, end);
    }
}
