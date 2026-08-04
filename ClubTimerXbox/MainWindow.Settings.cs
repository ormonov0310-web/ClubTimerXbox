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
                installUpdate: progress => AppUpdateService.InstallLatestUpdateAsync(
                    _places.ToList(),
                    progress,
                    AppUpdateInstallMode.SettingsResume))
            {
                Owner = this
            };

            window.ShowDialog();
            _settingsUpdateInfo = AppUpdateService.GetLocalUpdateInfo(_places);
            UpdateSettingsButtonUpdateState();
        }

        private async Task RefreshSettingsUpdateIndicatorAsync(bool forceRefresh)
        {
            try
            {
                _settingsUpdateInfo = await AppUpdateService.GetLatestUpdateInfoAsync(
                    _places.ToList(),
                    forceRefresh);
                UpdateSettingsButtonUpdateState();
            }
            catch
            {
                _settingsUpdateInfo = AppUpdateService.GetLocalUpdateInfo(_places);
                UpdateSettingsButtonUpdateState();
            }
        }

        private void UpdateSettingsButtonUpdateState()
        {
            if (SettingsButton == null)
                return;

            SettingsButton.Content = "⚙ Настройки";
            var info = _settingsUpdateInfo;
            if (info?.HasUpdate != true)
            {
                SettingsButton.ClearValue(BorderBrushProperty);
                SettingsButton.ClearValue(BorderThicknessProperty);
                SettingsButton.ClearValue(BackgroundProperty);
                SettingsButton.ClearValue(ToolTipProperty);
                return;
            }

            SettingsButton.BorderThickness = new Thickness(2);
            SettingsButton.Foreground = Brushes.White;
            SettingsButton.Background = new SolidColorBrush(Color.FromRgb(31, 41, 55));

            switch (info.Stage)
            {
                case AppUpdateService.AppUpdateStage.Downloading:
                    SettingsButton.BorderBrush = CreateRotatingUpdateBrush();
                    SettingsButton.ToolTip =
                        $"Скачивается обновление {info.DisplayLatestVersion}: {info.DownloadPercent}%";
                    break;

                case AppUpdateService.AppUpdateStage.Verifying:
                case AppUpdateService.AppUpdateStage.Installing:
                case AppUpdateService.AppUpdateStage.Recovering:
                    SettingsButton.BorderBrush = CreateRotatingUpdateBrush();
                    SettingsButton.ToolTip = info.StateMessage;
                    break;

                case AppUpdateService.AppUpdateStage.DownloadedBlocked:
                    SettingsButton.BorderBrush = new SolidColorBrush(Color.FromRgb(59, 130, 246));
                    SettingsButton.ToolTip =
                        $"Обновление {info.DisplayLatestVersion} скачано. Активных мест: {info.ActivePlaces}.";
                    break;

                case AppUpdateService.AppUpdateStage.Ready:
                    ApplyBlinkingState(
                        Color.FromRgb(34, 197, 94),
                        Color.FromRgb(20, 83, 45));
                    SettingsButton.ToolTip =
                        $"Обновление {info.DisplayLatestVersion} готово к установке.";
                    break;

                case AppUpdateService.AppUpdateStage.Failed:
                    ApplyBlinkingState(
                        Color.FromRgb(239, 68, 68),
                        Color.FromRgb(127, 29, 29));
                    SettingsButton.ToolTip = info.StateMessage;
                    break;

                default:
                    ApplyBlinkingState(
                        Color.FromRgb(245, 158, 11),
                        Color.FromRgb(120, 78, 12));
                    SettingsButton.ToolTip =
                        $"Вышло обновление {info.DisplayLatestVersion}. Подготавливаем пакет.";
                    break;
            }
        }

        private void ApplyBlinkingState(Color bright, Color dark)
        {
            SettingsButton.BorderBrush = new SolidColorBrush(
                _settingsUpdateBlinkState ? bright : dark);
            SettingsButton.Background = new SolidColorBrush(
                _settingsUpdateBlinkState
                    ? Color.FromRgb(31, 48, 43)
                    : Color.FromRgb(31, 41, 55));
        }

        private static Brush CreateRotatingUpdateBrush()
        {
            double angle = Environment.TickCount64 % 2400 / 2400.0 * 360.0;
            var brush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0.5),
                EndPoint = new Point(1, 0.5),
                RelativeTransform = new RotateTransform(angle, 0.5, 0.5)
            };
            brush.GradientStops.Add(new GradientStop(Color.FromRgb(30, 64, 175), 0));
            brush.GradientStops.Add(new GradientStop(Color.FromRgb(56, 189, 248), 0.48));
            brush.GradientStops.Add(new GradientStop(Color.FromRgb(30, 64, 175), 1));
            return brush;
        }

        private void OpenTariffSettings()
        {
            var settingsWindow = new SettingsWindow
            {
                Owner = this
            };
            bool? result = settingsWindow.ShowDialog();
            if (result == true)
                ReloadPlacesFromSettings();
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
