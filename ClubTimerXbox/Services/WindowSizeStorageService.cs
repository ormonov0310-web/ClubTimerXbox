using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows;

namespace ClubTimerXbox.Services
{
    public static class WindowSizeStorageService
    {
        private static readonly string SettingsFolderPath =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ClubTimerXbox"
            );

        private static readonly string SettingsFilePath =
            Path.Combine(SettingsFolderPath, "window_sizes.json");

        private static readonly DependencyProperty IsAttachedProperty =
            DependencyProperty.RegisterAttached(
                "IsAttached",
                typeof(bool),
                typeof(WindowSizeStorageService),
                new PropertyMetadata(false)
            );

        private static bool _isEnabled;
        private static Dictionary<string, WindowSizeState> _states = Load();

        public static void EnableForAllWindows()
        {
            if (_isEnabled)
                return;

            _isEnabled = true;

            EventManager.RegisterClassHandler(
                typeof(Window),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler(OnWindowLoaded)
            );
        }

        private static void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is not Window window || !CanPersist(window))
                return;

            if ((bool)window.GetValue(IsAttachedProperty))
                return;

            window.SetValue(IsAttachedProperty, true);

            ApplySavedSize(window);
            window.Closing += (_, _) => SaveWindowSize(window);
        }

        private static bool CanPersist(Window window)
        {
            return window.ResizeMode != ResizeMode.NoResize &&
                   window.SizeToContent == SizeToContent.Manual &&
                   !string.IsNullOrWhiteSpace(GetWindowKey(window));
        }

        private static void ApplySavedSize(Window window)
        {
            var key = GetWindowKey(window);

            if (!_states.TryGetValue(key, out var state))
                return;

            var width = ClampSize(
                state.Width,
                Math.Max(window.MinWidth, 320),
                Math.Max(window.MinWidth, SystemParameters.WorkArea.Width - 40)
            );

            var height = ClampSize(
                state.Height,
                Math.Max(window.MinHeight, 260),
                Math.Max(window.MinHeight, SystemParameters.WorkArea.Height - 40)
            );

            if (width > 0)
                window.Width = width;

            if (height > 0)
                window.Height = height;

            KeepInsideWorkArea(window);
        }

        private static void SaveWindowSize(Window window)
        {
            var key = GetWindowKey(window);
            var bounds = window.WindowState == WindowState.Normal
                ? new Rect(window.Left, window.Top, window.Width, window.Height)
                : window.RestoreBounds;

            var width = GetValidSize(bounds.Width, window.ActualWidth);
            var height = GetValidSize(bounds.Height, window.ActualHeight);

            if (width <= 0 || height <= 0)
                return;

            _states[key] = new WindowSizeState
            {
                Width = width,
                Height = height
            };

            Save();
        }

        private static double GetValidSize(double preferred, double fallback)
        {
            if (double.IsFinite(preferred) && preferred > 0)
                return preferred;

            if (double.IsFinite(fallback) && fallback > 0)
                return fallback;

            return 0;
        }

        private static double ClampSize(double value, double min, double max)
        {
            if (!double.IsFinite(value) || value <= 0)
                return 0;

            return Math.Clamp(value, min, max);
        }

        private static void KeepInsideWorkArea(Window window)
        {
            var area = SystemParameters.WorkArea;

            if (!double.IsFinite(window.Left) || !double.IsFinite(window.Top))
                return;

            if (window.Left + window.Width > area.Right)
                window.Left = Math.Max(area.Left, area.Right - window.Width);

            if (window.Top + window.Height > area.Bottom)
                window.Top = Math.Max(area.Top, area.Bottom - window.Height);

            if (window.Left < area.Left)
                window.Left = area.Left;

            if (window.Top < area.Top)
                window.Top = area.Top;
        }

        private static string GetWindowKey(Window window)
        {
            return window.GetType().FullName ?? window.GetType().Name;
        }

        private static Dictionary<string, WindowSizeState> Load()
        {
            try
            {
                if (!File.Exists(SettingsFilePath))
                    return new Dictionary<string, WindowSizeState>();

                var json = File.ReadAllText(SettingsFilePath);
                var states = JsonSerializer.Deserialize<Dictionary<string, WindowSizeState>>(json);

                return states ?? new Dictionary<string, WindowSizeState>();
            }
            catch
            {
                return new Dictionary<string, WindowSizeState>();
            }
        }

        private static void Save()
        {
            try
            {
                Directory.CreateDirectory(SettingsFolderPath);

                var options = new JsonSerializerOptions
                {
                    WriteIndented = true
                };

                var json = JsonSerializer.Serialize(_states, options);
                File.WriteAllText(SettingsFilePath, json);
            }
            catch
            {
                // Размер окна не критичен для работы клуба.
            }
        }

        private sealed class WindowSizeState
        {
            public double Width { get; set; }
            public double Height { get; set; }
        }
    }
}
