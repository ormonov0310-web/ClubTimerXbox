using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ClubTimerXbox.Services;

namespace ClubTimerXbox
{
    public class FirebaseLoginWindow : Window
    {
        private readonly TextBox _emailBox = new TextBox();
        private readonly PasswordBox _passwordBox = new PasswordBox();
        private readonly TextBlock _errorText = new TextBlock();
        private readonly Button _loginButton = new Button();

        public FirebaseLoginWindow()
        {
            Title = "Вход Firebase";
            Width = 470;
            Height = 520;
            MinWidth = 420;
            MinHeight = 420;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.CanResize;
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
                Margin = new Thickness(28, 24, 28, 24)
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
                Text = "Введите логин и пароль Firebase. Пример: club1@xbox.local. Для другого клуба меняется только номер: club2@xbox.local, club3@xbox.local и далее.",
                Foreground = new SolidColorBrush(Color.FromRgb(170, 180, 195)),
                FontSize = 15,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 22)
            });

            root.Children.Add(Label("Email Firebase"));
            _emailBox.Text = FirebaseAuthService.SuggestedEmail;
            _emailBox.FontSize = 18;
            _emailBox.Height = 42;
            _emailBox.Padding = new Thickness(10, 4, 10, 4);
            _emailBox.Margin = new Thickness(0, 0, 0, 14);
            root.Children.Add(_emailBox);

            root.Children.Add(Label("Пароль"));
            _passwordBox.FontSize = 18;
            _passwordBox.Height = 42;
            _passwordBox.Padding = new Thickness(10, 4, 10, 4);
            _passwordBox.PasswordChar = '●';
            _passwordBox.KeyDown += async (_, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Enter)
                    await TryLoginAsync();
            };
            root.Children.Add(_passwordBox);

            _errorText.Text = "";
            _errorText.Foreground = new SolidColorBrush(Color.FromRgb(248, 113, 113));
            _errorText.FontSize = 14;
            _errorText.TextWrapping = TextWrapping.Wrap;
            _errorText.Margin = new Thickness(0, 8, 0, 0);
            root.Children.Add(_errorText);

            _loginButton.Content = "Войти";
            _loginButton.Height = 44;
            _loginButton.FontSize = 17;
            _loginButton.FontWeight = FontWeights.SemiBold;
            _loginButton.Margin = new Thickness(0, 22, 0, 0);
            _loginButton.Click += async (_, _) => await TryLoginAsync();
            root.Children.Add(_loginButton);

            Loaded += (_, _) =>
            {
                if (string.IsNullOrWhiteSpace(_emailBox.Text))
                    _emailBox.Focus();
                else
                    _passwordBox.Focus();
            };

            return root;
        }

        private static TextBlock Label(string text)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = Brushes.White,
                FontSize = 15,
                Margin = new Thickness(0, 0, 0, 7)
            };
        }

        private async Task TryLoginAsync()
        {
            string email = _emailBox.Text.Trim();
            string password = _passwordBox.Password;

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                _errorText.Text = "Введите email и пароль Firebase.";
                return;
            }

            _loginButton.IsEnabled = false;
            _loginButton.Content = "Входим...";
            _errorText.Text = "";

            try
            {
                await FirebaseAuthService.SignInAsync(email, password);
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                _errorText.Text = ex.Message;
                _passwordBox.Clear();
                _passwordBox.Focus();
            }
            finally
            {
                _loginButton.IsEnabled = true;
                _loginButton.Content = "Войти";
            }
        }
    }
}
