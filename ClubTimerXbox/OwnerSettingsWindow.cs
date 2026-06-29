using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ClubTimerXbox.Models;
using ClubTimerXbox.Services;

namespace ClubTimerXbox
{
    public class OwnerSettingsWindow : Window
    {
        private readonly Action _openTariffSettings;
        private readonly Action _openStockSettings;
        private readonly Action _openTuyaSettings;
        private readonly Action _openAlarmSettings;
        private readonly Func<IReadOnlyList<ClubPlace>> _getPlaces;
        private readonly Func<
            IProgress<AppUpdateService.AppUpdateProgress>,
            Task<AppUpdateService.InstallUpdateResult>> _installUpdate;
        private readonly DispatcherTimer _refreshTimer = new DispatcherTimer();

        private Border? _updateCard;
        private TextBlock? _updateTitleText;
        private TextBlock? _updateSubtitleText;
        private Button? _updateButton;
        private bool _isInstallingUpdate;

        public OwnerSettingsWindow(
            Action openTariffSettings,
            Action openStockSettings,
            Action openTuyaSettings,
            Action openAlarmSettings,
            Func<IReadOnlyList<ClubPlace>> getPlaces,
            Func<
                IProgress<AppUpdateService.AppUpdateProgress>,
                Task<AppUpdateService.InstallUpdateResult>> installUpdate)
        {
            _openTariffSettings = openTariffSettings;
            _openStockSettings = openStockSettings;
            _openTuyaSettings = openTuyaSettings;
            _openAlarmSettings = openAlarmSettings;
            _getPlaces = getPlaces;
            _installUpdate = installUpdate;

            Title = "Настройки";
            Width = 640;
            Height = 660;
            MinWidth = 580;
            MinHeight = 540;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromRgb(16, 20, 28));

            Content = CreateContent();

            Loaded += async (_, _) => await RefreshUpdateCardAsync(forceRefresh: true);
            Closed += (_, _) => _refreshTimer.Stop();

            _refreshTimer.Interval = TimeSpan.FromSeconds(3);
            _refreshTimer.Tick += async (_, _) => await RefreshUpdateCardAsync(forceRefresh: false);
            _refreshTimer.Start();
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
                    $"Club Timer Xbox\n" +
                    $"Версия {AppUpdateService.FormatDisplayVersion(AppVersionService.Version)}\n" +
                    $"Разработчик: ormonov0310-web\n" +
                    $"GitHub: github.com/ormonov0310-web\n" +
                    $"Email: ormonov0310@gmail.com",
                Foreground = new SolidColorBrush(Color.FromRgb(170, 180, 195)),
                FontSize = 15,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 23,
                Margin = new Thickness(0, 0, 0, 22)
            });

            _updateCard = CreateUpdateCard();
            root.Children.Add(_updateCard);

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

            return new ScrollViewer
            {
                Content = root,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
        }

        private Border CreateUpdateCard()
        {
            var panel = new StackPanel();

            _updateTitleText = new TextBlock
            {
                Text = "Проверяем обновления...",
                Foreground = Brushes.White,
                FontSize = 21,
                FontWeight = FontWeights.Bold
            };

            _updateSubtitleText = new TextBlock
            {
                Text = "Статус обновления появится здесь.",
                Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 21,
                Margin = new Thickness(0, 6, 0, 12)
            };

            _updateButton = new Button
            {
                Content = "Проверяем...",
                Height = 42,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                IsEnabled = false
            };
            _updateButton.Click += async (_, _) => await InstallUpdateFromSettingsAsync();

            panel.Children.Add(_updateTitleText);
            panel.Children.Add(_updateSubtitleText);
            panel.Children.Add(_updateButton);

            return new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(36, 28, 18)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(245, 158, 11)),
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16),
                Margin = new Thickness(0, 0, 0, 16),
                Child = panel
            };
        }

        private async Task RefreshUpdateCardAsync(bool forceRefresh)
        {
            if (_updateCard == null ||
                _updateTitleText == null ||
                _updateSubtitleText == null ||
                _updateButton == null)
            {
                return;
            }

            if (_isInstallingUpdate)
                return;

            try
            {
                var info = await AppUpdateService.GetLatestUpdateInfoAsync(_getPlaces(), forceRefresh);

                _updateCard.Visibility = info.HasUpdate
                    ? Visibility.Visible
                    : Visibility.Collapsed;

                if (!info.HasUpdate)
                    return;

                _updateTitleText.Text = $"Вышло обновление {info.DisplayLatestVersion}";

                if (info.SafeToInstall)
                {
                    _updateSubtitleText.Text =
                        "Клуб свободен. Можно установить обновление сейчас. " +
                        "Программа закроется, updater установит новую версию и откроет приложение обратно.";
                    _updateButton.Content = "Обновить";
                    _updateButton.IsEnabled = true;
                    _updateButton.Background = new SolidColorBrush(Color.FromRgb(245, 158, 11));
                    _updateButton.Foreground = Brushes.White;
                }
                else
                {
                    _updateSubtitleText.Text =
                        $"Обновление готово, но сейчас активных мест: {info.ActivePlaces}. " +
                        "Кнопка станет доступной, когда все сеансы будут закрыты.";
                    _updateButton.Content = "Обновить нельзя: есть активные сеансы";
                    _updateButton.IsEnabled = false;
                    _updateButton.Background = new SolidColorBrush(Color.FromRgb(75, 85, 99));
                    _updateButton.Foreground = new SolidColorBrush(Color.FromRgb(209, 213, 219));
                }
            }
            catch (Exception ex)
            {
                _updateCard.Visibility = Visibility.Visible;
                _updateTitleText.Text = "Не удалось проверить обновление";
                _updateSubtitleText.Text = ex.Message;
                _updateButton.Content = "Повторить проверку";
                _updateButton.IsEnabled = true;
                _updateButton.Background = new SolidColorBrush(Color.FromRgb(37, 99, 235));
                _updateButton.Foreground = Brushes.White;
            }
        }

        private async Task InstallUpdateFromSettingsAsync()
        {
            if (_updateButton == null || _updateSubtitleText == null)
                return;

            if (_isInstallingUpdate)
                return;

            _isInstallingUpdate = true;
            _refreshTimer.Stop();

            _updateButton.IsEnabled = false;
            _updateButton.Content = "Обновление уже запускается...";
            _updateSubtitleText.Text =
                "Открываем экран скачивания. Не нажимайте кнопку повторно.";

            UpdateInstallProgressWindow? progressWindow = null;

            try
            {
                progressWindow = new UpdateInstallProgressWindow
                {
                    Owner = this
                };

                progressWindow.Show();
                var result = await progressWindow.RunAsync(_installUpdate);
                _updateSubtitleText.Text = result.Message;

                if (result.ShouldShutdown)
                {
                    Application.Current.Shutdown();
                    return;
                }

                _isInstallingUpdate = false;
                progressWindow.Close();
                _refreshTimer.Start();
                await RefreshUpdateCardAsync(forceRefresh: true);
            }
            catch (Exception ex)
            {
                progressWindow?.Close();
                _isInstallingUpdate = false;
                _refreshTimer.Start();
                _updateSubtitleText.Text = ex.Message;
                _updateButton.Content = "Попробовать снова";
                _updateButton.IsEnabled = true;
            }
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
