using ClubTimerXbox;
using ClubTimerXbox.Services;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static class SalikhEmployeeRecoveryTests
{
    internal static void Run()
    {
        int checks = 0;
        void Check(bool condition, string message)
        {
            checks++;
            if (!condition) throw new InvalidOperationException("Employee recovery: " + message);
        }
        Dictionary<string, JsonNode> Fixture() => JsonNode.Parse(File.ReadAllBytes(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "SalikhEmployeeRecovery.json")))!.AsObject()
            .ToDictionary(pair => pair.Key, pair => pair.Value!.DeepClone(), StringComparer.Ordinal);
        JsonObject Find(JsonNode array, string id) => array.AsArray().OfType<JsonObject>().Single(row =>
            row["Id"]!.GetValue<string>() == id);
        void Reject(Action<Dictionary<string, JsonNode>> change, string message)
        {
            var input = Fixture();
            change(input);
            string before = JsonSerializer.Serialize(input);
            bool rejected = false;
            try { SalikhEmployeeRecoveryPlan.Build(input); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected, message);
            Check(JsonSerializer.Serialize(input) == before, "failed plan does not mutate caller inputs");
        }

        var source = Fixture();
        string sourceBefore = JsonSerializer.Serialize(source);
        var output = SalikhEmployeeRecoveryPlan.Build(source);
        Check(output.Count == 7, "seven business stores corrected");
        Check(JsonSerializer.Serialize(source) == sourceBefore, "successful plan owns its inputs");
        var after = source.ToDictionary(pair => pair.Key,
            pair => output.GetValueOrDefault(pair.Key, pair.Value), StringComparer.Ordinal);
        Check(SalikhEmployeeRecoveryPlan.Build(after).Count == 0, "second plan is semantically empty");
        Check(after["employees.json"].ToJsonString() == source["employees.json"].ToJsonString(), "roster and PINs not edited");
        Check(after["business_accounting_ledger.json"].ToJsonString() == source["business_accounting_ledger.json"].ToJsonString(),
            "July payroll and all existing payroll transactions untouched");
        Check(after["employee_payout_transactions.json"].ToJsonString() == source["employee_payout_transactions.json"].ToJsonString(),
            "payouts not repeated or changed");
        Check(!SalikhEmployeeRecoveryPlan.Files.Contains("employee_debts.json"), "debts and high-water cursors not written");
        JsonObject firstSale = Find(after["logs.json"]["GameSessions"]!, "0180a711-329b-44f4-aaa6-1b4d0315df17")
            ["SaleLines"]![0]!.AsObject();
        Check(firstSale["CreatedByEmployeeName"]!.GetValue<string>() == "\u0422\u0435\u0441\u0442", "Test creator preserved");
        Check(firstSale["PaidByEmployeeName"]!.GetValue<string>() == SalikhEmployeeRecoveryPlan.Name, "paid role restored");
        Check(firstSale["DebtResponsibleEmployeeName"]!.GetValue<string>() == SalikhEmployeeRecoveryPlan.Name,
            "accepted product responsibility restored");
        JsonObject oldRating = Find(after["employee_rating.json"]["Events"]!, SalikhEmployeeRecoveryPlan.RatingId);
        JsonObject rating = Find(after["employee_rating.json"]["Events"]!, SalikhEmployeeRecoveryPlan.ReplacementRatingId);
        Check(oldRating["Status"]!.GetValue<int>() == 2, "wrong derived rating cancelled, not deleted");
        Check(rating["TargetPercent"]!.GetValue<int>() == 105, "new employee base used, not old target 79");
        Check(JsonNode.DeepEquals(rating["EffectiveFrom"], oldRating["EffectiveFrom"]) &&
              JsonNode.DeepEquals(rating["ScheduledUntil"], oldRating["ScheduledUntil"]), "original effective interval retained");
        Check(rating["SourceId"]!.GetValue<string>() == SalikhEmployeeRecoveryPlan.RewardPrefix + SalikhEmployeeRecoveryPlan.EmployeeId,
            "new reward uses source deduplication key");
        var beforeTime = source["salary_work_time.json"]["Items"]!.AsArray().OfType<JsonObject>()
            .Single(row => row["EmployeeName"]!.GetValue<string>() == SalikhEmployeeRecoveryPlan.Name && row["MonthKey"]!.GetValue<string>() == "2026-11");
        var afterTime = after["salary_work_time.json"]["Items"]!.AsArray().OfType<JsonObject>()
            .Single(row => row["EmployeeName"]!.GetValue<string>() == SalikhEmployeeRecoveryPlan.Name && row["MonthKey"]!.GetValue<string>() == "2026-11");
        Check(JsonNode.DeepEquals(beforeTime, afterTime), "new employee checkpoint not summed or re-anchored");
        foreach (string file in new[] { "cash_records.json", "payments.json", "cash_acceptance_history.json", "cash_reconciliation.json" })
        {
            var beforeMoney = source[file].DeepClone();
            var afterMoney = after[file].DeepClone();
            StripNames(beforeMoney);
            StripNames(afterMoney);
            Check(JsonNode.DeepEquals(beforeMoney, afterMoney), "only identity fields change in " + file);
        }
        Check(source["cash_records.json"].AsArray().Count == after["cash_records.json"].AsArray().Count,
            "no new cash record created");
        foreach (JsonObject row in source["employee_rating.json"]["Events"]!.AsArray().OfType<JsonObject>())
        {
            string id = row["Id"]!.GetValue<string>();
            if (id != SalikhEmployeeRecoveryPlan.RatingId)
                Check(JsonNode.DeepEquals(row, Find(after["employee_rating.json"]["Events"]!, id)),
                    "unrelated manual and time ratings unchanged");
        }
        var extensionCase = Fixture();
        extensionCase["cash_records.json"]![0]!["FutureSchema"] = new JsonObject { ["Value"] = 19.5 };
        var extensionPlan = SalikhEmployeeRecoveryPlan.Build(extensionCase);
        Check(JsonNode.DeepEquals(extensionCase["cash_records.json"]![0]!["FutureSchema"],
            extensionPlan["cash_records.json"]![0]!["FutureSchema"]), "unknown schema fields retained");

        Reject(data => Find(data["cash_records.json"], "05b0107b-71d7-4223-b640-72999bf0a0e3")["Amount"] = 22,
            "changed payout rejected");
        Reject(data => data["logs.json"]["Shifts"]!.AsArray().Add(data["logs.json"]["Shifts"]![0]!.DeepClone()),
            "duplicate shift rejected");
        Reject(data => Find(data["payments.json"], "26dc9bb6-af0c-42e6-9308-1010ac243e2a")["CashAmount"] = 0,
            "mixed payment channels guarded");
        Reject(data => data["business_accounting_ledger.json"]["Months"]!["2026-11"] = new JsonObject { ["IsClosed"] = true },
            "closed month rejected");
        Reject(data => data["business_accounting_ledger.json"]["CloseJournal"]!["pending"] =
            new JsonObject { ["MonthKey"] = "2026-11" }, "closing month rejected");
        Reject(data => Find(data["employee_rating.json"]["Events"]!, SalikhEmployeeRecoveryPlan.RatingId)["Status"] = 1,
            "manually resolved rating rejected");
        Reject(data => data["salary_work_time.json"]["Items"]![0]!["CarriedHours"] = 10,
            "manual recovered time rejected instead of double counted");
        Reject(data => Find(data["logs.json"]["GameSessions"]!, "0180a711-329b-44f4-aaa6-1b4d0315df17")
            ["SaleLines"]![0]!["PaidByEmployeeId"] = "another-id", "permanent identity mismatch rejected");
        Reject(data => data["cash_records.json"].AsArray().Add(new JsonObject
        {
            ["Id"] = "unreviewed", ["CreatedAt"] = "2026-11-20T12:00:00", ["EmployeeName"] = SalikhEmployeeRecoveryPlan.FormerName
        }), "unknown November operation rejected");

        string root = Path.Combine(Path.GetTempPath(), "ClubTimer-employee-recovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string WriteCase(string name, string club = "club_2", string? installation = null)
            {
                string path = Path.Combine(root, name);
                Directory.CreateDirectory(path);
                foreach (var pair in Fixture())
                    File.WriteAllText(Path.Combine(path, pair.Key), pair.Value.ToJsonString());
                File.WriteAllText(Path.Combine(path, "pc_identity.json"), JsonSerializer.Serialize(new
                {
                    ClubId = club, InstallationId = installation ?? SalikhEmployeeRecoveryPlan.Installation
                }));
                File.Copy(Path.Combine(path, "logs.json"), Path.Combine(path, "logs.json.bak"));
                File.Copy(Path.Combine(path, "salary_work_time.json"), Path.Combine(path, "salary_work_time.json.bak"));
                File.WriteAllText(Path.Combine(path, "employee_debts.json"), "{\"unchangedDebtAndCursors\":true}");
                return path;
            }
            string Foreign = WriteCase("foreign-club", "club_1");
            Check(!SalikhEmployeeRecoveryService.Apply(Foreign, () => throw new Exception("process check not allowed")),
                "other club skipped before business reads");
            Check(!Directory.Exists(Path.Combine(Foreign, "repairs")), "no other-club writes");
            string foreignInstall = WriteCase("foreign-install", installation: "different-installation");
            Check(!SalikhEmployeeRecoveryService.Apply(foreignInstall, () => false), "same club, different installation skipped");
            string process = WriteCase("other-process");
            ExpectFailure(() => SalikhEmployeeRecoveryService.Apply(process, () => true));
            Check(!Directory.Exists(Path.Combine(process, "repairs")), "other process blocks before backups or writes");
            string missing = WriteCase("missing-source");
            File.WriteAllText(Path.Combine(missing, "payments.json"), "[]");
            var missingHashes = Hashes(missing);
            ExpectFailure(() => SalikhEmployeeRecoveryService.Apply(missing, () => false));
            Check(SameHashes(missingHashes, Hashes(missing)) && !Directory.Exists(Path.Combine(missing, "repairs")),
                "missing evidence refuses before any business write or backup");
            string normal = WriteCase("normal");
            var originalHashes = Hashes(normal);
            Check(SalikhEmployeeRecoveryService.Apply(normal, () => false), "transaction completed");
            var firstHashes = Hashes(normal);
            Check(!SalikhEmployeeRecoveryService.Apply(normal, () => true), "committed startup skips work and process checks");
            Check(SameHashes(firstHashes, Hashes(normal)), "committed retry makes zero file writes");
            Check(File.ReadAllText(Path.Combine(normal, "employee_debts.json")) == "{\"unchangedDebtAndCursors\":true}",
                "debt ledger byte-identical");
            Check(File.ReadAllText(Path.Combine(normal, "logs.json")) == File.ReadAllText(Path.Combine(normal, "logs.json.bak")),
                "fallback log belongs to repaired generation");
            var journal = JsonSerializer.Deserialize<SalikhEmployeeRecoveryService.RecoveryJournal>(
                File.ReadAllText(Path.Combine(normal, SalikhEmployeeRecoveryService.RecoveryDirectory, "journal.json")))!;
            foreach (var file in journal.Files)
                Check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(normal,
                    SalikhEmployeeRecoveryService.RecoveryDirectory, file.Name + ".original")))) == originalHashes[file.Name],
                    "original backup matches pre-repair " + file.Name);
            for (int failAfter = 1; failAfter <= journal.Files.Count; failAfter++)
            {
                string crashed = WriteCase("interruption-" + failAfter);
                int crashAt = failAfter;
                ExpectFailure(() => SalikhEmployeeRecoveryService.Apply(crashed, () => false,
                    count => { if (count == crashAt) throw new IOException("simulated power loss"); }));
                Check(SalikhEmployeeRecoveryService.Apply(crashed, () => false), "prepared journal resumes after file " + failAfter);
                Check(SameHashes(firstHashes, Hashes(crashed)), "resumed data equals uninterrupted result " + failAfter);
                Check(!SalikhEmployeeRecoveryService.Apply(crashed, () => false), "resumed repair committed once " + failAfter);
            }
            string conflict = WriteCase("conflict");
            ExpectFailure(() => SalikhEmployeeRecoveryService.Apply(conflict, () => false, _ => throw new IOException("interrupted")));
            File.AppendAllText(Path.Combine(conflict, "payments.json"), " ");
            var conflictHashes = Hashes(conflict);
            ExpectFailure(() => SalikhEmployeeRecoveryService.Apply(conflict, () => false));
            Check(SameHashes(conflictHashes, Hashes(conflict)), "unexpected change blocks all remaining replacements");
            string proofConflict = WriteCase("proof-conflict");
            ExpectFailure(() => SalikhEmployeeRecoveryService.Apply(proofConflict, () => false, _ => throw new IOException("interrupted")));
            File.AppendAllText(Path.Combine(proofConflict, "employees.json"), " ");
            var proofHashes = Hashes(proofConflict);
            ExpectFailure(() => SalikhEmployeeRecoveryService.Apply(proofConflict, () => false));
            Check(SameHashes(proofHashes, Hashes(proofConflict)), "changed employee proof blocks resume");
            string scopeConflict = WriteCase("scope-conflict");
            ExpectFailure(() => SalikhEmployeeRecoveryService.Apply(scopeConflict, () => false, _ => throw new IOException("interrupted")));
            File.WriteAllText(Path.Combine(scopeConflict, "pc_identity.json"), "{\"ClubId\":\"club_1\"}");
            ExpectFailure(() => SalikhEmployeeRecoveryService.Apply(scopeConflict, () => false));
            Check(true, "prepared mixed generation never bypassed after club reassignment");
            string corrected = WriteCase("already-corrected");
            foreach (var pair in after) File.WriteAllText(Path.Combine(corrected, pair.Key), pair.Value.ToJsonString());
            Check(SalikhEmployeeRecoveryService.Apply(corrected, () => false), "already corrected case receives completion marker");
            Check(!SalikhEmployeeRecoveryService.Apply(corrected, () => false), "already corrected case is not reprocessed");
        }
        finally
        {
            // Only this test-created, GUID-named temporary tree is removed.
            string resolved = Path.GetFullPath(root);
            string temporaryRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
            if (!resolved.StartsWith(temporaryRoot, StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFileName(resolved).StartsWith("ClubTimer-employee-recovery-", StringComparison.Ordinal))
                throw new InvalidOperationException("Unsafe test cleanup path.");
            Directory.Delete(resolved, true);
        }
        var method = typeof(App).GetMethod("OnStartup", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var stateMachine = method.GetCustomAttribute<System.Runtime.CompilerServices.AsyncStateMachineAttribute>()!.StateMachineType;
        var calls = EmployeeIdentityStartupTests.CalledMethods(stateMachine.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.NonPublic)!).ToList();
        int recoveryCall = calls.FindIndex(call => call.DeclaringType == typeof(SalikhEmployeeRecoveryService));
        int initializationCall = calls.FindIndex(call => call.DeclaringType == typeof(AppUpdateLaunchContext));
        Check(recoveryCall >= 0 && recoveryCall < initializationCall, "recovery precedes application business services");
        Console.WriteLine($"Employee recovery: {checks} checks passed (sanitized report fixture, temporary files only).");
    }

    private static void StripNames(JsonNode node)
    {
        if (node is JsonObject row)
        {
            foreach (string key in row.Select(pair => pair.Key).ToArray())
            {
                if (key.EndsWith("EmployeeName", StringComparison.Ordinal)) row.Remove(key);
                else if (row[key] is JsonNode child) StripNames(child);
            }
        }
        else if (node is JsonArray array)
            foreach (var child in array.OfType<JsonNode>()) StripNames(child);
    }
    private static Dictionary<string, string> Hashes(string root) => Directory.GetFiles(root)
        .ToDictionary(path => Path.GetFileName(path), path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
    private static bool SameHashes(Dictionary<string, string> first, Dictionary<string, string> second) =>
        first.Count == second.Count && first.All(pair => second.GetValueOrDefault(pair.Key) == pair.Value);
    private static void ExpectFailure(Action action)
    {
        try { action(); }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or InvalidDataException) { return; }
        throw new InvalidOperationException("Expected recovery to refuse or stop.");
    }
}
