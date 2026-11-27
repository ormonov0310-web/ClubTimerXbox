using System.Diagnostics;
using System.Text.Json;
using ClubTimerUpdater;
using ClubTimerXbox.Models;
using ClubTimerXbox.Services;

internal static class SessionReleaseGateTests
{
    private static readonly DateTime Boundary = new(2026, 11, 28, 6, 0, 0);
    private const string CompatibilityMarker = "session-storage-v1.required";
    private const string MainExe = "ClubTimerXbox.exe";
    private const string MainAssembly = "ClubTimerXbox.dll";

    public static void Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "clubtimer-release-gates-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string data = Path.Combine(root, "test-cash");
            SeedShiftAndCash(data);
            TestUpdater(root, data);
            RunProbe(data, "reserve");
            RunProbe(data, "settle");
            RunProbe(data, "verify");
            Console.WriteLine("Release gates: shift, night intervals, cash, compatible rollback and three fresh-process restarts passed.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void SeedShiftAndCash(string data)
    {
        Directory.CreateDirectory(data);
        var a = new ShiftLogItem
        {
            EmployeeName = "Admin A", StartedAt = Boundary.AddHours(-7), ClosedAt = Boundary.AddMinutes(-15), IsClosed = true
        };
        var b = new ShiftLogItem { EmployeeName = "Admin B", StartedAt = a.ClosedAt!.Value };
        var creator = new CheckoutActor("emp_a", a.Id);
        var receiver = new CheckoutActor("emp_b", b.Id);
        var original = new GameSessionLogItem
        {
            PlaceName = "TV 1", IsOpenMode = true, StartedByEmployeeName = a.EmployeeName, StartedAt = Boundary.AddMinutes(-70),
            PlayPauses = [new(Boundary.AddMinutes(-50), Boundary.AddMinutes(-40))],
            SaleLines = [new() { ItemName = "Drink", Quantity = 1, UnitPrice = 100, TotalAmount = 100 }]
        };
        var overlapping = new GameSessionLogItem
        {
            PlaceName = "TV 2", StartedAt = Boundary.AddMinutes(-67), ClosedAt = Boundary.AddMinutes(-60), IsClosed = true
        };
        var games = new List<GameSessionLogItem> { original, overlapping };
        string gamesPath = Path.Combine(data, "games.json");
        void SaveGames() => DurableCheckoutFile.Save(gamesPath, games);
        var ledger = Ledger(data);
        DateTime stopped = Boundary.AddMinutes(-20);
        var entry = ledger.Defer(Guid.NewGuid(), original.Id, original.PlaceName, At(stopped), creator, true,
            [new(original.Id, null, original.PlaceName, "Game", 1, 240, 0, "Games", ""),
             new(original.Id, original.SaleLines.Single().Id, original.PlaceName, "Drink", 1, 100, 30, "Goods", "Product")]);
        Require(File.Exists(Path.Combine(data, CompatibilityMarker)), "the deferred ledger establishes a compatibility requirement");
        DeferredCheckoutPosting.Detach(entry, games, a.EmployeeName, SaveGames);
        Require(ledger.AcceptAllResponsibility(ledger.GetOutstanding(), receiver, At(b.StartedAt)) == 1,
            "B accepts responsibility without paying");
        Require(ledger.GetOutstanding().Single().Responsible == receiver && original.IncomeEmployeeName == "",
            "unpaid receipt has no income recipient");
        AssertNightTime(games, a, b);

        var current = new GameSessionLogItem
        {
            PlaceName = original.PlaceName, StartedByEmployeeName = b.EmployeeName, StartedAt = b.StartedAt,
            IncomeEmployeeName = b.EmployeeName, PaidAmount = 120,
            SaleLines = [new() { ItemName = "New client's drink", Quantity = 1, UnitPrice = 60, TotalAmount = 60 }]
        };
        games.Add(current);
        SaveGames();
        var place = new ClubPlace { Name = current.PlaceName, IsBusy = true, StartTime = current.StartedAt, RemainingSeconds = 3600 };
        DateTime pausedAt = b.StartedAt.AddMinutes(5);
        Require(SessionPausePolicy.TryPause(place, current.Id, pausedAt), "new prepaid client can pause on the same TV");
        SessionPausePolicy.RecordInterval(current, place.PauseState!);
        SaveGames();
        ActiveSessionStorageService.SaveTo(Path.Combine(data, "active_sessions.json"),
            [new SavedActivePlace
             {
                 Name = current.PlaceName, GameSessionId = current.Id, IsBusy = true, LastSavedAt = pausedAt,
                 PauseState = place.PauseState, RemainingSeconds = place.RemainingSeconds,
                 PaidAmount = 120, PrepaidCashAmount = 70, PrepaidMBankAmount = 50
             }], flushToDisk: true);
        DurableCheckoutFile.Save<ShiftLogItem>(Path.Combine(data, "shifts.json"), [a, b]);
        DurableCheckoutFile.Save(Path.Combine(data, "payments.json"), new List<PaymentRecord>
        {
            new() { GameSessionId = current.Id, EmployeeName = b.EmployeeName, PlaceName = current.PlaceName,
                CreatedAt = b.StartedAt, TotalAmount = 120, CashAmount = 70, MBankAmount = 50 }
        });
    }

    public static void RestartProbe(string data, string phase)
    {
        Require(phase is "reserve" or "settle" or "verify", "valid restart phase");
        var games = Read<List<GameSessionLogItem>>(data, "games.json");
        var shifts = Read<ShiftLogItem[]>(data, "shifts.json");
        var payments = Read<List<PaymentRecord>>(data, "payments.json");
        var incomePath = Path.Combine(data, "cash_records.json");
        var cashRows = File.Exists(incomePath) ? Read<List<CashRecord>>(data, "cash_records.json") : [];
        var ledger = Ledger(data);
        var pending = ledger.GetOutstanding();
        var saved = ActiveSessionStorageService.LoadFrom(Path.Combine(data, "active_sessions.json"))
            .Where(state => DeferredCheckoutPosting.ShouldRestoreActivePlace(state, pending, games)).Single();
        var current = games.Single(game => !game.IsClosed);
        var place = new ClubPlace { Name = saved.Name, IsBusy = saved.IsBusy, RemainingSeconds = saved.RemainingSeconds };
        var now = Boundary.AddMinutes(10);
        SessionPausePolicy.Restore(place, saved, current.Id);
        SessionPausePolicy.RestorePrepaidCountdown(place, saved.LastSavedAt, now);
        Require(place.IsPaused && place.RemainingSeconds == 3300 && current.Id == saved.GameSessionId &&
            current.SaleLines is [{ IsPaid: false, ItemName: "New client's drink" }],
            "only the new client restores and its clock and goods are intact");
        AssertNightTime(games, shifts[0], shifts[1]);
        string before = JsonSerializer.Serialize(current);
        var original = games.Single(game => game.DeferredCheckoutId.HasValue);
        var entry = ledger.Find(original.DeferredCheckoutId!.Value)!;

        if (phase == "reserve")
        {
            var cashier = new CheckoutActor("emp_b", shifts[1].Id);
            Require(entry.Responsible == cashier && pending.Length == 1 && entry.PaymentIntent is null &&
                payments.Count == 1 && CashOnHand(payments, cashRows) == 1070,
                "after restart the old unpaid KKM still belongs to the new shift without adding cash");
            ledger.PreparePayment(entry.Id, entry.Revision, cashier, cashier,
                new DeferredPaymentIntent(Guid.NewGuid(), cashier, At(now), 200, 140));
            Console.WriteLine("Restart reserve: outstanding receipt restored under B, cash still 1070.");
            return;
        }

        if (phase == "settle")
        {
            Require(CashOnHand(payments, cashRows) == 1070 && payments.Sum(row => row.MBankAmount) == 50 &&
                payments.Count == 1 && pending.Length == 1 && entry.PaymentIntent is not null,
                "shift and saved intent do not increase cash before posting");
            DeferredCheckoutPosting.Complete(ledger, entry, games, shifts[1].EmployeeName,
                row => DeferredCheckoutPosting.SaveReceipt(payments, row,
                    rows => DurableCheckoutFile.Save(Path.Combine(data, "payments.json"), rows)),
                rows => DeferredCheckoutPosting.SaveIncome(cashRows, rows, updated => DurableCheckoutFile.Save(incomePath, updated)),
                () => DurableCheckoutFile.Save(Path.Combine(data, "games.json"), games));
        }
        else
            Require(pending.IsEmpty && entry.IsPaid, "second restart has no outstanding payment to replay");

        var receipt = payments.Single(row => row.DeferredCheckoutId == entry.Id);
        Require(payments.Count == 2 && receipt.Id == entry.PaymentIntent!.PaymentId &&
            receipt.EmployeeName == shifts[1].EmployeeName && original.IncomeEmployeeName == shifts[1].EmployeeName &&
            receipt.GameSessionId == original.Id && receipt.TotalAmount == 340 &&
            receipt.CashAmount == 200 && receipt.MBankAmount == 140 &&
            receipt.BusinessDateKey == "2026-11-28" && BusinessCalendarService.GetBusinessDay(original.ClosedAt!.Value).Key == "2026-11-27",
            "actual cashier receives exactly one 200/140 receipt on the next business day");
        Require(cashRows.Count == 2 && cashRows.Sum(row => row.Amount) == 340 &&
            cashRows.All(row => row.PaymentRecordId == receipt.Id && row.GameSessionId == original.Id) &&
            CashOnHand(payments, cashRows) == 1270 && payments.Sum(row => row.MBankAmount) == 190,
            "test cash reconciles by channel; income rows do not double-count tender");
        AssertNightTime(games, shifts[0], shifts[1]);
        Require(before == JsonSerializer.Serialize(current) && place.IsPaused && place.RemainingSeconds == 3300,
            "settling the old client leaves the new client untouched");
        Console.WriteLine($"Restart {phase}: cash 1270, cashless income 190, A night 40 min, B night 0 min, frozen 3300 sec.");
    }

    private static void AssertNightTime(List<GameSessionLogItem> games, ShiftLogItem a, ShiftLogItem b)
    {
        var play = AutoSalaryService.GetLatePlayIntervals(games, Boundary.AddHours(-5), Boundary);
        var aIntervals = AutoSalaryService.ClipLatePlayToShift(play, a.StartedAt, a.ClosedAt!.Value).ToArray();
        var bIntervals = AutoSalaryService.ClipLatePlayToShift(play, b.StartedAt, Boundary.AddMinutes(10)).ToArray();
        Require(aIntervals.SequenceEqual([
                new PaidInterval(Boundary.AddMinutes(-70), Boundary.AddMinutes(-50)),
                new PaidInterval(Boundary.AddMinutes(-40), Boundary.AddMinutes(-20))]) && bIntervals.Length == 0,
            "40 minutes go to A's shift, overlap is merged and pause/collection do not add hours");
        var crossesBoundary = new GameSessionLogItem
        {
            StartedAt = Boundary.AddMinutes(-5), ClosedAt = Boundary.AddMinutes(8), IsClosed = true
        };
        Require(AutoSalaryService.GetLatePlayIntervals([crossesBoundary], Boundary.AddHours(-5), Boundary)
                .SequenceEqual([new PaidInterval(Boundary.AddMinutes(-5), Boundary)]),
            "night hours stop at the business-day boundary even when the game closes later");
    }

    private static int CashOnHand(List<PaymentRecord> payments, List<CashRecord> rows) =>
        CashPhysicalBalancePolicy.Calculate([], [new() { CashAmount = 1000, CreatedAt = Boundary.AddHours(-7) }],
            payments, rows, Boundary.AddHours(-24), Boundary.AddMinutes(11)).Amount;

    private static void TestUpdater(string root, string data)
    {
        string install = MakeProgram("current", compatible: true);
        string prior = MakeProgram("legacy", compatible: false);
        string candidate = MakeProgram("compatible", compatible: true);
        string packageFile = Path.Combine(root, "fixture.package");
        File.WriteAllText(packageFile, "fixture");
        string journal = Path.Combine(root, "update-journal.json");
        string boxes = Path.Combine(root, "backups");
        var request = new UpdateTransactionRequest
        {
            PackagePath = packageFile, TargetDir = install, BackupRoot = boxes, JournalPath = journal,
            ClubDataDirectory = data, ManageRecoveryRegistration = false
        };
        var before = Snapshot(data);
        Require(Rejects(() => UpdateTransactionEngine.InstallPrepared(new() { SourceDir = prior }, request)) &&
            !Directory.Exists(boxes) && !File.Exists(journal) && File.ReadAllText(Path.Combine(install, MainExe)) == "current",
            "legacy update rejected before creating backup, journal or replacing the application");

        var pauseOnly = Path.Combine(root, "unmarked-pause");
        var ledgerOnly = Path.Combine(root, "unmarked-ledger");
        var markerOnly = Path.Combine(root, "marker-only");
        Directory.CreateDirectory(pauseOnly);
        Directory.CreateDirectory(ledgerOnly);
        Directory.CreateDirectory(markerOnly);
        File.Copy(Path.Combine(data, "active_sessions.json"), Path.Combine(pauseOnly, "active_sessions.json"));
        File.Copy(Path.Combine(data, "deferred_checkouts.json"), Path.Combine(ledgerOnly, "deferred_checkouts.json"));
        File.Copy(Path.Combine(data, CompatibilityMarker), Path.Combine(markerOnly, CompatibilityMarker));
        foreach (string preview in new[] { pauseOnly, ledgerOnly, markerOnly })
            Require(Rejects(() => UpdateTransactionEngine.InstallPrepared(new() { SourceDir = prior },
                new() { PackagePath = packageFile, TargetDir = install, BackupRoot = boxes, JournalPath = journal,
                    ClubDataDirectory = preview, ManageRecoveryRegistration = false })),
                "an unmarked preview file or the permanent requirement alone prevents downgrade");

        SaveRecoveryJournal(prior, "Committing");
        Require(Rejects(() => UpdateTransactionEngine.RecoverFromJournal(journal, data, manageRecoveryRegistration: false)) &&
            File.Exists(journal) && File.ReadAllText(Path.Combine(install, MainExe)) == "current",
            "recovery cannot restore an incompatible backup or remove its recovery journal");
        File.Delete(journal);

        Require(Throws<UpdateRolledBackException>(() => UpdateTransactionEngine.InstallPrepared(
                new() { SourceDir = candidate }, request, failureHook: phase =>
                {
                    if (phase == UpdateTransactionPhase.ValidatingInstall)
                        throw new IOException("fixture install interruption");
                })) && File.ReadAllText(Path.Combine(install, MainExe)) == "current",
            "failed update can still restore a compatible backup after feature use");
        Require(UpdateTransactionEngine.RecoverFromJournal(journal, data, manageRecoveryRegistration: false).Result == "rolled_back" &&
            !File.Exists(journal), "compatible rollback journal can be completed");

        SaveRecoveryJournal(candidate, "Committing");
        Require(UpdateTransactionEngine.RecoverFromJournal(journal, data, manageRecoveryRegistration: false).Result == "rolled_back" &&
            File.ReadAllText(Path.Combine(install, MainExe)) == "compatible",
            "restart after interruption restores a compatible backup");
        UpdateTransactionEngine.InstallPrepared(new() { SourceDir = candidate }, request);
        Require(before.SequenceEqual(Snapshot(data)), "rollback and upgrade never replace or modify the live data directory");

        var emptyBeforeUse = new UpdateTransactionRequest
        {
            PackagePath = packageFile, TargetDir = prior, BackupRoot = boxes, JournalPath = journal,
            ClubDataDirectory = Path.Combine(root, "not-used-yet"), ManageRecoveryRegistration = false
        };
        Require(Throws<UpdateRolledBackException>(() => UpdateTransactionEngine.InstallPrepared(
                new() { SourceDir = candidate }, emptyBeforeUse, failureHook: phase =>
                {
                    if (phase == UpdateTransactionPhase.ValidatingInstall)
                        throw new IOException("fixture interruption before feature use");
                })) && File.ReadAllText(Path.Combine(prior, MainExe)) == "legacy",
            "initial install may roll back before any new-format data exists");

        string MakeProgram(string name, bool compatible)
        {
            string dir = Path.Combine(root, name);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, MainExe), name);
            string dll = Path.Combine(dir, MainAssembly);
            if (compatible)
                File.Copy(typeof(ClubTimerXbox.MainWindow).Assembly.Location, dll);
            else
                File.Copy(typeof(UpdateTransactionEngine).Assembly.Location, dll);
            return dir;
        }

