using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Media;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;

namespace ClubTimerXbox.Services
{
    public static class AlarmSoundService
    {
        private enum AudioBackend
        {
            Auto,
            MediaPlayer,
            Mci
        }

        private sealed class MediaPlayerEntry
        {
            public required string Path { get; init; }
            public required MediaPlayer Player { get; init; }
            public bool IsOpened { get; set; }
        }

        private sealed class MciPlayback
        {
            public required string Alias { get; init; }
            public required CancellationTokenSource CleanupCancellation { get; init; }
        }

        private static readonly object MediaPlayerSync = new object();
        private static readonly object MciSync = new object();
        private static readonly object PreferenceSync = new object();
        private static readonly object LogSync = new object();
        private static readonly Dictionary<string, SoundPlayer> WavPlayers = new Dictionary<string, SoundPlayer>();
        private static readonly Dictionary<string, MediaPlayerEntry> MediaPlayers = new Dictionary<string, MediaPlayerEntry>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, MciPlayback> MciPlaybacks = new Dictionary<string, MciPlayback>(StringComparer.OrdinalIgnoreCase);
        private static readonly string[] SupportedExtensions = { ".mp3", ".wav", ".ogg", ".m4a" };
        private static readonly string AudioDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ClubTimerXbox");
        private static readonly string BackendPreferencePath = Path.Combine(AudioDataDirectory, "audio_backend.txt");
        private static readonly string AudioLogPath = Path.Combine(AudioDataDirectory, "audio.log");
        private static AudioBackend _preferredBackend = LoadPreferredBackend();
        private static long _mciSequence;

        static AlarmSoundService()
        {
            AppDomain.CurrentDomain.ProcessExit += (_, _) => CloseAllMciPlaybacks();
        }

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

            path = Path.GetFullPath(path);
            string extension = Path.GetExtension(path).ToLowerInvariant();

            if (extension == ".wav")
            {
                try
                {
                    var player = GetOrCreateWavPlayer(path);
                    player.Play();
                    return true;
                }
                catch (Exception ex)
                {
                    LogAudioIssue($"WAV playback failed: {path}", ex);
                    return false;
                }
            }

            if (extension == ".mp3" && GetPreferredBackend() == AudioBackend.Mci)
            {
                if (TryPlayWithMci(path))
                    return true;

                LogAudioIssue($"MCI playback failed; trying MediaPlayer: {path}");
                return TryPlayWithMediaPlayer(path);
            }

            if (TryPlayWithMediaPlayer(path))
                return true;

            return extension == ".mp3" && TryPlayWithMci(path);
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

        private static bool TryPlayWithMediaPlayer(string path)
        {
            MediaPlayerEntry? entry = null;

            try
            {
                entry = GetOrCreateMediaPlayer(path);

                if (entry.IsOpened)
                {
                    entry.Player.Stop();
                    entry.Player.Position = TimeSpan.Zero;
                }

                entry.Player.Play();
                return true;
            }
            catch (Exception ex)
            {
                RemoveMediaPlayer(path, entry);
                LogAudioIssue($"MediaPlayer playback failed: {path}", ex);
                return false;
            }
        }

        private static MediaPlayerEntry GetOrCreateMediaPlayer(string path)
        {
            lock (MediaPlayerSync)
            {
                if (MediaPlayers.TryGetValue(path, out var existingEntry))
                    return existingEntry;

                var player = new MediaPlayer();
                var entry = new MediaPlayerEntry
                {
                    Path = path,
                    Player = player
                };

                player.MediaOpened += (_, _) => HandleMediaOpened(entry);
                player.MediaFailed += (_, args) => HandleMediaFailed(entry, args.ErrorException);
                MediaPlayers[path] = entry;

                try
                {
                    player.Open(new Uri(path, UriKind.Absolute));
                }
                catch
                {
                    MediaPlayers.Remove(path);
                    player.Close();
                    throw;
                }

                return entry;
            }
        }

        private static void HandleMediaOpened(MediaPlayerEntry entry)
        {
            lock (MediaPlayerSync)
            {
                if (!MediaPlayers.TryGetValue(entry.Path, out var currentEntry) ||
                    !ReferenceEquals(currentEntry, entry))
                {
                    return;
                }

                entry.IsOpened = true;
            }

            SetPreferredBackend(AudioBackend.MediaPlayer);
        }

