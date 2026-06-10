using System.Windows;

namespace ClubTimerXbox
{
    public partial class MainWindow
    {
        private void StockAuditButton_Click(object sender, RoutedEventArgs e)
        {
            var window = new StockAuditWindow
            {
                Owner = this
            };

            bool? result = window.ShowDialog();

            if (result == true)
            {
                // После приёмки могли измениться остатки.
                // Поэтому просто оставляем главный экран актуальным.
                DrawPlaces();
            }
        }
    }
}