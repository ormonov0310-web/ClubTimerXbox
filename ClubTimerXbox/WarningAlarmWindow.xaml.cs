using System;
using System.Windows;
using System.Windows.Threading;
using ClubTimerXbox.Services;

namespace ClubTimerXbox
{
    public partial class WarningAlarmWindow : Window
    {
        private readonly DispatcherTimer _tickTimer = new DispatcherTimer();
        private static readonly TimeSpan SoundRepeatGap = TimeSpan.FromSeconds(4);

        private readonly string _soundName;
        private readonly int _durationSeconds;
        private DateTime _nextSoundAllowedAt = DateTime.MinValue;
        private int _remainingSeconds;
        private int _elapsedSeconds;
        private bool _isSoundActive = true;

        public string PlaceName { get; }

        public WarningAlarmWindow(
            string placeName,
            int remainingSeconds,
            string soundName,
            int durationSeconds)
        {
            InitializeComponent();

            PlaceName = placeName;
            _soundName = soundName;
            _durationSeconds = durationSeconds;
            _remainingSeconds = Math.Max(0, remainingSeconds);

            TitleText.Text = $"Скоро {placeName} время заканчивается";
            UpdateMessageText();

            _tickTimer.Interval = TimeSpan.FromSeconds(1);
            _tickTimer.Tick += TickTimer_Tick;

            Loaded += WarningAlarmWindow_Loaded;
            Closed += WarningAlarmWindow_Closed;
        }

        private void WarningAlarmWindow_Loaded(object sender, RoutedEventArgs e)
        {
            TryPlaySound();
            _tickTimer.Start();
        }

        private void TickTimer_Tick(object? sender, EventArgs e)
        {
            if (_remainingSeconds > 0)
                _remainingSeconds--;

            _elapsedSeconds++;
            UpdateMessageText();

            if (_remainingSeconds <= 0)
                _isSoundActive = false;

            if (_durationSeconds > 0 && _elapsedSeconds >= _durationSeconds)
                _isSoundActive = false;

            TryPlaySound();
        }

        private void TryPlaySound()
        {
            if (!_isSoundActive)
                return;

            var now = DateTime.UtcNow;

            if (now < _nextSoundAllowedAt)
                return;

            AlarmSoundService.PlayOnce(_soundName);
            _nextSoundAllowedAt = now.Add(SoundRepeatGap);
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        public void StopAlarm()
        {
            StopSoundTimers();

            if (IsVisible)
                Close();
        }

        private void WarningAlarmWindow_Closed(object? sender, EventArgs e)
        {
            StopSoundTimers();
        }

        private void StopSoundTimers()
        {
            _isSoundActive = false;
            _tickTimer.Stop();
        }

        private void UpdateMessageText()
        {
            MessageText.Text = $"Осталось: {FormatRemainingTime(_remainingSeconds)}";
        }

        private static string FormatRemainingTime(int seconds)
        {
            if (seconds <= 0)
                return "0 секунд";

            if (seconds < 60)
                return $"{seconds} сек.";

            int minutes = seconds / 60;
            int restSeconds = seconds % 60;

            return restSeconds == 0
                ? $"{minutes} мин."
                : $"{minutes} мин. {restSeconds} сек.";
        }
    }
}
