using System;
using System.Windows;
using System.Windows.Media;
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
        private bool _isExpired;
        private int _expiredElapsedSeconds;
        private int _expiredPenaltyAmount;

        public string PlaceName { get; }
        public event EventHandler? Acknowledged;

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

            if (_remainingSeconds <= 0)
                MarkExpired();
            else
                UpdateMessageText();

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
            Acknowledged?.Invoke(this, EventArgs.Empty);

            if (IsVisible)
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
            if (_isExpired)
            {
                if (_expiredPenaltyAmount > 0)
                {
                    MessageText.Text =
                        $"Тариф закончился. Просрочка: {FormatElapsed(_expiredElapsedSeconds)}.\n" +
                        $"Штраф сотруднику: {_expiredPenaltyAmount} сом.";
                }
                else
                {
                    int firstChargeAtSeconds =
                        (ExpiredSessionPenaltyService.GraceMinutes + 1) * 60;
                    int secondsUntilCharge = Math.Max(
                        0,
                        firstChargeAtSeconds - _expiredElapsedSeconds);
                    MessageText.Text = secondsUntilCharge > 0
                        ? $"Тариф закончился. До первого штрафа: " +
                          $"{secondsUntilCharge / 60:00}:{secondsUntilCharge % 60:00}.\n" +
                          "Нажмите «Понятно», чтобы освободить место."
                        : "Тариф закончился. Началась первая штрафная минута.\n" +
                          "Нажмите «Понятно», чтобы освободить место.";
                }
                return;
            }

            MessageText.Text = $"Осталось: {FormatRemainingTime(_remainingSeconds)}";
        }

        public void MarkExpired()
        {
            if (_isExpired)
                return;

            AlarmSoundService.PlayOnce(_soundName);

            _isExpired = true;
            _remainingSeconds = 0;
            _isSoundActive = false;

            Title = "Время вышло";
            TitleText.Text = $"{PlaceName}: время вышло";
            TitleText.Foreground = new SolidColorBrush(Color.FromRgb(248, 113, 113));
            AlarmBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(248, 113, 113));
            AlarmBorder.Background = new SolidColorBrush(Color.FromRgb(69, 10, 10));
            OkButton.Background = new SolidColorBrush(Color.FromRgb(220, 38, 38));

            UpdateMessageText();
        }

        public void UpdateExpiredPenalty(int elapsedSeconds, int penaltyAmount)
        {
            if (!_isExpired)
                MarkExpired();

            _expiredElapsedSeconds = Math.Max(0, elapsedSeconds);
            _expiredPenaltyAmount = Math.Max(0, penaltyAmount);
            UpdateMessageText();
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

        private static string FormatElapsed(int seconds)
        {
            int hours = seconds / 3600;
            int minutes = seconds % 3600 / 60;
            int restSeconds = seconds % 60;
            return hours > 0
                ? $"{hours:00}:{minutes:00}:{restSeconds:00}"
                : $"{minutes:00}:{restSeconds:00}";
        }
    }
}
