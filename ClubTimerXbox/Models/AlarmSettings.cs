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
        public string SoundName { get; set; } = "Exclamation";

        // Сколько секунд играть звук.
        // Если 0 — играть пока не нажмут OK.
        public int SoundDurationSeconds { get; set; } = 10;
    }
}