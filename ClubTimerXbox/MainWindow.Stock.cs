using System.Windows;

namespace ClubTimerXbox
{
    public partial class MainWindow
    {
        private void StockButton_Click(object sender, RoutedEventArgs e)
        {
            var window = new StockWindow
            {
                Owner = this
            };

            window.ShowDialog();
        }
    }
}