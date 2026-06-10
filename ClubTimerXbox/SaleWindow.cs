using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ClubTimerXbox.Models;
using ClubTimerXbox.Services;

namespace ClubTimerXbox
{
    public enum SaleWindowResultType
    {
        None,
        SoldNow,
        AttachToPlace
    }

    public class SaleWindow : Window
    {
        public SaleItem? SelectedSaleItem { get; private set; }
        public int Quantity { get; private set; }
        public int TotalAmount { get; private set; }
        public SaleWindowResultType ResultType { get; private set; } = SaleWindowResultType.None;

        private readonly ComboBox _itemComboBox = new ComboBox();
        private readonly TextBox _quantityTextBox = new TextBox();
        private readonly TextBlock _priceText = new TextBlock();
        private readonly TextBlock _totalText = new TextBlock();
        private readonly TextBlock _typeText = new TextBlock();
        private readonly TextBlock _stockText = new TextBlock();

        private readonly List<SaleItem> _items;

        public SaleWindow()
        {
            _items = SaleItemService.GetActiveItems();

            Title = "Товар / услуга";
            Width = 620;
            Height = 650;
            MinWidth = 560;
            MinHeight = 560;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromRgb(16, 20, 28));

            Content = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = CreateContent()
            };

            LoadItems();
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
                Text = "Товар / услуга",
                Foreground = Brushes.White,
                FontSize = 30,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 8)
            });

            root.Children.Add(new TextBlock
            {
                Text =
                    "Если клиент сразу заплатил — нажмите “Продано сразу”.\n" +
                    "Если клиент играет и оплатит потом вместе с сеансом — нажмите “Оформить на ТВ”.",
                Foreground = new SolidColorBrush(Color.FromRgb(170, 180, 195)),
                FontSize = 15,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 23,
                Margin = new Thickness(0, 0, 0, 22)
            });

            root.Children.Add(CreateLabel("Товар / услуга"));

            _itemComboBox.Height = 42;
            _itemComboBox.FontSize = 17;
            _itemComboBox.Margin = new Thickness(0, 0, 0, 14);
            _itemComboBox.SelectionChanged += (_, _) => UpdateCalculation();

            root.Children.Add(_itemComboBox);

            root.Children.Add(CreateLabel("Количество"));

            _quantityTextBox.Text = "1";
            _quantityTextBox.Height = 42;
            _quantityTextBox.FontSize = 18;
            _quantityTextBox.Padding = new Thickness(10, 5, 10, 5);
            _quantityTextBox.Margin = new Thickness(0, 0, 0, 14);
            _quantityTextBox.TextChanged += (_, _) => UpdateCalculation();

            _quantityTextBox.PreviewMouseWheel += (_, e) =>
            {
                int current = 1;

                if (int.TryParse(_quantityTextBox.Text.Trim(), out int parsed))
                    current = parsed;

                if (e.Delta > 0)
                    current++;
                else
                    current--;

                if (current < 1)
                    current = 1;

                _quantityTextBox.Text = current.ToString();
                _quantityTextBox.CaretIndex = _quantityTextBox.Text.Length;

                e.Handled = true;
            };

            root.Children.Add(_quantityTextBox);

            root.Children.Add(CreateInfoCard("Тип", _typeText));
            root.Children.Add(CreateInfoCard("Остаток", _stockText));
            root.Children.Add(CreateInfoCard("Цена за 1 шт.", _priceText));
            root.Children.Add(CreateInfoCard("Итого", _totalText));

            root.Children.Add(CreateButtonsPanel());

            return root;
        }

        private TextBlock CreateLabel(string text)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = Brushes.White,
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 8)
            };
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
            valueText.FontSize = 22;
            valueText.FontWeight = FontWeights.Bold;
            valueText.Margin = new Thickness(0, 5, 0, 0);
            valueText.TextWrapping = TextWrapping.Wrap;

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
            var root = new StackPanel
            {
                Margin = new Thickness(0, 18, 0, 0)
            };

            var soldNowButton = new Button
            {
                Content = "Продано сразу",
                Height = 46,
                FontSize = 17,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 10)
            };

            soldNowButton.Click += (_, _) =>
            {
                if (!ReadAndSaveSelection())
                    return;

                ResultType = SaleWindowResultType.SoldNow;
                DialogResult = true;
                Close();
            };

            var attachButton = new Button
            {
                Content = "Оформить на ТВ",
                Height = 46,
                FontSize = 17,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 10)
            };

            attachButton.Click += (_, _) =>
            {
                if (!ReadAndSaveSelection())
                    return;

                ResultType = SaleWindowResultType.AttachToPlace;
                DialogResult = true;
                Close();
            };

            var cancelButton = new Button
            {
                Content = "Отмена",
                Height = 42,
                FontSize = 16
            };

            cancelButton.Click += (_, _) =>
            {
                ResultType = SaleWindowResultType.None;
                DialogResult = false;
                Close();
            };

            root.Children.Add(soldNowButton);
            root.Children.Add(attachButton);
            root.Children.Add(cancelButton);

            return root;
        }

        private bool ReadAndSaveSelection()
        {
            if (_itemComboBox.SelectedItem is not SaleItem item)
            {
                MessageBox.Show("Выберите товар или услугу.", "Товар / услуга");
                return false;
            }

            if (!int.TryParse(_quantityTextBox.Text.Trim(), out int quantity) || quantity <= 0)
            {
                MessageBox.Show("Количество должно быть больше 0.", "Товар / услуга");
                return false;
            }

            if (item.Type == SaleItemType.Product)
            {
                int stock = ProductStockService.GetQuantity(item.Name);

                if (stock < quantity)
                {
                    MessageBox.Show(
                        $"{item.Name}\n\n" +
                        $"На складе осталось: {stock} шт\n" +
                        $"Вы выбрали: {quantity} шт\n\n" +
                        "Нельзя продать или оформить больше, чем есть на складе.",
                        "Недостаточно товара"
                    );

                    return false;
                }
            }

            SelectedSaleItem = item;
            Quantity = quantity;
            TotalAmount = item.SalePrice * quantity;

            return true;
        }

        private void LoadItems()
        {
            _itemComboBox.Items.Clear();

            foreach (var item in _items)
            {
                _itemComboBox.Items.Add(item);
            }

            _itemComboBox.DisplayMemberPath = "Name";

            if (_items.Count > 0)
                _itemComboBox.SelectedIndex = 0;
        }

        private void UpdateCalculation()
        {
            if (_itemComboBox.SelectedItem is not SaleItem item)
            {
                _typeText.Text = "—";
                _stockText.Text = "—";
                _priceText.Text = "—";
                _totalText.Text = "—";
                return;
            }

            int quantity = 1;

            if (int.TryParse(_quantityTextBox.Text.Trim(), out int parsed) && parsed > 0)
                quantity = parsed;

            int total = item.SalePrice * quantity;

            if (item.Type == SaleItemType.Product)
            {
                int stock = ProductStockService.GetQuantity(item.Name);

                _typeText.Text = "Товар";
                _stockText.Text = $"Осталось: {stock} шт";
            }
            else
            {
                _typeText.Text = "Услуга";
                _stockText.Text = "Склад не нужен";
            }

            _priceText.Text = $"{item.SalePrice} сом";
            _totalText.Text = $"{total} сом";
        }
    }
}