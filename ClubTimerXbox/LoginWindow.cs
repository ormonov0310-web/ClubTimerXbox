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
            Width = 520;
            Height = 440;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;

            Content = CreateContent();
        }

        private UIElement CreateContent()
        {
            var panel = new StackPanel
            {
                Margin = new Thickness(28)
            };

            panel.Children.Add(new TextBlock
            {
                Text = "Club Timer Xbox",
                Foreground = Brushes.White,
                FontSize = 30,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 8)
            });

            panel.Children.Add(new TextBlock
            {
                Text = "Введите код сотрудника для начала смены",
                Foreground = new SolidColorBrush(Color.FromRgb(170, 180, 195)),
                FontSize = 16,
                Margin = new Thickness(0, 0, 0, 24)
            });

            panel.Children.Add(new TextBlock
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

            panel.Children.Add(_pinBox);

            _errorText.Text = "";
            _errorText.Foreground = new SolidColorBrush(Color.FromRgb(248, 113, 113));
            _errorText.FontSize = 14;
            _errorText.Margin = new Thickness(0, 8, 0, 0);

            panel.Children.Add(_errorText);

            var loginButton = new Button
            {
                Content = "Войти",
                Height = 44,
                FontSize = 17,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 22, 0, 0)
            };

            loginButton.Click += (_, _) => TryLogin();

            panel.Children.Add(loginButton);

            panel.Children.Add(new TextBlock
            {
                Text = "Тестовые коды: 1111, 2222, 3333, 4444",
                Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                FontSize = 13,
                Margin = new Thickness(0, 18, 0, 0)
            });

            Loaded += (_, _) => _pinBox.Focus();

            var card = new Border
            {
                Width = 430,
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(2),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Child = panel,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 28,
                    ShadowDepth = 8,
                    Opacity = 0.32,
                    Color = Colors.Black
                }
            };
            card.SetResourceReference(Border.BackgroundProperty, "Theme.CardBrush");
            card.SetResourceReference(Border.BorderBrushProperty, "Theme.BorderBrush");

            return new Grid
            {
                Margin = new Thickness(22),
                Children = { card }
            };
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
