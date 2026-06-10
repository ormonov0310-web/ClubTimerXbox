using System;
using System.Windows;
using System.Windows.Threading;
using ClubTimerXbox.Services;

namespace ClubTimerXbox
{
    public partial class WarningAlarmWindow : Window
    {
        private readonly DispatcherTimer _soundTimer = new DispatcherTimer();
        private readonly DispatcherTimer _durationTimer = new DispatcherTimer();

        private readonly string _soundName;
        private readonly int _durationSeconds;
        private int _playedSeconds;

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

            TitleText.Text = $"Скоро {placeName} время заканчивается";
            MessageText.Text = $"Осталось: {remainingSeconds} секунд";

            _soundTimer.Interval = TimeSpan.FromSeconds(1);
            _soundTimer.Tick += SoundTimer_Tick;

            _durationTimer.Interval = TimeSpan.FromSeconds(1);
            _durationTimer.Tick += DurationTimer_Tick;

            Loaded += WarningAlarmWindow_Loaded;
            Closed += WarningAlarmWindow_Closed;
        }

        private void WarningAlarmWindow_Loaded(object sender, RoutedEventArgs e)
        {
            AlarmSoundService.PlayOnce(_soundName);

            _soundTimer.Start();

            if (_durationSeconds > 0)
                _durationTimer.Start();
        }

        private void SoundTimer_Tick(object? sender, EventArgs e)
        {
            AlarmSoundService.PlayOnce(_soundName);
        }

        private void DurationTimer_Tick(object? sender, EventArgs e)
        {
            _playedSeconds++;

            if (_playedSeconds >= _durationSeconds)
            {
                StopSoundTimers();
            }
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
            _soundTimer.Stop();
            _durationTimer.Stop();
        }
    }
}