using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ClubTimerXbox
{
    public enum EmployeeActionResult
    {
        None,
        ChangeEmployee,
        ShowStatistics,
        AccessSalary
    }

    public class EmployeeActionWindow : AdaptiveDialogWindow
    {
        public EmployeeActionResult SelectedAction { get; private set; } = EmployeeActionResult.None;

        private readonly string _currentEmployeeName;

        public EmployeeActionWindow(string currentEmployeeName) : base(500, 360)
        {
            _currentEmployeeName = currentEmployeeName;

            Title = "Смена сотрудника";
            SetAdaptiveContent(CreateContent());
        }

        private UIElement CreateContent()
        {
            var root = new StackPanel
            {
                Margin = new Thickness(26)
            };

            root.Children.Add(new TextBlock
            {
                Text = $"Смена: {_currentEmployeeName}",
                Foreground = Brushes.White,
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            });

            var changeButton = CreateBigButton("Сменить сотрудника");
            changeButton.Click += (_, _) =>
            {
                SelectedAction = EmployeeActionResult.ChangeEmployee;
                DialogResult = true;
                Close();
            };

            var statsButton = CreateBigButton("Посмотреть статистику");
            statsButton.Click += (_, _) =>
            {
                SelectedAction = EmployeeActionResult.ShowStatistics;
                DialogResult = true;
                Close();
            };

            var cancelButton = CreateBigButton("Отмена");
            cancelButton.Click += (_, _) =>
            {
                SelectedAction = EmployeeActionResult.None;
                DialogResult = false;
                Close();
            };

            root.Children.Add(changeButton);
            root.Children.Add(statsButton);
            var salaryButton = CreateBigButton("Посмотреть / взять зарплату");
            salaryButton.Click += (_, _) =>
            {
                SelectedAction = EmployeeActionResult.AccessSalary;
                DialogResult = true;
            };
            root.Children.Add(salaryButton);
            root.Children.Add(cancelButton);

            return root;
        }

        private Button CreateBigButton(string text)
        {
            return new Button
            {
                Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center },
                MinHeight = 48,
                Padding = new Thickness(10, 8, 10, 8),
                FontSize = 17,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 12)
            };
        }
    }
}
