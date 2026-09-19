using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ClubTimerXbox
{
    public sealed class EmployeeDebtAmountWindow : AdaptiveDialogWindow
    {
        private readonly TextBox _amountBox = new();

        public int Amount { get; private set; }

        public EmployeeDebtAmountWindow(string title, string prompt, int suggestedAmount)
            : base(410, 390)
        {
            Title = title;

            var panel = new StackPanel { Margin = new Thickness(20) };
            panel.Children.Add(new TextBlock
            {
                Text = prompt,
                Foreground = Brushes.White,
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 14)
            });

            _amountBox.Text = suggestedAmount > 0 ? suggestedAmount.ToString() : "";
            _amountBox.Height = 42;
            _amountBox.FontSize = 20;
            _amountBox.Padding = new Thickness(10, 5, 10, 5);
            _amountBox.Margin = new Thickness(0, 0, 0, 18);
            panel.Children.Add(_amountBox);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            var cancel = new Button
            {
                Content = "Отмена",
                Width = 105,
                Height = 40,
                Margin = new Thickness(0, 0, 8, 0)
            };
            cancel.Click += (_, _) => DialogResult = false;
            var ok = new Button
            {
                Content = "ОК",
                Width = 105,
                Height = 40,
                Background = new SolidColorBrush(Color.FromRgb(22, 101, 52)),
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold
            };
            ok.Click += (_, _) => Accept();
            buttons.Children.Add(cancel);
            buttons.Children.Add(ok);
            panel.Children.Add(buttons);
            SetAdaptiveContent(panel);

            Loaded += (_, _) =>
            {
                _amountBox.Focus();
                _amountBox.SelectAll();
            };
        }

        private void Accept()
        {
            if (!int.TryParse(_amountBox.Text.Trim(), out int amount) || amount <= 0)
            {
                MessageBox.Show(
                    "Введите целую сумму больше 0.",
                    Title,
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            Amount = amount;
            DialogResult = true;
        }
    }
}
