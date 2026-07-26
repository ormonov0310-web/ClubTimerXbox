using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using ClubTimerXbox.Services;

namespace ClubTimerXbox
{
    public partial class MainWindow
    {
        private bool _runtimeShiftChecked = false;
        private readonly string _notificationSessionId = Guid.NewGuid().ToString("N");

        private readonly DispatcherTimer _firebaseStatePushTimer = new DispatcherTimer();
        private readonly DispatcherTimer _firebaseCommandTimer = new DispatcherTimer();
        private readonly DispatcherTimer _firebaseOverviewMonitorTimer = new DispatcherTimer();
        private bool _isFirebaseStatePushRunning;
        private bool _isFirebaseCommandCheckRunning;
        private string _lastFirebaseOverviewSignature = "";
        private string _pendingFirebaseOverviewSignature = "";
        private DateTime _pendingFirebaseOverviewChangedAt = DateTime.MinValue;

        private void CheckRuntimeShiftAfterStart()
        {
            if (_runtimeShiftChecked)
                return;

            _runtimeShiftChecked = true;

            StartOwnerApiServer();
            StartFirebaseSync();

            var state = AppRuntimeStateStorageService.Load();

            AppRuntimeStateStorageService.SaveOpenedNow();
            _ = FirebaseEventService.PublishClubOpenedAsync(
                _notificationSessionId,
                EmployeeService.CurrentEmployee?.Name ?? "Не выбран"
            );

            if (state.LastClosedAt == null)
                return;

            var currentShift = ActionLogService.CurrentShift;

            if (currentShift == null)
                return;

            DateTime lastClosedAt = state.LastClosedAt.Value;
            DateTime now = DateTime.Now;

            if (lastClosedAt < currentShift.StartedAt)
                return;

            TimeSpan closedDuration = now - lastClosedAt;

            if (closedDuration.TotalMinutes < 0)
                return;

            if (closedDuration.TotalMinutes < 30)
                return;

            ActionLogService.CloseCurrentShiftAt(lastClosedAt);

            UpdateCurrentEmployeeText();

            MessageBox.Show(
                $"Прошлая смена автоматически закрыта.\n\n" +
                $"Сотрудник: {currentShift.EmployeeName}\n" +
                $"Закрытие программы: {lastClosedAt:dd.MM.yyyy HH:mm}\n" +
                $"Программа была закрыта: {FormatClosedDuration(closedDuration)}\n\n" +
                $"Это время не будет накручиваться в статистику сотрудника.",
                "Смена закрыта автоматически"
            );
        }

        private void StartOwnerApiServer()
        {
            OwnerApiServer.Start(() => _places.ToList());
        }

        private void StartFirebaseSync()
        {
            _firebaseStatePushTimer.Stop();
            _firebaseCommandTimer.Stop();
            _firebaseOverviewMonitorTimer.Stop();

            _firebaseCommandTimer.Interval = TimeSpan.FromSeconds(2);
            _firebaseCommandTimer.Tick += async (_, _) => await RunFirebaseCommandCheckAsync();

            _firebaseStatePushTimer.Interval = TimeSpan.FromSeconds(90);
            _firebaseStatePushTimer.Tick += async (_, _) => await RunFirebaseStatePushAsync();

            _firebaseOverviewMonitorTimer.Interval = TimeSpan.FromSeconds(1);
            _firebaseOverviewMonitorTimer.Tick += (_, _) => MonitorFirebaseOverviewState();

            _firebaseCommandTimer.Start();
            _firebaseStatePushTimer.Start();
            _firebaseOverviewMonitorTimer.Start();

            _ = RunFirebaseCommandCheckAsync();
            string initialSignature = FirebaseSyncService.BuildOverviewSignature(_places);
            _pendingFirebaseOverviewSignature = initialSignature;
            _pendingFirebaseOverviewChangedAt = DateTime.UtcNow.AddSeconds(-2);
            _ = RunFirebaseOverviewPushAsync(initialSignature, includeMaintenance: true);
        }

