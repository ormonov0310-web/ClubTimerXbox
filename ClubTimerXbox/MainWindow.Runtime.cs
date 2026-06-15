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

        private readonly DispatcherTimer _firebaseSyncTimer = new DispatcherTimer();

        private void CheckRuntimeShiftAfterStart()
        {
            if (_runtimeShiftChecked)
                return;

            _runtimeShiftChecked = true;

            StartOwnerApiServer();
            StartFirebaseSync();

            var state = AppRuntimeStateStorageService.Load();

            AppRuntimeStateStorageService.SaveOpenedNow();

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
            _firebaseSyncTimer.Stop();

            _firebaseSyncTimer.Interval = TimeSpan.FromSeconds(5);
            _firebaseSyncTimer.Tick += async (_, _) =>
            {
                var places = _places.ToList();
                await FirebaseSyncService.PushCurrentStateAsync(places);
                await FirebaseSyncService.CheckCommandsAsync(places);
                await AppUpdateService.CheckAndReportAsync(places);
            };

            _firebaseSyncTimer.Start();

            _ = FirebaseSyncService.PushCurrentStateAsync(_places.ToList());
            _ = FirebaseSyncService.CheckCommandsAsync(_places.ToList());
            _ = AppUpdateService.CheckAndReportAsync(_places.ToList());
        }

        private void SaveRuntimeClosedNow()
        {
            AppRuntimeStateStorageService.SaveClosedNow();

            _firebaseSyncTimer.Stop();

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
