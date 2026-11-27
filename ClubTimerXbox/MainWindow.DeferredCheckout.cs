using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ClubTimerXbox.Models;
using ClubTimerXbox.Services;

namespace ClubTimerXbox;

public partial class MainWindow
{
    private ImmutableArray<DeferredCheckout> _pendingCheckouts = [];
    private DispatcherTimer? _deferredClickTimer;
    private Guid? _scheduledDeferredCheckoutId;
    private bool _deferredEditorOpen;
    private readonly HashSet<Guid> _failedDeferredStops = [];
    private readonly HashSet<Guid> _visibleDeferredReminderIds = [];
    private long? _deferredReminderFailureAt;

    private void TickDeferredReminders()
    {
        if (_pendingCheckouts.IsEmpty || !IsActive || !IsVisible || !IsEnabled || _deferredEditorOpen ||
            WindowState == WindowState.Minimized ||
            (_deferredReminderFailureAt is { } failedAt &&
             Stopwatch.GetElapsedTime(failedAt) < TimeSpan.FromSeconds(30)))
            return;

        var now = new DateTimeOffset(ClubClock.Current.UtcNow);
        if (!_pendingCheckouts.Any(entry => entry.IsReminderDue(now) && !_failedDeferredStops.Contains(entry.Id)) ||
            GetCurrentCheckoutActor() is null)
            return;

        var candidates = _pendingCheckouts
            .Where(entry => entry.IsReminderDue(now) && !_failedDeferredStops.Contains(entry.Id))
            .Select(entry => entry.Id).ToHashSet();
        try
        {
            var result = DeferredCheckoutLedger.ForCurrentClub().TakeDueReminders(candidates, now);
            _deferredReminderFailureAt = null;
            _pendingCheckouts = result.Outstanding;
            _visibleDeferredReminderIds.UnionWith(result.Reminded.Select(entry => entry.Id));
            RefreshDeferredReminderBanner();
        }
        catch (Exception error) when (IsDeferredFailure(error))
        {
            Trace.WriteLine(error);
            _deferredReminderFailureAt = Stopwatch.GetTimestamp();
        }
    }

    private void RefreshDeferredReminderBanner()
    {
        var visible = _pendingCheckouts
            .Where(entry => entry.PaymentIntent is null && _visibleDeferredReminderIds.Contains(entry.Id))
            .ToArray();
        _visibleDeferredReminderIds.IntersectWith(visible.Select(entry => entry.Id));
        DeferredReminderBanner.ShowOutstanding(visible);
    }

    private static CheckoutActor? GetCurrentCheckoutActor()
    {
        var employee = EmployeeService.CurrentEmployee;
        var shift = ActionLogService.CurrentShift;
        if (!EmployeeActivityPolicy.CanParticipate(employee) || shift is null ||
            !string.Equals(employee.Name, shift.EmployeeName, StringComparison.OrdinalIgnoreCase))
            return null;

        var actor = new CheckoutActor(employee.EmployeeId, shift.Id);
        return actor.IsSpecified ? actor : null;
    }

    private bool RecoverDeferredCheckouts()
    {
        try
        {
            var ledger = DeferredCheckoutLedger.ForCurrentClub();
            _pendingCheckouts = ledger.GetOutstanding();
            if (ActionLogService.GameSessions.Any(game => game.DeferredCheckoutId is { } id &&
                    game.DeferredCheckoutPaidAt is null && !_pendingCheckouts.Any(entry => entry.Id == id)))
                throw new InvalidDataException("A detached game is missing its unpaid checkout.");

            foreach (var entry in _pendingCheckouts)
            {
                ActionLogService.DetachDeferredGame(entry, HistoricalEmployeeName(entry.StoppedBy));
                if (entry.PaymentIntent is { } intent)
                    ActionLogService.FinishDeferredPayment(ledger, entry, HistoricalEmployeeName(intent.Actor));
            }
            _pendingCheckouts = ledger.GetOutstanding();
            return true;
        }
        catch (Exception error) when (IsDeferredFailure(error))
        {
            System.Diagnostics.Trace.WriteLine(error);
            MessageBox.Show("Не удалось восстановить отложенные ККМ. Не принимайте оплату повторно. Расчёты заблокированы до проверки.", "Отложенные ККМ");
            IsEnabled = false;
            return false;
        }
    }

