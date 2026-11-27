using System;

namespace ClubTimerXbox.Models;

internal sealed record SessionPauseState(SessionPlayClock FrozenClock, DateTimeOffset? ResumedAt);

public sealed record GameSessionPauseInterval(DateTime StartedAt, DateTime? EndedAt);
