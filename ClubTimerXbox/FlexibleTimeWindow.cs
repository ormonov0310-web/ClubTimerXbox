using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ClubTimerXbox.Models;
using ClubTimerXbox.Services;

namespace ClubTimerXbox
{
    public sealed class FlexibleTimeWindow : Window
    {
        private readonly ClubPlace _place;
        private readonly TariffSettings _tariff;
        private readonly bool _isAddTime;
        private readonly TextBox _minutesBox = new TextBox();
        private readonly TextBox _amountBox = new TextBox();
        private readonly TextBlock _resultText = new TextBlock();
        private bool _isUpdating;
        private int _calculatedSeconds;
        private int _calculatedAmount;

        public int Seconds => _calculatedSeconds;

        public int Amount => _calculatedAmount;

        public FlexibleTimeWindow(
            ClubPlace place,
            TariffSettings tariff,
            bool isAddTime)
        {
            _place = place;
            _tariff = tariff;
            _isAddTime = isAddTime;

            Title = isAddTime ? "Добавить время" : "Любое время";
            Width = 560;
            Height = 620;
            MinWidth = 500;
            MinHeight = 520;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromRgb(16, 20, 28));
            Content = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = CreateContent()
            };

            _minutesBox.Text = "30";
            UpdateFromMinutes();
        }

        private UIElement CreateContent()
        {
            var root = new StackPanel { Margin = new Thickness(24) };
            root.Children.Add(new TextBlock
            {
                Text = _isAddTime
                    ? $"Добавить время — {_place.Name}"
                    : $"Любое время — {_place.Name}",
                Foreground = Brushes.White,
                FontSize = 26,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 10)
            });
            root.Children.Add(new TextBlock
            {
                Text =
                    "Можно ввести минуты или сумму. Второе поле пересчитается по тарифу автоматически.",
                Foreground = new SolidColorBrush(Color.FromRgb(170, 180, 195)),
                FontSize = 15,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 23,
                Margin = new Thickness(0, 0, 0, 18)
            });

            if (_isAddTime)
            {
                root.Children.Add(CreateCard(
                    "Сейчас",
                    $"Осталось: {TariffService.FormatTime(_place.RemainingSeconds)}\n" +
                    $"Оплачено: {_place.PaidAmount} сом"));
            }

            ConfigureInput(_minutesBox);
            ConfigureInput(_amountBox);
            _minutesBox.TextChanged += (_, _) => UpdateFromMinutes();
            _amountBox.TextChanged += (_, _) => UpdateFromAmount();
            _minutesBox.PreviewMouseWheel += (_, e) =>
            {
                double current = TryReadMinutes(_minutesBox.Text, out double parsed)
                    ? parsed
                    : 0;
                current = Math.Max(1, current + (e.Delta > 0 ? 1 : -1));
                _minutesBox.Text = current.ToString("0.##", CultureInfo.InvariantCulture);
                _minutesBox.CaretIndex = _minutesBox.Text.Length;
                e.Handled = true;
            };
            _amountBox.PreviewMouseWheel += (_, e) =>
            {
                int current = int.TryParse(_amountBox.Text.Trim(), out int parsed)
                    ? parsed
                    : 0;
                current = Math.Max(1, current + (e.Delta > 0 ? 1 : -1));
                _amountBox.Text = current.ToString(CultureInfo.InvariantCulture);
                _amountBox.CaretIndex = _amountBox.Text.Length;
                e.Handled = true;
            };

            root.Children.Add(CreateInputRow("Минуты", _minutesBox));
            root.Children.Add(CreateInputRow("Сумма", _amountBox, "сом"));

            _resultText.Foreground = new SolidColorBrush(Color.FromRgb(74, 222, 128));
            _resultText.FontSize = 21;
            _resultText.FontWeight = FontWeights.Bold;
            _resultText.TextWrapping = TextWrapping.Wrap;
            root.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(24, 32, 43)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16),
                Margin = new Thickness(0, 4, 0, 18),
                Child = _resultText
            });

            var buttons = new Grid();
            buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var cancel = new Button { Content = "Отмена", Height = 46, FontSize = 16 };
            cancel.Click += (_, _) => { DialogResult = false; Close(); };
            var confirm = new Button
            {
                Content = _isAddTime ? "Добавить" : "Открыть",
                Height = 46,
                FontSize = 16,
                FontWeight = FontWeights.SemiBold
            };
            confirm.Click += (_, _) => Confirm();
            Grid.SetColumn(cancel, 0);
            Grid.SetColumn(confirm, 2);
            buttons.Children.Add(cancel);
            buttons.Children.Add(confirm);
            root.Children.Add(buttons);
            return root;
        }

        private static void ConfigureInput(TextBox input)
        {
            input.Height = 44;
            input.FontSize = 19;
            input.Padding = new Thickness(10, 5, 10, 5);
            input.GotKeyboardFocus += (_, _) => input.SelectAll();
        }

        private static Border CreateCard(string title, string text)
        {
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock
            {
                Text = title,
                Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                FontSize = 14
            });
            panel.Children.Add(new TextBlock
            {
                Text = text,
                Foreground = Brushes.White,
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 5, 0, 0)
            });
            return new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(24, 32, 43)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16),
                Margin = new Thickness(0, 0, 0, 14),
                Child = panel
            };
        }

        private static UIElement CreateInputRow(string title, TextBox input, string suffix = "")
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 14) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var label = new TextBlock
            {
                Text = title,
                Foreground = Brushes.White,
                FontSize = 18,
                VerticalAlignment = VerticalAlignment.Center
            };
            var suffixText = new TextBlock
            {
                Text = suffix,
                Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                FontSize = 17,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            };
            Grid.SetColumn(label, 0);
            Grid.SetColumn(input, 1);
            Grid.SetColumn(suffixText, 2);
            grid.Children.Add(label);
            grid.Children.Add(input);
            grid.Children.Add(suffixText);
            return grid;
        }

        private void UpdateFromMinutes()
        {
            if (_isUpdating)
                return;

            if (!TryReadMinutes(_minutesBox.Text, out double minutes) || minutes <= 0)
            {
                SetInvalidResult();
                return;
            }

            _calculatedSeconds = Math.Max(1, (int)Math.Floor(minutes * 60));
            _calculatedAmount = TariffService.CalculatePriceBySeconds(_tariff, _calculatedSeconds);
            _isUpdating = true;
            _amountBox.Text = _calculatedAmount.ToString(CultureInfo.InvariantCulture);
            _isUpdating = false;
            UpdateResult();
        }

        private void UpdateFromAmount()
        {
            if (_isUpdating)
                return;

            if (!int.TryParse(_amountBox.Text.Trim(), out int amount) || amount <= 0)
            {
                SetInvalidResult();
                return;
            }

            _calculatedAmount = amount;
            _calculatedSeconds = TariffService.CalculateSecondsByAmount(_tariff, amount);
            _isUpdating = true;
            _minutesBox.Text = (_calculatedSeconds / 60.0)
                .ToString("0.##", CultureInfo.InvariantCulture);
            _isUpdating = false;
            UpdateResult();
        }

        private void UpdateResult()
        {
            if (_calculatedSeconds <= 0 || _calculatedAmount <= 0)
            {
                SetInvalidResult();
                return;
            }

            string result =
                $"{TariffService.FormatMenuTime(_calculatedSeconds)} = {_calculatedAmount} сом";

            if (_isAddTime)
            {
                result +=
                    $"\nПосле добавления: {TariffService.FormatTime(_place.RemainingSeconds + _calculatedSeconds)}" +
                    $"\nВсего оплачено: {_place.PaidAmount + _calculatedAmount} сом";
            }

            _resultText.Text = result;
        }

        private void SetInvalidResult()
        {
            _calculatedSeconds = 0;
            _calculatedAmount = 0;
            _resultText.Text = "Введите положительные минуты или сумму.";
        }

        private void Confirm()
        {
            if (_calculatedSeconds <= 0 || _calculatedAmount <= 0)
            {
                MessageBox.Show("Проверьте минуты и сумму.", Title);
                return;
            }

            DialogResult = true;
            Close();
        }

        private static bool TryReadMinutes(string text, out double value)
        {
            text = text.Trim().Replace(',', '.');
            return double.TryParse(
                text,
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out value);
        }
    }
}