        private async Task RunFirebaseCommandCheckAsync()
        {
            if (!FirebaseConnectionService.CanConnect || _isFirebaseCommandCheckRunning)
                return;

            _isFirebaseCommandCheckRunning = true;

            try
            {
                if (!await FirebaseChannelBindingService.EnsureCurrentBindingAsync())
                    return;

                await FirebaseSyncService.CheckCommandsAsync(_places.ToList());
            }
            finally
            {
                _isFirebaseCommandCheckRunning = false;
            }
        }

        private async Task RunFirebaseStatePushAsync()
        {
            if (!FirebaseConnectionService.CanConnect || _isFirebaseStatePushRunning)
                return;

            _isFirebaseStatePushRunning = true;

            try
            {
                if (!await FirebaseChannelBindingService.EnsureCurrentBindingAsync())
                    return;

                var places = _places.ToList();
                await FirebaseEventService.FlushPendingAsync();
                await FirebaseSyncService.PushHeartbeatAsync(places);
                await AppUpdateService.CheckAndReportAsync(places);
                await RefreshSettingsUpdateIndicatorAsync(forceRefresh: false);
            }
            finally
            {
                _isFirebaseStatePushRunning = false;
            }
        }

        private void MonitorFirebaseOverviewState()
        {
            if (!FirebaseConnectionService.CanConnect)
                return;

            string signature = FirebaseSyncService.BuildOverviewSignature(_places);
            if (signature == _lastFirebaseOverviewSignature)
            {
                _pendingFirebaseOverviewSignature = "";
                _pendingFirebaseOverviewChangedAt = DateTime.MinValue;
                return;
            }

            if (signature != _pendingFirebaseOverviewSignature)
            {
                _pendingFirebaseOverviewSignature = signature;
                _pendingFirebaseOverviewChangedAt = DateTime.UtcNow;
                return;
            }

            if (_isFirebaseStatePushRunning ||
                DateTime.UtcNow - _pendingFirebaseOverviewChangedAt <
                    TimeSpan.FromMilliseconds(1500))
            {
                return;
            }

            _ = RunFirebaseOverviewPushAsync(signature, includeMaintenance: false);
        }

        private async Task RunFirebaseOverviewPushAsync(
            string signature,
            bool includeMaintenance)
        {
            if (!FirebaseConnectionService.CanConnect || _isFirebaseStatePushRunning)
                return;

            _isFirebaseStatePushRunning = true;

            try
            {
                if (!await FirebaseChannelBindingService.EnsureCurrentBindingAsync())
                    return;

                var places = _places.ToList();
                if (includeMaintenance)
                    await FirebaseEventService.FlushPendingAsync();

                bool published = await FirebaseSyncService.PushOverviewStateAsync(places);
                if (!published)
                    return;

                _lastFirebaseOverviewSignature = signature;
                _pendingFirebaseOverviewSignature = "";
                _pendingFirebaseOverviewChangedAt = DateTime.MinValue;

                if (includeMaintenance)
                {
                    await AppUpdateService.CheckAndReportAsync(places);
                    await RefreshSettingsUpdateIndicatorAsync(forceRefresh: false);
                }
            }
            finally
            {
                _isFirebaseStatePushRunning = false;
            }
        }

        private void SaveRuntimeClosedNow()
        {
            AppRuntimeStateStorageService.SaveClosedNow();

            try
            {
                FirebaseSyncService.PushClosedStateAsync(
                    EmployeeService.CurrentEmployee?.Name ?? "Не выбран"
                ).Wait(TimeSpan.FromSeconds(3));
            }
            catch
            {
                // Явное закрытие повторно подтвердит обычное событие клуба.
            }

            FirebaseEventService.PublishClubClosedAndWait(
                _notificationSessionId,
                EmployeeService.CurrentEmployee?.Name ?? "Не выбран",
                TimeSpan.FromSeconds(3)
            );

            _firebaseStatePushTimer.Stop();
            _firebaseCommandTimer.Stop();
            _firebaseOverviewMonitorTimer.Stop();

            OwnerApiServer.Stop();
        }

        private string FormatClosedDuration(TimeSpan duration)
        {
            int hours = (int)duration.TotalHours;
            int minutes = duration.Minutes;

            if (hours <= 0)
                return $"{minutes} мин";

            return $"{hours} ч {minutes} мин";
        }
    }
}
