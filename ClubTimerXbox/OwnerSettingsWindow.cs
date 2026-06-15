using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ClubTimerXbox
{
    public class OwnerSettingsWindow : Window
    {
        private readonly Action _openTariffSettings;
        private readonly Action _openStockSettings;
        private readonly Action _openTuyaSettings;
        private readonly Action _openAlarmSettings;

        public OwnerSettingsWindow(
            Action openTariffSettings,
            Action openStockSettings,
            Action openTuyaSettings,
            Action openAlarmSettings)
        {
            _openTariffSettings = openTariffSettings;
            _openStockSettings = openStockSettings;
            _openTuyaSettings = openTuyaSettings;
            _openAlarmSettings = openAlarmSettings;

            Title = "Настройки";
            Width = 640;
            Height = 620;
            MinWidth = 580;
            MinHeight = 520;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromRgb(16, 20, 28));

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
                Text = "Настройки владельца",
                Foreground = Brushes.White,
                FontSize = 30,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 8)
            });

            root.Children.Add(new TextBlock
            {
                Text =
                    "Здесь настройки, которые не должны быть доступны обычным сотрудникам. " +
                    "Тарифы, склад, закупы и розетки находятся здесь.",
                Foreground = new SolidColorBrush(Color.FromRgb(170, 180, 195)),
                FontSize = 15,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 23,
                Margin = new Thickness(0, 0, 0, 22)
            });

            root.Children.Add(CreateSettingsButton(
                "Тарифы / места",
                "Количество ТВ, рулей и тарифы по времени.",
                () => _openTariffSettings()
            ));

            root.Children.Add(CreateSettingsButton(
                "Будильник",
                "Предупреждение перед окончанием времени, звук и длительность сигнала.",
                () => _openAlarmSettings()
            ));

            root.Children.Add(CreateSettingsButton(
                "Склад / закупы",
                "Остатки товаров, приёмка закупов, цены покупки и продажи.",
                () => _openStockSettings()
            ));

            root.Children.Add(CreateSettingsButton(
                "Tuya розетки",
                "Подключение Wi-Fi розеток через облако Tuya. Пока безопасная проверка.",
                () => _openTuyaSettings()
            ));

            var closeButton = new Button
            {
                Content = "Закрыть",
                Height = 44,
                FontSize = 16,
                Margin = new Thickness(0, 20, 0, 0)
            };

            closeButton.Click += (_, _) => Close();

            root.Children.Add(closeButton);

            return root;
        }

        private Button CreateSettingsButton(
            string title,
            string subtitle,
            Action clickAction)
        {
            var panel = new StackPanel
            {
                Margin = new Thickness(6)
            };

            panel.Children.Add(new TextBlock
            {
                Text = title,
                Foreground = Brushes.White,
                FontSize = 20,
                FontWeight = FontWeights.Bold
            });

            panel.Children.Add(new TextBlock
            {
                Text = subtitle,
                Foreground = new SolidColorBrush(Color.FromRgb(170, 180, 195)),
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 5, 0, 0)
            });

            var button = new Button
            {
                Content = panel,
                MinHeight = 90,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 0, 0, 12),
                Padding = new Thickness(12)
            };

            button.Click += (_, _) => clickAction();

            return button;
        }
    }
}
