using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using ClubTimerXbox.Services;

namespace ClubTimerXbox
{
    public partial class MainWindow
    {
        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var window = new OwnerSettingsWindow(
                openTariffSettings: OpenTariffSettings,
                openStockSettings: OpenStockSettings,
                openTuyaSettings: OpenTuyaSettings,
                openAlarmSettings: OpenAlarmSettingsWindow,
                getPlaces: () => _places.ToList(),
                installUpdate: () => AppUpdateService.InstallLatestUpdateAsync(_places.ToList())
            )
            {
                Owner = this
            };

            window.ShowDialog();
            UpdateSettingsButtonUpdateState();
        }

        private async Task RefreshSettingsUpdateIndicatorAsync(bool forceRefresh)
        {
            try
            {
                _settingsUpdateInfo = await AppUpdateService.GetLatestUpdateInfoAsync(
                    _places.ToList(),
                    forceRefresh
                );

                UpdateSettingsButtonUpdateState();
            }
            catch
            {
                _settingsUpdateInfo = null;
                UpdateSettingsButtonUpdateState();
            }
        }

        private void UpdateSettingsButtonUpdateState()
        {
            if (SettingsButton == null)
                return;

            if (_settingsUpdateInfo?.HasUpdate != true)
            {
                SettingsButton.Content = "⚙ Настройки";
                SettingsButton.ClearValue(BorderBrushProperty);
                SettingsButton.ClearValue(BorderThicknessProperty);
                SettingsButton.ClearValue(BackgroundProperty);
                SettingsButton.ClearValue(ToolTipProperty);
                return;
            }

            SettingsButton.Content = "⚙ Настройки";
            SettingsButton.BorderThickness = new Thickness(2);
            SettingsButton.ToolTip =
                $"Вышло обновление {_settingsUpdateInfo.DisplayLatestVersion}. " +
                (_settingsUpdateInfo.SafeToInstall
                    ? "Клуб свободен, можно установить."
                    : $"Активных мест: {_settingsUpdateInfo.ActivePlaces}.");

            if (_settingsUpdateInfo.SafeToInstall)
            {
                SettingsButton.BorderBrush = new SolidColorBrush(
                    _settingsUpdateBlinkState
                        ? Color.FromRgb(245, 158, 11)
                        : Color.FromRgb(120, 78, 12)
                );
                SettingsButton.Background = new SolidColorBrush(
                    _settingsUpdateBlinkState
                        ? Color.FromRgb(92, 58, 9)
                        : Color.FromRgb(31, 41, 55)
                );
                SettingsButton.Foreground = Brushes.White;
            }
            else
            {
                SettingsButton.BorderBrush = new SolidColorBrush(Color.FromRgb(245, 158, 11));
                SettingsButton.Background = new SolidColorBrush(Color.FromRgb(31, 41, 55));
                SettingsButton.Foreground = Brushes.White;
            }
        }

        private void OpenTariffSettings()
        {
            var settingsWindow = new SettingsWindow
            {
                Owner = this
            };

            bool? result = settingsWindow.ShowDialog();

            if (result == true)
            {
                ReloadPlacesFromSettings();
            }
        }

        private void OpenStockSettings()
        {
            var stockWindow = new StockWindow
            {
                Owner = this
            };

            stockWindow.ShowDialog();
        }

        private void OpenTuyaSettings()
        {
            var tuyaWindow = new TuyaSettingsWindow
            {
                Owner = this
            };

            tuyaWindow.ShowDialog();
        }
    }
}
