namespace ClubTimerXbox.Models
{
    public class AlarmSettings
    {
        public bool IsEnabled { get; set; } = true;

        // За сколько секунд до конца сработает будильник.
        // По умолчанию 60 секунд.
        public int TriggerBeforeEndSeconds { get; set; } = 60;

        // Какой звук использовать.
        // Standard / Beep / Asterisk / Exclamation / Hand
        public string SoundName { get; set; } = "short-calm-pleasant-notification-sound.mp3";

        // Сколько секунд играть звук.
        // Если 0 — играть пока не нажмут OK.
        public int SoundDurationSeconds { get; set; } = 10;

        public bool IsHoverSoundEnabled { get; set; } = true;

        public bool IsClickSoundEnabled { get; set; } = true;

        public bool IsActionSoundEnabled { get; set; } = true;
    }
}
