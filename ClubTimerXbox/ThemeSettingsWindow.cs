using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ClubTimerXbox.Models;
using ClubTimerXbox.Services;

namespace ClubTimerXbox
{
    public sealed class ThemeSettingsWindow : Window
    {
        private readonly Dictionary<string, Border> _selectionBorders = new();

        public ThemeSettingsWindow()
        {
            Title = "Стиль приложения";
            Width = 620;
            Height = 500;
            MinWidth = 540;
            MinHeight = 420;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            Content = CreateContent();
        }

        private UIElement CreateContent()
        {
            var root = new StackPanel
            {
                Margin = new Thickness(24)
            };

            root.Children.Add(new TextBlock
            {
                Text = "Стиль приложения",
                Foreground = Brushes.White,
                FontSize = 30,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 18)
            });

            foreach (var theme in VisualThemeService.AvailableThemes)
                root.Children.Add(CreateThemeCard(theme));

            var closeButton = new Button
            {
                Content = "Закрыть",
                Height = 44,
                FontSize = 16,
                HorizontalAlignment = HorizontalAlignment.Right,
                Width = 130,
                Margin = new Thickness(0, 10, 0, 0)
            };
            closeButton.Click += (_, _) => Close();
            root.Children.Add(closeButton);

            return new ScrollViewer
            {
                Content = root,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
        }

        private Border CreateThemeCard(ClubVisualTheme theme)
        {
            var preview = new Border
            {
                Width = 150,
                Height = 88,
                CornerRadius = new CornerRadius(8),
                Background = VisualThemeService.CreateThemePreviewBrush(theme),
                BorderBrush = new SolidColorBrush(Color.FromArgb(96, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 18, 0)
            };

            var textPanel = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center
            };
            textPanel.Children.Add(new TextBlock
            {
                Text = theme.DisplayName,
                Foreground = Brushes.White,
                FontSize = 21,
                FontWeight = FontWeights.Bold
            });
            textPanel.Children.Add(new TextBlock
            {
                Text = theme.Description,
                Foreground = new SolidColorBrush(Color.FromRgb(170, 180, 195)),
                FontSize = 14,
                Margin = new Thickness(0, 5, 0, 0)
            });

            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal
            };
            row.Children.Add(preview);
            row.Children.Add(textPanel);

            var card = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14),
                Margin = new Thickness(0, 0, 0, 12),
                Background = Application.Current.TryFindResource("Theme.CardBrush") as Brush,
                Child = row,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            card.MouseLeftButtonUp += (_, _) =>
            {
                VisualThemeService.SelectTheme(theme.Id);
                UpdateSelection();
            };

            _selectionBorders[theme.Id] = card;
            UpdateSelection();
            return card;
        }

        private void UpdateSelection()
        {
            foreach (var pair in _selectionBorders)
            {
                bool selected = pair.Key == VisualThemeService.Current.Id;
                pair.Value.BorderBrush = new SolidColorBrush(
                    selected
                        ? Color.FromRgb(74, 222, 128)
                        : Color.FromArgb(72, 255, 255, 255)
                );
                pair.Value.BorderThickness = new Thickness(selected ? 2 : 1);
            }
        }
    }
}
