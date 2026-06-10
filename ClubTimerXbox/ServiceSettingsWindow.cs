using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ClubTimerXbox.Models;
using ClubTimerXbox.Services;

namespace ClubTimerXbox
{
    public class ServiceSettingsWindow : Window
    {
        private readonly StackPanel _itemsPanel = new StackPanel();

        private readonly TextBox _nameBox = new TextBox();
        private readonly TextBox _priceBox = new TextBox();

        public ServiceSettingsWindow()
        {
            Title = "Услуги";
            Width = 720;
            Height = 650;
            MinWidth = 640;
            MinHeight = 540;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromRgb(16, 20, 28));

            Content = CreateContent();

            LoadServices();
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
                Text = "Услуги",
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
                    "Здесь владелец может добавить услуги без склада. " +
                    "Например: Джойстик, VIP джойстик, доп. аккаунт. " +
                    "После добавления услуга появится в списке “+ Товар / услуга”.",
                Foreground = new SolidColorBrush(Color.FromRgb(170, 180, 195)),
                FontSize = 15,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 23,
                Margin = new Thickness(0, 0, 0, 14)
            };

            DockPanel.SetDock(subtitleText, Dock.Top);
            root.Children.Add(subtitleText);

            var mainPanel = new StackPanel();

            mainPanel.Children.Add(CreateAddServiceCard());

            mainPanel.Children.Add(new TextBlock
            {
                Text = "Список услуг",
                Foreground = Brushes.White,
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 22, 0, 12)
            });

            mainPanel.Children.Add(_itemsPanel);

            var scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = mainPanel
            };

            root.Children.Add(scrollViewer);

            return root;
        }

        private Border CreateAddServiceCard()
        {
            var panel = new StackPanel();

            panel.Children.Add(new TextBlock
            {
                Text = "Добавить новую услугу",
                Foreground = Brushes.White,
                FontSize = 22,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 12)
            });

            panel.Children.Add(CreateFieldLabel("Название услуги"));

            _nameBox.Height = 40;
            _nameBox.FontSize = 17;
            _nameBox.Padding = new Thickness(10, 5, 10, 5);
            _nameBox.Margin = new Thickness(0, 0, 0, 10);
            _nameBox.Text = "";

            panel.Children.Add(_nameBox);

            panel.Children.Add(CreateFieldLabel("Цена продажи"));

            _priceBox.Height = 40;
            _priceBox.FontSize = 17;
            _priceBox.Padding = new Thickness(10, 5, 10, 5);
            _priceBox.Margin = new Thickness(0, 0, 0, 12);
            _priceBox.Text = "0";

            panel.Children.Add(_priceBox);

            var addButton = new Button
            {
                Content = "Добавить услугу",
                Height = 42,
                FontSize = 16,
                FontWeight = FontWeights.SemiBold
            };

            addButton.Click += (_, _) => AddService();

            panel.Children.Add(addButton);

            return CreateCard(panel, Color.FromRgb(24, 32, 43));
        }

        private void AddService()
        {
            string name = _nameBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show("Введите название услуги.", "Услуги");
                return;
            }

            if (!int.TryParse(_priceBox.Text.Trim(), out int price))
            {
                MessageBox.Show("Цена должна быть числом.", "Услуги");
                return;
            }

            if (price < 0)
                price = 0;

            if (CustomServiceService.ExistsByName(name))
            {
                MessageBox.Show(
                    "Такая услуга уже есть.",
                    "Услуги"
                );

                return;
            }

            CustomServiceService.AddService(name, price);

            MessageBox.Show(
                $"{name}\n\n" +
                $"Цена: {price} сом\n\n" +
                "Услуга добавлена и появится в “+ Товар / услуга”.",
                "Услуги"
            );

            _nameBox.Text = "";
            _priceBox.Text = "0";

            LoadServices();
        }

        private void LoadServices()
        {
            _itemsPanel.Children.Clear();

            var services = CustomServiceService.GetActiveServices();

            if (services.Count == 0)
            {
                _itemsPanel.Children.Add(new TextBlock
                {
                    Text = "Пока нет активных услуг.",
                    Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                    FontSize = 16,
                    Margin = new Thickness(0, 0, 0, 12)
                });

                return;
            }

            foreach (var service in services)
            {
                _itemsPanel.Children.Add(CreateServiceCard(service));
            }
        }

        private Border CreateServiceCard(SaleItem service)
        {
            var root = new Grid();

            root.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });

            root.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = GridLength.Auto
            });

            var leftPanel = new StackPanel();

            leftPanel.Children.Add(new TextBlock
            {
                Text = service.Name,
                Foreground = Brushes.White,
                FontSize = 20,
                FontWeight = FontWeights.Bold
            });

            leftPanel.Children.Add(new TextBlock
            {
                Text =
                    $"Цена продажи: {service.SalePrice} сом\n" +
                    "Тип: услуга без склада",
                Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                FontSize = 14,
                LineHeight = 21,
                Margin = new Thickness(0, 6, 0, 0)
            });

            Grid.SetColumn(leftPanel, 0);
            root.Children.Add(leftPanel);

            var rightPanel = new StackPanel
            {
                Width = 190
            };

            var priceBox = new TextBox
            {
                Text = service.SalePrice.ToString(),
                Height = 38,
                FontSize = 16,
                Padding = new Thickness(10, 5, 10, 5),
                Margin = new Thickness(0, 0, 0, 8)
            };

            var saveButton = new Button
            {
                Content = "Сохранить цену",
                Height = 38,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 8)
            };

            saveButton.Click += (_, _) =>
            {
                if (!int.TryParse(priceBox.Text.Trim(), out int price))
                {
                    MessageBox.Show("Цена должна быть числом.", "Услуги");
                    return;
                }

                if (price < 0)
                    price = 0;

                CustomServiceService.UpdateService(
                    name: service.Name,
                    salePrice: price,
                    isActive: true
                );

                MessageBox.Show(
                    $"{service.Name}\n\nЦена сохранена.",
                    "Услуги"
                );

                LoadServices();
            };

            var hideButton = new Button
            {
                Content = "Скрыть",
                Height = 38,
                FontSize = 14
            };

            hideButton.Click += (_, _) =>
            {
                var result = MessageBox.Show(
                    $"Скрыть услугу “{service.Name}” из списка продаж?",
                    "Услуги",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question
                );

                if (result != MessageBoxResult.Yes)
                    return;

                CustomServiceService.UpdateService(
                    name: service.Name,
                    salePrice: service.SalePrice,
                    isActive: false
                );

                LoadServices();
            };

            rightPanel.Children.Add(CreateFieldLabel("Цена"));
            rightPanel.Children.Add(priceBox);
            rightPanel.Children.Add(saveButton);
            rightPanel.Children.Add(hideButton);

            Grid.SetColumn(rightPanel, 1);
            root.Children.Add(rightPanel);

            return CreateCard(root, Color.FromRgb(24, 32, 43));
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

        private Border CreateCard(UIElement content, Color backgroundColor)
        {
            return new Border
            {
                Background = new SolidColorBrush(backgroundColor),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(16),
                Margin = new Thickness(0, 0, 0, 12),
                Child = content
            };
        }
    }
}