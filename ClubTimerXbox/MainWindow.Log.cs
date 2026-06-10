using System.Windows;

namespace ClubTimerXbox
{
    public partial class MainWindow
    {
        private void ActionLogButton_Click(object sender, RoutedEventArgs e)
        {
            var window = new ActionLogWindow
            {
                Owner = this
            };

            window.ShowDialog();
        }
    }
}