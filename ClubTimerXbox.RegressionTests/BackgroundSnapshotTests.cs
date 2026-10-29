using System.Text.Json;
using System.Text.Json.Nodes;
using ClubTimerXbox.Services;

internal static class BackgroundSnapshotTests
{
    public static void Run() => RunAsync().GetAwaiter().GetResult();

    public static void MeasureIdle() => MeasureIdleAsync().GetAwaiter().GetResult();

    private static async Task MeasureIdleAsync()
    {
        string folder = Path.Combine(Path.GetTempPath(), "clubtimer-background-idle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var delivery = new FirebaseSnapshotDelivery(Path.Combine(folder, "delivery.json"));
            var clock = System.Diagnostics.Stopwatch.StartNew();
            int captures = 0, completions = 0, sends = 0, bytes = 0;
            var source = new JsonObject
            {
                ["cash"] = new JsonObject { ["amount"] = 34478 },
                ["reportsByMonth"] = new JsonObject { ["fixture"] = new string('x', 7000000) }
            };
            Console.WriteLine("Background mock idle: 300 real seconds, 90s existing refresh cadence, no UI/cloud requests.");
            for (int refresh = 0; clock.Elapsed.TotalSeconds < 300;)
            {
                if (clock.Elapsed.TotalSeconds >= refresh * 90)
                {
                    refresh++;
                    long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    delivery.Request("fixture:idle", now, true);
                    Check(await delivery.PublishCapturedAsync(() =>
                    {
                        captures++;
                        var owned = (JsonObject)source.DeepClone();
                        owned["updatedAtUnixMs"] = now;
                        return new(owned, token =>
                        {
                            token.ThrowIfCancellationRequested();
                            completions++;
                            owned["journal"] = new JsonArray("same business data");
                        });
                    }, patch =>
                    {
                        sends++;
                        bytes += patch.Utf8.Length;
                        return Task.CompletedTask;
                    }, now), "real idle publication");
                }
                await Task.Delay(250);
            }
            Check(captures == 4 && completions == 4 && sends == 1 && !delivery.Pending,
                "real five-minute idle: one initial write, zero redundant writes");
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                elapsedSeconds = Math.Round(clock.Elapsed.TotalSeconds, 3), captures, completions,
                sends, bytes, redundantWrites = sends - 1,
                scope = "Owned 7MB fixture; mock transport. Existing command polling/heartbeat and phone download fan-out not measured."
            }));
        }
        finally
        {
            foreach (string file in Directory.EnumerateFiles(folder)) File.Delete(file);
            Directory.Delete(folder);
        }
    }

    private static async Task RunAsync()
    {
        PaceMetadataChecks();
        string folder = Path.Combine(Path.GetTempPath(), "clubtimer-background-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            long now = 100000;
            string path = Path.Combine(folder, "delivery.json");
            var delivery = new FirebaseSnapshotDelivery(path, () => now);
            delivery.Request("burst", now, false);
            for (int change = 1; change <= 19; change++)
                delivery.Request("burst", now + change * 100, false);
            Check(!delivery.CanAttempt(now + 1999) && delivery.CanAttempt(now + 2000),
                "continuous changes do not postpone first-event coalescing indefinitely");
            int sends = 0;
            int amount = 100;
            var sentAmounts = new List<int>();
            Task Send(FirebaseSnapshotDelivery.PreparedPatch patch)
            {
                sends++;
                if (patch.Patch["amount"] is JsonNode value) sentAmounts.Add(value.GetValue<int>());
                if (patch.Patch.ToJsonString().Contains("BackgroundBuild")) throw new Exception("Diagnostic leaked");
                return Task.CompletedTask;
            }
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            int captureThread = 0, workerThread = 0;
            delivery.Request("A", now, true);
            Task<bool> first = delivery.PublishCapturedAsync(() =>
            {
                captureThread = Environment.CurrentManagedThreadId;
                var captured = new JsonObject { ["amount"] = amount };
                return new(captured, token =>
                {
                    workerThread = Environment.CurrentManagedThreadId;
                    entered.Set();
                    if (!release.Wait(TimeSpan.FromSeconds(10), token)) throw new Exception("Worker barrier timed out");
                    captured["journal"] = new JsonArray("captured");
                });
            }, Send, now);
            Check(entered.Wait(TimeSpan.FromSeconds(10)), "worker started");
            Check(!first.IsCompleted && captureThread != workerThread, "capture returns while worker runs separately");
            amount = 200;
            delivery.Request("A", ++now, false);
            bool capturedTwice = false;
            Check(!await delivery.PublishCapturedAsync(() =>
            {
                capturedTwice = true;
                return new(new JsonObject());
            }, Send, now), "one publication/build at a time");
            Check(!capturedTwice, "busy worker does not start a second capture");
            release.Set();
            Check(await first && delivery.Pending && sends == 1, "in-flight edit remains durable pending, not acknowledged");
            Check(sentAmounts.SequenceEqual(new[] { 100 }), "completed worker sends captured value, not later live edit");
            var state = JsonSerializer.Deserialize<FirebaseSnapshotDelivery.State>(File.ReadAllText(path))!;
            Check(state.BackgroundBuildThreadId == workerThread && state.BuildThreadId == captureThread,
                "capture and background timings have separate local diagnostics");

            FirebaseSnapshotDelivery.CapturedSnapshot Capture()
            {
                int value = amount;
                var data = new JsonObject { ["amount"] = value, ["updatedAtUnixMs"] = now };
                return new(data, token =>
                {
                    token.ThrowIfCancellationRequested();
                    data["journal"] = new JsonArray("captured");
                });
            }
            now += 2000;
            Check(await delivery.PublishCapturedAsync(Capture, Send, now) && !delivery.Pending && sends == 2,
                "next generation publishes automatically after bounded coalescing");
            Check(sentAmounts.SequenceEqual(new[] { 100, 200 }), "latest value follows in the next generation");
            for (int second = 1; second <= 600; second++)
            {
                now += 1000;
                delivery.Request("A", now, true);
                Check(await delivery.PublishCapturedAsync(Capture, Send, now), "simulated idle capture");
            }
            Check(sends == 2, "ten simulated minutes: zero redundant business sends after initial/changed states");

            delivery.Request("A", ++now, true);
            amount = 300;
            Check(!await delivery.PublishCapturedAsync(Capture,
                _ => throw new IOException("fixture offline"), now), "offline failure not acknowledged");
            Check(delivery.Pending && !delivery.CanAttempt(now + 1), "offline keeps backoff and durable pending");
            delivery = new FirebaseSnapshotDelivery(path, () => now);
            Check(delivery.Pending, "pending survives process restart");
            now += 200000;
            Check(await delivery.PublishCapturedAsync(Capture, Send, now) && !delivery.Pending,
                "reconnect automatically rebuilds and acknowledges success");

            // A -> B -> A must not accept the old A work just because its scope text matches.
            entered.Reset(); release.Reset();
            delivery.Request("A", ++now, true);
            var old = delivery.PublishCapturedAsync(() => new(new JsonObject { ["amount"] = -1 }, token =>
            {
                entered.Set();
                if (!release.Wait(TimeSpan.FromSeconds(10), token)) throw new Exception("Scope barrier timed out");
            }), Send, now);
            Check(entered.Wait(TimeSpan.FromSeconds(10)), "scope worker started");
            delivery.Request("B", ++now, true);
            delivery.Request("A", ++now, true);
            int priorSends = sends;
            release.Set();
            Check(!await old && sends == priorSends && delivery.Pending, "old binding epoch discarded before send");
            Check(await delivery.PublishCapturedAsync(Capture, Send, now), "new scope can publish next");

            using var cancel = new CancellationTokenSource();
            delivery.Request("A", ++now, true);
            Check(!await delivery.PublishCapturedAsync(() => new(new JsonObject { ["amount"] = 400 }, token =>
            {
                cancel.Cancel(); token.ThrowIfCancellationRequested();
            }), Send, now, cancel.Token), "closing cancels detached computation");
            Check(delivery.Pending && sends == priorSends + 1, "canceled result never sent or acknowledged");
            Console.WriteLine("Background snapshots: single worker, owned capture, in-flight edits, 10-minute simulated idle, offline/restart, binding epoch and close cancellation passed; mocked sends only.");
        }
        finally
        {
            foreach (string file in Directory.EnumerateFiles(folder)) File.Delete(file);
            Directory.Delete(folder);
        }
    }

    private static void PaceMetadataChecks()
    {
        var previous = JsonNode.Parse("""
            {"financialPace":{"days":[{"businessDateKey":"2026-10-01","calculatedAt":"old",
              "gameRevenue":120,"totalExpense":30,"isClosed":false,"timeline":[]}]},
             "reportsByMonth":{"2026-10":{"financialPace":{"days":[{"calculatedAt":"old","gameRevenue":120}]}}},
             "unrelated":{"days":[{"calculatedAt":"old","amount":1}]}}
            """)!.AsObject();
        var next = (JsonObject)previous.DeepClone();
        next["financialPace"]!["days"]![0]!["calculatedAt"] = "new";
        next["reportsByMonth"]!["2026-10"]!["financialPace"]!["days"]![0]!["calculatedAt"] = "new";
        Check(FirebaseSnapshotDelivery.BuildPatch(previous, next).Count == 0,
            "pace source-as-of strings alone cause no current/history write");
        foreach (string key in new[] { "gameRevenue", "totalExpense", "isClosed", "timeline" })
        {
            var changed = (JsonObject)next.DeepClone();
            var day = changed["financialPace"]!["days"]![0]!;
            day[key] = key switch
            {
                "isClosed" => JsonValue.Create(true),
                "timeline" => new JsonArray(new JsonObject { ["createdAt"] = "new observation" }),
                _ => JsonValue.Create(0)
            };
            var patch = FirebaseSnapshotDelivery.BuildPatch(previous, changed);
            Check(patch.ContainsKey("financialPace/days") &&
                patch["financialPace/days"]![0]!["calculatedAt"]!.GetValue<string>() == "new",
                "meaningful pace change includes its source as-of: " + key);
        }
        next["financialPace"]!["days"]![0]!.AsObject().Remove("calculatedAt");
        Check(FirebaseSnapshotDelivery.BuildPatch(previous, next).ContainsKey("financialPace/days"),
            "pace metadata removal remains a schema change");
        next = (JsonObject)previous.DeepClone();
        next["unrelated"]!["days"]![0]!["calculatedAt"] = "new";
        Check(FirebaseSnapshotDelivery.BuildPatch(previous, next).ContainsKey("unrelated/days"),
            "same field name elsewhere is never globally ignored");
    }

    private static void Check(bool condition, string label)
    {
        if (!condition) throw new Exception("Background snapshot: " + label);
    }
}
