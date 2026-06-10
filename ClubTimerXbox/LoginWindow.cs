using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ClubTimerXbox.Services;

namespace ClubTimerXbox
{
    public class LoginWindow : Window
    {
        private readonly PasswordBox _pinBox = new PasswordBox();
        private readonly TextBlock _errorText = new TextBlock();

        public LoginWindow()
        {
            Title = "Вход сотрудника";
            Width = 440;
            Height = 360;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;
            Background = new SolidColorBrush(Color.FromRgb(16, 20, 28));

            Content = CreateContent();
        }

        private UIElement CreateContent()
        {
            var root = new StackPanel
            {
                Margin = new Thickness(28)
            };

            root.Children.Add(new TextBlock
            {
                Text = "Club Timer Xbox",
                Foreground = Brushes.White,
                FontSize = 30,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 8)
            });

            root.Children.Add(new TextBlock
            {
                Text = "Введите код сотрудника для начала смены",
                Foreground = new SolidColorBrush(Color.FromRgb(170, 180, 195)),
                FontSize = 16,
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
            _pinBox.Height = 46;
            _pinBox.Padding = new Thickness(10, 4, 10, 4);
            _pinBox.PasswordChar = '●';
            _pinBox.KeyDown += (_, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Enter)
                    TryLogin();
            };

            root.Children.Add(_pinBox);

            _errorText.Text = "";
            _errorText.Foreground = new SolidColorBrush(Color.FromRgb(248, 113, 113));
            _errorText.FontSize = 14;
            _errorText.Margin = new Thickness(0, 8, 0, 0);

            root.Children.Add(_errorText);

            var loginButton = new Button
            {
                Content = "Войти",
                Height = 44,
                FontSize = 17,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 22, 0, 0)
            };

            loginButton.Click += (_, _) => TryLogin();

            root.Children.Add(loginButton);

            root.Children.Add(new TextBlock
            {
                Text = "Тестовые коды: 1111, 2222, 3333, 4444",
                Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                FontSize = 13,
                Margin = new Thickness(0, 18, 0, 0)
            });

            Loaded += (_, _) => _pinBox.Focus();

            return root;
        }

        private void TryLogin()
        {
            string pin = _pinBox.Password.Trim();

            if (EmployeeService.Login(pin))
            {
                DialogResult = true;
                Close();
                return;
            }

            _errorText.Text = "Неверный код или сотрудник отключён.";
            _pinBox.Clear();
            _pinBox.Focus();
        }
    }
}