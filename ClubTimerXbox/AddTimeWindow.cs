using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ClubTimerXbox.Models;
using ClubTimerXbox.Services;

namespace ClubTimerXbox
{
    public class AddTimeWindow : Window
    {
        public int MinutesToAdd { get; private set; }
        public int PriceToAdd { get; private set; }

        private readonly ClubPlace _place;
        private readonly TextBox _minutesTextBox = new TextBox();
        private readonly TextBlock _priceText = new TextBlock();
        private readonly TextBlock _newTimeText = new TextBlock();
        private readonly TextBlock _totalPaidText = new TextBlock();

        public AddTimeWindow(ClubPlace place)
        {
            _place = place;

            Title = "Добавить время";
            Width = 560;
            Height = 560;
            MinWidth = 520;
            MinHeight = 500;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromRgb(16, 20, 28));

            Content = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = CreateContent()
            };

            UpdateCalculation();
        }

        private UIElement CreateContent()
        {
            var root = new StackPanel
            {
                Margin = new Thickness(24)
            };

            root.Children.Add(new TextBlock
            {
                Text = $"Добавить время — {_place.Name}",
                Foreground = Brushes.White,
                FontSize = 26,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 14)
            });

            root.Children.Add(new TextBlock
            {
                Text = "Напиши минуты вручную или наведи мышку на поле минут и крути колёсико. Система сразу посчитает стоимость добавления, новое время и итоговую оплату.",
                Foreground = new SolidColorBrush(Color.FromRgb(170, 180, 195)),
                FontSize = 15,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 23,
                Margin = new Thickness(0, 0, 0, 20)
            });

            root.Children.Add(CreateCurrentInfoCard());
            root.Children.Add(CreateMinutesRow());

            root.Children.Add(CreateInfoCard("Стоимость добавления", _priceText));
            root.Children.Add(CreateInfoCard("Новое оставшееся время", _newTimeText));
            root.Children.Add(CreateInfoCard("Итого оплачено", _totalPaidText));

            root.Children.Add(CreateButtonsPanel());

            return root;
        }

        private Border CreateCurrentInfoCard()
        {
            var panel = new StackPanel();

            panel.Children.Add(new TextBlock
            {
                Text = "Текущие данные",
                Foreground = new SolidColorBrush(Color.FromRgb(170, 180, 195)),
                FontSize = 14
            });

            panel.Children.Add(new TextBlock
            {
                Text =
                    $"Осталось: {TariffService.FormatTime(_place.RemainingSeconds)}\n" +
                    $"Оплачено: {_place.PaidAmount} сом\n" +
                    $"Тариф: {FormatPrice(_place.PricePerMinute)}",
                Foreground = Brushes.White,
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 6, 0, 0),
                LineHeight = 27
            });

            return new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(24, 32, 43)),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(16),
                Margin = new Thickness(0, 0, 0, 14),
                Child = panel
            };
        }

        private UIElement CreateMinutesRow()
        {
            var grid = new Grid
            {
                Margin = new Thickness(0, 0, 0, 16)
            };

            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });

            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(170)
            });

            var label = new TextBlock
            {
                Text = "Минуты добавить",
                Foreground = Brushes.White,
                FontSize = 18,
                VerticalAlignment = VerticalAlignment.Center
            };

            _minutesTextBox.Text = "15";
            _minutesTextBox.FontSize = 20;
            _minutesTextBox.Height = 42;
            _minutesTextBox.Padding = new Thickness(10, 5, 10, 5);

            _minutesTextBox.TextChanged += (_, _) => UpdateCalculation();

            _minutesTextBox.PreviewMouseWheel += (_, e) =>
            {
                int current = 0;

                if (int.TryParse(_minutesTextBox.Text.Trim(), out int parsed))
                    current = parsed;

                if (e.Delta > 0)
                    current++;
                else
                    current--;

                if (current < 1)
                    current = 1;

                _minutesTextBox.Text = current.ToString();
                _minutesTextBox.CaretIndex = _minutesTextBox.Text.Length;

                e.Handled = true;
            };

            Grid.SetColumn(label, 0);
            Grid.SetColumn(_minutesTextBox, 1);

            grid.Children.Add(label);
            grid.Children.Add(_minutesTextBox);

            return grid;
        }

        private Border CreateInfoCard(string title, TextBlock valueText)
        {
            var panel = new StackPanel();

            panel.Children.Add(new TextBlock
            {
                Text = title,
                Foreground = new SolidColorBrush(Color.FromRgb(170, 180, 195)),
                FontSize = 14
            });

            valueText.Foreground = Brushes.White;
            valueText.FontSize = 24;
            valueText.FontWeight = FontWeights.Bold;
            valueText.Margin = new Thickness(0, 5, 0, 0);

            panel.Children.Add(valueText);

            return new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(24, 32, 43)),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(16),
                Margin = new Thickness(0, 0, 0, 12),
                Child = panel
            };
        }

        private UIElement CreateButtonsPanel()
        {
            var buttonsPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 18, 0, 0)
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

            okButton.Click += (_, _) =>
            {
                if (!ReadMinutes(out int minutes))
                {
                    MessageBox.Show("Введите минуты числом.", "Добавить время");
                    return;
                }

                if (minutes <= 0)
                {
                    MessageBox.Show("Минуты должны быть больше 0.", "Добавить время");
                    return;
                }

                MinutesToAdd = minutes;
                PriceToAdd = CalculatePrice(minutes);

                DialogResult = true;
                Close();
            };

            buttonsPanel.Children.Add(cancelButton);
            buttonsPanel.Children.Add(okButton);

            return buttonsPanel;
        }

        private void UpdateCalculation()
        {
            if (!ReadMinutes(out int minutes) || minutes <= 0)
            {
                _priceText.Text = "—";
                _newTimeText.Text = "—";
                _totalPaidText.Text = "—";
                return;
            }

            int price = CalculatePrice(minutes);
            int newRemainingSeconds = _place.RemainingSeconds + minutes * 60;
            int newPaid = _place.PaidAmount + price;

            _priceText.Text = $"{price} сом";
            _newTimeText.Text = TariffService.FormatTime(newRemainingSeconds);
            _totalPaidText.Text = $"{newPaid} сом";
        }

        private int CalculatePrice(int minutes)
        {
            double price = minutes * _place.PricePerMinute;
            return (int)Math.Ceiling(price);
        }

        private bool ReadMinutes(out int minutes)
        {
            return int.TryParse(_minutesTextBox.Text.Trim(), out minutes);
        }

        private string FormatPrice(double pricePerMinute)
        {
            if (pricePerMinute % 1 == 0)
                return $"{pricePerMinute:0} сом/мин";

            return $"{pricePerMinute:0.##} сом/мин";
        }
    }
}