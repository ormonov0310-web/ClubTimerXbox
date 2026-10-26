using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClubTimerXbox.Models;
using ClubTimerXbox.Services;

internal static class ClubClosedDeliveryTests
{
    public static void Run()
    {
        var places = new List<ClubPlace>
        {
            new() { Name = "TV 1", IsBusy = true, PaidAmount = 150, RemainingSeconds = 100 },
            new() { Name = "TV 2", IsBusy = false }
        };
        var identity = new PcIdentity { ClubId = "fixture", ClubName = "Fixture", InstallationId = "fixture-pc" };
        long now = new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        var closed = ClubClosedState.BuildPatch(identity, places, "Fixture employee", now, now + 1);
        Equal(false, closed["liveState/isOpen"]);
        Equal("closed", closed["liveState/connectionState"]);
        Equal(0, closed["liveState/busyPlaces"]);
        Equal(2, closed["liveState/freePlaces"]);
        Equal(false, closed["overview/isOpen"]);
        Equal(0, closed["overview/busyPlaces"]);
        Equal(true, places[0].IsBusy);
        Equal(150, places[0].PaidAmount);
        Equal(100, places[0].RemainingSeconds);
        if (closed.Keys.Any(key => key.Contains("cash", StringComparison.OrdinalIgnoreCase) ||
                                   key.Contains("salary", StringComparison.OrdinalIgnoreCase)))
            throw new Exception("Close presence must not recalculate or replace financial data");

        var record = new { id = "closed_fixture", type = "club_closed", sourceInstallationId = "fixture-pc" };
        var write = FirebaseEventService.BuildEventWrite("fixture", "closed_fixture", record, closed);
        Equal(HttpMethod.Patch, write.Method);
        Equal("clubs/fixture", write.Path);
        var packet = JsonSerializer.SerializeToNode(write.Data)!.AsObject();
        if (packet["events/closed_fixture"] == null || packet["liveState/isOpen"]!.GetValue<bool>())
            throw new Exception("Closed event and closed presence must be in the same request");

        var server = JsonNode.Parse("""
            {"liveState":{"isOpen":true,"busyPlaces":2,"gamesToday":1234,"financialPacePercent":95},
             "overview":{"cash":{"gamesToday":1234}},"current":{"cash":{"balance":34478}},"events":{}}
            """)!.AsObject();
        var initial = (JsonObject)server.DeepClone();
        Apply(server, packet);
        Equal(false, server["liveState"]!["isOpen"]!.GetValue<bool>());
        Equal(0, server["liveState"]!["busyPlaces"]!.GetValue<int>());
        Equal(1234, server["liveState"]!["gamesToday"]!.GetValue<int>());
        Equal(95, server["liveState"]!["financialPacePercent"]!.GetValue<int>());
        if (!JsonNode.DeepEquals(initial["current"], server["current"]) ||
            !JsonNode.DeepEquals(initial["overview"]!["cash"], server["overview"]!["cash"]))
            throw new Exception("Close packet changed cash/history");
        Equal("club_closed", server["events"]!["closed_fixture"]!["type"]!.GetValue<string>());

        long later = now + 4 * 60 * 1000;
        bool lost = server["liveState"]!["isOpen"]!.GetValue<bool>() &&
            later - server["liveState"]!["lastHeartbeatAtUnixMs"]!.GetValue<long>() > 3 * 60 * 1000;
        Equal(false, lost);
        Apply(server, packet);
        Equal(1, server["events"]!.AsObject().Count);

        // Old durable outbox records carry no presence patch into a new process.
        var replay = FirebaseEventService.BuildEventWrite("fixture", "closed_fixture", record, null);
        Equal(HttpMethod.Put, replay.Method);
        Equal("clubs/fixture/events/closed_fixture", replay.Path);
        if (JsonSerializer.Serialize(replay.Data).Contains("isOpen"))
            throw new Exception("Historical close replay can close a new session");
        if (closed.Keys.Any(key => key.StartsWith("events/", StringComparison.Ordinal)))
            throw new Exception("Packet builder mutated shared presence");

        DeliveryOrdering().GetAwaiter().GetResult();
        Console.WriteLine("Club close delivery: atomic packet, failure isolation, history and replay checks passed; fake server only.");
    }

    private static async Task DeliveryOrdering()
    {
        var close = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        bool snapshotStarted = false;
        var delivery = ClubClosedState.DeliverAsync(close.Task, presencePublished =>
        {
            Equal(true, presencePublished);
            snapshotStarted = true;
            return Task.FromResult(false);
        }, CancellationToken.None);
        Equal(false, snapshotStarted);
        close.SetResult(true);
        Equal(false, await delivery);
        Equal(true, snapshotStarted);

        using var deadline = new CancellationTokenSource();
        var full = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        delivery = ClubClosedState.DeliverAsync(Task.FromResult(true), _ => full.Task, deadline.Token);
        deadline.Cancel();
        bool canceled = false;
        try { await delivery; }
        catch (OperationCanceledException) { canceled = true; }
        Equal(true, canceled);

        bool retryPresence = false;
        bool complete = await ClubClosedState.DeliverAsync(Task.FromResult(false), alreadyPublished =>
        {
            retryPresence = !alreadyPublished;
            return Task.FromResult(true);
        }, CancellationToken.None);
        Equal(true, retryPresence);
        Equal(false, complete);
    }

    private static void Apply(JsonObject target, JsonObject patch)
    {
        foreach (var pair in patch)
        {
            string[] path = pair.Key.Split('/');
            JsonObject parent = target;
            foreach (string segment in path.SkipLast(1))
            {
                if (parent[segment] is not JsonObject child)
                {
                    child = new JsonObject();
                    parent[segment] = child;
                }
                parent = child;
            }
            parent[path[^1]] = pair.Value?.DeepClone();
        }
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception($"Expected {expected}, got {actual}");
    }
}