        private static void HandleMediaFailed(MediaPlayerEntry entry, Exception error)
        {
            RemoveMediaPlayer(entry.Path, entry);
            LogAudioIssue($"MediaPlayer reported an asynchronous failure: {entry.Path}", error);

            if (Path.GetExtension(entry.Path).Equals(".mp3", StringComparison.OrdinalIgnoreCase) &&
                TryPlayWithMci(entry.Path))
            {
                return;
            }

            LogAudioIssue($"All MP3 playback backends failed: {entry.Path}");
            SystemSounds.Exclamation.Play();
        }

        private static void RemoveMediaPlayer(string path, MediaPlayerEntry? expectedEntry)
        {
            MediaPlayerEntry? removedEntry = null;

            lock (MediaPlayerSync)
            {
                if (!MediaPlayers.TryGetValue(path, out var currentEntry))
                    return;

                if (expectedEntry != null && !ReferenceEquals(currentEntry, expectedEntry))
                    return;

                MediaPlayers.Remove(path);
                removedEntry = currentEntry;
            }

            try
            {
                removedEntry.Player.Close();
            }
            catch
            {
            }
        }

        private static bool TryPlayWithMci(string path)
        {
            if (path.Contains('"'))
            {
                LogAudioIssue($"MCI cannot open a path containing a quote: {path}");
                return false;
            }

            StopMciPlayback(path);

            string alias = $"clubtimer_{Environment.ProcessId}_{Interlocked.Increment(ref _mciSequence)}";
            uint openResult;

            try
            {
                openResult = MciSendString(
                    $"open \"{path}\" type mpegvideo alias {alias}",
                    null,
                    0,
                    IntPtr.Zero);
            }
            catch (Exception ex)
            {
                LogAudioIssue($"MCI is unavailable for: {path}", ex);
                return false;
            }

            if (openResult != 0)
            {
                LogMciFailure("open", path, openResult);
                return false;
            }

            uint playResult;

            try
            {
                playResult = MciSendString($"play {alias} from 0", null, 0, IntPtr.Zero);
            }
            catch (Exception ex)
            {
                LogAudioIssue($"MCI could not start playback for: {path}", ex);
                CloseMciAlias(alias);
                return false;
            }

            if (playResult != 0)
            {
                LogMciFailure("play", path, playResult);
                CloseMciAlias(alias);
                return false;
            }

            var cleanupCancellation = new CancellationTokenSource();
            var playback = new MciPlayback
            {
                Alias = alias,
                CleanupCancellation = cleanupCancellation
            };

            lock (MciSync)
            {
                MciPlaybacks[path] = playback;
            }

            SetPreferredBackend(AudioBackend.Mci);

            TimeSpan playbackDuration;

            try
            {
                playbackDuration = GetMciPlaybackDuration(alias);
            }
            catch (Exception ex)
            {
                LogAudioIssue($"MCI could not read playback duration for: {path}", ex);
                playbackDuration = TimeSpan.FromMinutes(10);
            }

            _ = CloseMciAfterPlaybackAsync(
                path,
                playback,
                playbackDuration,
                cleanupCancellation.Token);
            return true;
        }

        private static TimeSpan GetMciPlaybackDuration(string alias)
        {
            var value = new StringBuilder(64);
            uint result = MciSendString($"status {alias} length", value, value.Capacity, IntPtr.Zero);

            if (result == 0 &&
                long.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long milliseconds) &&
                milliseconds > 0)
            {
                return TimeSpan.FromMilliseconds(Math.Min(milliseconds + 2000, 24L * 60 * 60 * 1000));
            }

            return TimeSpan.FromMinutes(10);
        }

        private static async Task CloseMciAfterPlaybackAsync(
            string path,
            MciPlayback playback,
            TimeSpan delay,
            CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            lock (MciSync)
            {
                if (!MciPlaybacks.TryGetValue(path, out var currentPlayback) ||
                    !ReferenceEquals(currentPlayback, playback))
                {
                    return;
                }

                MciPlaybacks.Remove(path);
            }

            CloseMciAlias(playback.Alias);
            playback.CleanupCancellation.Dispose();
        }

