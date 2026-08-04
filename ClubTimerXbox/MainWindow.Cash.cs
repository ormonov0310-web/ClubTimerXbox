using System;
using System.Windows.Input;
using System.Windows.Threading;
using ClubTimerXbox.Models;
using ClubTimerXbox.Services;

namespace ClubTimerXbox
{
    public partial class MainWindow
    {
        private readonly DispatcherTimer _cashTextTimer = new DispatcherTimer();
        private bool _runtimeCloseSaved = false;

        protected override void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);

            CheckRuntimeShiftAfterStart();

            Closing += (_, e) =>
            {
                if (e.Cancel || _runtimeCloseSaved)
                    return;

                _runtimeCloseSaved = true;
                bool preserveOpenState = AppUpdateShutdownCoordinator.IsPlannedUpdate &&
                    (AppUpdateShutdownCoordinator.Mode == AppUpdateInstallMode.SettingsResume ||
                     AppUpdateShutdownCoordinator.Mode == AppUpdateInstallMode.RemoteResume);
                if (preserveOpenState)
                    return;

                SaveRuntimeClosedNow(
                    publishNormalEvent: !AppUpdateShutdownCoordinator.IsPlannedUpdate);
            };

            MainCashText.Cursor = Cursors.Hand;
            MainCashText.MouseLeftButtonUp += MainCashText_MouseLeftButtonUp;

            UpdateCashShortText();

            _cashTextTimer.Interval = TimeSpan.FromSeconds(1);
            _cashTextTimer.Tick += (_, _) => UpdateCashShortText();
            _cashTextTimer.Start();
        }

        private void UpdateCashShortText()
        {
            int total = GetNewCashTotalForToday();

            MainCashText.Text = $"Касса: {total} сом";
        }

        private int GetNewCashTotalForToday()
        {
            var gamesFilter = new CashReportFilter
            {
                Section = CashReportSection.Games,
                PeriodMode = CashReportPeriodMode.Day,
                ViewMode = CashReportViewMode.Records,
                SelectedDay = BusinessCalendarService.GetBusinessDate(
                    ClubClock.Current.LocalNow)
            };

            var productsFilter = new CashReportFilter
            {
                Section = CashReportSection.ProductsAndServices,
                PeriodMode = CashReportPeriodMode.Day,
                ViewMode = CashReportViewMode.Records,
                SelectedDay = BusinessCalendarService.GetBusinessDate(
                    ClubClock.Current.LocalNow)
            };

            var gamesReport = CashReportService.BuildReport(gamesFilter);
            var productsReport = CashReportService.BuildReport(productsFilter);

            return gamesReport.Summary.TotalAmount + productsReport.Summary.TotalAmount;
        }

        private void MainCashText_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;

            var window = new CashReportWindow
            {
                Owner = this
            };

            window.ShowDialog();

            UpdateCashShortText();
        }
    }
}