        void SaveRecoveryJournal(string backup, string phase) => File.WriteAllText(journal,
            JsonSerializer.Serialize(new { Phase = phase, TargetDir = install, BackupPath = backup, MainExe }));
    }

    private static string[] Snapshot(string directory) => Directory.GetFiles(directory)
        .OrderBy(path => path, StringComparer.Ordinal)
        .Select(path => Path.GetFileName(path) + ":" + Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)))).ToArray();

    private static void RunProbe(string path, string phase)
    {
        var info = new ProcessStartInfo(Environment.ProcessPath!)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        if (Path.GetFileNameWithoutExtension(info.FileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            info.ArgumentList.Add(typeof(SessionReleaseGateTests).Assembly.Location);
        info.ArgumentList.Add("--probe-session-release-gates");
        info.ArgumentList.Add(path);
        info.ArgumentList.Add(phase);
        using var process = Process.Start(info)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(20_000))
        {
            process.Kill();
            process.WaitForExit();
            throw new TimeoutException("Session release-gate restart probe timed out.");
        }
        Require(process.ExitCode == 0, "fresh process " + phase + ": " + stderr.GetAwaiter().GetResult());
        Console.Write(stdout.GetAwaiter().GetResult());
    }

    private static DeferredCheckoutLedger Ledger(string path) => new(Path.Combine(path, "deferred_checkouts.json"), "fixture");
    private static DateTimeOffset At(DateTime local) => new(local, BusinessCalendarService.ClubUtcOffset);
    private static T Read<T>(string directory, string file) =>
        JsonSerializer.Deserialize<T>(File.ReadAllText(Path.Combine(directory, file)))!;
    private static bool Rejects(Action action)
    {
        try { action(); }
        catch (InvalidOperationException error) when (error.Message.Contains("не поддерживает сохранённые паузы")) { return true; }
        return false;
    }

    private static bool Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return true; }
        return false;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException("Session release gate: " + message);
    }
}
