using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ClubTimerXbox.Models;
using ClubTimerXbox.Services;

namespace ClubTimerXbox
{
    public class CashWindow : Window
    {
        private readonly StackPanel _itemsPanel = new StackPanel();

        private DateTime _periodFrom = DateTime.Today;
        private DateTime _periodTo = DateTime.Today.AddDays(1);
        private string _periodTitle = "Сегодня";

        public CashWindow()
        {
            Title = "Касса";
            Width = 920;
            Height = 760;
            MinWidth = 820;
            MinHeight = 640;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromRgb(16, 20, 28));

            Content = CreateContent();

            SetTodayPeriod();
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
                Text = "Касса",
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

            var periodPanel = CreatePeriodPanel();
            DockPanel.SetDock(periodPanel, Dock.Top);
            root.Children.Add(periodPanel);

            var scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = _itemsPanel
            };

            root.Children.Add(scrollViewer);

            return root;
        }

        private UIElement CreatePeriodPanel()
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 16)
            };

            panel.Children.Add(CreatePeriodButton("Сегодня", SetTodayPeriod));
            panel.Children.Add(CreatePeriodButton("Месяц", SetMonthPeriod));
            panel.Children.Add(CreatePeriodButton("Год", SetYearPeriod));

            return panel;
        }

        private Button CreatePeriodButton(string text, Action action)
        {
            var button = new Button
            {
                Content = text,
                Width = 110,
                Height = 38,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 8, 0)
            };

            button.Click += (_, _) => action();

            return button;
        }

        private void SetTodayPeriod()
        {
            _periodFrom = DateTime.Today;
            _periodTo = DateTime.Today.AddDays(1);
            _periodTitle = "Сегодня";

            LoadCash();
        }

        private void SetMonthPeriod()
        {
            _periodFrom = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            _periodTo = _periodFrom.AddMonths(1);
            _periodTitle = $"Месяц: {_periodFrom:MM.yyyy}";

            LoadCash();
        }

        private void SetYearPeriod()
        {
            _periodFrom = new DateTime(DateTime.Today.Year, 1, 1);
            _periodTo = _periodFrom.AddYears(1);
            _periodTitle = $"Год: {_periodFrom:yyyy}";

            LoadCash();
        }

        private void LoadCash()
        {
            _itemsPanel.Children.Clear();

            int games = CashService.GetTotalByPeriodAndCategory(_periodFrom, _periodTo, "Игры");
            int products = CashService.GetTotalByPeriodAndCategory(_periodFrom, _periodTo, "Товары и услуги");
            int corrections = CashService.GetTotalByPeriodAndCategory(_periodFrom, _periodTo, "Коррекция");

            int income = CashService.GetCashIncomeTotalByPeriod(_periodFrom, _periodTo);

            int shortages = CashService.GetShortageTotalByPeriod(_periodFrom, _periodTo);
            int expenses = CashService.GetClubExpenseTotalByPeriod(_periodFrom, _periodTo);

            int purchases = StockPurchaseService.GetTotalByPeriod(_periodFrom, _periodTo);

            int cashless = CashlessService.GetAmountByPeriod(_periodFrom, _periodTo);
            int expectedCash = income - cashless;

            if (expectedCash < 0)
                expectedCash = 0;

            int netAfterExpenses = income - expenses - purchases;

            _itemsPanel.Children.Add(CreateSummaryCard(
                games,
                products,
                corrections,
                income,
                shortages,
                expenses,
                purchases,
                cashless,
                expectedCash,
                netAfterExpenses
            ));

            _itemsPanel.Children.Add(CreateExpenseInputCard());

            AddRecordsSection("Игры", "Игры");
            AddRecordsSection("Товары и услуги", "Товары и услуги");
            AddExpenseSection();
            AddProductIncomingSection();
            AddRecordsSection("Недостачи", "Недостачи");
            AddRecordsSection("Коррекции", "Коррекция");
        }

        private Border CreateSummaryCard(
            int games,
            int products,
            int corrections,
            int income,
            int shortages,
            int expenses,
            int purchases,
            int cashless,
            int expectedCash,
            int netAfterExpenses)
        {
            var panel = new StackPanel();

            panel.Children.Add(new TextBlock
            {
                Text = _periodTitle,
                Foreground = Brushes.White,
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 12)
            });

            panel.Children.Add(CreateSummaryLine("Игры", games));
            panel.Children.Add(CreateSummaryLine("Товары и услуги", products));
            panel.Children.Add(CreateSummaryLine("Коррекции", corrections));

            panel.Children.Add(CreateDivider());

            panel.Children.Add(new TextBlock
            {
                Text = $"Общая касса: {income} сом",
                Foreground = new SolidColorBrush(Color.FromRgb(74, 222, 128)),
                FontSize = 26,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 10)
            });

            panel.Children.Add(new TextBlock
            {
                Text =
                    $"Безнал: {cashless} сом\n" +
                    $"Ожидаемая наличка: {expectedCash} сом",
                Foreground = new SolidColorBrush(Color.FromRgb(251, 191, 36)),
                FontSize = 17,
                FontWeight = FontWeights.SemiBold,
                LineHeight = 25,
                Margin = new Thickness(0, 0, 0, 10)
            });

            panel.Children.Add(new TextBlock
            {
                Text =
                    "Эти строки нужны для будущей приёмки наличных: новый админ введёт фактическую наличку, " +
                    "а система сравнит её с ожидаемой.",
                Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 21,
                Margin = new Thickness(0, 0, 0, 10)
            });

            panel.Children.Add(CreateDivider());

            panel.Children.Add(CreateSummaryLine("Расходы", expenses));
            panel.Children.Add(CreateSummaryLine("Закупка товара / приход", purchases));
            panel.Children.Add(CreateSummaryLine("Недостачи", shortages));

            panel.Children.Add(CreateDivider());

            panel.Children.Add(new TextBlock
            {
                Text = $"Остаток после расходов и закупок: {netAfterExpenses} сом",
                Foreground = netAfterExpenses >= 0
                    ? new SolidColorBrush(Color.FromRgb(74, 222, 128))
                    : new SolidColorBrush(Color.FromRgb(248, 113, 113)),
                FontSize = 21,
                FontWeight = FontWeights.Bold
            });

            return CreateCard(panel, Color.FromRgb(24, 32, 43));
        }

        private Border CreateExpenseInputCard()
        {
            var titleBox = new TextBox();
            var amountBox = new TextBox();
            var descriptionBox = new TextBox();

            var panel = new StackPanel();

            panel.Children.Add(new TextBlock
            {
                Text = "Добавить расход",
                Foreground = Brushes.White,
                FontSize = 22,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 12)
            });

            panel.Children.Add(CreateFieldLabel("Название расхода"));
            SetupTextBox(titleBox, "Например: интернет, ремонт, аренда");
            panel.Children.Add(titleBox);

            panel.Children.Add(CreateFieldLabel("Сумма"));
            SetupTextBox(amountBox, "0");
            panel.Children.Add(amountBox);

            panel.Children.Add(CreateFieldLabel("Комментарий"));
            SetupTextBox(descriptionBox, "");
            descriptionBox.Height = 70;
            descriptionBox.TextWrapping = TextWrapping.Wrap;
            descriptionBox.AcceptsReturn = true;
            panel.Children.Add(descriptionBox);

            var addButton = new Button
            {
                Content = "Добавить расход",
                Height = 42,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 8, 0, 0)
            };

            addButton.Click += (_, _) =>
            {
                AddExpense(
                    titleBox.Text,
                    amountBox.Text,
                    descriptionBox.Text
                );
            };

            panel.Children.Add(addButton);

            return CreateCard(panel, Color.FromRgb(24, 32, 43));
        }

        private void AddExpense(string titleText, string amountText, string descriptionText)
        {
            string title = titleText.Trim();

            if (string.IsNullOrWhiteSpace(title))
            {
                MessageBox.Show("Введите название расхода.", "Расход");
                return;
            }

            if (!int.TryParse(amountText.Trim(), out int amount))
            {
                MessageBox.Show("Сумма расхода должна быть числом.", "Расход");
                return;
            }

            if (amount <= 0)
            {
                MessageBox.Show("Сумма расхода должна быть больше 0.", "Расход");
                return;
            }

            string employeeName = EmployeeService.CurrentEmployee?.Name ?? "Неизвестно";

            CashService.AddExpense(
                employeeName: employeeName,
                title: title,
                description: descriptionText.Trim(),
                amount: amount
            );

            MessageBox.Show(
                $"{title}\n\n" +
                $"Расход: {amount} сом\n\n" +
                "Расход добавлен в кассу.",
                "Расход"
            );

            LoadCash();
        }

        private void AddRecordsSection(string title, string category)
        {
            AddSectionTitle(title);

            var records = CashService.GetRecordsByPeriodAndCategory(_periodFrom, _periodTo, category);

            if (records.Count == 0)
            {
                _itemsPanel.Children.Add(CreateEmptyText($"В разделе “{title}” пока нет записей."));
                return;
            }

            foreach (var record in records)
            {
                _itemsPanel.Children.Add(CreateRecordCard(record));
            }
        }

        private void AddExpenseSection()
        {
            AddSectionTitle("Расходы");

            var records = CashService
                .GetRecordsByPeriodAndCategory(_periodFrom, _periodTo, "Расходы")
                .Where(record => !record.ExpenseCategory.Equals("Закупка", System.StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (records.Count == 0)
            {
                _itemsPanel.Children.Add(CreateEmptyText("Расходов за выбранный период пока нет."));
                return;
            }

            foreach (var record in records)
            {
                _itemsPanel.Children.Add(CreateRecordCard(record));
            }
        }

        private void AddProductIncomingSection()
        {
            AddSectionTitle("Закупка товара / приход");

            var purchases = StockPurchaseService.GetPurchasesByPeriod(_periodFrom, _periodTo);

            if (purchases.Count == 0)
            {
                _itemsPanel.Children.Add(CreateEmptyText("Закупов за выбранный период пока нет."));
                return;
            }

            foreach (var purchase in purchases)
            {
                _itemsPanel.Children.Add(CreateIncomingCard(purchase));
            }
        }

        private Border CreateRecordCard(CashRecord record)
        {
            var panel = new StackPanel();

            string amountPrefix = record.Category == "Расходы" ? "-" : "+";
            Brush amountBrush = record.Category == "Расходы"
                ? new SolidColorBrush(Color.FromRgb(248, 113, 113))
                : Brushes.White;

            panel.Children.Add(new TextBlock
            {
                Text = GetRecordHeaderText(record),
                Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                FontSize = 13
            });

            panel.Children.Add(new TextBlock
            {
                Text = $"{record.Title} • {amountPrefix}{record.Amount} сом",
                Foreground = amountBrush,
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 4, 0, 4)
            });

            if (!string.IsNullOrWhiteSpace(record.PlaceName))
            {
                panel.Children.Add(CreateSmallLine($"Место: {record.PlaceName}"));
            }

            panel.Children.Add(CreateSmallLine($"Операцию сделал: {record.EmployeeName}"));

            if (!string.IsNullOrWhiteSpace(record.IncomeEmployeeName))
            {
                if (record.Category == "Недостачи")
                    panel.Children.Add(CreateSmallLine($"Ответственный: {record.IncomeEmployeeName}"));
                else
                    panel.Children.Add(CreateSmallLine($"Выручка относится к: {record.IncomeEmployeeName}"));
            }

            if (!string.IsNullOrWhiteSpace(record.PaymentMethod) && record.PaymentMethod != "Не указано")
            {
                panel.Children.Add(CreateSmallLine($"Оплата: {record.PaymentMethod}"));
            }

            if (!string.IsNullOrWhiteSpace(record.Description))
            {
                panel.Children.Add(new TextBlock
                {
                    Text = record.Description,
                    Foreground = new SolidColorBrush(Color.FromRgb(170, 180, 195)),
                    FontSize = 14,
                    TextWrapping = TextWrapping.Wrap,
                    LineHeight = 21,
                    Margin = new Thickness(0, 8, 0, 0)
                });
            }

            return CreateCard(panel, Color.FromRgb(24, 32, 43));
        }

        private static string GetRecordHeaderText(CashRecord record)
        {
            string header = $"{record.CreatedAt:dd.MM.yyyy HH:mm:ss} • {record.Category}";

            if (record.Category == "Расходы" &&
                !string.IsNullOrWhiteSpace(record.ExpenseCategory))
            {
                header += $" • {record.ExpenseCategory}";
            }

            return header;
        }

        private Border CreateIncomingCard(StockPurchase purchase)
        {
            var panel = new StackPanel();

            panel.Children.Add(new TextBlock
            {
                Text = $"{purchase.CreatedAt:dd.MM.yyyy HH:mm:ss} • {purchase.AddedBy}",
                Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                FontSize = 13
            });

            panel.Children.Add(new TextBlock
            {
                Text = $"Закупка товара • -{purchase.TotalAmount} сом",
                Foreground = new SolidColorBrush(Color.FromRgb(248, 113, 113)),
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 4, 0, 4)
            });

            foreach (var item in purchase.Items)
            {
                panel.Children.Add(CreateSmallLine(
                    $"{item.ProductName}: {item.Quantity} шт × {item.PurchasePrice} сом = {item.TotalAmount} сом"
                ));
            }

            if (!string.IsNullOrWhiteSpace(purchase.Note))
                panel.Children.Add(CreateSmallLine($"Комментарий: {purchase.Note}"));

            return CreateCard(panel, Color.FromRgb(24, 32, 43));
        }

        private TextBlock CreateSummaryLine(string title, int amount)
        {
            return new TextBlock
            {
                Text = $"{title}: {amount} сом",
                Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                FontSize = 17,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 6)
            };
        }

        private TextBlock CreateSmallLine(string text)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 21,
                Margin = new Thickness(0, 2, 0, 0)
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

        private void SetupTextBox(TextBox textBox, string defaultText)
        {
            textBox.Text = defaultText;
            textBox.Height = 38;
            textBox.FontSize = 16;
            textBox.Padding = new Thickness(10, 5, 10, 5);
            textBox.Margin = new Thickness(0, 0, 0, 8);
        }

        private Border CreateDivider()
        {
            return new Border
            {
                Height = 1,
                Background = new SolidColorBrush(Color.FromRgb(51, 65, 85)),
                Margin = new Thickness(0, 12, 0, 12)
            };
        }

        private void AddSectionTitle(string title)
        {
            _itemsPanel.Children.Add(new TextBlock
            {
                Text = title,
                Foreground = Brushes.White,
                FontSize = 22,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 18, 0, 12)
            });
        }

        private TextBlock CreateEmptyText(string text)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                FontSize = 16,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12)
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
