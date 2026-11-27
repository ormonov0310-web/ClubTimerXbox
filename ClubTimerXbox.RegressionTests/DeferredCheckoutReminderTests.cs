using System.Collections.Immutable;
using System.Runtime.ExceptionServices;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ClubTimerXbox;
using ClubTimerXbox.Models;
using ClubTimerXbox.Services;

internal static class DeferredCheckoutReminderTests
{
    public static void Run()
    {
        int checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException("Deferred reminder: " + message);
            checks++;
        }

        var reminders = CheckLedger(Check);
        CheckBanner(reminders, Check);
        Console.WriteLine($"Deferred reminders: {checks} local ledger and WPF checks passed.");
    }

    private static ImmutableArray<DeferredCheckout> CheckLedger(Action<bool, string> check)
    {
        string root = Path.Combine(Path.GetTempPath(), "clubtimer-reminders-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(root, "deferred_checkouts.json");
        var stop = new DateTimeOffset(2026, 11, 24, 5, 43, 0, TimeSpan.FromHours(6));
        var deferred = stop.AddMinutes(8);
        var ready = deferred.AddMinutes(15);
        var by = new CheckoutActor("emp_first", Guid.NewGuid());
        var next = new CheckoutActor("emp_next", Guid.NewGuid());
        try
        {
            var ledger = new DeferredCheckoutLedger(path, "test-club");
            DeferredCheckout Add(string place, int amount, DateTimeOffset? deferredAt)
            {
                Guid game = Guid.NewGuid();
                return ledger.Defer(Guid.NewGuid(), game, place, stop, by, true,
                    [new(game, null, place, "Open mode", 1, amount, 0, "Games", "")], deferredAt);
            }

            var first = Add("ТВ 7", 340, deferred);
            var second = Add("ТВ 12", 280, deferred);
            var legacy = Add("ТВ 4", 90, null);
            var paid = Add("ТВ 3", 120, deferred);
            var paying = Add("ТВ 1", 100, deferred);
            var paidIntent = new DeferredPaymentIntent(Guid.NewGuid(), by, deferred.AddMinutes(5), 120, 0);
            ledger.PreparePayment(paid.Id, paid.Revision, by, by, paidIntent);
            ledger.ConfirmPersistedPayment(paid.Id, paidIntent);
            ledger.PreparePayment(paying.Id, paying.Revision, by, by,
                new DeferredPaymentIntent(Guid.NewGuid(), by, deferred.AddMinutes(6), 0, 100));
            var accepted = ledger.AcceptResponsibility(first.Id, first.Revision, next, deferred.AddMinutes(10));
            var candidates = new HashSet<Guid> { first.Id, second.Id, paid.Id, paying.Id };

            string before = File.ReadAllText(path);
            var early = ledger.TakeDueReminders(candidates, ready.AddTicks(-1));
            check(early.Reminded.IsEmpty && File.ReadAllText(path) == before,
                "no file change or reminder before 15 minutes from Defer, not from Stop");
            check(ledger.Find(legacy.Id)!.IsReminderDue(stop.AddMinutes(15)),
                "earlier saved checkouts without the optional Defer timestamp use their original stop time");
            check(!accepted.IsReminderDue(deferred.AddMinutes(-1)), "clock movement backwards cannot trigger early");

            ledger = new DeferredCheckoutLedger(path, "test-club");
            var due = ledger.TakeDueReminders(candidates, ready);
            check(due.Reminded.Select(entry => entry.Id).ToHashSet().SetEquals([first.Id, second.Id]),
                "after reopening the ledger exactly the two unpaid and selected checkouts are reminded");
            check(due.Reminded.All(entry => entry.RemindedAt == ready) &&
                due.Reminded.Single(entry => entry.Id == first.Id).Responsible == next &&
                ledger.Find(first.Id)!.Revision == accepted.Revision,
                "handover preserves original reminder deadline and marker does not invalidate payment revision");
            check(ledger.Find(paying.Id)!.RemindedAt is null && ledger.Find(paid.Id)!.RemindedAt is null,
                "paid and in-progress receipts are never reminders");

            string marked = File.ReadAllText(path);
            var rootAfter = JsonNode.Parse(marked)!;
            foreach (var item in rootAfter["Checkouts"]!.AsArray())
                item!.AsObject().Remove("RemindedAt");
            check(JsonNode.DeepEquals(JsonNode.Parse(before), rootAfter), "only the nonfinancial reminder marker changed");

            ledger = new DeferredCheckoutLedger(path, "test-club");
            var repeated = ledger.TakeDueReminders(candidates, ready.AddHours(5));
            check(repeated.Reminded.IsEmpty && File.ReadAllText(path) == marked &&
                repeated.Outstanding.Any(entry => entry.Id == first.Id),
                "restart neither repeats reminders nor creates payments or penalties, even after 06:00");

            using (var fileLock = File.OpenRead(path))
            {
                bool writeRejected = false;
                try { ledger.TakeDueReminders(new HashSet<Guid> { legacy.Id }, ready); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { writeRejected = true; }
                check(writeRejected, "uncommitted marker is not acknowledged as a delivered reminder");
            }
            check(ledger.Find(legacy.Id)!.RemindedAt is null &&
                ledger.TakeDueReminders(new HashSet<Guid> { legacy.Id }, ready).Reminded.Single().Id == legacy.Id,
                "after a temporary failure the same valid reminder remains available");
            return due.Reminded;
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static void CheckBanner(ImmutableArray<DeferredCheckout> reminders, Action<bool, string> check)
    {
        Exception? error = null;
        var ui = new Thread(() =>
        {
            var banner = new DeferredCheckoutReminderBanner();
            var work = new Button { Content = "Другие операции" };
            var panel = new StackPanel();
            panel.Children.Add(banner);
            panel.Children.Add(work);
            var window = new Window
            {
                Width = 320, Height = 200, Content = panel, Left = -10000, Top = -10000,
                ShowInTaskbar = false, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual
            };
            try
            {
                check(banner.Visibility == Visibility.Collapsed, "banner does not take up space without a reminder");
                banner.Dismissed += (_, _) => banner.ShowOutstanding([]);
                banner.ShowOutstanding(reminders);
                window.Show();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                window.UpdateLayout();
                var layout = (Grid)banner.Child;
                var message = layout.Children.OfType<TextBlock>().Single();
                var close = layout.Children.OfType<Button>().Single();
                check(banner.IsVisible && message.Text.Contains("ТВ 7") &&
                    message.Text.Contains("Остановка 0") && message.Text.Contains("340 сом") &&
                    message.Text.Contains("ещё 1") && work.IsEnabled,
                    "nonmodal grouped banner shows checkout identity and does not disable another command");
                banner.ShowOutstanding([reminders[1]]);
                window.UpdateLayout();
                check(message.Text.Contains("ТВ 12") && !message.Text.Contains("ТВ 7") && message.ActualWidth > 0,
                    "resolved reminder disappears and the remaining one keeps a readable label");
                close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                check(banner.Visibility == Visibility.Collapsed && work.IsEnabled,
                    "dismiss only hides the banner and leaves other commands usable");
            }
            catch (Exception exception) { error = exception; }
            finally { window.Close(); }
        });
        ui.SetApartmentState(ApartmentState.STA);
        ui.Start();
        ui.Join();
        if (error != null)
            ExceptionDispatchInfo.Capture(error).Throw();
    }
}
