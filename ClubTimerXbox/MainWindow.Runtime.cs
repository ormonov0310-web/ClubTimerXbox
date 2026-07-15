using System;
using System.Linq;
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
        private bool _isFirebaseStatePushRunning;
        private bool _isFirebaseCommandCheckRunning;

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

            _firebaseCommandTimer.Interval = TimeSpan.FromSeconds(1);
            _firebaseCommandTimer.Tick += async (_, _) => await RunFirebaseCommandCheckAsync();

            _firebaseStatePushTimer.Interval = TimeSpan.FromSeconds(5);
            _firebaseStatePushTimer.Tick += async (_, _) => await RunFirebaseStatePushAsync();

            _firebaseCommandTimer.Start();
            _firebaseStatePushTimer.Start();

            _ = RunFirebaseCommandCheckAsync();
            _ = RunFirebaseStatePushAsync();
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
                await FirebaseSyncService.PushCurrentStateAsync(places);
                await AppUpdateService.CheckAndReportAsync(places);
                await RefreshSettingsUpdateIndicatorAsync(forceRefresh: false);
            }
            finally
            {
                _isFirebaseStatePushRunning = false;
            }
        }

        private void SaveRuntimeClosedNow()
        {
            AppRuntimeStateStorageService.SaveClosedNow();

            FirebaseEventService.PublishClubClosedAndWait(
                _notificationSessionId,
                EmployeeService.CurrentEmployee?.Name ?? "Не выбран",
                TimeSpan.FromSeconds(3)
            );

            _firebaseStatePushTimer.Stop();
            _firebaseCommandTimer.Stop();

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