        private static void StopMciPlayback(string path)
        {
            MciPlayback? playback = null;

            lock (MciSync)
            {
                if (MciPlaybacks.TryGetValue(path, out playback))
                    MciPlaybacks.Remove(path);
            }

            if (playback == null)
                return;

            playback.CleanupCancellation.Cancel();
            playback.CleanupCancellation.Dispose();
            CloseMciAlias(playback.Alias);
        }

        private static void CloseAllMciPlaybacks()
        {
            MciPlayback[] playbacks;

            lock (MciSync)
            {
                playbacks = MciPlaybacks.Values.ToArray();
                MciPlaybacks.Clear();
            }

            foreach (var playback in playbacks)
            {
                playback.CleanupCancellation.Cancel();
                playback.CleanupCancellation.Dispose();
                CloseMciAlias(playback.Alias);
            }
        }

        private static void CloseMciAlias(string alias)
        {
            try
            {
                MciSendString($"close {alias}", null, 0, IntPtr.Zero);
            }
            catch
            {
            }
        }

        private static void LogMciFailure(string operation, string path, uint errorCode)
        {
            string details = "Unknown MCI error";

            try
            {
                var errorText = new StringBuilder(256);
                if (MciGetErrorString(errorCode, errorText, errorText.Capacity))
                    details = errorText.ToString();
            }
            catch
            {
            }

            LogAudioIssue($"MCI {operation} failed for {path}. Code {errorCode}: {details}");
        }

        private static AudioBackend GetPreferredBackend()
        {
            lock (PreferenceSync)
            {
                return _preferredBackend;
            }
        }

        private static AudioBackend LoadPreferredBackend()
        {
            try
            {
                if (!File.Exists(BackendPreferencePath))
                    return AudioBackend.Auto;

                string value = File.ReadAllText(BackendPreferencePath).Trim();
                return Enum.TryParse(value, ignoreCase: true, out AudioBackend backend)
                    ? backend
                    : AudioBackend.Auto;
            }
            catch
            {
                return AudioBackend.Auto;
            }
        }

        private static void SetPreferredBackend(AudioBackend backend)
        {
            lock (PreferenceSync)
            {
                if (_preferredBackend == backend)
                    return;

                _preferredBackend = backend;

                try
                {
                    Directory.CreateDirectory(AudioDataDirectory);
                    string tempPath = BackendPreferencePath + ".tmp";
                    File.WriteAllText(tempPath, backend.ToString(), Encoding.UTF8);
                    File.Move(tempPath, BackendPreferencePath, overwrite: true);
                    LogAudioIssue($"Audio backend changed to {backend}.");
                }
                catch (Exception ex)
                {
                    LogAudioIssue($"Could not save audio backend {backend}.", ex);
                }
            }
        }

        private static void LogAudioIssue(string message, Exception? exception = null)
        {
            try
            {
                lock (LogSync)
                {
                    Directory.CreateDirectory(AudioDataDirectory);

                    if (File.Exists(AudioLogPath) && new FileInfo(AudioLogPath).Length > 512 * 1024)
                    {
                        File.Copy(AudioLogPath, AudioLogPath + ".old", overwrite: true);
                        File.Delete(AudioLogPath);
                    }

                    string suffix = exception == null
                        ? string.Empty
                        : $" {exception.GetType().Name}: {exception.Message}";
                    File.AppendAllText(
                        AudioLogPath,
                        $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{suffix}{Environment.NewLine}",
                        Encoding.UTF8);
                }
            }
            catch
            {
            }
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

        [DllImport("winmm.dll", EntryPoint = "mciSendStringW", CharSet = CharSet.Unicode)]
        private static extern uint MciSendString(
            string command,
            StringBuilder? returnValue,
            int returnLength,
            IntPtr callback);

        [DllImport("winmm.dll", EntryPoint = "mciGetErrorStringW", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool MciGetErrorString(
            uint errorCode,
            StringBuilder errorText,
            int errorTextLength);
    }
}
