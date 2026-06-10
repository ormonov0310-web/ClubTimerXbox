using System.Media;

namespace ClubTimerXbox.Services
{
    public static class AlarmSoundService
    {
        public static void PlayOnce(string soundName)
        {
            soundName = NormalizeSoundName(soundName);

            if (soundName == "Standard")
            {
                SystemSounds.Beep.Play();
                return;
            }

            if (soundName == "Beep")
            {
                SystemSounds.Beep.Play();
                return;
            }

            if (soundName == "Asterisk")
            {
                SystemSounds.Asterisk.Play();
                return;
            }

            if (soundName == "Hand")
            {
                SystemSounds.Hand.Play();
                return;
            }

            // По умолчанию.
            SystemSounds.Exclamation.Play();
        }

        public static string NormalizeSoundName(string soundName)
        {
            if (soundName == "Standard")
                return "Standard";

            if (soundName == "Beep")
                return "Beep";

            if (soundName == "Asterisk")
                return "Asterisk";

            if (soundName == "Hand")
                return "Hand";

            return "Exclamation";
        }

        public static string GetDisplayName(string soundName)
        {
            soundName = NormalizeSoundName(soundName);

            if (soundName == "Standard")
                return "Стандартный";

            if (soundName == "Beep")
                return "Короткий";

            if (soundName == "Asterisk")
                return "Мягкий";

            if (soundName == "Hand")
                return "Тревога";

            return "Будильник";
        }
    }
}