using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public static class VisualThemeService
    {
        public const string ClassicThemeId = "classic";
        public const string GlassClubThemeId = "glass-club";
        private const string DefaultThemeId = GlassClubThemeId;

        private static readonly string SettingsFolderPath =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ClubTimerXbox"
            );

        private static readonly string SettingsFilePath =
            Path.Combine(SettingsFolderPath, "visual_theme.json");

        private static readonly DependencyProperty ThemeAttachedProperty =
            DependencyProperty.RegisterAttached(
                "ThemeAttached",
                typeof(bool),
                typeof(VisualThemeService),
                new PropertyMetadata(false)
            );

        private static readonly IReadOnlyList<ClubVisualTheme> Themes =
            new List<ClubVisualTheme>
            {
                new ClubVisualTheme
                {
                    Id = ClassicThemeId,
                    DisplayName = "Классический",
                    Description = "Спокойный тёмный интерфейс",
                    BackdropKind = ThemeBackdropKind.None,
                    UsesGlassSurfaces = false
                },
                new ClubVisualTheme
                {
                    Id = GlassClubThemeId,
                    DisplayName = "Стекло клуба",
                    Description = "Фон игрового клуба и прозрачные панели",
                    BackdropKind = ThemeBackdropKind.Image,
                    AssetRelativePath = Path.Combine("Assets", "Themes", "glass-club.png"),
                    UsesGlassSurfaces = true
                }
            };

        private static bool _isEnabled;
        private static string _currentThemeId = LoadThemeId();
        private static readonly ConditionalWeakTable<Window, WindowAppearanceState> WindowStates = new();

        public static event EventHandler? ThemeChanged;

        public static IReadOnlyList<ClubVisualTheme> AvailableThemes => Themes;

        public static ClubVisualTheme Current =>
            FindTheme(_currentThemeId) ?? FindTheme(DefaultThemeId)!;

        public static bool UsesGlassSurfaces => Current.UsesGlassSurfaces;

        public static void EnableForAllWindows()
        {
            ApplyApplicationResources();

            if (_isEnabled)
                return;

            _isEnabled = true;

            EventManager.RegisterClassHandler(
                typeof(Window),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler(OnWindowLoaded)
            );
        }

        public static void SelectTheme(string themeId)
        {
            var theme = FindTheme(themeId);
            if (theme == null || string.Equals(theme.Id, _currentThemeId, StringComparison.Ordinal))
                return;

            _currentThemeId = theme.Id;
            SaveThemeId();
            ApplyApplicationResources();
            RefreshOpenWindows();
            ThemeChanged?.Invoke(null, EventArgs.Empty);
        }

        public static Brush CreateTintedSurfaceBrush(Color baseColor, byte glassAlpha)
        {
            return UsesGlassSurfaces
                ? new SolidColorBrush(Color.FromArgb(glassAlpha, baseColor.R, baseColor.G, baseColor.B))
                : new SolidColorBrush(baseColor);
        }

        public static Brush CreateThemePreviewBrush(ClubVisualTheme theme)
        {
            if (theme.BackdropKind == ThemeBackdropKind.None)
                return new SolidColorBrush(Color.FromRgb(16, 20, 28));

            var image = TryLoadImage(theme.AssetRelativePath);
            if (image == null)
                return new SolidColorBrush(Color.FromRgb(16, 24, 32));

            return new ImageBrush(image)
            {
                Stretch = Stretch.UniformToFill,
                AlignmentX = AlignmentX.Center,
                AlignmentY = AlignmentY.Center
            };
        }

        private static void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is not Window window)
                return;

            if ((bool)window.GetValue(ThemeAttachedProperty))
                return;

            window.SetValue(ThemeAttachedProperty, true);
            ApplyToWindow(window);
        }

        private static void ApplyToWindow(Window window)
        {
            ApplyWindowFrame(window);
            window.Background = GetBrush("Theme.WindowBrush");

            if (window.Content is ThemeBackdropHost existingHost)
            {
                existingHost.Refresh();
                return;
            }

            if (window.Content is not UIElement originalContent)
                return;

            var surface = window is MainWindow
                ? ThemeBackdropSurface.Main
                : ThemeBackdropSurface.Dialog;

            window.Content = null;
            window.Content = new ThemeBackdropHost(window, originalContent, surface);
        }

        private static void ApplyWindowFrame(Window window)
        {
            var state = WindowStates.GetValue(
                window,
                target => new WindowAppearanceState
                {
                    OriginalWindowStyle = target.WindowStyle
                }
            );

            if (!UsesGlassSurfaces)
            {
                WindowChrome.SetWindowChrome(window, null);
                window.WindowStyle = state.OriginalWindowStyle;
                return;
            }

            window.WindowStyle = WindowStyle.None;
            WindowChrome.SetWindowChrome(
                window,
                new WindowChrome
                {
                    CaptionHeight = 0,
                    CornerRadius = new CornerRadius(0),
                    GlassFrameThickness = new Thickness(0),
                    ResizeBorderThickness = window.ResizeMode == ResizeMode.NoResize
                        ? new Thickness(0)
                        : new Thickness(6),
                    UseAeroCaptionButtons = false
                }
            );
        }

        private static void RefreshOpenWindows()
        {
            if (Application.Current == null)
                return;

            foreach (Window window in Application.Current.Windows)
                ApplyToWindow(window);
        }

        private static void ApplyApplicationResources()
        {
            if (Application.Current == null)
                return;

            bool glass = UsesGlassSurfaces;
            var resources = Application.Current.Resources;

            resources["Theme.WindowBrush"] = Brush(
                glass ? Color.FromRgb(6, 9, 13) : Color.FromRgb(16, 20, 28)
            );
            resources["Theme.HeaderBrush"] = Brush(
                glass ? Color.FromArgb(218, 19, 25, 33) : Color.FromRgb(24, 32, 43)
            );
            resources["Theme.CardBrush"] = Brush(
                glass ? Color.FromArgb(188, 24, 32, 43) : Color.FromRgb(24, 32, 43)
            );
            resources["Theme.CardAltBrush"] = Brush(
                glass ? Color.FromArgb(164, 15, 23, 42) : Color.FromRgb(15, 23, 42)
            );
            resources["Theme.ButtonBrush"] = Brush(
                glass ? Color.FromArgb(200, 37, 48, 68) : Color.FromRgb(37, 48, 68)
            );
            resources["Theme.ButtonHoverBrush"] = Brush(
                glass ? Color.FromArgb(224, 51, 65, 85) : Color.FromRgb(51, 65, 85)
            );
            resources["Theme.InputBrush"] = Brush(
                glass ? Color.FromArgb(218, 13, 20, 29) : Color.FromRgb(15, 23, 42)
            );
            resources["Theme.BorderBrush"] = Brush(
                glass ? Color.FromArgb(112, 255, 255, 255) : Color.FromRgb(51, 65, 85)
            );
            resources["Theme.TextBrush"] = Brush(Color.FromRgb(248, 250, 252));
            resources["Theme.MutedTextBrush"] = Brush(Color.FromRgb(170, 180, 195));
        }

        private static SolidColorBrush GetBrush(string key)
        {
            return Application.Current?.TryFindResource(key) as SolidColorBrush
                ?? Brush(Color.FromRgb(16, 20, 28));
        }

        private static SolidColorBrush Brush(Color color)
        {
            return new SolidColorBrush(color);
        }

        private static ClubVisualTheme? FindTheme(string? themeId)
        {
            foreach (var theme in Themes)
            {
                if (string.Equals(theme.Id, themeId, StringComparison.Ordinal))
                    return theme;
            }

            return null;
        }

        private static string LoadThemeId()
        {
            try
            {
                if (!File.Exists(SettingsFilePath))
                    return DefaultThemeId;

                var json = File.ReadAllText(SettingsFilePath);
                var settings = JsonSerializer.Deserialize<VisualThemeSettings>(json);

                return FindTheme(settings?.ThemeId) != null
                    ? settings!.ThemeId
                    : DefaultThemeId;
            }
            catch
            {
                return DefaultThemeId;
            }
        }

        private static void SaveThemeId()
        {
            try
            {
                Directory.CreateDirectory(SettingsFolderPath);

                var json = JsonSerializer.Serialize(
                    new VisualThemeSettings { ThemeId = _currentThemeId },
                    new JsonSerializerOptions { WriteIndented = true }
                );

                File.WriteAllText(SettingsFilePath, json);
            }
            catch
            {
                // The selected style is cosmetic and must never block club operation.
            }
        }

        internal static BitmapImage? TryLoadImage(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                return null;

            string path = Path.Combine(AppContext.BaseDirectory, relativePath);
            if (!File.Exists(path))
                return null;

            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.UriSource = new Uri(path, UriKind.Absolute);
                image.EndInit();
                image.Freeze();
                return image;
            }
            catch
            {
                return null;
            }
        }

        internal static string ResolveAssetPath(string relativePath)
        {
            return string.IsNullOrWhiteSpace(relativePath)
                ? ""
                : Path.Combine(AppContext.BaseDirectory, relativePath);
        }

        private sealed class VisualThemeSettings
        {
            public string ThemeId { get; set; } = DefaultThemeId;
        }

        private sealed class WindowAppearanceState
        {
            public WindowStyle OriginalWindowStyle { get; init; }
        }
    }

    internal enum ThemeBackdropSurface
    {
        Main,
        Dialog
    }

    internal sealed class ThemeBackdropHost : Grid
    {
        private readonly Window _window;
        private readonly UIElement _content;
        private readonly ThemeBackdropSurface _surface;
        private MediaElement? _video;

        public ThemeBackdropHost(
            Window window,
            UIElement content,
            ThemeBackdropSurface surface)
        {
            _window = window;
            _content = content;
            _surface = surface;
            ClipToBounds = true;
            UseLayoutRounding = true;
            Refresh();
        }

        public void Refresh()
        {
            StopVideo();
            Children.Clear();
            RowDefinitions.Clear();

            var theme = VisualThemeService.Current;
            bool glass = theme.UsesGlassSurfaces;

            RowDefinitions.Add(new RowDefinition
            {
                Height = new GridLength(glass ? 36 : 0)
            });
            RowDefinitions.Add(new RowDefinition
            {
                Height = new GridLength(1, GridUnitType.Star)
            });

            Background = Application.Current?.TryFindResource("Theme.WindowBrush") as Brush
                ?? Brushes.Black;

            if (theme.BackdropKind != ThemeBackdropKind.None)
            {
                var backdrop = CreateBackdrop(theme);
                if (backdrop != null)
                {
                    Panel.SetZIndex(backdrop, 0);
                    Grid.SetRowSpan(backdrop, 2);
                    Children.Add(backdrop);
                }

                var overlay = new Border
                {
                    Background = new SolidColorBrush(
                        _surface == ThemeBackdropSurface.Main
                            ? Color.FromArgb(98, 5, 8, 12)
                            : Color.FromArgb(164, 5, 8, 12)
                    )
                };

                Panel.SetZIndex(overlay, 1);
                Grid.SetRowSpan(overlay, 2);
                Children.Add(overlay);
            }

            if (glass)
            {
                var titleBar = CreateTitleBar();
                Grid.SetRow(titleBar, 0);
                Panel.SetZIndex(titleBar, 3);
                Children.Add(titleBar);
            }

            Grid.SetRow(_content, 1);
            Panel.SetZIndex(_content, 2);
            Children.Add(_content);
        }

        private Border CreateTitleBar()
        {
            var title = new TextBlock
            {
                Foreground = Brushes.White,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            title.SetBinding(
                TextBlock.TextProperty,
                new Binding(nameof(Window.Title)) { Source = _window }
            );

            var leftPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(12, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            if (_window.Icon != null)
            {
                leftPanel.Children.Add(new Image
                {
                    Source = _window.Icon,
                    Width = 16,
                    Height = 16,
                    Margin = new Thickness(0, 0, 8, 0)
                });
            }
            leftPanel.Children.Add(title);

            var controls = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            if (_window.ResizeMode != ResizeMode.NoResize)
            {
                controls.Children.Add(CreateCaptionButton(
                    "—",
                    "Свернуть",
                    () => _window.WindowState = WindowState.Minimized
                ));
            }

            if (_window.ResizeMode is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip)
            {
                controls.Children.Add(CreateCaptionButton(
                    "□",
                    "Развернуть",
                    ToggleMaximized
                ));
            }

            controls.Children.Add(CreateCaptionButton(
                "×",
                "Закрыть",
                _window.Close,
                isClose: true
            ));

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = GridLength.Auto
            });
            Grid.SetColumn(leftPanel, 0);
            Grid.SetColumn(controls, 1);
            grid.Children.Add(leftPanel);
            grid.Children.Add(controls);

            var bar = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(208, 16, 22, 30)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(72, 255, 255, 255)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Child = grid
            };

            bar.MouseLeftButtonDown += (_, e) =>
            {
                if (controls.IsMouseOver)
                    return;

                if (e.ClickCount == 2 &&
                    _window.ResizeMode is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip)
                {
                    ToggleMaximized();
                    return;
                }

                try
                {
                    _window.DragMove();
                }
                catch (InvalidOperationException)
                {
                    // The mouse button may have been released during a fast drag.
                }
            };

            return bar;
        }

        private Border CreateCaptionButton(
            string glyph,
            string tooltip,
            Action click,
            bool isClose = false)
        {
            bool pressed = false;
            var normal = Brushes.Transparent;
            var hover = new SolidColorBrush(
                isClose
                    ? Color.FromRgb(190, 38, 51)
                    : Color.FromArgb(92, 255, 255, 255)
            );

            var button = new Border
            {
                Width = 46,
                Background = normal,
                Cursor = Cursors.Hand,
                ToolTip = tooltip,
                Child = new TextBlock
                {
                    Text = glyph,
                    Foreground = Brushes.White,
                    FontSize = glyph == "×" ? 20 : 16,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };

            button.MouseEnter += (_, _) => button.Background = hover;
            button.MouseLeave += (_, _) => button.Background = normal;
            button.PreviewMouseLeftButtonDown += (_, e) =>
            {
                pressed = true;
                button.CaptureMouse();
                e.Handled = true;
            };
            button.PreviewMouseLeftButtonUp += (_, e) =>
            {
                bool shouldClick = pressed && button.IsMouseOver;
                pressed = false;
                button.ReleaseMouseCapture();
                e.Handled = true;

                if (shouldClick)
                    click();
            };
            button.LostMouseCapture += (_, _) => pressed = false;

            return button;
        }

        private void ToggleMaximized()
        {
            _window.WindowState = _window.WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }

        private UIElement? CreateBackdrop(ClubVisualTheme theme)
        {
            if (theme.BackdropKind == ThemeBackdropKind.Video)
            {
                string videoPath = VisualThemeService.ResolveAssetPath(theme.AssetRelativePath);
                if (File.Exists(videoPath))
                {
                    _video = new MediaElement
                    {
                        Source = new Uri(videoPath, UriKind.Absolute),
                        LoadedBehavior = MediaState.Manual,
                        UnloadedBehavior = MediaState.Stop,
                        Stretch = Stretch.UniformToFill,
                        IsMuted = true,
                        ScrubbingEnabled = true
                    };
                    _video.Loaded += (_, _) => _video.Play();
                    _video.MediaEnded += (_, _) =>
                    {
                        _video.Position = TimeSpan.Zero;
                        _video.Play();
                    };
                    return _video;
                }
            }

            string imagePath = theme.BackdropKind == ThemeBackdropKind.Image
                ? theme.AssetRelativePath
                : theme.FallbackImageRelativePath;

            var source = VisualThemeService.TryLoadImage(imagePath);
            if (source == null)
                return null;

            var brush = new ImageBrush(source)
            {
                Stretch = Stretch.UniformToFill,
                AlignmentX = AlignmentX.Center,
                AlignmentY = AlignmentY.Center,
                TileMode = TileMode.None
            };

            RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.HighQuality);

            return new Border
            {
                Background = brush,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Effect = _surface == ThemeBackdropSurface.Dialog
                    ? new BlurEffect { Radius = 5 }
                    : null
            };
        }

        private void StopVideo()
        {
            if (_video == null)
                return;

            _video.Stop();
            _video.Source = null;
            _video = null;
        }
    }
}
