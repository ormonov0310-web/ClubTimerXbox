using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ClubTimerXbox.Models;
using ClubTimerXbox.Services;

namespace ClubTimerXbox
{
    public class StockWindow : Window
    {
        private readonly StackPanel _itemsPanel = new StackPanel();
        private readonly StackPanel _historyPanel = new StackPanel();

        private readonly TextBox _newProductNameBox = new TextBox();
        private readonly TextBox _newProductQuantityBox = new TextBox();
        private readonly TextBox _newProductPurchasePriceBox = new TextBox();
        private readonly TextBox _newProductSalePriceBox = new TextBox();
        private readonly TextBox _newProductMinimumBox = new TextBox();

        public StockWindow()
        {
            Title = "Склад / товары";
            Width = 930;
            Height = 790;
            MinWidth = 820;
            MinHeight = 680;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromRgb(16, 20, 28));

            Content = CreateContent();

            LoadStockItems();
            LoadIncomingHistory();
        }

        private UIElement CreateContent()
        {
            var root = new DockPanel
            {
                Margin = new Thickness(20)
            };

            var topPanel = new DockPanel
            {
                Margin = new Thickness(0, 0, 0, 16)
            };

            var titleText = new TextBlock
            {
                Text = "Склад / товары",
                Foreground = Brushes.White,
                FontSize = 30,
                FontWeight = FontWeights.Bold
            };

            DockPanel.SetDock(titleText, Dock.Left);
            topPanel.Children.Add(titleText);

            var closeButton = new Button
            {
                Content = "Закрыть",
                Width = 120,
                Height = 42,
                FontSize = 16
            };

            closeButton.Click += (_, _) => Close();

            DockPanel.SetDock(closeButton, Dock.Right);
            topPanel.Children.Add(closeButton);

            DockPanel.SetDock(topPanel, Dock.Top);
            root.Children.Add(topPanel);

            var subtitleText = new TextBlock
            {
                Text =
                    "Это окно для владельца. Здесь можно создавать новые товары, добавлять приход товара, менять цену прихода, цену продажи и минимальный остаток.",
                Foreground = new SolidColorBrush(Color.FromRgb(170, 180, 195)),
                FontSize = 15,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 23,
                Margin = new Thickness(0, 0, 0, 14)
            };

            DockPanel.SetDock(subtitleText, Dock.Top);
            root.Children.Add(subtitleText);

            var mainPanel = new StackPanel();

            mainPanel.Children.Add(CreateAddProductCard());

            mainPanel.Children.Add(new TextBlock
            {
                Text = "Товары",
                Foreground = Brushes.White,
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 8, 0, 12)
            });

            mainPanel.Children.Add(_itemsPanel);

