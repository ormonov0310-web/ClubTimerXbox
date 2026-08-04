using System.Windows;
using ClubTimerXbox.Services;

namespace ClubTimerXbox
{
    public partial class App : Application
    {
        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            AppUpdateLaunchContext.Initialize(e.Args);

            // Пока открыто окно входа, нельзя завершать приложение,
            // даже если окно входа временно закрывается.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            WindowSizeStorageService.EnableForAllWindows();
            UiSoundService.EnableGlobalUiSounds();
            VisualThemeService.EnableForAllWindows();

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

            if (FirebaseAuthService.IsConfigured)
                await FirebaseAuthService.TryRestoreAsync();

            if (AppUpdateLaunchContext.Ticket != null)
            {
                UpdateSessionTicket ticket = AppUpdateLaunchContext.Ticket;
                AppUpdateService.ApplyLaunchResult(
                    AppUpdateLaunchContext.Result,
                    ticket.TargetVersion);
                await FirebaseEventService.PublishUpdateResultAsync(
                    ticket,
                    AppUpdateLaunchContext.Result);
                try
                {
                    await AppUpdateService.FinalizeUpdateSessionAsync(
                        ticket,
                        AppUpdateLaunchContext.Result);
                }
                catch
                {
                    // The local updater status is reported again by the regular sync loop.
                }

                if (AppUpdateLaunchContext.ReportOnly)
                {
                    try
                    {
                        await FirebaseSyncService.PushClosedStateAsync(
                            string.IsNullOrWhiteSpace(ticket.EmployeeName)
                                ? "Не выбран"
                                : ticket.EmployeeName);
                        await FirebaseEventService.FlushPendingAsync();
                    }
                    catch
                    {
                        // The event remains in the local outbox for the next normal start.
                    }

                    AppUpdateLaunchContext.Complete("Результат обновления отправлен владельцу.");
                    AppUpdateRuntimeGuard.MarkCleanShutdown();
                    Shutdown();
                    return;
                }
            }

            if (!AppUpdateLaunchContext.WasUpdateLaunch &&
                await AppUpdateService.TryInstallPreparedUpdateAtStartupAsync())
            {
                Shutdown();
                return;
            }

            KnownDataRepairService.Apply();
            CashPenaltyPostingService.Recover();
            CashMonthCloseService.Start();
            BusinessAccountingService.EnsureActivated();
            BusinessDayTransitionService.Start();

            bool resumedAfterUpdate = AppUpdateLaunchContext.IsResumeLaunch &&
                AppUpdateLaunchContext.Ticket != null &&
                EmployeeService.ResumeAfterUpdate(
                    AppUpdateLaunchContext.Ticket.EmployeeId,
                    AppUpdateLaunchContext.Ticket.EmployeeName);
            bool? loginResult;

            if (resumedAfterUpdate)
            {
                loginResult = true;
            }
            else
            {
                var loginWindow = new LoginWindow();
                loginResult = loginWindow.ShowDialog();
            }

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

                if (AppUpdateLaunchContext.WasUpdateLaunch)
                    AppUpdateLaunchContext.Complete("Программа успешно запущена после обновления.");
            }
            else
            {
                Shutdown();
            }
        }
    }
}
