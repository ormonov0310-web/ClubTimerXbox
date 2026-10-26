using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services;

internal static class ClubClosedState
{
    public static async Task<bool> DeliverAsync(Task<bool> closeEvent,
        Func<bool, Task<bool>> fullSnapshot, CancellationToken token)
    {
        bool presencePublished = await closeEvent.WaitAsync(token);
        token.ThrowIfCancellationRequested();
        bool snapshotPublished = await fullSnapshot(presencePublished).WaitAsync(token);
        return presencePublished && snapshotPublished;
    }

    public static Dictionary<string, object?> BuildPatch(PcIdentity identity,
        IReadOnlyList<ClubPlace> places, string employeeName, long nowUnixMs, long revision)
    {
        // Presence must not recalculate payroll, pace or historical cash reports.
        return new Dictionary<string, object?>
        {
            ["liveState/messageType"] = "club_state",
            ["liveState/signalType"] = "club_closed",
            ["liveState/clubId"] = identity.ClubId,
            ["liveState/clubName"] = identity.ClubName,
            ["liveState/sourceInstallationId"] = identity.InstallationId,
            ["liveState/revision"] = revision,
            ["liveState/updatedAtUnixMs"] = nowUnixMs,
            ["liveState/lastHeartbeatAtUnixMs"] = nowUnixMs,
            ["liveState/isOpen"] = false,
            ["liveState/connectionState"] = "closed",
            ["liveState/employeeName"] = employeeName,
            ["liveState/busyPlaces"] = 0,
            ["liveState/freePlaces"] = places.Count,
            ["overview/updatedAtUnixMs"] = nowUnixMs,
            ["overview/updatedAt"] = DateTimeOffset.FromUnixTimeMilliseconds(nowUnixMs)
                .ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
            ["overview/lastHeartbeatAtUnixMs"] = nowUnixMs,
            ["overview/revision"] = revision,
            ["overview/isOpen"] = false,
            ["overview/connectionState"] = "closed",
            ["overview/busyPlaces"] = 0,
            ["overview/freePlaces"] = places.Count,
            ["overview/currentEmployeeName"] = "",
            ["overview/places"] = places.Select(place => new
                { name = place.Name, type = place.Type.ToString(), isBusy = false }).ToArray()
        };
    }
}
