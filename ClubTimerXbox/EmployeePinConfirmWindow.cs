using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ClubTimerXbox.Services;

namespace ClubTimerXbox
{
    public class EmployeePinConfirmWindow : Window
    {
        private readonly string _employeeName;
        private readonly PasswordBox _pinBox = new PasswordBox();
        private readonly TextBlock _errorText = new TextBlock();

        public EmployeePinConfirmWindow(string employeeName)
        {
            _employeeName = employeeName;

            Title = "Подтверждение сотрудника";
            Width = 500;
            Height = 420;
            MinWidth = 460;
            MinHeight = 380;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromRgb(16, 20, 28));

            Content = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = CreateContent()
            };
        }

        private UIElement CreateContent()
        {
            var root = new StackPanel
            {
                Margin = new Thickness(28)
            };

            root.Children.Add(new TextBlock
            {
                Text = "Подтверждение",
                Foreground = Brushes.White,
                FontSize = 30,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 8)
            });

            root.Children.Add(new TextBlock
            {
                Text = $"Чтобы открыть статистику сотрудника \"{_employeeName}\", введите именно его код.",
                Foreground = new SolidColorBrush(Color.FromRgb(170, 180, 195)),
                FontSize = 16,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 24,
                Margin = new Thickness(0, 0, 0, 24)
            });

            root.Children.Add(new TextBlock
            {
                Text = "Код сотрудника",
                Foreground = Brushes.White,
                FontSize = 16,
                Margin = new Thickness(0, 0, 0, 8)
            });

            _pinBox.FontSize = 24;
            _pinBox.Height = 48;
            _pinBox.Padding = new Thickness(10, 5, 10, 5);
            _pinBox.PasswordChar = '●';
            _pinBox.KeyDown += (_, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Enter)
                    TryConfirm();
            };

            root.Children.Add(_pinBox);

            _errorText.Text = "";
            _errorText.Foreground = new SolidColorBrush(Color.FromRgb(248, 113, 113));
            _errorText.FontSize = 14;
            _errorText.TextWrapping = TextWrapping.Wrap;
            _errorText.Margin = new Thickness(0, 10, 0, 0);

            root.Children.Add(_errorText);

            var buttonsPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 26, 0, 0)
            };

            var cancelButton = new Button
            {
                Content = "Отмена",
                Width = 120,
                Height = 44,
                FontSize = 16,
                Margin = new Thickness(0, 0, 10, 0)
            };

            cancelButton.Click += (_, _) =>
            {
                DialogResult = false;
                Close();
            };

            var okButton = new Button
            {
                Content = "ОК",
                Width = 120,
                Height = 44,
                FontSize = 16
            };

            okButton.Click += (_, _) => TryConfirm();

            buttonsPanel.Children.Add(cancelButton);
            buttonsPanel.Children.Add(okButton);

            root.Children.Add(buttonsPanel);

            Loaded += (_, _) => _pinBox.Focus();

            return root;
        }

        private void TryConfirm()
        {
            string pin = _pinBox.Password.Trim();

            if (EmployeeService.ValidateEmployeePin(_employeeName, pin))
            {
                DialogResult = true;
                Close();
                return;
            }

            _errorText.Text = "Неверный код. Для просмотра статистики нужен код именно этого сотрудника.";
            _pinBox.Clear();
            _pinBox.Focus();
        }
    }
}