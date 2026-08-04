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
        private TextBlock? _themeSubtitleText;
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

            root.Children.Add(CreateFirebaseStatusCard());

            var identity = PcIdentityService.Current;
            string phoneConnectionSubtitle = string.IsNullOrWhiteSpace(identity.ClubId)
                ? "Канал не выбран"
                : $"{identity.ClubName} · {identity.ClubId}";

            root.Children.Add(CreateSettingsButton(
                "Связь с телефоном",
                phoneConnectionSubtitle,
                OpenPhoneConnectionSettings
            ));

            root.Children.Add(CreateSettingsButton(
                "Изменить стиль",
                $"Сейчас выбран: {VisualThemeService.Current.DisplayName}",
                OpenThemeSettings,
                subtitle => _themeSubtitleText = subtitle
            ));

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

            root.Children.Add(CreateSettingsButton(
                "Новый филиал",
                "Временная акция нового клуба.",
                OpenNewBranchPromoWindow
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

        private void OpenNewBranchPromoWindow()
        {
            var accessWindow = new NewBranchPromoAccessWindow
            {
                Owner = this
            };

            if (accessWindow.ShowDialog() != true || !accessWindow.IsAccessGranted)
                return;

            var window = new NewBranchPromoWindow
            {
                Owner = this
            };

            window.ShowDialog();
        }

        private void OpenPhoneConnectionSettings()
        {
            var window = new PhoneConnectionSettingsWindow
            {
                Owner = this
            };

            window.ShowDialog();
        }

        private void OpenThemeSettings()
        {
            var window = new ThemeSettingsWindow
            {
                Owner = this
            };

            window.ShowDialog();

            if (_themeSubtitleText != null)
            {
                _themeSubtitleText.Text =
                    $"Сейчас выбран: {VisualThemeService.Current.DisplayName}";
            }
        }

        private Border CreateFirebaseStatusCard()
        {
            bool configured = FirebaseAuthService.IsConfigured;
            bool signedIn = configured && !string.IsNullOrWhiteSpace(FirebaseAuthService.CurrentEmail);

            string title = configured
                ? "Firebase защита готова"
                : "Firebase: открытый режим";

            string subtitle = configured
                ? (signedIn
                    ? $"Вход выполнен: {FirebaseAuthService.CurrentEmail}. После закрытия правил база продолжит работать."
                    : "Firebase Auth включен в коде, но вход ещё не выполнен.")
                : "Сейчас приложение работает по старой открытой схеме. Закрывать правила Firebase пока нельзя.";

            var panel = new StackPanel();

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
                Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 21,
                Margin = new Thickness(0, 6, 0, 0)
            });

            return new Border
            {
                Background = new SolidColorBrush(
                    configured
                        ? Color.FromRgb(20, 83, 45)
                        : Color.FromRgb(36, 28, 18)
                ),
                BorderBrush = new SolidColorBrush(
                    configured
                        ? Color.FromRgb(34, 197, 94)
                        : Color.FromRgb(245, 158, 11)
                ),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16),
                Margin = new Thickness(0, 0, 0, 16),
                Child = panel
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

                _updateTitleText.Text = $"Обновление {info.DisplayLatestVersion}";
                _updateButton.Foreground = Brushes.White;

                switch (info.Stage)
                {
                    case AppUpdateService.AppUpdateStage.Downloading:
                        SetUpdateCardColors(Color.FromRgb(37, 99, 235), Color.FromRgb(17, 36, 68));
                        _updateSubtitleText.Text =
                            $"Скачивание в фоне: {info.DownloadPercent}%. " +
                            "Игровые места и работа программы продолжаются как обычно.";
                        _updateButton.Content = $"Скачивается: {info.DownloadPercent}%";
                        _updateButton.IsEnabled = false;
                        _updateButton.Background = new SolidColorBrush(Color.FromRgb(37, 99, 235));
                        break;

                    case AppUpdateService.AppUpdateStage.Verifying:
                        SetUpdateCardColors(Color.FromRgb(56, 189, 248), Color.FromRgb(17, 36, 68));
                        _updateSubtitleText.Text = "Скачивание завершено. Проверяем размер и SHA-256 пакета.";
                        _updateButton.Content = "Проверяем пакет...";
                        _updateButton.IsEnabled = false;
                        _updateButton.Background = new SolidColorBrush(Color.FromRgb(37, 99, 235));
                        break;

                    case AppUpdateService.AppUpdateStage.DownloadedBlocked:
                        SetUpdateCardColors(Color.FromRgb(59, 130, 246), Color.FromRgb(17, 36, 68));
                        _updateSubtitleText.Text =
                            $"Пакет скачан и проверен. Активных мест: {info.ActivePlaces}. " +
                            "Установка станет доступна после завершения всех сеансов.";
                        _updateButton.Content = "Скачано, ждём свободный клуб";
                        _updateButton.IsEnabled = false;
                        _updateButton.Background = new SolidColorBrush(Color.FromRgb(75, 85, 99));
                        break;

                    case AppUpdateService.AppUpdateStage.Ready:
                        SetUpdateCardColors(Color.FromRgb(34, 197, 94), Color.FromRgb(20, 83, 45));
                        _updateSubtitleText.Text =
                            "Пакет скачан и проверен, клуб свободен. После установки программа " +
                            "вернётся в текущего сотрудника без повторного кода и приёмки.";
                        _updateButton.Content = "Установить обновление";
                        _updateButton.IsEnabled = true;
                        _updateButton.Background = new SolidColorBrush(Color.FromRgb(22, 163, 74));
                        break;

                    case AppUpdateService.AppUpdateStage.Installing:
                    case AppUpdateService.AppUpdateStage.Recovering:
                        SetUpdateCardColors(Color.FromRgb(56, 189, 248), Color.FromRgb(17, 36, 68));
                        _updateSubtitleText.Text = info.StateMessage;
                        _updateButton.Content = "Установка уже началась";
                        _updateButton.IsEnabled = false;
                        break;

                    case AppUpdateService.AppUpdateStage.Failed:
                        SetUpdateCardColors(Color.FromRgb(239, 68, 68), Color.FromRgb(69, 10, 10));
                        _updateSubtitleText.Text = info.StateMessage;
                        _updateButton.Content = "Скачать и проверить заново";
                        _updateButton.IsEnabled = info.SafeToInstall;
                        _updateButton.Background = new SolidColorBrush(Color.FromRgb(185, 28, 28));
                        break;

                    default:
                        SetUpdateCardColors(Color.FromRgb(245, 158, 11), Color.FromRgb(36, 28, 18));
                        _updateSubtitleText.Text =
                            "Новая версия найдена. Фоновая подготовка начнётся автоматически.";
                        _updateButton.Content = "Подготавливаем...";
                        _updateButton.IsEnabled = false;
                        _updateButton.Background = new SolidColorBrush(Color.FromRgb(75, 85, 99));
                        break;
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

        private void SetUpdateCardColors(Color border, Color background)
        {
            if (_updateCard == null)
                return;
            _updateCard.BorderBrush = new SolidColorBrush(border);
            _updateCard.Background = new SolidColorBrush(background);
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
            Action clickAction,
            Action<TextBlock>? captureSubtitle = null)
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

            var subtitleText = new TextBlock
            {
                Text = subtitle,
                Foreground = new SolidColorBrush(Color.FromRgb(170, 180, 195)),
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 5, 0, 0)
            };
            panel.Children.Add(subtitleText);
            captureSubtitle?.Invoke(subtitleText);

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