    private static string HistoricalEmployeeName(CheckoutActor actor) =>
        EmployeeService.FindById(actor.EmployeeId)?.Name ??
        ActionLogService.Shifts.FirstOrDefault(shift => shift.Id == actor.ShiftId)?.EmployeeName ??
        throw new InvalidDataException("A recorded checkout employee is missing.");

    private IEnumerable<DeferredCheckout> PendingFor(ClubPlace place) =>
        _pendingCheckouts.Where(entry => entry.PlaceName.Equals(place.Name, StringComparison.OrdinalIgnoreCase));

    private void AnimateDeferredOutline(Border card, ClubPlace place)
    {
        if (place.IsTimeExpiredAwaitingAcknowledgement || !PendingFor(place).Any())
            return;
        var brush = new SolidColorBrush(Color.FromRgb(253, 224, 71));
        card.BorderBrush = brush;
        brush.BeginAnimation(SolidColorBrush.ColorProperty,
            new ColorAnimation(Color.FromRgb(146, 104, 27), TimeSpan.FromMilliseconds(520))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            });
    }

    private string WithDeferredCheckoutBadge(ClubPlace place, string current)
    {
        var pending = PendingFor(place).ToArray();
        if (pending.Length == 0)
            return current;
        string badge = pending.Length == 1
            ? $"Остановка {pending[0].StopNumber}: {pending[0].Total} сом"
            : $"Отложенные ККМ: {pending.Length}";
        return current.Length == 0 ? badge : current + "\n" + badge;
    }

    private void AddDeferredCheckoutMenuItems(ContextMenu menu, ClubPlace place)
    {
        var pending = PendingFor(place).OrderBy(entry => entry.StopNumber).ToArray();
        if (pending.Length == 0)
            return;
        menu.Items.Add(new Separator());
        foreach (var entry in pending)
        {
            var item = CreateMenuItem($"Остановка {entry.StopNumber} · {entry.Total} сом", () => OpenDeferredCheckout(entry.Id));
            item.BorderBrush = Brushes.Goldenrod;
            item.BorderThickness = new Thickness(1);
            menu.Items.Add(item);
        }
        menu.Opened += (_, _) => _deferredClickTimer?.Stop();
    }

    private bool ScheduleDeferredCheckout(ClubPlace place)
    {
        if (_deferredEditorOpen || PendingFor(place).FirstOrDefault() is not { } selected)
        {
            _deferredClickTimer?.Stop();
            return false;
        }
        if (_deferredClickTimer?.IsEnabled == true && _scheduledDeferredCheckoutId == selected.Id)
            return true;

        _deferredClickTimer?.Stop();
        Guid selectedId = selected.Id;
        _scheduledDeferredCheckoutId = selectedId;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _deferredClickTimer = timer;
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (IsActive && IsEnabled && !place.IsTimeExpiredAwaitingAcknowledgement &&
                _pendingCheckouts.Any(entry => entry.Id == selectedId))
                OpenDeferredCheckout(selectedId);
        };
        timer.Start();
        return true;
    }

    private bool TryDeferCheckout(ClubPlace place, Guid gameId, CheckoutActor actor,
        DateTimeOffset stoppedAt, List<CheckoutItem> items)
    {
        if (GetCurrentCheckoutActor() != actor || !place.IsOpenMode || !place.IsCalculating ||
            ActionLogService.GetActiveGameSessionByPlace(place.Name)?.Id != gameId)
        {
            MessageBox.Show("Сеанс или смена изменились. Откройте расчёт заново.", "Отложить ККМ");
            return false;
        }

        bool saved = false;
        try
        {
            var ledger = DeferredCheckoutLedger.ForCurrentClub();
            var lines = items.Select(DeferredCheckoutLine.Freeze).ToArray();
            DeferredCheckoutPosting.ValidateOrigins(lines, ActionLogService.GameSessions, paymentId: null);
            var entry = ledger.Defer(Guid.NewGuid(), gameId, place.Name, stoppedAt, actor, true, lines,
                deferredAt: new DateTimeOffset(ClubClock.Current.UtcNow));
            saved = true;
            _failedDeferredStops.Add(entry.Id);
            _pendingCheckouts = ledger.GetOutstanding();
            ActionLogService.DetachDeferredGame(entry, HistoricalEmployeeName(actor));
            ClearPlace(place);
            SaveActivePlacesToStorage(flushToDisk: true);
            _failedDeferredStops.Remove(entry.Id);
            DrawPlaces();
        }
        catch (Exception error) when (IsDeferredFailure(error))
        {
            System.Diagnostics.Trace.WriteLine(error);
            MessageBox.Show(saved
                ? "ККМ сохранён, но завершить остановку не удалось. Не запускайте новый сеанс на этом ТВ до восстановления."
                : "Не удалось сохранить отложенный ККМ. Исходный сеанс не закрыт.", "Отложить ККМ");
        }
        return saved;
    }

    private void OpenDeferredCheckout(Guid id)
    {
        _deferredClickTimer?.Stop();
        if (_deferredEditorOpen)
            return;
        var actor = GetCurrentCheckoutActor();
        if (actor is null)
        {
            MessageBox.Show("Откройте смену, чтобы провести расчёт.", "Отложенный ККМ");
            return;
        }

        _deferredEditorOpen = true;
        try
        {
            var ledger = DeferredCheckoutLedger.ForCurrentClub();
            if (ledger.Find(id) is not { IsPaid: false } entry)
                return;
            var original = ActionLogService.GetGameSession(entry.GameSessionId);
            if (original is null || !original.IsClosed || original.DeferredCheckoutId != id ||
                _failedDeferredStops.Contains(id))
                throw new InvalidOperationException("The original game stop has not been completed.");

            if (entry.PaymentIntent is null)
            {
                var window = new CashCheckoutWindow(HistoricalEmployeeName(actor), $"Остановка {entry.StopNumber}",
                    entry.Lines.Select(line => line.ToCheckoutItem()).ToList(), entry.PlaceName, entry.GameSessionId,
                    allowDefer: true) { Owner = this };

                if (window.ShowDialog() != true || window.ResultType != CashCheckoutResultType.Paid ||
                    window.PaymentRecord is not { } payment)
                    return;

                if (GetCurrentCheckoutActor() is not { } current || current != actor)
                    throw new InvalidOperationException("The checkout shift changed.");
                DeferredCheckoutPosting.ValidateOrigins(entry, ActionLogService.GameSessions, paymentId: null);
                entry = ledger.PreparePayment(id, entry.Revision, actor, current,
                    new DeferredPaymentIntent(payment.Id, actor, new DateTimeOffset(ClubClock.Current.UtcNow),
                        payment.CashAmount, payment.MBankAmount));
                _visibleDeferredReminderIds.Remove(id);
                RefreshDeferredReminderBanner();
            }

            ActionLogService.FinishDeferredPayment(ledger, entry, HistoricalEmployeeName(entry.PaymentIntent!.Actor));
            _pendingCheckouts = ledger.GetOutstanding();
            RefreshDeferredReminderBanner();
            UpdateMainCashText();
            DrawPlaces();
        }
        catch (Exception error) when (IsDeferredFailure(error))
        {
            System.Diagnostics.Trace.WriteLine(error);
            MessageBox.Show("Расчёт не подтверждён. Откройте эту остановку снова для проверки. Если деньги уже приняты, не принимайте их повторно.", "Отложенный ККМ");
        }
        finally
        {
            _deferredEditorOpen = false;
        }
    }

    private static bool IsDeferredFailure(Exception error) =>
        error is IOException or InvalidDataException or UnauthorizedAccessException
            or InvalidOperationException or JsonException or OverflowException;
}
