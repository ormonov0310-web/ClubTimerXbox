using System.Windows;
using ClubTimerXbox.Services;

using System;
using System.Windows.Media;
using System.Windows.Threading;

namespace ClubTimerXbox
{
    public partial class MainWindow
    {
        private readonly DispatcherTimer _employeeRatingBorderAnimationTimer = new()
        {
            Interval = TimeSpan.FromMilliseconds(120)
        };
        private bool _isEmployeeRatingBorderActive;

        private void InitializeEmployeeRatingBorderAnimation()
        {
            _employeeRatingBorderAnimationTimer.Tick += (_, _) =>
            {
                if (_isEmployeeRatingBorderActive)
                    CurrentEmployeeButton.BorderBrush = CreateEmployeeRatingBorderBrush();
            };
        }

        private void UpdateCurrentEmployeeRatingBorderState()
        {
            string employeeName = EmployeeService.CurrentEmployee?.Name.Trim() ?? "";
            _isEmployeeRatingBorderActive = !string.IsNullOrWhiteSpace(employeeName) &&
                EmployeeRatingService
                    .GetSnapshot(employeeName, ClubClock.Current.LocalNow)
                    .OverallPercent > 100;

            if (_isEmployeeRatingBorderActive)
            {
                CurrentEmployeeButton.BorderThickness = new Thickness(2);
                CurrentEmployeeButton.BorderBrush = CreateEmployeeRatingBorderBrush();
                if (!_employeeRatingBorderAnimationTimer.IsEnabled)
                    _employeeRatingBorderAnimationTimer.Start();
                return;
            }

            _employeeRatingBorderAnimationTimer.Stop();
            CurrentEmployeeButton.ClearValue(
                System.Windows.Controls.Control.BorderBrushProperty);
            CurrentEmployeeButton.ClearValue(
                System.Windows.Controls.Control.BorderThicknessProperty);
        }

        private static Brush CreateEmployeeRatingBorderBrush()
        {
            double angle = Environment.TickCount64 % 5200 / 5200.0 * 360.0;
            var brush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0.5),
                EndPoint = new Point(1, 0.5),
                RelativeTransform = new RotateTransform(angle, 0.5, 0.5)
            };
            brush.GradientStops.Add(new GradientStop(Color.FromRgb(20, 83, 45), 0));
            brush.GradientStops.Add(new GradientStop(Color.FromRgb(134, 239, 172), 0.48));
            brush.GradientStops.Add(new GradientStop(Color.FromRgb(20, 83, 45), 1));
            return brush;
        }

        private void UpdateCurrentEmployeeText()
        {
            if (EmployeeService.CurrentEmployee == null)
            {
                CurrentEmployeeButton.Content = "Смена: не выбрана";
                UpdateCurrentEmployeeRatingBorderState();
                return;
            }

            CurrentEmployeeButton.Content = $"Смена: {EmployeeService.CurrentEmployee.Name}";
            UpdateCurrentEmployeeRatingBorderState();
        }

        private void CurrentEmployeeButton_Click(object sender, RoutedEventArgs e)
        {
            string currentName = EmployeeService.CurrentEmployee?.Name ?? "не выбрана";

            var actionWindow = new EmployeeActionWindow(currentName)
            {
                Owner = this
            };

            bool? result = actionWindow.ShowDialog();

            if (result != true)
                return;

            if (actionWindow.SelectedAction == EmployeeActionResult.ChangeEmployee)
            {
                ChangeEmployee();
                return;
            }

            if (actionWindow.SelectedAction == EmployeeActionResult.ShowStatistics)
            {
                ConfirmAndShowCurrentEmployeeInfo();
            }
        }

        private void ChangeEmployee()
        {
            string oldEmployeeName = EmployeeService.CurrentEmployee?.Name ?? "Неизвестно";

            var loginWindow = new LoginWindow
            {
                Owner = this
            };

            bool? result = loginWindow.ShowDialog();

            if (result == true)
            {
                string newEmployeeName = EmployeeService.CurrentEmployee?.Name ?? "Неизвестно";

                // Умный журнал:
                // закрывает старую смену и открывает новую.
                ActionLogService.SwitchShift(newEmployeeName);

                UpdateCurrentEmployeeText();
                _ = FirebaseEventService.PublishEmployeeChangedAsync(
                    oldEmployeeName,
                    newEmployeeName
                );

                MessageBox.Show(
                    $"Смена изменена.\n\n" +
                    $"Было: {oldEmployeeName}\n" +
                    $"Стало: {newEmployeeName}\n\n" +
                    $"Активные места не сброшены. Таймеры продолжают работать.",
                    "Смена сотрудника"
                );
            }
        }

        private void ConfirmAndShowCurrentEmployeeInfo()
        {
            if (EmployeeService.CurrentEmployee == null)
            {
                MessageBox.Show("Смена не выбрана.", "Сотрудник");
                return;
            }

            if (IsStockAcceptanceBlockingEmployeeStats())
            {
                MessageBox.Show(
                    "Сначала завершите приёмку.\n\n" +
                    "После приёмки можно открыть статистику сотрудника и взять аванс.",
                    "Приёмка смены",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );

                return;
            }

            string employeeName = EmployeeService.CurrentEmployee.Name;

            var confirmWindow = new EmployeePinConfirmWindow(employeeName)
            {
                Owner = this
            };

            bool? result = confirmWindow.ShowDialog();

            if (result == true)
            {
                ShowCurrentEmployeeInfo();
            }
        }

        private void ShowCurrentEmployeeInfo()
        {
            if (EmployeeService.CurrentEmployee == null)
            {
                MessageBox.Show("Смена не выбрана.", "Сотрудник");
                return;
            }

            if (IsStockAcceptanceBlockingEmployeeStats())
            {
                MessageBox.Show(
                    "Сначала завершите приёмку.",
                    "Приёмка смены",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );

                return;
            }

            var statsWindow = new EmployeeStatsWindow(EmployeeService.CurrentEmployee.Name)
            {
                Owner = this
            };

            statsWindow.ShowDialog();
        }

        private bool IsStockAcceptanceBlockingEmployeeStats()
        {
            ActionLogService.EnsureAcceptanceForCurrentShift();
            return ShiftAcceptanceService.IsAcceptanceRequired();
        }
    }
}
