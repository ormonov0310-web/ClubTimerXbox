using System.Windows;

namespace ClubTimerXbox
{
    public partial class MainWindow
    {
        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var window = new OwnerSettingsWindow(
                openTariffSettings: OpenTariffSettings,
                openStockSettings: OpenStockSettings,
                openServiceSettings: OpenServiceSettings
            )
            {
                Owner = this
            };

            window.ShowDialog();
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

        private void OpenServiceSettings()
        {
            var serviceWindow = new ServiceSettingsWindow
            {
                Owner = this
            };

            serviceWindow.ShowDialog();
        }
    }
}