using System.Text.Json;
using ClubTimerXbox.Models;
using ClubTimerXbox.Services;

internal static class SessionLifecycleTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 24, 3, 0, 0, TimeSpan.FromHours(6));

    public static void Run()
    {
        int checks = 0;
        void Check(bool condition, string name)
        {
            if (!condition)
                throw new InvalidOperationException("Session lifecycle: " + name);
            checks++;
        }

        TestClock(Check);
        TestLedger(Check);
        TestDeferredPosting(Check);
        TestDeferredPosting(Check, pauseNewClient: true);
        Console.WriteLine($"Session lifecycle: {checks} isolated checks passed.");
    }

    private static void TestClock(Action<bool, string> check)
    {
        var gameId = Guid.NewGuid();
        var open = SessionPlayClock.Start(gameId, Start);
        var paused = open.Pause(Start.AddSeconds(20));
        var restored = JsonSerializer.Deserialize<SessionPlayClock>(JsonSerializer.Serialize(paused))!;
        check(restored == paused && restored.PlayedAt(Start.AddHours(2)).TotalSeconds == 20,
            "paused open time survives serialization without charging offline time");
        check(restored.Pause(Start.AddMinutes(30)) == restored && restored.Resume(Start) == restored,
            "repeated and out-of-order pause events do not alter saved state");
        check(TariffService.CalculateOpenModePrice(0, 2, restored.PlayedAt(Start.AddHours(1))) == 0,
            "first twenty active seconds remain free while paused");
        var resumed = restored.Resume(Start.AddHours(2));
        var next = resumed.Pause(Start.AddHours(2).AddSeconds(20)).Resume(Start.AddHours(3));
        check(next.GameSessionId == gameId && next.PlayedAt(Start.AddHours(3).AddSeconds(19)).TotalSeconds == 59,
            "resume retains the same game and cumulative active seconds");
        check(TariffService.CalculateOpenModePrice(0, 2, next.PlayedAt(Start.AddHours(3).AddSeconds(19))) == 0 &&
            TariffService.CalculateOpenModePrice(0, 2, next.PlayedAt(Start.AddHours(3).AddSeconds(20))) == 5,
            "three partial runs share the same original 60-second threshold");

        var rounded = SessionPlayClock.Start(gameId, Start);
        var segmentStart = Start;
        for (int i = 0; i < 2; i++)
        {
            rounded = rounded.Pause(segmentStart.AddSeconds(61));
            segmentStart = segmentStart.AddMinutes(10);
            rounded = rounded.Resume(segmentStart);
        }
        var stopped = rounded.Stop(segmentStart.AddSeconds(61));
        check(stopped.PlayedAt(Start.AddDays(2)).TotalSeconds == 183 &&
            TariffService.CalculateOpenModePrice(0, 2, stopped.PlayedAt(Start.AddDays(2))) == 10,
            "rounded once over active seconds, not independently on each pause");
        check(stopped.Resume(Start.AddDays(2)) == stopped && stopped.Stop(Start.AddDays(2)) == stopped,
            "committed stop cannot resurrect the old game");
        check(rounded.PlayedAt(segmentStart.AddSeconds(-1)).TotalSeconds == 122,
            "time rollback cannot create negative additional time");

        var prepaid = SessionPlayClock.Start(gameId, Start, TimeSpan.FromMinutes(5));
        var prepaidPaused = prepaid.Pause(Start.AddSeconds(60.5));
        var prepaidRestored = JsonSerializer.Deserialize<SessionPlayClock>(JsonSerializer.Serialize(prepaidPaused))!;
        check(prepaidRestored.RemainingAt(Start.AddHours(5)) == TimeSpan.FromSeconds(239.5),
            "fractional prepaid remainder stays frozen across serialization");
        var prepaidAgain = prepaidRestored.Resume(Start.AddHours(5));
        check(prepaidAgain.RemainingAt(Start.AddHours(5).AddSeconds(0.5)) == TimeSpan.FromSeconds(239),
            "prepaid resume counts new active seconds from the frozen remainder");
        check(prepaidAgain.RemainingAt(Start.AddHours(6)) == TimeSpan.Zero &&
            prepaidAgain.Pause(Start.AddHours(6)) == prepaidAgain,
            "expired tariff cannot enter pause and does not produce negative remainder");
    }

    private static void TestLedger(Action<bool, string> check)
    {
        string directory = Path.Combine(Path.GetTempPath(), "clubtimer-session-lifecycle-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "pending-checkouts.json");
        var creator = new CheckoutActor("emp_creator", Guid.NewGuid());
        var cashier = new CheckoutActor("emp_cashier", Guid.NewGuid());
        var firstGame = Guid.NewGuid();
        var secondGame = Guid.NewGuid();
        var firstId = Guid.NewGuid();
        var firstLines = new[]
        {
            Charge(firstGame, 240),
            new DeferredCheckoutLine(firstGame, Guid.NewGuid(), "TV 1", "Product", 2, 50, 30, "Stock", "Product")
        };
        try
        {
            var ledger = new DeferredCheckoutLedger(path, "test-club");
            check(ledger.GetOutstanding().IsEmpty && !Directory.Exists(directory),
                "empty first-run read does not create files");
            check(Throws<InvalidOperationException>(() => ledger.Defer(firstId, firstGame, "TV 1", Start, creator, false, firstLines)),
                "prepaid sessions cannot create deferred checkout");
            var first = ledger.Defer(firstId, firstGame, "TV 1", Start, creator, true, firstLines);
            check(first.StopNumber == 0 && first.Total == 340 && first.Responsible == creator,
                "independent stop freezes game and stock position amounts");
            var second = ledger.Defer(Guid.NewGuid(), secondGame, "tv 1", Start.AddMinutes(1),
                creator, true, [Charge(secondGame, 180)]);
            check(second.StopNumber == 1 && second.GameSessionId != first.GameSessionId,
                "same place can hold two independent client checkouts");

            ledger = new DeferredCheckoutLedger(path, "test-club");
            string original = File.ReadAllText(path);
            firstLines[0] = Charge(firstGame, 890);
            check(ledger.GetOutstanding().Select(entry => entry.Id).SequenceEqual([first.Id, second.Id]) &&
                ledger.Find(first.Id)!.Total == 340 && File.ReadAllText(path) == original,
                "restart and repeated reads preserve original amounts and do not write");
            check(ledger.Defer(firstId, firstGame, "TV 1", Start, creator, true, first.Lines).Id == firstId &&
                File.ReadAllText(path) == original, "same stop replay does not add or rewrite checkout");
            check(Throws<InvalidOperationException>(() => ledger.Defer(firstId, firstGame, "TV 1", Start, creator, true, firstLines)) &&
                Throws<InvalidOperationException>(() => ledger.Defer(Guid.NewGuid(), firstGame, "TV 1", Start, creator, true, first.Lines)),
                "a conflicting retry or second checkout for the same game is refused");

            var handedOver = ledger.AcceptResponsibility(firstId, first.Revision, cashier, Start.AddMinutes(2));
            check(handedOver.Responsible == cashier && handedOver.StoppedBy == creator && handedOver.Total == 340 &&
                handedOver.Handovers.Length == 1 && handedOver.PaymentIntent is null,
                "shift acceptance records responsibility without creating a payment");
            check(ledger.AcceptResponsibility(firstId, first.Revision, cashier, Start.AddMinutes(2)).Revision == handedOver.Revision,
                "replayed handover does not create another acceptance");
            check(Throws<InvalidDataException>(() => ledger.AcceptResponsibility(
                    firstId, handedOver.Revision, creator, Start.AddMinutes(1))) &&
                ledger.Find(firstId)!.Responsible == cashier,
                "out-of-order handover cannot overwrite the accepted employee");

            var intent = new DeferredPaymentIntent(Guid.NewGuid(), cashier, Start.AddMinutes(3), 200, 140);
            check(Throws<InvalidOperationException>(() => ledger.PreparePayment(firstId, first.Revision, cashier, cashier, intent)),
                "payment after handover requires an up-to-date checkout revision");
            check(Throws<InvalidOperationException>(() => ledger.PreparePayment(firstId, handedOver.Revision, cashier,
                    cashier with { ShiftId = Guid.NewGuid() }, intent)),
                "old confirmation cannot be used after the same employee starts another shift");
            check(Throws<InvalidDataException>(() => ledger.PreparePayment(firstId, handedOver.Revision, cashier, cashier,
                    intent with { MBankAmount = 141 })) &&
                Throws<InvalidDataException>(() => ledger.PreparePayment(firstId, handedOver.Revision, cashier, cashier,
                    intent with { CashAmount = -1, MBankAmount = 341 })) &&
                ledger.Find(firstId)!.PaymentIntent is null,
                "invalid tender is refused without reserving any payment");
            var reserved = ledger.PreparePayment(firstId, handedOver.Revision, cashier, cashier, intent);
            check(!reserved.IsPaid && reserved.PaymentIntent == intent,
                "reservation is not a receipt and remains outstanding");

            ledger = new DeferredCheckoutLedger(path, "test-club");
            check(ledger.GetOutstanding().First().PaymentIntent == intent &&
                ledger.PreparePayment(firstId, handedOver.Revision, cashier, cashier, intent).Revision == reserved.Revision,
                "restart reuses the same intent instead of reserving a second payment");
            check(Throws<InvalidOperationException>(() => ledger.PreparePayment(firstId, reserved.Revision, cashier, cashier,
                    intent with { PaymentId = Guid.NewGuid() })) &&
                Throws<InvalidOperationException>(() => ledger.ConfirmPersistedPayment(firstId, intent with { CashAmount = 199 })),
                "while reserved, neither a different intent nor a mismatched receipt can settle it");
            check(Throws<InvalidDataException>(() => ledger.PreparePayment(second.Id, second.Revision, cashier, cashier,
                    intent with { CashAmount = 180, MBankAmount = 0 })),
                "a receipt ID cannot be reused on a different client");
            var paid = ledger.ConfirmPersistedPayment(firstId, intent);
            check(paid.IsPaid && ledger.ConfirmPersistedPayment(firstId, intent).Revision == paid.Revision &&
                ledger.GetOutstanding().Single().Id == second.Id &&
                ledger.Find(second.Id)!.GameSessionId == secondGame,
                "confirmed receipt closes only the chosen old checkout and replays once");
            check(new DeferredCheckoutLedger(path, "test-club").Find(firstId)!.PaymentIntent == intent,
                "paid entry and posting reference remain in history");
            var thirdGame = Guid.NewGuid();
            var third = ledger.Defer(Guid.NewGuid(), thirdGame, "TV 1", Start.AddMinutes(4),
                creator, true, [Charge(thirdGame, 100)]);
            check(third.StopNumber == 2 && ledger.Find(second.Id)!.StopNumber == 1,
                "removing paid entry from pending does not renumber the others");
            check(Throws<InvalidDataException>(() => new DeferredCheckoutLedger(path, "other-club").GetOutstanding()),
                "wrong-club file cannot be accepted");

            string saved = File.ReadAllText(path);
            var fourthGame = Guid.NewGuid();
            using (var blocked = File.OpenRead(path))
            {
                check(WriteFails(() => ledger.Defer(Guid.NewGuid(), fourthGame, "TV 1",
                        Start.AddMinutes(5), creator, true, [Charge(fourthGame, 70)])),
                    "a blocked atomic replace fails, rather than acknowledging an unsaved stop");
            }
            check(File.ReadAllText(path) == saved && ledger.GetOutstanding().Length == 2 &&
                !Directory.EnumerateFiles(directory, "*.tmp").Any(),
                "failed write preserves exact previous state and clears temporary output");
            using (var busy = new FileStream(path + ".lock", FileMode.Open, FileAccess.Write, FileShare.None))
            {
                check(Throws<IOException>(() => ledger.AcceptResponsibility(second.Id, second.Revision, cashier, Start.AddMinutes(3))),
                    "concurrent writer cannot bypass the existing local ledger lock");
            }
            File.WriteAllText(path, "{\"Version\":1,\"ClubId\":\"test-club\",\"Checkouts\":[");
            check(Throws<InvalidDataException>(() => ledger.GetOutstanding()) &&
                Throws<InvalidDataException>(() => ledger.Defer(Guid.NewGuid(), fourthGame, "TV 1",
                    Start.AddMinutes(5), creator, true, [Charge(fourthGame, 70)])),
                "persisted corrupt data fails closed for both reads and writes");
            File.WriteAllText(path, "{\"Version\":1,\"ClubId\":\"test-club\"}");
            check(Throws<InvalidDataException>(() => ledger.GetOutstanding()),
                "missing entries in an existing file is not an empty queue");
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    private static DeferredCheckoutLine Charge(Guid gameId, int amount) =>
        new(gameId, null, "TV 1", "Game", 1, amount, 0, "Games", "");

    internal static void TestDeferredPosting(Action<bool, string> check, PaymentRecord? submitted = null,
        bool pauseNewClient = false)
    {
        string directory = Path.Combine(Path.GetTempPath(), "clubtimer-deferred-posting-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var old = new GameSessionLogItem { PlaceName = "TV 1", IsOpenMode = true, StartedByEmployeeName = "First" };
            var oldSale = new GameSessionSaleLine { ItemName = "Drink", UnitPrice = 100, Quantity = 1, TotalAmount = 100 };
            old.SaleLines.Add(oldSale);
            var sessions = new List<GameSessionLogItem> { old };
            var creator = new CheckoutActor("emp_first", Guid.NewGuid());
            var cashier = new CheckoutActor("emp_second", Guid.NewGuid());
            string ledgerPath = Path.Combine(directory, "deferred.json");
            string receiptPath = Path.Combine(directory, "payments.json");
            string incomePath = Path.Combine(directory, "income.json");
            string sessionPath = Path.Combine(directory, "logs.json");
            var ledger = new DeferredCheckoutLedger(ledgerPath, "fixture-club");
            var entry = ledger.Defer(Guid.NewGuid(), old.Id, old.PlaceName, Start, creator, true,
                [Charge(old.Id, 240), new(old.Id, oldSale.Id, old.PlaceName, "Drink", 1, 100, 30, "Goods", "Product")]);
            void SaveLogs() => DurableCheckoutFile.Save(sessionPath, sessions);
            DeferredCheckoutPosting.Detach(entry, sessions, "First", SaveLogs);
            check(old.IsClosed && old.DeferredCheckoutId == entry.Id && old.CashIncomeAmount == 0 &&
                !oldSale.IsPaid && !File.Exists(receiptPath) && !File.Exists(incomePath),
                "detaching leaves an unpaid bill with no cash, revenue or sale payment");
            var staleSave = new SavedActivePlace { Name = old.PlaceName, GameSessionId = old.Id, IsBusy = true };
            check(!DeferredCheckoutPosting.ShouldRestoreActivePlace(staleSave, ledger.GetOutstanding(), sessions),
                "an earlier active-TV save cannot resurrect the deferred game");

            var newGame = new GameSessionLogItem { PlaceName = old.PlaceName, IsOpenMode = true, StartedByEmployeeName = "Second" };
            newGame.SaleLines.Add(new() { ItemName = "Different goods", UnitPrice = 60, Quantity = 1, TotalAmount = 60 });
            sessions.Add(newGame);
            SaveLogs();
            var newPlace = new ClubPlace
            {
                Name = newGame.PlaceName, IsBusy = true, IsOpenMode = true,
                StartTime = Start.AddHours(2).DateTime, ActivePricePerMinute = 3, AccruedAmountBeforeCurrentSegment = 15
            };
            DateTime compareAt = Start.AddHours(2).AddMinutes(10).DateTime;
            if (pauseNewClient)
                check(SessionPausePolicy.TryPause(newPlace, newGame.Id, compareAt),
                    "new client on the same TV can pause while the old checkout is outstanding");
            var originalPause = newPlace.PauseState;
            string originalNewPlace = JsonSerializer.Serialize(newPlace);
            string originalNewSession = JsonSerializer.Serialize(newGame);
            int activePrice = SessionPausePolicy.OpenPrice(newPlace, compareAt);
            var handover = ledger.AcceptAllResponsibility(ledger.GetOutstanding(), cashier, Start.AddHours(1));
            check(handover == 1 && ledger.Find(entry.Id)!.Responsible == cashier && !File.Exists(receiptPath),
                "handover for pending checkouts records the recipient without taking payment");
            var beforePay = ledger.Find(entry.Id)!;
            var tender = new DeferredPaymentIntent(submitted?.Id ?? Guid.NewGuid(), cashier, Start.AddHours(4),
                submitted?.CashAmount ?? 200, submitted?.MBankAmount ?? 140);
            var reserved = ledger.PreparePayment(entry.Id, beforePay.Revision, cashier, cashier, tender);
            var payments = new List<PaymentRecord>();
            var income = new List<CashRecord>();
            void SaveReceipt(PaymentRecord row) => DeferredCheckoutPosting.SaveReceipt(payments, row,
                items => DurableCheckoutFile.Save(receiptPath, items));
            void SaveIncome(IReadOnlyList<CashRecord> rows) => DeferredCheckoutPosting.SaveIncome(income, rows,
                items => DurableCheckoutFile.Save(incomePath, items));

            check(Throws<IOException>(() => DeferredCheckoutPosting.Complete(ledger, reserved, sessions,
                "Second", SaveReceipt, _ => throw new IOException("simulated interruption after receipt"), SaveLogs)) &&
                payments.Count == 1 && ledger.Find(entry.Id) is { IsPaid: false } && !oldSale.IsPaid,
                "interruption after durable receipt leaves one reserved intent for recovery");

            ledger = new DeferredCheckoutLedger(ledgerPath, "fixture-club");
            payments = JsonSerializer.Deserialize<List<PaymentRecord>>(File.ReadAllText(receiptPath))!;
            sessions = JsonSerializer.Deserialize<List<GameSessionLogItem>>(File.ReadAllText(sessionPath))!;
            reserved = ledger.Find(entry.Id)!;
            DeferredCheckoutPosting.Complete(ledger, reserved, sessions, "Second", SaveReceipt, SaveIncome, SaveLogs);
            DeferredCheckoutPosting.Complete(ledger, reserved, sessions, "Second", SaveReceipt, SaveIncome, SaveLogs);
            var receipt = payments.Single();
            var paidOld = sessions.Single(game => game.Id == old.Id);
            check(receipt.GameSessionId == old.Id && receipt.Id == tender.PaymentId && receipt.TotalAmount == 340 &&
                receipt.CashAmount == 200 && receipt.MBankAmount == 140 && receipt.EmployeeName == "Second" &&
                receipt.BusinessDateKey == "2026-11-24" && paidOld.ClosedAt == Start.DateTime,
                "old receipt is posted once, belongs to the actual cashier, and retains both dates and channels");
            check(income.Count == 2 && income.Any(row => row.Type == CashRecordType.GameSession && row.Amount == 240) &&
                income.Any(row => row.Type == CashRecordType.ProductOrService && row.Amount == 100) &&
                income.All(row => row.GameSessionId == old.Id && row.PaymentRecordId == receipt.Id) &&
                paidOld.SaleLines.Single().PaidShiftId == cashier.ShiftId &&
                paidOld.SaleLines.Single().PaymentRecordId == receipt.Id,
                "only the old client's game and goods are posted after replay");
            check(ledger.GetOutstanding().IsEmpty && ledger.Find(entry.Id) is { IsPaid: true },
                "paid stop leaves the queue but stays in history");
            var current = sessions.Single(game => !game.IsClosed && game.PlaceName == old.PlaceName);
            check(!DeferredCheckoutPosting.ShouldRestoreActivePlace(staleSave, ledger.GetOutstanding(), sessions) &&
                DeferredCheckoutPosting.ShouldRestoreActivePlace(
                    new SavedActivePlace { Name = current.PlaceName, GameSessionId = current.Id, IsBusy = true },
                    ledger.GetOutstanding(), sessions),
                "restart distinguishes old saved stop from a new session on that TV after payment");
            check(current.Id == newGame.Id && JsonSerializer.Serialize(current) == originalNewSession &&
                JsonSerializer.Serialize(newPlace) == originalNewPlace && !current.SaleLines.Single().IsPaid &&
                newPlace.PauseState == originalPause && newPlace.IsPaused == pauseNewClient &&
                SessionPausePolicy.OpenPrice(newPlace, pauseNewClient ? compareAt.AddHours(2) : compareAt) == activePrice,
                "new client on the same TV keeps the same session, clock, running cost and unpaid goods");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static bool Throws<TException>(Action action) where TException : Exception
    {
        try { action(); }
        catch (TException) { return true; }
        return false;
    }

    private static bool WriteFails(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return true; }
        return false;
    }
}
