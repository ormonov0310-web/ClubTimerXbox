using System;
using System.Windows;
using ClubTimerXbox.Models;
using ClubTimerXbox.Services;

namespace ClubTimerXbox;

public partial class MainWindow
{
    private bool TryRestoreActivePlaces()
    {
        try
        {
            RestoreActivePlacesFromStorage();
            return true;
        }
        catch (Exception error) when (IsDeferredFailure(error))
        {
            System.Diagnostics.Trace.WriteLine(error);
            IsEnabled = false;
            MessageBox.Show("Не удалось восстановить состояние ТВ. Таймеры не запущены.", "Восстановление сеансов");
            return false;
        }
    }

    private void ToggleSessionPause(ClubPlace place)
    {
        if (!EnsureActiveEmployeeActivity(GetCurrentEmployeeName()) ||
            ActionLogService.GetActiveGameSessionByPlace(place.Name) is not { } game)
            return;

        var previousState = place.PauseState;
        var previousStart = place.StartTime;
        var previousRemaining = place.RemainingSeconds;
        var now = DateTime.Now;
        bool changed = place.IsPaused
            ? SessionPausePolicy.TryResume(place, game.Id, now)
            : SessionPausePolicy.TryPause(place, game.Id, now);
        if (!changed)
            return;

        try
        {
            SaveActivePlacesToStorage(flushToDisk: true);
        }
        catch (Exception error) when (IsDeferredFailure(error))
        {
            place.PauseState = previousState;
            place.StartTime = previousStart;
            place.RemainingSeconds = previousRemaining;
            System.Diagnostics.Trace.WriteLine(error);
            MessageBox.Show("Пауза не сохранена. Состояние ТВ не изменено.", "Пауза");
            return;
        }

        try
        {
            ActionLogService.SavePauseInterval(game, place.PauseState!);
        }
        catch (Exception error) when (IsDeferredFailure(error))
        {
            System.Diagnostics.Trace.WriteLine(error);
            _mainTimer.Stop();
            IsEnabled = false;
            MessageBox.Show("Состояние ТВ сохранено. Не удалось записать журнал паузы; требуется перезапуск.", "Пауза");
            return;
        }

        if (place.IsPaused)
            CloseAlarmWindowForPlace(place.Name);
        DrawPlaces();
    }
}
