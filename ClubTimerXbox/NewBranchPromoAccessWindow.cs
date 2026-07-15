using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ClubTimerXbox.Services;

namespace ClubTimerXbox
{
    public class NewBranchPromoAccessWindow : Window
    {
        private readonly PasswordBox _codeBox = new PasswordBox();
        private readonly TextBlock _errorText = new TextBlock();

        public bool IsAccessGranted { get; private set; }

        public NewBranchPromoAccessWindow()
        {
            Title = "Доступ владельца";
            Width = 460;
            Height = 300;
            MinWidth = 420;
            MinHeight = 280;
            ResizeMode = ResizeMode.CanResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromRgb(16, 20, 28));
            Content = CreateContent();

            Loaded += (_, _) => _codeBox.Focus();
        }

        private UIElement CreateContent()
        {
            var root = new StackPanel
            {
                Margin = new Thickness(24)
            };

            root.Children.Add(new TextBlock
            {
                Text = "Только владелец может открыть этот параметр",
                Foreground = Brushes.White,
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 18)
            });

            root.Children.Add(new TextBlock
            {
                Text = "Введите код",
                Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                FontSize = 15,
                Margin = new Thickness(0, 0, 0, 8)
            });

            _codeBox.FontSize = 18;
            _codeBox.Height = 40;
            _codeBox.Padding = new Thickness(8, 4, 8, 4);
            _codeBox.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    e.Handled = true;
                    ConfirmCode();
                }
            };
            root.Children.Add(_codeBox);

            _errorText.Foreground = new SolidColorBrush(Color.FromRgb(248, 113, 113));
            _errorText.FontSize = 14;
            _errorText.TextWrapping = TextWrapping.Wrap;
            _errorText.Margin = new Thickness(0, 10, 0, 0);
            root.Children.Add(_errorText);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 22, 0, 0)
            };

            var cancelButton = new Button
            {
                Content = "Отмена",
                Width = 120,
                Height = 42,
                FontSize = 16,
                Margin = new Thickness(0, 0, 8, 0)
            };
            cancelButton.Click += (_, _) => Close();

            var okButton = new Button
            {
                Content = "Подтвердить",
                Width = 140,
                Height = 42,
                FontSize = 16,
                FontWeight = FontWeights.SemiBold
            };
            okButton.Click += (_, _) => ConfirmCode();

            buttons.Children.Add(cancelButton);
            buttons.Children.Add(okButton);
            root.Children.Add(buttons);

            return new ScrollViewer
            {
                Content = root,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
        }

        private void ConfirmCode()
        {
            if (OwnerAccessService.IsValidCode(_codeBox.Password))
            {
                IsAccessGranted = true;
                DialogResult = true;
                Close();
                return;
            }

            _errorText.Text = "Неверный код.";
            _codeBox.SelectAll();
            _codeBox.Focus();
        }
    }
}
