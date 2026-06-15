using System.Collections.Generic;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
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

        private readonly WrapPanel _itemCardsPanel = new WrapPanel();
        private readonly Button _productsTabButton = new Button();
        private readonly Button _servicesTabButton = new Button();
        private readonly TextBox _quantityTextBox = new TextBox();
        private readonly TextBlock _totalText = new TextBlock();
        private readonly TextBlock _stockText = new TextBlock();

        private readonly List<SaleItem> _items;
        private SaleItemType _activeItemType = SaleItemType.Product;
        private SaleItem? _selectedItem;

        public SaleWindow()
        {
            _items = SaleItemService.GetActiveItems();

            Title = "Товар / услуга";
            Width = 760;
            Height = 680;
            MinWidth = 680;
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

            root.Children.Add(CreateItemTabs());

            _itemCardsPanel.Margin = new Thickness(0, 0, 0, 16);

            root.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(13, 18, 26)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(31, 41, 55)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(18),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 16),
                Child = new ScrollViewer
                {
                    Height = 310,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Content = _itemCardsPanel
                }
            });

            root.Children.Add(CreateLabel("Количество"));

            _quantityTextBox.Text = "1";
            _quantityTextBox.Width = 140;
            _quantityTextBox.Height = 42;
            _quantityTextBox.FontSize = 18;
            _quantityTextBox.Padding = new Thickness(10, 5, 10, 5);
            _quantityTextBox.HorizontalAlignment = HorizontalAlignment.Left;
            _quantityTextBox.Margin = new Thickness(0, 0, 0, 14);
            _quantityTextBox.TextChanged += (_, _) => UpdateCalculation();
            _quantityTextBox.PreviewMouseWheel += (_, e) =>
            {
                int current = 1;

                if (int.TryParse(_quantityTextBox.Text.Trim(), out int parsed))
                    current = parsed;

                current += e.Delta > 0 ? 1 : -1;

                if (current < 1)
                    current = 1;

                _quantityTextBox.Text = current.ToString();
                _quantityTextBox.CaretIndex = _quantityTextBox.Text.Length;

                e.Handled = true;
            };

            root.Children.Add(_quantityTextBox);

            var infoGrid = new UniformGrid
            {
                Columns = 2,
                Margin = new Thickness(0, 0, 0, 4)
            };

            infoGrid.Children.Add(CreateInfoCard("Остаток", _stockText));
            infoGrid.Children.Add(CreateInfoCard("Итого", _totalText));

            root.Children.Add(infoGrid);

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

        private UIElement CreateItemTabs()
        {
            var tabs = new UniformGrid
            {
                Columns = 2,
                Margin = new Thickness(0, 0, 0, 12)
            };

            ConfigureTabButton(_productsTabButton, "Товары", SaleItemType.Product);
            ConfigureTabButton(_servicesTabButton, "Услуги", SaleItemType.Service);

            tabs.Children.Add(_productsTabButton);
            tabs.Children.Add(_servicesTabButton);

            return tabs;
        }

        private void ConfigureTabButton(Button button, string text, SaleItemType type)
        {
            button.Content = text;
            button.Height = 42;
            button.FontSize = 16;
            button.FontWeight = FontWeights.SemiBold;
            button.Margin = type == SaleItemType.Product
                ? new Thickness(0, 0, 8, 0)
                : new Thickness(8, 0, 0, 0);
            button.Click += (_, _) =>
            {
                _activeItemType = type;
                LoadItems();
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
                Margin = new Thickness(0, 0, 10, 10),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromRgb(38, 50, 67)),
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

            root.Children.Add(soldNowButton);
            root.Children.Add(attachButton);

            return root;
        }

        private bool ReadAndSaveSelection()
        {
            if (_selectedItem is not SaleItem item)
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
            _itemCardsPanel.Children.Clear();
            UpdateTabStyles();

            var filteredItems = _items
                .FindAll(item => item.Type == _activeItemType);

            foreach (var item in filteredItems)
            {
                _itemCardsPanel.Children.Add(CreateSaleCard(item));
            }

            _selectedItem = filteredItems.Count > 0
                ? filteredItems[0]
                : null;

            RefreshSaleCardSelection();
            UpdateCalculation();
        }

        private void UpdateCalculation()
        {
            if (_selectedItem is not SaleItem item)
            {
                _stockText.Text = "—";
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

                _stockText.Text = $"Осталось: {stock} шт";
            }
            else
            {
                _stockText.Text = "Склад не нужен";
            }

            _totalText.Text = $"{total} сом";
        }

        private Border CreateSaleCard(SaleItem item)
        {
            bool isProduct = item.Type == SaleItemType.Product;
            int stock = isProduct ? ProductStockService.GetQuantity(item.Name) : 0;
            bool lowStock = isProduct && stock <= 3;

            var visual = CreateSaleItemVisual(item, isProduct);

            var title = new TextBlock
            {
                Text = item.Name,
                Foreground = Brushes.White,
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                TextWrapping = TextWrapping.Wrap,
                Height = 40,
                Margin = new Thickness(0, 10, 0, 3)
            };

            var subtitle = new TextBlock
            {
                Text = isProduct
                    ? $"Остаток: {stock} шт"
                    : "Без складского остатка",
                Foreground = new SolidColorBrush(lowStock
                    ? Color.FromRgb(251, 191, 36)
                    : Color.FromRgb(148, 163, 184)),
                FontSize = 13,
                FontWeight = lowStock ? FontWeights.SemiBold : FontWeights.Normal
            };

            var left = new StackPanel();
            left.Children.Add(visual);
            left.Children.Add(title);
            left.Children.Add(subtitle);

            var price = new TextBlock
            {
                Text = $"{item.SalePrice} сом",
                Foreground = new SolidColorBrush(Color.FromRgb(74, 222, 128)),
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 8, 0, 0)
            };
            left.Children.Add(price);

            var card = new Border
            {
                Width = 180,
                MinHeight = 194,
                Background = new SolidColorBrush(Color.FromRgb(24, 32, 43)),
                BorderBrush = new SolidColorBrush(lowStock
                    ? Color.FromRgb(251, 191, 36)
                    : Color.FromRgb(38, 50, 67)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(14),
                Margin = new Thickness(0, 0, 12, 12),
                Cursor = System.Windows.Input.Cursors.Hand,
                Tag = item,
                Effect = new DropShadowEffect
                {
                    BlurRadius = 14,
                    ShadowDepth = 3,
                    Opacity = 0.16,
                    Color = Color.FromRgb(0, 0, 0)
                },
                Child = left
            };

            card.MouseLeftButtonUp += (_, _) =>
            {
                _selectedItem = item;
                RefreshSaleCardSelection();
                UpdateCalculation();
            };

            return card;
        }

        private UIElement CreateSaleItemVisual(SaleItem item, bool isProduct)
        {
            var visual = new Border
            {
                Width = 150,
                Height = 84,
                CornerRadius = new CornerRadius(14),
                Background = new SolidColorBrush(Color.FromRgb(3, 6, 11)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(35, 45, 61)),
                BorderThickness = new Thickness(1),
                ClipToBounds = true
            };

            var logoPath = SaleItemLogoService.GetLogoPath(item);

            if (!string.IsNullOrWhiteSpace(logoPath))
            {
                try
                {
                    visual.Child = new Image
                    {
                        Source = LoadBitmap(logoPath),
                        Stretch = Stretch.UniformToFill
                    };

                    return visual;
                }
                catch
                {
                    // Если файл картинки повреждён, карточка всё равно должна открыться.
                }
            }

            visual.Background = new LinearGradientBrush(
                isProduct
                    ? Color.FromRgb(30, 64, 175)
                    : Color.FromRgb(88, 28, 135),
                Color.FromRgb(15, 23, 42),
                35);

            visual.Child = new TextBlock
            {
                Text = GetInitials(item.Name),
                Foreground = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                FontSize = 28,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            return visual;
        }

        private static BitmapImage LoadBitmap(string path)
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();

            return bitmap;
        }

        private void RefreshSaleCardSelection()
        {
            foreach (var child in _itemCardsPanel.Children)
            {
                if (child is not Border card ||
                    card.Tag is not SaleItem item)
                {
                    continue;
                }

                bool isSelected = ReferenceEquals(item, _selectedItem);
                bool isProduct = item.Type == SaleItemType.Product;
                int stock = isProduct ? ProductStockService.GetQuantity(item.Name) : 0;
                bool lowStock = isProduct && stock <= 3;

                card.Background = new SolidColorBrush(isSelected
                    ? Color.FromRgb(30, 41, 59)
                    : Color.FromRgb(24, 32, 43));
                card.BorderBrush = new SolidColorBrush(isSelected
                    ? Color.FromRgb(96, 165, 250)
                    : lowStock
                        ? Color.FromRgb(251, 191, 36)
                        : Color.FromRgb(38, 50, 67));
            }
        }

        private void UpdateTabStyles()
        {
            StyleTabButton(_productsTabButton, _activeItemType == SaleItemType.Product);
            StyleTabButton(_servicesTabButton, _activeItemType == SaleItemType.Service);
        }

        private void StyleTabButton(Button button, bool active)
        {
            button.Background = new SolidColorBrush(active
                ? Color.FromRgb(37, 99, 235)
                : Color.FromRgb(24, 32, 43));
            button.Foreground = active
                ? Brushes.White
                : new SolidColorBrush(Color.FromRgb(203, 213, 225));
            button.BorderBrush = new SolidColorBrush(active
                ? Color.FromRgb(96, 165, 250)
                : Color.FromRgb(38, 50, 67));
        }

        private string GetInitials(string name)
        {
            name = name.Trim();

            if (string.IsNullOrWhiteSpace(name))
                return "?";

            var parts = name
                .Split(' ', System.StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length == 1)
                return parts[0].Substring(0, System.Math.Min(2, parts[0].Length)).ToUpperInvariant();

            return $"{parts[0][0]}{parts[1][0]}".ToUpperInvariant();
        }
    }
}
