using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using ClubTimerXbox.Services;

namespace ClubTimerXbox;

public partial class MainWindow
{
    private bool _finalCloseStarted;
    private bool _finalCloseReady;

    private async void MainWindow_FinalClosing(object? sender, CancelEventArgs e)
    {
        if (e.Cancel || _finalCloseReady || AppUpdateShutdownCoordinator.IsPlannedUpdate) return;
        e.Cancel = true;
        if (_finalCloseStarted) return;
        _finalCloseStarted = true;
        IsEnabled = false;
        _mainTimer.Stop();
        _cashTextTimer.Stop();
        _firebaseStatePushTimer.Stop();
        _firebaseCommandTimer.Stop();
        _firebaseOverviewMonitorTimer.Stop();
        string employeeName = EmployeeService.CurrentEmployee?.Name ?? "Не выбран";
        var progress = new ClubCloseProgressWindow { Owner = this };
        progress.Show();
        var elapsed = Stopwatch.StartNew();
        var countdown = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        countdown.Tick += (_, _) => progress.SetRemaining(Math.Max(0,
            5 - (int)elapsed.Elapsed.TotalSeconds));
        countdown.Start();
        bool delivered = false;
        bool localPrepared = false;
        bool abortClose = false;
        try
        {
            SaveActivePlacesToStorage();
            ActionLogService.CloseCurrentShift();
            AppRuntimeStateStorageService.SaveClosedNow();
            _runtimeCloseSaved = true;
            FirebaseSyncService.RequestCurrentStatePublish(urgent: true);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            Task<bool> notification = FirebaseEventService.PublishClubClosedAsync(
                _notificationSessionId, employeeName,
                FirebaseSyncService.CreateClosedPresenceUpdate(employeeName, _places), deadline.Token,
                () => FirebaseSyncService.BeginClosing(_places.ToList()));
            localPrepared = true;
            delivered = await ClubClosedState.DeliverAsync(notification,
                presencePublished => FirebaseSyncService.PublishFinalClosedStateAsync(
                    _places.ToList(), employeeName, deadline.Token, presencePublished), deadline.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            abortClose = !localPrepared;
        }
        finally
        {
            countdown.Stop();
            progress.Finish(delivered);
            if (!delivered) await Task.Delay(700);
            progress.Close();
            IsEnabled = true;
            if (abortClose)
            {
                _finalCloseStarted = false;
                _runtimeCloseSaved = false;
                _mainTimer.Start();
                _cashTextTimer.Start();
                _firebaseStatePushTimer.Start();
                _firebaseCommandTimer.Start();
                _firebaseOverviewMonitorTimer.Start();
                System.Windows.MessageBox.Show("Не удалось сохранить данные закрытия. Приложение осталось открыто. Проверьте свободное место и повторите закрытие.", "Ошибка сохранения");
            }
            else
            {
                OwnerApiServer.Stop();
                _finalCloseReady = true;
                _allowWindowClose = true;
                Close();
            }
        }
    }
}
