using ClubTimerXbox.Services;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;

internal sealed class FirebaseSnapshotDeliveryTests
{
    private int _passed;
    public void Run()
    {
        TestAsync().GetAwaiter().GetResult();
        Console.WriteLine($"Firebase snapshot delivery: {_passed} checks passed.");
    }

    public void MeasureIdle()
    {
        string folder = Path.Combine(Path.GetTempPath(), "clubtimer-idle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var delivery = new FirebaseSnapshotDelivery(Path.Combine(folder, "delivery.json"));
            var source = new JsonObject { ["cash"] = new JsonObject { ["actualCashlessBalanceMonth"] = 34478 } };
            int writes = 0, bytes = 0;
            Task Send(JsonObject patch)
            {
                writes++; bytes += System.Text.Encoding.UTF8.GetByteCount(patch.ToJsonString());
                return Task.CompletedTask;
            }
            var clock = System.Diagnostics.Stopwatch.StartNew();
            Console.WriteLine("Local mock idle measurement started (300 seconds); no Firebase requests.");
            do
            {
                long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                source["updatedAtUnixMs"] = now;
                delivery.Request("club_2:pc", now, true);
                delivery.PublishAsync(() => (JsonObject)source.DeepClone(), Send, now).GetAwaiter().GetResult();
                System.Threading.Thread.Sleep(1000);
            } while (clock.Elapsed < TimeSpan.FromSeconds(300));
            Check(writes == 1, "real 5-minute mock idle has no redundant writes");
            Console.WriteLine($"Idle elapsed={clock.Elapsed.TotalSeconds:F1}s writes={writes} bytes={bytes}; redundant writes=0.");
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    private async Task TestAsync()
    {
        string folder = Path.Combine(Path.GetTempPath(), "clubtimer-sync-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string path = Path.Combine(folder, "delivery.json");
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var delivery = new FirebaseSnapshotDelivery(path);
            var source = JsonNode.Parse("""
                {"updatedAt":"old","updatedAtUnixMs":1,"cash":{"actualCashlessBalanceMonth":33808},
                 "places":[{"name":"TV 4","isBusy":true}],"reportsByMonth":{"September":{"total":999}}}
                """)!.AsObject();
            int writes = 0;
            var sent = new List<JsonObject>();
            Task Send(JsonObject patch) { writes++; sent.Add((JsonObject)patch.DeepClone()); return Task.CompletedTask; }
            delivery.Request("club_2:pc", now, true);
            Check(delivery.CanAttempt(now), "new request is due");
            Check(await delivery.PublishAsync(() => source, Send, now), "initial publication succeeds");
            Check(!delivery.Pending && writes == 1, "ack clears durable pending");
            Check(sent[0].ContainsKey("reportsByMonth"), "initial publication retains full history");
            long firstRevision = delivery.Revision;

            source = (JsonObject)source.DeepClone();
            source["updatedAt"] = "new clock only";
            source["updatedAtUnixMs"] = now + 10;
            delivery.Request("club_2:pc", now + 10, true);
            Check(await delivery.PublishAsync(() => source, Send, now + 10), "unchanged request acknowledged");
            Check(writes == 1 && delivery.Revision == firstRevision, "generated clocks cause no write");

            source = (JsonObject)source.DeepClone();
            source["cash"]!["actualCashlessBalanceMonth"] = 34478;
            source["places"]![0]!["isBusy"] = false;
            delivery.Request("club_2:pc", now + 20, true);
            Check(await delivery.PublishAsync(() => source, Send, now + 20), "cashless correction publishes");
            Check(sent[1]["cash/actualCashlessBalanceMonth"]!.GetValue<int>() == 34478,
                "exact cashless delta sent");
            Check(!sent[1].ContainsKey("reportsByMonth") && sent[1].ContainsKey("places"),
                "unchanged historical reports excluded, places replaced atomically");

            delivery.Request("club_2:pc", now + 30, true);
            source = (JsonObject)source.DeepClone(); source["cash"]!["actualCashlessBalanceMonth"] = 34500;
            Check(!await delivery.PublishAsync(() => source,
                _ => throw new TaskCanceledException(), now + 30), "timeout not reported as publication");
            Check(delivery.Pending && !delivery.CanAttempt(now + 31), "timeout waits for backoff");
            string diagnostic = File.ReadAllText(path);
            Check(diagnostic.Contains("timeout") && !diagnostic.Contains("34500"),
                "diagnostic saves category, not financial payload or tokens");
            var restarted = new FirebaseSnapshotDelivery(path);
            Check(restarted.Pending, "pending survives restart");
            Check(await restarted.PublishAsync(() => source, Send, now + 200000), "restart automatically resumes");
            Check(!restarted.Pending, "recovery acknowledges only successful send");

            restarted.Request("club_2:pc", now + 200010, true);
            source = (JsonObject)source.DeepClone(); source["cash"]!["actualCashlessBalanceMonth"] = 34600;
            await restarted.PublishAsync(() => source, patch =>
            {
                restarted.Request("club_2:pc", now + 200011, false);
                return Send(patch);
            }, now + 200010);
            Check(restarted.Pending, "change arriving in flight is not lost");

            restarted.Request("club_2:pc", now + 210000, true);
            source = (JsonObject)source.DeepClone(); source["cash"]!["actualCashlessBalanceMonth"] = 34700;
            Check(!await restarted.PublishAsync(() => source,
                _ => throw new HttpRequestException("not logged secret", null, HttpStatusCode.Forbidden),
                now + 210000), "permission failure remains pending");
            Check(!restarted.CanAttempt(now + 999999), "permanent failure is not polled endlessly");
            Check(!File.ReadAllText(path).Contains("not logged secret"), "exception text excluded from diagnostic");
            restarted.Request("club_2:pc", now + 220000, true);
            Check(restarted.CanAttempt(now + 220000), "explicit request can retry after corrected authorization");

            var patch = FirebaseSnapshotDelivery.BuildPatch(
                JsonNode.Parse("{\"cash\":{\"old\":2,\"zero\":5}}")!.AsObject(),
                JsonNode.Parse("{\"cash\":{\"zero\":0}}")!.AsObject());
            Check(patch.ContainsKey("cash/old") && patch["cash/old"] == null &&
                patch["cash/zero"]!.GetValue<int>() == 0, "removals and explicit zero preserved");

            // 5 minutes of local idle recalculation: no redundant application writes.
            var idle = new FirebaseSnapshotDelivery(Path.Combine(folder, "idle.json"));
            int idleWrites = 0;
            Task IdleSend(JsonObject _) { idleWrites++; return Task.CompletedTask; }
            idle.Request("club_2:pc", now, true);
            await idle.PublishAsync(() => source, IdleSend, now);
            for (int second = 1; second <= 300; second++)
            {
                long time = now + second * 1000L;
                idle.Request("club_2:pc", time, true);
                source["updatedAtUnixMs"] = time;
                await idle.PublishAsync(() => (JsonObject)source.DeepClone(), IdleSend, time);
            }
            Check(idleWrites == 1, "simulated 5-minute idle has zero redundant writes");

            var bulk = (JsonObject)source.DeepClone();
            bulk["reportsByMonth"] = new JsonObject { ["history"] = new string('x', 7000000) };
            var corrected = (JsonObject)bulk.DeepClone(); corrected["cash"]!["actualCashlessBalanceMonth"] = 34478;
            var compact = FirebaseSnapshotDelivery.BuildPatch(bulk, corrected);
            Check(compact.ToJsonString().Length < 100, "7 MB history not resent for one cashless value");
            Console.WriteLine($"Synthetic full payload={bulk.ToJsonString().Length}, correction={compact.ToJsonString().Length} bytes (ASCII).");
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    private void Check(bool ok, string message)
    {
        if (!ok) throw new InvalidOperationException("Firebase snapshot: " + message);
        _passed++;
    }
}
