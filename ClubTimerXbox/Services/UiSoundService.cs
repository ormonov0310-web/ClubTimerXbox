using System;
using System.IO;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace ClubTimerXbox.Services
{
    public static class UiSoundService
    {
        private static bool _isEnabled;
        private static DateTime _lastHoverSoundAt = DateTime.MinValue;
        private static DateTime _lastActionSoundAt = DateTime.MinValue;
        private static bool _hasLastCardHover;
        private static string _lastCardHoverKey = string.Empty;
        private static Point _lastCardHoverPosition;

        public static void EnableGlobalUiSounds()
        {
            if (_isEnabled)
                return;

            _isEnabled = true;

            EventManager.RegisterClassHandler(
                typeof(ButtonBase),
                UIElement.MouseEnterEvent,
                new RoutedEventHandler((_, _) => PlayHover()),
                handledEventsToo: true);

            EventManager.RegisterClassHandler(
                typeof(ButtonBase),
                ButtonBase.ClickEvent,
                new RoutedEventHandler((_, _) => PlayAction()),
                handledEventsToo: true);
        }

        public static void PlayHover()
        {
            if (!AlarmSettingsService.Current.IsHoverSoundEnabled)
                return;

            if (!CanPlayNow(ref _lastHoverSoundAt, 80))
                return;

            AlarmSoundService.PlayFile(GetUiSoundPath("hover.mp3"));
        }

        public static bool TryPlayCardHover(string hoverKey)
        {
            var currentPosition = GetMousePosition();

            if (_hasLastCardHover &&
                string.Equals(_lastCardHoverKey, hoverKey, StringComparison.Ordinal) &&
                IsNear(currentPosition, _lastCardHoverPosition, 3))
            {
                return false;
            }

            _lastCardHoverKey = hoverKey;
            _lastCardHoverPosition = currentPosition;
            _hasLastCardHover = true;

            PlayHover();
            return true;
        }

        public static void PlayAction()
        {
            if (!AlarmSettingsService.Current.IsClickSoundEnabled)
                return;

            if (!CanPlayNow(ref _lastActionSoundAt, 80))
                return;

            AlarmSoundService.PlayFile(GetUiSoundPath("click.mp3"));
        }

        public static void PlayTariffAction()
        {
            if (!AlarmSettingsService.Current.IsActionSoundEnabled)
                return;

            if (!CanPlayNow(ref _lastActionSoundAt, 80))
                return;

            AlarmSoundService.PlayFile(GetUiSoundPath("action.mp3"));
        }

        private static string GetUiSoundPath(string fileName)
        {
            return Path.Combine(AppContext.BaseDirectory, "Assets", "UiSounds", fileName);
        }

        private static Point GetMousePosition()
        {
            var window = Application.Current?.MainWindow;

            return window == null
                ? new Point(double.NaN, double.NaN)
                : Mouse.GetPosition(window);
        }

        private static bool IsNear(Point first, Point second, double tolerance)
        {
            if (double.IsNaN(first.X) || double.IsNaN(first.Y) ||
                double.IsNaN(second.X) || double.IsNaN(second.Y))
            {
                return false;
            }

            return Math.Abs(first.X - second.X) <= tolerance &&
                   Math.Abs(first.Y - second.Y) <= tolerance;
        }

        private static bool CanPlayNow(ref DateTime lastPlayedAt, int minGapMilliseconds)
        {
            var now = DateTime.UtcNow;

            if ((now - lastPlayedAt).TotalMilliseconds < minGapMilliseconds)
                return false;

            lastPlayedAt = now;
            return true;
        }
    }
}
