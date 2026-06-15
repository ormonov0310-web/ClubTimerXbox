using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ClubTimerXbox.Services;

namespace ClubTimerXbox
{
    public class ActivationWindow : Window
    {
        private readonly TextBox _codeBox = new TextBox();
        private readonly TextBlock _statusText = new TextBlock();
        private readonly Button _activateButton = new Button();

        public ActivationWindow()
        {
            Title = "Активация клуба";
            Width = 500;
            Height = 390;
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
                Text = "Активация ПК",
                Foreground = Brushes.White,
                FontSize = 30,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 8)
            });

            root.Children.Add(new TextBlock
            {
                Text = "Создайте клуб на телефоне и введите сюда код активации.",
                Foreground = new SolidColorBrush(Color.FromRgb(170, 180, 195)),
                FontSize = 16,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 24)
            });

            root.Children.Add(new TextBlock
            {
                Text = "Код активации",
                Foreground = Brushes.White,
                FontSize = 16,
                Margin = new Thickness(0, 0, 0, 8)
            });

            _codeBox.FontSize = 26;
            _codeBox.Height = 48;
            _codeBox.Padding = new Thickness(10, 4, 10, 4);
            _codeBox.MaxLength = 12;
            _codeBox.HorizontalContentAlignment = HorizontalAlignment.Center;
            _codeBox.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                    _ = TryActivateAsync();
            };

            root.Children.Add(_codeBox);

            _statusText.Text = "";
            _statusText.Foreground = new SolidColorBrush(Color.FromRgb(248, 113, 113));
            _statusText.FontSize = 14;
            _statusText.TextWrapping = TextWrapping.Wrap;
            _statusText.Margin = new Thickness(0, 10, 0, 0);

            root.Children.Add(_statusText);

            _activateButton.Content = "Активировать";
            _activateButton.Height = 44;
            _activateButton.FontSize = 17;
            _activateButton.FontWeight = FontWeights.SemiBold;
            _activateButton.Margin = new Thickness(0, 22, 0, 0);
            _activateButton.Click += async (_, _) => await TryActivateAsync();

            root.Children.Add(_activateButton);

            root.Children.Add(new TextBlock
            {
                Text = "Этот код одноразовый. После активации ПК будет привязан к выбранному клубу.",
                Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 18, 0, 0)
            });

            Loaded += (_, _) => _codeBox.Focus();

            return root;
        }

        private async System.Threading.Tasks.Task TryActivateAsync()
        {
            string code = _codeBox.Text.Trim();

            _activateButton.IsEnabled = false;
            _statusText.Foreground = new SolidColorBrush(Color.FromRgb(250, 204, 21));
            _statusText.Text = "Проверяем код...";

            try
            {
                var result = await PcActivationService.ActivateAsync(code);

                if (!result.Success)
                {
                    _statusText.Foreground = new SolidColorBrush(Color.FromRgb(248, 113, 113));
                    _statusText.Text = result.ErrorMessage;
                    _codeBox.Focus();
                    _codeBox.SelectAll();
                    return;
                }

                _statusText.Foreground = new SolidColorBrush(Color.FromRgb(74, 222, 128));
                _statusText.Text = $"ПК привязан к клубу: {result.ClubName}";

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                _statusText.Foreground = new SolidColorBrush(Color.FromRgb(248, 113, 113));
                _statusText.Text = "Не удалось подключиться к Firebase.\n" + ex.Message;
            }
            finally
            {
                _activateButton.IsEnabled = true;
            }
        }
    }
}
