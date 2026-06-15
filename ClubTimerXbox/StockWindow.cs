using System.Linq;
using System;
using System.Collections.Generic;
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
        private readonly ComboBox _newProductPaymentMethodBox = new ComboBox();

        public StockWindow()
        {
            Title = "Склад / закупы";
            Width = 930;
            Height = 790;
            MinWidth = 820;
            MinHeight = 680;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromRgb(16, 20, 28));

            Content = CreateContent();

            LoadStockItems();
            LoadPurchaseHistory();
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
                Text = "Склад / закупы",
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
                    "Здесь админ принимает закупы. История уйдёт в телефон с именем того, кто принял товар.",
                Foreground = new SolidColorBrush(Color.FromRgb(170, 180, 195)),
                FontSize = 15,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 23,
                Margin = new Thickness(0, 0, 0, 14)
            };

            DockPanel.SetDock(subtitleText, Dock.Top);
            root.Children.Add(subtitleText);

            var mainPanel = new StackPanel();

            mainPanel.Children.Add(new TextBlock
            {
                Text = "Товары",
                Foreground = Brushes.White,
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 8, 0, 12)
            });

            mainPanel.Children.Add(_itemsPanel);

            mainPanel.Children.Add(CreateAddProductCard());

            mainPanel.Children.Add(new TextBlock
            {
                Text = "История закупов",
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

            var col1 = new StackPanel
            {
                Margin = new Thickness(0, 0, 10, 0)
            };

            var col2 = new StackPanel
            {
                Margin = new Thickness(5, 0, 5, 0)
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

            ConfigurePaymentMethodBox(_newProductPaymentMethodBox);

            col1.Children.Add(CreateFieldLabel("Название товара"));
            col1.Children.Add(_newProductNameBox);

            col1.Children.Add(CreateFieldLabel("Начальный остаток, шт"));
            col1.Children.Add(_newProductQuantityBox);

            col2.Children.Add(CreateFieldLabel("Цена прихода / закупки"));
            col2.Children.Add(_newProductPurchasePriceBox);

            col2.Children.Add(CreateFieldLabel("Оплата закупа"));
            col2.Children.Add(_newProductPaymentMethodBox);

            var createButton = new Button
            {
                Content = "Создать товар / принять закуп",
                Height = 42,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 8, 0, 0)
            };

            createButton.Click += (_, _) => AddNewProduct();

            col2.Children.Add(createButton);

            Grid.SetColumn(col1, 0);
            Grid.SetColumn(col2, 1);

            grid.Children.Add(col1);
            grid.Children.Add(col2);

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

            int salePrice = purchasePrice;
            int minimum = 0;

            StockPurchase? purchase = null;

            if (initialQuantity > 0)
            {
                purchase = StockPurchaseService.AddPurchase(
                    items: new List<StockPurchaseItem>
                    {
                        new StockPurchaseItem
                        {
                            ProductName = productName,
                            Quantity = initialQuantity,
                            PurchasePrice = purchasePrice,
                            SalePrice = salePrice,
                            MinimumQuantity = minimum
                        }
                    },
                    addedBy: GetCurrentActorName(),
                    note: "Первый закуп нового товара"
                );

                AddPurchaseExpense(purchase, GetSelectedPaymentMethod(_newProductPaymentMethodBox));
            }
            else
            {
                ProductStockService.AddNewProduct(
                    productName: productName,
                    initialQuantity: 0,
                    purchasePrice: purchasePrice,
                    salePrice: salePrice,
                    minimumQuantity: minimum
                );
            }

            MessageBox.Show(
                $"{productName}\n\n" +
                $"Товар создан.\n" +
                $"Начальный остаток: {initialQuantity} шт\n" +
                $"Цена прихода: {purchasePrice} сом" +
                (purchase == null ? "" : $"\nЗакуп записан: {purchase.TotalAmount} сом"),
                "Новый товар"
            );

            _newProductNameBox.Text = "";
            _newProductQuantityBox.Text = "0";
            _newProductPurchasePriceBox.Text = "0";
            _newProductPaymentMethodBox.SelectedIndex = 0;

            LoadStockItems();
            LoadPurchaseHistory();
        }

        private void LoadStockItems()
        {
            _itemsPanel.Children.Clear();

            foreach (var item in ProductStockService.StockItems)
            {
                _itemsPanel.Children.Add(CreateStockCard(item));
            }
        }

        private void LoadPurchaseHistory()
        {
            _historyPanel.Children.Clear();

            var purchases = StockPurchaseService.Purchases
                .OrderByDescending(purchase => purchase.CreatedAt)
                .Take(15)
                .ToList();

            if (purchases.Count == 0)
            {
                _historyPanel.Children.Add(new TextBlock
                {
                    Text = "История закупов пока пустая.",
                    Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                    FontSize = 16,
                    Margin = new Thickness(0, 0, 0, 12)
                });

                return;
            }

            foreach (var purchase in purchases)
            {
                _historyPanel.Children.Add(CreatePurchaseHistoryCard(purchase));
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

            mainPanel.Children.Add(CreateIncomingPanel(item));

            return new Border
            {
                Background = GetCardBackground(item),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(16),
                Margin = new Thickness(0, 0, 0, 14),
                Child = mainPanel
            };
        }

        private UIElement CreateIncomingPanel(ProductStockItem item)
        {
            var panel = new StackPanel
            {
                Margin = new Thickness(10, 0, 0, 0)
            };

            panel.Children.Add(new TextBlock
            {
                Text = "Принять закуп",
                Foreground = Brushes.White,
                FontSize = 17,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 10)
            });

            var addQuantityBox = CreateNumberBox("0");
            var purchasePriceBox = CreateNumberBox(item.PurchasePrice.ToString());
            var paymentMethodBox = CreatePaymentMethodBox();

            panel.Children.Add(CreateFieldLabel("Добавить товар, шт"));
            panel.Children.Add(addQuantityBox);

            panel.Children.Add(CreateFieldLabel("Цена прихода этого товара"));
            panel.Children.Add(purchasePriceBox);

            panel.Children.Add(CreateFieldLabel("Оплата закупа"));
            panel.Children.Add(paymentMethodBox);

            var addButton = new Button
            {
                Content = "Принять закуп",
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

                if (quantityToAdd <= 0)
                {
                    MessageBox.Show(
                        "Количество добавления должно быть больше 0.",
                        "Склад / закупы"
                    );

                    return;
                }

                int quantityBefore = ProductStockService.GetQuantity(item.ProductName);

                var purchase = StockPurchaseService.AddPurchase(
                    items: new List<StockPurchaseItem>
                    {
                        new StockPurchaseItem
                        {
                            ProductName = item.ProductName,
                            Quantity = quantityToAdd,
                            PurchasePrice = purchasePrice,
                            SalePrice = item.SalePrice,
                            MinimumQuantity = item.MinimumQuantity
                        }
                    },
                    addedBy: GetCurrentActorName(),
                    note: "Закуп принят на ПК"
                );

                int quantityAfter = ProductStockService.GetQuantity(item.ProductName);

                AddPurchaseExpense(purchase, GetSelectedPaymentMethod(paymentMethodBox));

                MessageBox.Show(
                    $"{item.ProductName}\n\n" +
                    $"Добавлено: {quantityToAdd} шт\n" +
                    $"Было: {quantityBefore} шт\n" +
                    $"Стало: {quantityAfter} шт\n" +
                    $"Цена прихода: {purchasePrice} сом\n" +
                    $"Сумма закупки: {purchase.TotalAmount} сом\n" +
                    $"Принял: {purchase.AddedBy}",
                    "Закуп принят"
                );

                LoadStockItems();
                LoadPurchaseHistory();
            };

            panel.Children.Add(addButton);

            return panel;
        }

        private Border CreatePurchaseHistoryCard(StockPurchase purchase)
        {
            var panel = new StackPanel();

            panel.Children.Add(new TextBlock
            {
                Text = $"{purchase.CreatedAt:dd.MM.yyyy HH:mm} • {purchase.TotalAmount} сом",
                Foreground = Brushes.White,
                FontSize = 18,
                FontWeight = FontWeights.Bold
            });

            panel.Children.Add(CreateSmallLine($"Закуп принял: {purchase.AddedBy}"));

            if (!string.IsNullOrWhiteSpace(purchase.Note))
                panel.Children.Add(CreateSmallLine($"Комментарий: {purchase.Note}"));

            panel.Children.Add(new TextBlock
            {
                Text = BuildPurchaseItemsText(purchase),
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

        private static string BuildPurchaseItemsText(StockPurchase purchase)
        {
            return string.Join(
                "\n",
                purchase.Items.Select(item =>
                    $"{item.ProductName}: {item.Quantity} шт × {item.PurchasePrice} сом = {item.TotalAmount} сом"
                )
            );
        }

        private static TextBlock CreateSmallLine(string text)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = new SolidColorBrush(Color.FromRgb(170, 180, 195)),
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0)
            };
        }

        private ComboBox CreatePaymentMethodBox()
        {
            var comboBox = new ComboBox();
            ConfigurePaymentMethodBox(comboBox);
            return comboBox;
        }

        private static void ConfigurePaymentMethodBox(ComboBox comboBox)
        {
            comboBox.Height = 38;
            comboBox.FontSize = 15;
            comboBox.Margin = new Thickness(0, 0, 0, 8);
            comboBox.Items.Clear();
            comboBox.Items.Add("Наличные");
            comboBox.Items.Add("Безнал");
            comboBox.SelectedIndex = 0;
        }

        private static string GetSelectedPaymentMethod(ComboBox comboBox)
        {
            return comboBox.SelectedItem?.ToString() == "Безнал"
                ? "Безнал"
                : "Наличные";
        }

        private static string GetCurrentActorName()
        {
            return EmployeeService.CurrentEmployee?.Name ?? "Владелец";
        }

        private static void AddPurchaseExpense(StockPurchase purchase, string paymentMethod)
        {
            if (purchase.TotalAmount <= 0)
                return;

            CashService.AddExpense(
                employeeName: purchase.AddedBy,
                title: "Закуп товаров",
                description: BuildPurchaseDescription(purchase),
                amount: purchase.TotalAmount,
                paymentMethod: paymentMethod,
                expenseCategory: "Закупка"
            );
        }

        private static string BuildPurchaseDescription(StockPurchase purchase)
        {
            var lines = new List<string>
            {
                $"Закуп принял: {purchase.AddedBy}"
            };

            if (!string.IsNullOrWhiteSpace(purchase.Note))
                lines.Add(purchase.Note);

            lines.AddRange(purchase.Items.Select(item =>
                $"{item.ProductName}: {item.Quantity} шт × {item.PurchasePrice} сом = {item.TotalAmount} сом"));

            return string.Join("\n", lines);
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
                    "Склад / закупы"
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
