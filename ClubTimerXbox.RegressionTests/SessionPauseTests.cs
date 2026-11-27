using System.Diagnostics;
using ClubTimerXbox.Models;
using ClubTimerXbox.Services;

internal static class SessionPauseTests
{
    private static readonly DateTime BeforePause = new(2026, 11, 24, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime AfterRestart = BeforePause.AddHours(8);
    private const int PrepaidSeconds = 300;
    private const int PrepaidCharge = 10;

    public static void Run()
    {
        int checks = 0;
        void Check(bool condition, string message)
        {
            Require(condition, message);
            checks++;
        }

        string directory = Path.Combine(Path.GetTempPath(), "clubtimer-pause-" + Guid.NewGuid().ToString("N"));
        string file = Path.Combine(directory, "active_sessions.json");
        try
        {
            var saved = new List<SavedActivePlace>();
            foreach (bool open in new[] { false, true })
            {
                var gameId = Guid.NewGuid();
                var place = new ClubPlace
                {
                    Name = open ? "TV open" : "TV prepaid", IsBusy = true, IsOpenMode = open,
                    StartTime = BeforePause.AddSeconds(-61.25), RemainingSeconds = open ? 0 : PrepaidSeconds,
                    PaidAmount = open ? 0 : PrepaidCharge, PrepaidCashAmount = open ? 0 : 4,
                    PrepaidMBankAmount = open ? 0 : 6,
                    ActivePricePerMinute = open ? 3.5 : 2,
                    AccruedAmountBeforeCurrentSegment = open ? 30 : 0
                };
                var before = Measure(place, BeforePause);
                Check(SessionPausePolicy.TryPause(place, gameId, BeforePause) && place.IsPaused && place.IsBusy,
                    "running game becomes paused without releasing the TV");
                Check(Measure(place, BeforePause.AddHours(4)) == before && place.StartTime is null,
                    "elapsed hours do not change the visible clock or amount");
                Check(!SessionPausePolicy.TryPause(place, gameId, BeforePause.AddHours(1)) &&
                    !SessionPausePolicy.TryResume(place, Guid.NewGuid(), BeforePause.AddHours(1)) &&
                    !SessionPenaltyPolicy.TryApply(place, 5, BeforePause, out _),
                    "duplicate pause, wrong game ID and penalty while paused are rejected");
                saved.Add(new SavedActivePlace
                {
                    Name = place.Name, IsBusy = place.IsBusy, IsOpenMode = place.IsOpenMode,
                    IsCalculating = !open, // Simulate a paused prepaid game with Stop's dialog still open.
                    PauseState = place.PauseState, GameSessionId = gameId, StartTime = place.StartTime,
                    RemainingSeconds = place.RemainingSeconds, PaidAmount = place.PaidAmount,
                    PrepaidCashAmount = place.PrepaidCashAmount, PrepaidMBankAmount = place.PrepaidMBankAmount,
                    ActivePricePerMinute = place.ActivePricePerMinute, LastSavedAt = BeforePause,
                    AccruedAmountBeforeCurrentSegment = place.AccruedAmountBeforeCurrentSegment
                });
            }

            ActiveSessionStorageService.SaveTo(file, saved, flushToDisk: true);
            Check(File.ReadAllText(file).Contains("PauseState", StringComparison.Ordinal),
                "production-format active file includes pause checkpoint");
            Check(File.Exists(Path.Combine(directory, "session-storage-v1.required")),
                "a pause without any deferred ledger also establishes a compatibility requirement");
            Check(RunFreshProcess(file).Contains("Restart probe passed for both modes", StringComparison.Ordinal),
                "separate process restores both modes and resumes the same saved game clocks");

            var free = new ClubPlace { IsBusy = true, IsOpenMode = true, StartTime = BeforePause.AddSeconds(-59.5), ActivePricePerMinute = 2 };
            Guid freeId = Guid.NewGuid();
            Check(SessionPausePolicy.TryPause(free, freeId, BeforePause) &&
                SessionPausePolicy.OpenPrice(free, AfterRestart) == 0 &&
                SessionPausePolicy.TryResume(free, freeId, AfterRestart) &&
                SessionPausePolicy.OpenPrice(free, AfterRestart.AddMilliseconds(499)) == 0 &&
                SessionPausePolicy.OpenPrice(free, AfterRestart.AddMilliseconds(500)) == 5,
                "resume retains partial seconds and does not grant another free minute");

            var expired = new ClubPlace { IsBusy = true, StartTime = BeforePause.AddMinutes(-5), RemainingSeconds = 300 };
            Check(!SessionPausePolicy.TryPause(expired, Guid.NewGuid(), BeforePause),
                "elapsed prepaid tariff cannot be paused instead of expiring");
            VerifyNightIntervals(Check);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        Console.WriteLine($"Session pause: {checks} checks and fresh-process restore passed.");
    }

    public static void ProbeAfterRestart(string file)
    {
        var checkpoints = ActiveSessionStorageService.LoadFrom(file);
        Require(checkpoints.Count == 2, "read both saved TV records");
        foreach (var saved in checkpoints)
        {
            var place = new ClubPlace
            {
                Name = saved.Name, IsBusy = saved.IsBusy, IsOpenMode = saved.IsOpenMode,
                IsCalculating = saved.IsCalculating, StartTime = saved.StartTime,
                RemainingSeconds = saved.RemainingSeconds, PaidAmount = saved.PaidAmount,
                PrepaidCashAmount = saved.PrepaidCashAmount, PrepaidMBankAmount = saved.PrepaidMBankAmount,
                ActivePricePerMinute = saved.ActivePricePerMinute,
                AccruedAmountBeforeCurrentSegment = saved.AccruedAmountBeforeCurrentSegment
            };
            Guid gameId = saved.GameSessionId!.Value;
            SessionPausePolicy.Restore(place, saved, gameId);
            SessionPausePolicy.RestorePrepaidCountdown(place, saved.LastSavedAt, AfterRestart);
            var expected = saved.IsOpenMode ? (2, 40) : (239, 2);
            Require(place.IsBusy && place.IsPaused && !place.IsCalculating && place.StartTime is null &&
                Measure(place, AfterRestart) == expected && Measure(place, AfterRestart.AddDays(1)) == expected,
                "after eight offline hours, pause still freezes both time and cost");
            Require(SessionPausePolicy.TryResume(place, gameId, AfterRestart) && !place.IsPaused &&
                place.PauseState!.FrozenClock.GameSessionId == gameId && Measure(place, AfterRestart) == expected,
                "resume starts the same game from the frozen position without extra charges");
            Require(Measure(place, AfterRestart.AddMinutes(2)) == (saved.IsOpenMode ? (4, 45) : (119, 6)),
                "only time played after resuming changes the remaining time and charge");
            Require(place.PaidAmount == saved.PaidAmount && place.PrepaidCashAmount == saved.PrepaidCashAmount &&
                place.PrepaidMBankAmount == saved.PrepaidMBankAmount, "original prepaid receipt split is unchanged");
            var journal = new GameSessionLogItem { Id = gameId };
            Require(SessionPausePolicy.RecordInterval(journal, saved.PauseState!) &&
                SessionPausePolicy.RecordInterval(journal, place.PauseState!) &&
                !SessionPausePolicy.RecordInterval(journal, place.PauseState!) &&
                journal.PlayPauses is [{ EndedAt: not null }],
                "journal recovery can replay the same pause and resume without a duplicate interval");
        }
        Console.WriteLine("Restart probe passed for both modes (eight hours offline).");
    }

    private static void VerifyNightIntervals(Action<bool, string> check)
    {
        DateTime day = new(2026, 11, 24, 6, 0, 0);
        DateTime oneAm = day.AddHours(19);
        var game = new GameSessionLogItem
        {
            PlaceName = "TV 1", StartedAt = oneAm.AddMinutes(-30),
            PlayPauses = [new(oneAm.AddMinutes(-8), null)]
        };
        var shifts = new[] { new ShiftLogItem { EmployeeName = "Fixture", StartedAt = day } };
        check(LateActiveSessionRewardPolicy.FindFirstReward([game], shifts, day, day.AddDays(1), oneAm.AddHours(1)) is null,
            "an active pause spanning 01:00 does not qualify for a night award");
        game.PlayPauses[0] = game.PlayPauses[0] with { EndedAt = oneAm.AddMinutes(15) };
        check(LateActiveSessionRewardPolicy.FindFirstReward([game], shifts, day, day.AddDays(1), oneAm.AddHours(1))
                    ?.QualifiedAt == oneAm.AddMinutes(45),
            "night award uses actual uninterrupted play after resuming");
        check(SessionPausePolicy.ActiveIntervals(game.StartedAt, oneAm.AddHours(1), game.PlayPauses)
                    .Sum(interval => (interval.End - interval.Start).TotalMinutes) == 67,
            "nightly active duration excludes the 23 paused minutes");
    }

    private static (int Time, int Amount) Measure(ClubPlace place, DateTime now) => place.IsOpenMode
        ? (TariffService.CalculateOpenModePlayedMinutes(SessionPausePolicy.OpenSegmentTime(place, now)),
            SessionPausePolicy.OpenPrice(place, now))
        : (SessionPenaltyPolicy.GetPrepaidRemainingSeconds(place, now),
            SessionPenaltyPolicy.GetPrepaidUsedAmount(place, now));

    private static string RunFreshProcess(string file)
    {
        string executable = Environment.ProcessPath!;
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            info.ArgumentList.Add(typeof(SessionPauseTests).Assembly.Location);
        info.ArgumentList.Add("--probe-session-pause");
        info.ArgumentList.Add(file);
        using var process = Process.Start(info)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(15_000))
        {
            process.Kill();
            process.WaitForExit();
            throw new TimeoutException("Pause restart probe timed out.");
        }
        Require(process.ExitCode == 0, stderr.GetAwaiter().GetResult());
        return stdout.GetAwaiter().GetResult();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException("Session pause: " + message);
    }
}
