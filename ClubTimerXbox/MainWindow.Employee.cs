using System.Windows;
using ClubTimerXbox.Services;

namespace ClubTimerXbox
{
    public partial class MainWindow
    {
        private void UpdateCurrentEmployeeText()
        {
            if (EmployeeService.CurrentEmployee == null)
            {
                CurrentEmployeeButton.Content = "Смена: не выбрана";
                return;
            }

            CurrentEmployeeButton.Content = $"Смена: {EmployeeService.CurrentEmployee.Name}";
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

            var statsWindow = new EmployeeStatsWindow(EmployeeService.CurrentEmployee.Name)
            {
                Owner = this
            };

            statsWindow.ShowDialog();
        }
    }
}