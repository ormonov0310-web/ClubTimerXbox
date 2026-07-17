using System.Windows;
using ClubTimerXbox.Services;

namespace ClubTimerXbox
{
    public partial class App : Application
    {
        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Пока открыто окно входа, нельзя завершать приложение,
            // даже если окно входа временно закрывается.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            WindowSizeStorageService.EnableForAllWindows();
            UiSoundService.EnableGlobalUiSounds();

            if (!PcIdentityService.HasAssignedClub)
            {
                if (FirebaseAuthService.IsConfigured)
                {
                    bool hasFirebaseSession = await FirebaseAuthService.TryRestoreAsync();
                    if (!hasFirebaseSession)
                    {
                        var firebaseLoginWindow = new FirebaseLoginWindow();
                        bool? firebaseLoginResult = firebaseLoginWindow.ShowDialog();

                        if (firebaseLoginResult != true)
                        {
                            Shutdown();
                            return;
                        }
                    }
                }

                var activationWindow = new ActivationWindow();
                bool? activationResult = activationWindow.ShowDialog();

                if (activationResult != true || !PcIdentityService.HasAssignedClub)
                {
                    Shutdown();
                    return;
                }
            }

            KnownDataRepairService.Apply();

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
