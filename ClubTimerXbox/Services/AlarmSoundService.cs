using System;
using System.IO;
using System.Linq;
using System.Media;
using System.Windows.Media;

namespace ClubTimerXbox.Services
{
    public static class AlarmSoundService
    {
        private static readonly System.Collections.Generic.Dictionary<string, SoundPlayer> WavPlayers = new System.Collections.Generic.Dictionary<string, SoundPlayer>();
        private static readonly System.Collections.Generic.Dictionary<string, MediaPlayer> MediaPlayers = new System.Collections.Generic.Dictionary<string, MediaPlayer>();
        private static readonly string[] SupportedExtensions = { ".mp3", ".wav", ".ogg", ".m4a" };

        public static void PlayOnce(string soundName)
        {
            soundName = NormalizeSoundName(soundName);
            string path = GetAlarmSoundPath(soundName);

            if (PlayFile(path))
                return;

            PlaySystemSound(soundName);
        }

        public static string[] GetAvailableAlarmSoundNames()
        {
            string directory = GetAlarmSoundsDirectory();

            if (!Directory.Exists(directory))
                return Array.Empty<string>();

            return Directory
                .GetFiles(directory)
                .Where(path => SupportedExtensions.Contains(Path.GetExtension(path).ToLowerInvariant()))
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public static bool PlayFile(string path)
        {
            if (!File.Exists(path))
                return false;

            string extension = Path.GetExtension(path).ToLowerInvariant();

            try
            {
                if (extension == ".wav")
                {
                    var player = GetOrCreateWavPlayer(path);
                    player.Play();
                    return true;
                }

                var mediaPlayer = GetOrCreateMediaPlayer(path);
                mediaPlayer.Stop();
                mediaPlayer.Position = TimeSpan.Zero;
                mediaPlayer.Play();
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static string NormalizeSoundName(string soundName)
        {
            if (string.IsNullOrWhiteSpace(soundName))
                return GetAvailableAlarmSoundNames().FirstOrDefault() ?? "Exclamation";

            soundName = soundName.Trim();

            if (GetAvailableAlarmSoundNames().Any(name =>
                    name.Equals(soundName, StringComparison.OrdinalIgnoreCase)))
                return soundName;

            if (soundName == "Standard")
                return "Standard";

            if (soundName == "Beep")
                return "Beep";

            if (soundName == "Asterisk")
                return "Asterisk";

            if (soundName == "Hand")
                return "Hand";

            return GetAvailableAlarmSoundNames().FirstOrDefault() ?? "Exclamation";
        }

        public static string GetDisplayName(string soundName)
        {
            soundName = NormalizeSoundName(soundName);

            return soundName;
        }

        private static string GetAlarmSoundPath(string fileName)
        {
            return Path.Combine(GetAlarmSoundsDirectory(), fileName);
        }

        private static string GetAlarmSoundsDirectory()
        {
            return Path.Combine(AppContext.BaseDirectory, "Assets", "AlarmSounds");
        }

        private static SoundPlayer GetOrCreateWavPlayer(string path)
        {
            if (WavPlayers.TryGetValue(path, out var existingPlayer))
                return existingPlayer;

            var player = new SoundPlayer(path);
            player.Load();
            WavPlayers[path] = player;
            return player;
        }

        private static MediaPlayer GetOrCreateMediaPlayer(string path)
        {
            if (MediaPlayers.TryGetValue(path, out var existingPlayer))
                return existingPlayer;

            var player = new MediaPlayer();
            player.Open(new Uri(path, UriKind.Absolute));
            MediaPlayers[path] = player;
            return player;
        }

        private static void PlaySystemSound(string soundName)
        {
            if (soundName == "Standard" || soundName == "Beep")
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

            SystemSounds.Exclamation.Play();
        }
    }
}
