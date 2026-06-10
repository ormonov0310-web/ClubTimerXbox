using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ClubTimerXbox
{
    public enum EmployeeActionResult
    {
        None,
        ChangeEmployee,
        ShowStatistics
    }

    public class EmployeeActionWindow : Window
    {
        public EmployeeActionResult SelectedAction { get; private set; } = EmployeeActionResult.None;

        private readonly string _currentEmployeeName;

        public EmployeeActionWindow(string currentEmployeeName)
        {
            _currentEmployeeName = currentEmployeeName;

            Title = "Смена сотрудника";
            Width = 500;
            Height = 380;
            MinWidth = 460;
            MinHeight = 340;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromRgb(16, 20, 28));

            Content = CreateContent();
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
                FontSize = 30,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 10)
            });

            root.Children.Add(new TextBlock
            {
                Text = "Выберите действие. Для просмотра статистики потребуется код текущего сотрудника.",
                Foreground = new SolidColorBrush(Color.FromRgb(170, 180, 195)),
                FontSize = 15,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 23,
                Margin = new Thickness(0, 0, 0, 24)
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
            root.Children.Add(cancelButton);

            return root;
        }

        private Button CreateBigButton(string text)
        {
            return new Button
            {
                Content = text,
                Height = 48,
                FontSize = 17,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 12)
            };
        }
    }
}