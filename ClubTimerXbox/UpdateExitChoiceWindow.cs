using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ClubTimerXbox
{
    public sealed class UpdateExitChoiceWindow : Window
    {
        public UpdateExitChoiceWindow(string version)
        {
            Title = "Завершение работы";
            Width = 540;
            Height = 330;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var panel = new StackPanel
            {
                Margin = new Thickness(28)
            };
            panel.Children.Add(new TextBlock
            {
                Text = $"Обновление {version} готово",
                Foreground = Brushes.White,
                FontSize = 26,
                FontWeight = FontWeights.Bold
            });
            panel.Children.Add(new TextBlock
            {
                Text =
                    "Все игровые места свободны, пакет уже скачан и проверен. " +
                    "Можно установить обновление перед завершением работы.",
                Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                FontSize = 15,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 22,
                Margin = new Thickness(0, 10, 0, 22)
            });

            var updateButton = new Button
            {
                Content = "Завершить и обновить систему",
                Height = 46,
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Background = new SolidColorBrush(Color.FromRgb(22, 163, 74)),
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 10)
            };
            updateButton.Click += (_, _) =>
            {
                DialogResult = true;
                Close();
            };
            panel.Children.Add(updateButton);

            var closeButton = new Button
            {
                Content = "Завершить работу",
                Height = 44,
                FontSize = 16
            };
            closeButton.Click += (_, _) =>
            {
                DialogResult = false;
                Close();
            };
            panel.Children.Add(closeButton);

            var card = new Border
            {
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(2),
                Child = panel
            };
            card.SetResourceReference(Border.BackgroundProperty, "Theme.CardBrush");
            card.SetResourceReference(Border.BorderBrushProperty, "Theme.BorderBrush");
            Content = new Grid
            {
                Margin = new Thickness(18),
                Children = { card }
            };
        }
    }
}
