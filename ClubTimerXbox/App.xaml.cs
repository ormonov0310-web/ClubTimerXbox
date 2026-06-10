using System.Windows;
using ClubTimerXbox.Services;

namespace ClubTimerXbox
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Пока открыто окно входа, нельзя завершать приложение,
            // даже если окно входа временно закрывается.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var loginWindow = new LoginWindow();

            bool? loginResult = loginWindow.ShowDialog();

            if (loginResult == true)
            {
                string employeeName = EmployeeService.CurrentEmployee?.Name ?? "Неизвестно";

                // Умный журнал смен:
                // открываем смену сотрудника.
                ActionLogService.StartShift(employeeName);

                var mainWindow = new MainWindow();

                MainWindow = mainWindow;

                // Теперь приложение закрывается вместе с главным окном.
                ShutdownMode = ShutdownMode.OnMainWindowClose;

                mainWindow.Show();
            }
            else
            {
                Shutdown();
            }
        }
    }
}