            mainPanel.Children.Add(new TextBlock
            {
                Text = "История прихода",
                Foreground = Brushes.White,
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 22, 0, 12)
            });

            mainPanel.Children.Add(_historyPanel);

            var scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = mainPanel
            };

            root.Children.Add(scrollViewer);

            return root;
        }

        private Border CreateAddProductCard()
        {
            var root = new StackPanel();

            root.Children.Add(new TextBlock
            {
                Text = "Добавить новый товар",
                Foreground = Brushes.White,
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 8)
            });

            root.Children.Add(new TextBlock
            {
                Text =
                    "Новый товар появится в складе, в “+ Товар / услуга” и в приёмке смены.",
                Foreground = new SolidColorBrush(Color.FromRgb(170, 180, 195)),
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 22,
                Margin = new Thickness(0, 0, 0, 14)
            });

            var grid = new Grid();

            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });

            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });

            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });

            var col1 = new StackPanel
            {
                Margin = new Thickness(0, 0, 10, 0)
            };

            var col2 = new StackPanel
            {
                Margin = new Thickness(5, 0, 5, 0)
            };

            var col3 = new StackPanel
            {
                Margin = new Thickness(10, 0, 0, 0)
            };

            _newProductNameBox.Text = "";
            _newProductNameBox.Height = 38;
            _newProductNameBox.FontSize = 16;
            _newProductNameBox.Padding = new Thickness(10, 5, 10, 5);
            _newProductNameBox.Margin = new Thickness(0, 0, 0, 8);

            _newProductQuantityBox.Text = "0";
            _newProductQuantityBox.Height = 38;
            _newProductQuantityBox.FontSize = 16;
            _newProductQuantityBox.Padding = new Thickness(10, 5, 10, 5);
            _newProductQuantityBox.Margin = new Thickness(0, 0, 0, 8);

            _newProductPurchasePriceBox.Text = "0";
            _newProductPurchasePriceBox.Height = 38;
            _newProductPurchasePriceBox.FontSize = 16;
            _newProductPurchasePriceBox.Padding = new Thickness(10, 5, 10, 5);
            _newProductPurchasePriceBox.Margin = new Thickness(0, 0, 0, 8);

            _newProductSalePriceBox.Text = "0";
            _newProductSalePriceBox.Height = 38;
            _newProductSalePriceBox.FontSize = 16;
            _newProductSalePriceBox.Padding = new Thickness(10, 5, 10, 5);
            _newProductSalePriceBox.Margin = new Thickness(0, 0, 0, 8);

            _newProductMinimumBox.Text = "0";
            _newProductMinimumBox.Height = 38;
            _newProductMinimumBox.FontSize = 16;
            _newProductMinimumBox.Padding = new Thickness(10, 5, 10, 5);
            _newProductMinimumBox.Margin = new Thickness(0, 0, 0, 8);

            col1.Children.Add(CreateFieldLabel("Название товара"));
            col1.Children.Add(_newProductNameBox);

            col1.Children.Add(CreateFieldLabel("Начальный остаток, шт"));
            col1.Children.Add(_newProductQuantityBox);

            col2.Children.Add(CreateFieldLabel("Цена прихода / закупки"));
            col2.Children.Add(_newProductPurchasePriceBox);

            col2.Children.Add(CreateFieldLabel("Цена продажи"));
            col2.Children.Add(_newProductSalePriceBox);

            col3.Children.Add(CreateFieldLabel("Минимальный остаток"));
            col3.Children.Add(_newProductMinimumBox);

            var createButton = new Button
            {
                Content = "Создать товар",
                Height = 42,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 24, 0, 0)
            };

            createButton.Click += (_, _) => AddNewProduct();

            col3.Children.Add(createButton);

            Grid.SetColumn(col1, 0);
            Grid.SetColumn(col2, 1);
            Grid.SetColumn(col3, 2);

            grid.Children.Add(col1);
            grid.Children.Add(col2);
            grid.Children.Add(col3);

            root.Children.Add(grid);

            return new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(24, 32, 43)),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(16),
                Margin = new Thickness(0, 0, 0, 18),
                Child = root
            };
        }

        private void AddNewProduct()
        {
            string productName = _newProductNameBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(productName))
            {
                MessageBox.Show(
                    "Введите название товара.",
                    "Новый товар"
                );

                return;
            }

            if (ProductStockService.ExistsByProductName(productName))
            {
                MessageBox.Show(
                    "Такой товар уже есть в складе.",
                    "Новый товар"
                );

                return;
            }

            if (!ReadNumber(_newProductQuantityBox, "Начальный остаток", out int initialQuantity))
                return;

            if (!ReadNumber(_newProductPurchasePriceBox, "Цена прихода", out int purchasePrice))
                return;

            if (!ReadNumber(_newProductSalePriceBox, "Цена продажи", out int salePrice))
                return;

            if (!ReadNumber(_newProductMinimumBox, "Минимальный остаток", out int minimum))
                return;

            ProductStockService.AddNewProduct(
                productName: productName,
                initialQuantity: initialQuantity,
                purchasePrice: purchasePrice,
                salePrice: salePrice,
                minimumQuantity: minimum
            );

            MessageBox.Show(
                $"{productName}\n\n" +
                $"Товар создан.\n" +
                $"Начальный остаток: {initialQuantity} шт\n" +
                $"Цена прихода: {purchasePrice} сом\n" +
                $"Цена продажи: {salePrice} сом",
                "Новый товар"
            );

            _newProductNameBox.Text = "";
            _newProductQuantityBox.Text = "0";
            _newProductPurchasePriceBox.Text = "0";
            _newProductSalePriceBox.Text = "0";
            _newProductMinimumBox.Text = "0";

            LoadStockItems();
            LoadIncomingHistory();
        }

        private void LoadStockItems()
        {
            _itemsPanel.Children.Clear();

            foreach (var item in ProductStockService.StockItems)
            {
                _itemsPanel.Children.Add(CreateStockCard(item));
            }
        }

        private void LoadIncomingHistory()
        {
            _historyPanel.Children.Clear();

            var incomingItems = ProductIncomingService.GetAll()
                .Take(15)
                .ToList();

            if (incomingItems.Count == 0)
            {
                _historyPanel.Children.Add(new TextBlock
                {
                    Text = "История прихода пока пустая.",
                    Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                    FontSize = 16,
                    Margin = new Thickness(0, 0, 0, 12)
                });

                return;
            }

            foreach (var item in incomingItems)
            {
                _historyPanel.Children.Add(CreateIncomingHistoryCard(item));
            }
        }

        private Border CreateStockCard(ProductStockItem item)
        {
            var mainPanel = new StackPanel();

            mainPanel.Children.Add(new TextBlock
            {
                Text = item.ProductName,
                Foreground = Brushes.White,
                FontSize = 22,
                FontWeight = FontWeights.Bold
            });

            mainPanel.Children.Add(new TextBlock
            {
                Text =
                    $"Остаток сейчас: {item.Quantity} шт\n" +
                    $"Цена прихода: {item.PurchasePrice} сом\n" +
                    $"Цена продажи: {item.SalePrice} сом\n" +
                    $"Минимум: {item.MinimumQuantity} шт\n" +
                    $"Обновлено: {item.UpdatedAt:dd.MM.yyyy HH:mm}",
                Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                FontSize = 14,
                LineHeight = 22,
                Margin = new Thickness(0, 8, 0, 14)
            });

            if (item.MinimumQuantity > 0 && item.Quantity <= item.MinimumQuantity)
            {
                mainPanel.Children.Add(new TextBlock
                {
                    Text = "⚠ Товар заканчивается",
                    Foreground = new SolidColorBrush(Color.FromRgb(251, 191, 36)),
                    FontSize = 14,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 0, 0, 12)
                });
            }

            var grid = new Grid();

            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });

            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });

            var settingsPanel = CreateSettingsPanel(item);
            Grid.SetColumn(settingsPanel, 0);
            grid.Children.Add(settingsPanel);

            var incomingPanel = CreateIncomingPanel(item);
            Grid.SetColumn(incomingPanel, 1);
            grid.Children.Add(incomingPanel);

            mainPanel.Children.Add(grid);

            return new Border
            {
                Background = GetCardBackground(item),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(16),
                Margin = new Thickness(0, 0, 0, 14),
                Child = mainPanel
            };
        }

        private UIElement CreateSettingsPanel(ProductStockItem item)
        {
            var panel = new StackPanel
            {
                Margin = new Thickness(0, 0, 10, 0)
            };

            panel.Children.Add(new TextBlock
            {
                Text = "Настройки товара",
                Foreground = Brushes.White,
                FontSize = 17,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 10)
            });

            var purchasePriceBox = CreateNumberBox(item.PurchasePrice.ToString());
            var salePriceBox = CreateNumberBox(item.SalePrice.ToString());
            var minimumBox = CreateNumberBox(item.MinimumQuantity.ToString());

            panel.Children.Add(CreateFieldLabel("Цена прихода / закупки"));
            panel.Children.Add(purchasePriceBox);

            panel.Children.Add(CreateFieldLabel("Цена продажи"));
            panel.Children.Add(salePriceBox);

            panel.Children.Add(CreateFieldLabel("Минимальный остаток"));
            panel.Children.Add(minimumBox);

            var saveButton = new Button
            {
                Content = "Сохранить настройки",
                Height = 42,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 8, 0, 0)
            };

            saveButton.Click += (_, _) =>
            {
                if (!ReadNumber(purchasePriceBox, "Цена прихода", out int purchasePrice))
                    return;

                if (!ReadNumber(salePriceBox, "Цена продажи", out int salePrice))
                    return;

                if (!ReadNumber(minimumBox, "Минимальный остаток", out int minimum))
                    return;

                ProductStockService.UpdateProductSettings(
                    productName: item.ProductName,
                    purchasePrice: purchasePrice,
                    salePrice: salePrice,
                    minimumQuantity: minimum
                );

                MessageBox.Show(
                    $"{item.ProductName}\n\nНастройки сохранены.",
                    "Склад / товары"
                );

                LoadStockItems();
            };

            panel.Children.Add(saveButton);

            return panel;
        }

        private UIElement CreateIncomingPanel(ProductStockItem item)
        {
            var panel = new StackPanel
            {
                Margin = new Thickness(10, 0, 0, 0)
            };

            panel.Children.Add(new TextBlock
            {
                Text = "Приход товара",
                Foreground = Brushes.White,
                FontSize = 17,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 10)
            });

            var addQuantityBox = CreateNumberBox("0");
            var purchasePriceBox = CreateNumberBox(item.PurchasePrice.ToString());
            var salePriceBox = CreateNumberBox(item.SalePrice.ToString());
            var minimumBox = CreateNumberBox(item.MinimumQuantity.ToString());

            panel.Children.Add(CreateFieldLabel("Добавить товар, шт"));
            panel.Children.Add(addQuantityBox);

            panel.Children.Add(CreateFieldLabel("Цена прихода для этого прихода"));
            panel.Children.Add(purchasePriceBox);

            panel.Children.Add(CreateFieldLabel("Цена продажи"));
            panel.Children.Add(salePriceBox);

            panel.Children.Add(CreateFieldLabel("Минимальный остаток"));
            panel.Children.Add(minimumBox);

            var addButton = new Button
            {
                Content = "Добавить товар",
                Height = 42,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 8, 0, 0)
            };

            addButton.Click += (_, _) =>
            {
                if (!ReadNumber(addQuantityBox, "Количество добавления", out int quantityToAdd))
                    return;

                if (!ReadNumber(purchasePriceBox, "Цена прихода", out int purchasePrice))
                    return;

                if (!ReadNumber(salePriceBox, "Цена продажи", out int salePrice))
                    return;

                if (!ReadNumber(minimumBox, "Минимальный остаток", out int minimum))
                    return;

                if (quantityToAdd <= 0)
                {
                    MessageBox.Show(
                        "Количество добавления должно быть больше 0.",
                        "Склад / товары"
                    );

                    return;
                }

                int quantityBefore = ProductStockService.GetQuantity(item.ProductName);

                ProductStockService.AddIncomingProduct(
                    productName: item.ProductName,
                    quantityToAdd: quantityToAdd,
                    purchasePrice: purchasePrice,
                    salePrice: salePrice,
                    minimumQuantity: minimum
                );

                int quantityAfter = ProductStockService.GetQuantity(item.ProductName);

                ProductIncomingService.AddIncoming(
                    productName: item.ProductName,
                    quantityAdded: quantityToAdd,
                    quantityBefore: quantityBefore,
                    quantityAfter: quantityAfter,
                    purchasePrice: purchasePrice,
                    salePrice: salePrice,
                    note: "Приход товара"
                );

                MessageBox.Show(
                    $"{item.ProductName}\n\n" +
                    $"Добавлено: {quantityToAdd} шт\n" +
                    $"Было: {quantityBefore} шт\n" +
                    $"Стало: {quantityAfter} шт\n" +
                    $"Цена прихода: {purchasePrice} сом\n" +
                    $"Сумма закупки: {quantityToAdd * purchasePrice} сом",
                    "Приход товара"
                );

                LoadStockItems();
                LoadIncomingHistory();
            };

            panel.Children.Add(addButton);

            return panel;
        }

        private Border CreateIncomingHistoryCard(ProductIncomingItem item)
        {
            var panel = new StackPanel();

            panel.Children.Add(new TextBlock
            {
                Text = $"{item.CreatedAt:dd.MM.yyyy HH:mm} • {item.ProductName}",
                Foreground = Brushes.White,
                FontSize = 18,
                FontWeight = FontWeights.Bold
            });

            panel.Children.Add(new TextBlock
            {
                Text =
                    $"Добавлено: {item.QuantityAdded} шт\n" +
                    $"Остаток: {item.QuantityBefore} → {item.QuantityAfter} шт\n" +
                    $"Цена прихода: {item.PurchasePrice} сом\n" +
                    $"Цена продажи: {item.SalePrice} сом\n" +
                    $"Сумма закупки: {item.TotalPurchaseAmount} сом",
                Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                FontSize = 14,
                LineHeight = 22,
                Margin = new Thickness(0, 8, 0, 0)
            });

            return new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(24, 32, 43)),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(16),
                Margin = new Thickness(0, 0, 0, 12),
                Child = panel
            };
        }

        private TextBox CreateNumberBox(string text)
        {
            return new TextBox
            {
                Text = text,
                Height = 38,
                FontSize = 16,
                Padding = new Thickness(10, 5, 10, 5),
                Margin = new Thickness(0, 0, 0, 8)
            };
        }

        private TextBlock CreateFieldLabel(string text)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = new SolidColorBrush(Color.FromRgb(170, 180, 195)),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4)
            };
        }

        private bool ReadNumber(TextBox textBox, string fieldName, out int value)
        {
            if (!int.TryParse(textBox.Text.Trim(), out value))
            {
                MessageBox.Show(
                    $"{fieldName} должно быть числом.",
                    "Склад / товары"
                );

                return false;
            }

            if (value < 0)
                value = 0;

            return true;
        }

        private Brush GetCardBackground(ProductStockItem item)
        {
            if (item.MinimumQuantity > 0 && item.Quantity <= item.MinimumQuantity)
                return new SolidColorBrush(Color.FromRgb(70, 45, 30));

            return new SolidColorBrush(Color.FromRgb(24, 32, 43));
        }
    }
}