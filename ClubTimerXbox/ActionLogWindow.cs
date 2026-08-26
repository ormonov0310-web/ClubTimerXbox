using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ClubTimerXbox.Models;
using ClubTimerXbox.Services;

namespace ClubTimerXbox
{
    public class ActionLogWindow : Window
    {
        private enum JournalSection
        {
            Shifts,
            ProductsAndServices,
            StockAudits,
            EmployeeLosses
        }

        private enum JournalPeriodMode
        {
            Day,
            Month,
            CustomPeriod
        }

        private enum AuditSubSection
        {
            Products,
            Cash
        }

        private readonly StackPanel _itemsPanel = new StackPanel();
        private readonly TextBlock _emptyText = new TextBlock();
        private readonly TextBlock _countText = new TextBlock();

        private Button _shiftsButton = null!;
        private Button _productsButton = null!;
        private Button _auditsButton = null!;
        private Button _lossesButton = null!;

        private StackPanel _auditSubPanel = null!;
        private Button _auditProductsButton = null!;
        private Button _auditCashButton = null!;

        private Button _dayButton = null!;
        private Button _monthButton = null!;
        private Button _periodButton = null!;

        private JournalSection _section = JournalSection.Shifts;
        private JournalPeriodMode _periodMode = JournalPeriodMode.Day;
        private AuditSubSection _auditSubSection = AuditSubSection.Products;

        private DateTime _selectedDay = BusinessCalendarService.GetBusinessDate(
            ClubClock.Current.LocalNow);
        private int _selectedYear = BusinessCalendarService.GetBusinessDate(
            ClubClock.Current.LocalNow).Year;
        private int _selectedMonth = BusinessCalendarService.GetBusinessDate(
            ClubClock.Current.LocalNow).Month;
        private DateTime _periodStart = BusinessCalendarService.GetBusinessDate(
            ClubClock.Current.LocalNow);
        private DateTime _periodEnd = BusinessCalendarService.GetBusinessDate(
            ClubClock.Current.LocalNow);

        public ActionLogWindow()
        {
            Title = "Журнал";
            Width = 980;
            Height = 720;
            MinWidth = 880;
            MinHeight = 620;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromRgb(16, 20, 28));
            ResizeMode = ResizeMode.CanResize;

            Content = CreateContent();
            Render();
        }

        private UIElement CreateContent()
        {
            var root = new Grid
            {
                Margin = new Thickness(18)
            };

            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var topPanel = new DockPanel
            {
                Margin = new Thickness(0, 0, 0, 14)
            };

            var titleText = new TextBlock
            {
                Text = "Журнал клуба",
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
                Height = 38,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            closeButton.Click += (_, _) => Close();

            DockPanel.SetDock(closeButton, Dock.Right);
            topPanel.Children.Add(closeButton);

            Grid.SetRow(topPanel, 0);
            root.Children.Add(topPanel);

            var infoText = new TextBlock
            {
                Text = "Журнал показывает простые записи: смены сотрудников, действия с товарами/услугами, историю приёмок и потери сотрудников. Денежные итоги смотрим в разделе “Касса” на главном экране.",
                Foreground = new SolidColorBrush(Color.FromRgb(170, 180, 195)),
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 22,
                Margin = new Thickness(0, 0, 0, 14)
            };

            Grid.SetRow(infoText, 1);
            root.Children.Add(infoText);

            var periodPanel = CreatePeriodPanel();
            Grid.SetRow(periodPanel, 2);
            root.Children.Add(periodPanel);

            var sectionPanel = CreateSectionPanel();
            Grid.SetRow(sectionPanel, 3);
            root.Children.Add(sectionPanel);

            _auditSubPanel = CreateAuditSubSectionPanel();
            Grid.SetRow(_auditSubPanel, 4);
            root.Children.Add(_auditSubPanel);

            var listBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(51, 65, 85)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(12)
            };

            var scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = _itemsPanel
            };

            listBorder.Child = scrollViewer;

            Grid.SetRow(listBorder, 5);
            root.Children.Add(listBorder);

            _countText.Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184));
            _countText.FontSize = 13;
            _countText.Margin = new Thickness(0, 10, 0, 0);

            Grid.SetRow(_countText, 6);
            root.Children.Add(_countText);

            return root;
        }

        private UIElement CreatePeriodPanel()
        {
            var root = new StackPanel
            {
                Margin = new Thickness(0, 0, 0, 12)
            };

            root.Children.Add(new TextBlock
            {
                Text = "Период",
                Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 6)
            });

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal
            };

            _dayButton = CreateTopButton("День", 170);
            _dayButton.Click += DayButton_Click;
            buttons.Children.Add(_dayButton);

            _monthButton = CreateTopButton("Месяц", 180);
            _monthButton.Click += MonthButton_Click;
            buttons.Children.Add(_monthButton);

            _periodButton = CreateTopButton("Период", 230);
            _periodButton.Click += PeriodButton_Click;
            buttons.Children.Add(_periodButton);

            root.Children.Add(buttons);

            return root;
        }

        private UIElement CreateSectionPanel()
        {
            var root = new StackPanel
            {
                Margin = new Thickness(0, 0, 0, 14)
            };

            root.Children.Add(new TextBlock
            {
                Text = "Раздел",
                Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 6)
            });

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal
            };

            _shiftsButton = CreateTopButton("Смены сотрудников", 180);
            _shiftsButton.Click += (_, _) =>
            {
                _section = JournalSection.Shifts;
                Render();
            };
            buttons.Children.Add(_shiftsButton);

            _productsButton = CreateTopButton("Товары/услуги", 160);
            _productsButton.Click += (_, _) =>
            {
                _section = JournalSection.ProductsAndServices;
                Render();
            };
            buttons.Children.Add(_productsButton);

            _auditsButton = CreateTopButton("Приёмки", 130);
            _auditsButton.Click += (_, _) =>
            {
                _section = JournalSection.StockAudits;
                Render();
            };
            buttons.Children.Add(_auditsButton);

            _lossesButton = CreateTopButton("Потери", 120);
            _lossesButton.Click += (_, _) =>
            {
                _section = JournalSection.EmployeeLosses;
                Render();
            };
            buttons.Children.Add(_lossesButton);

            root.Children.Add(buttons);

            return root;
        }

        private StackPanel CreateAuditSubSectionPanel()
        {
            var root = new StackPanel
            {
                Margin = new Thickness(0, 0, 0, 14),
                Visibility = Visibility.Collapsed
            };

            root.Children.Add(new TextBlock
            {
                Text = "Вид приёмки",
                Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 6)
            });

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal
            };

            _auditProductsButton = CreateTopButton("Товары", 130);
            _auditProductsButton.Click += (_, _) =>
            {
                _auditSubSection = AuditSubSection.Products;
                Render();
            };
            buttons.Children.Add(_auditProductsButton);

            _auditCashButton = CreateTopButton("Наличка", 130);
            _auditCashButton.Click += (_, _) =>
            {
                _auditSubSection = AuditSubSection.Cash;
                Render();
            };
            buttons.Children.Add(_auditCashButton);

            root.Children.Add(buttons);

            return root;
        }

        private Button CreateTopButton(string text, double width)
        {
            return new Button
            {
                Content = text,
                Width = width,
                Height = 38,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 8, 0)
            };
        }

        private void Render()
        {
            UpdateButtonTexts();
            UpdateButtonStyles();

            _itemsPanel.Children.Clear();

            if (_section == JournalSection.Shifts)
            {
                RenderShiftItems();
                return;
            }

            if (_section == JournalSection.ProductsAndServices)
            {
                RenderProductServiceItems();
                return;
            }

            if (_section == JournalSection.EmployeeLosses)
            {
                RenderEmployeeLossItems();
                return;
            }

            RenderAcceptanceItems();
        }

        private void UpdateButtonTexts()
        {
            _dayButton.Content = $"День: {_selectedDay:dd.MM.yyyy}";
            _monthButton.Content = $"Месяц: {GetMonthTitle(_selectedYear, _selectedMonth)}";
            _periodButton.Content = $"Период: {_periodStart:dd.MM.yyyy}–{_periodEnd:dd.MM.yyyy}";
        }

        private void UpdateButtonStyles()
        {
            SetButtonActive(_dayButton, _periodMode == JournalPeriodMode.Day);
            SetButtonActive(_monthButton, _periodMode == JournalPeriodMode.Month);
            SetButtonActive(_periodButton, _periodMode == JournalPeriodMode.CustomPeriod);

            SetButtonActive(_shiftsButton, _section == JournalSection.Shifts);
            SetButtonActive(_productsButton, _section == JournalSection.ProductsAndServices);
            SetButtonActive(_auditsButton, _section == JournalSection.StockAudits);
            SetButtonActive(_lossesButton, _section == JournalSection.EmployeeLosses);

            if (_auditSubPanel != null)
                _auditSubPanel.Visibility = _section == JournalSection.StockAudits
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            if (_auditProductsButton != null)
                SetButtonActive(_auditProductsButton, _auditSubSection == AuditSubSection.Products);

            if (_auditCashButton != null)
                SetButtonActive(_auditCashButton, _auditSubSection == AuditSubSection.Cash);
        }

        private void SetButtonActive(Button button, bool isActive)
        {
            if (isActive)
            {
                button.Background = new SolidColorBrush(Color.FromRgb(37, 99, 235));
                button.Foreground = Brushes.White;
                button.FontWeight = FontWeights.Bold;
            }
            else
            {
                button.Background = new SolidColorBrush(Color.FromRgb(51, 65, 85));
                button.Foreground = Brushes.White;
                button.FontWeight = FontWeights.SemiBold;
            }
        }

        private (DateTime FromInclusive, DateTime ToExclusive, string Title) GetDateRange()
        {
            if (_periodMode == JournalPeriodMode.Month)
            {
                var month = BusinessCalendarService.GetBusinessMonthByAnchor(
                    new DateTime(_selectedYear, _selectedMonth, 1));
                return (
                    month.StartInclusive,
                    month.EndExclusive,
                    GetMonthTitle(_selectedYear, _selectedMonth));
            }

            if (_periodMode == JournalPeriodMode.CustomPeriod)
            {
                DateTime from = _periodStart.Date;
                DateTime end = _periodEnd.Date;

                if (end < from)
                {
                    DateTime temp = from;
                    from = end;
                    end = temp;
                }

                DateTime rangeStart = from.AddHours(
                    BusinessCalendarService.BusinessDayStartHour);
                DateTime rangeEnd = end.AddDays(1).AddHours(
                    BusinessCalendarService.BusinessDayStartHour);
                return (rangeStart, rangeEnd, $"{from:dd.MM.yyyy}–{end:dd.MM.yyyy}");
            }

            DateTime businessDate = _selectedDay.Date;
            DateTime day = businessDate.AddHours(
                BusinessCalendarService.BusinessDayStartHour);
            return (day, day.AddDays(1), businessDate.ToString("dd.MM.yyyy"));
        }

        private string GetMonthTitle(int year, int month)
        {
            var culture = new CultureInfo("ru-RU");
            string monthName = culture.DateTimeFormat.GetMonthName(month);

            if (string.IsNullOrWhiteSpace(monthName))
                monthName = month.ToString("00");

            monthName = char.ToUpper(monthName[0]) + monthName.Substring(1);
            return $"{monthName} {year}";
        }

        private void RenderShiftItems()
        {
            var range = GetDateRange();

            var shifts = ActionLogService.GetAllShifts()
                .Where(shift => IsShiftInRange(shift, range.FromInclusive, range.ToExclusive))
                .OrderByDescending(shift => shift.StartedAt)
                .ToList();

            if (shifts.Count == 0)
            {
                AddEmptyText("За выбранный период смен сотрудников нет.");
                _countText.Text = "Записей: 0";
                return;
            }

            foreach (var shift in shifts)
            {
                _itemsPanel.Children.Add(CreateShiftCard(shift));
            }

            _countText.Text = $"Записей: {shifts.Count}";
        }

        private bool IsShiftInRange(ShiftLogItem shift, DateTime fromInclusive, DateTime toExclusive)
        {
            if (shift.StartedAt >= fromInclusive && shift.StartedAt < toExclusive)
                return true;

            if (shift.ClosedAt != null &&
                shift.ClosedAt.Value >= fromInclusive &&
                shift.ClosedAt.Value < toExclusive)
            {
                return true;
            }

            return false;
        }

        private Border CreateShiftCard(ShiftLogItem shift)
        {
            var panel = new StackPanel();

            string title = shift.IsClosed
                ? $"Смена закрыта • {shift.EmployeeName}"
                : $"Смена открыта • {shift.EmployeeName}";

            panel.Children.Add(CreateTitleText(title));

            string text = $"Начало: {shift.StartedAt:dd.MM.yyyy HH:mm}";

            if (shift.IsClosed && shift.ClosedAt != null)
            {
                TimeSpan duration = shift.ClosedAt.Value - shift.StartedAt;
                text += $"\nКонец: {shift.ClosedAt.Value:dd.MM.yyyy HH:mm}";
                text += $"\nДлительность: {FormatDuration(duration)}";
            }
            else
            {
                text += "\nСтатус: смена сейчас активна";
            }

            panel.Children.Add(CreateMutedText(text));

            return CreateCard(panel, shift.IsClosed
                ? Color.FromRgb(24, 32, 43)
                : Color.FromRgb(25, 55, 45));
        }

        private void RenderProductServiceItems()
        {
            var range = GetDateRange();
            var rows = new List<JournalRow>();

            AddPaymentProductRows(rows, range.FromInclusive, range.ToExclusive);
            AddAttachedProductRows(rows, range.FromInclusive, range.ToExclusive);

            rows = rows
                .OrderByDescending(row => row.CreatedAt)
                .ToList();

            if (rows.Count == 0)
            {
                AddEmptyText("За выбранный период действий по товарам/услугам нет.");
                _countText.Text = "Записей: 0";
                return;
            }

            foreach (var row in rows)
            {
                _itemsPanel.Children.Add(CreateJournalRowCard(row));
            }

            _countText.Text = $"Записей: {rows.Count}";
        }

        private void AddPaymentProductRows(List<JournalRow> rows, DateTime fromInclusive, DateTime toExclusive)
        {
            var payments = PaymentService.Records
                .Where(record => record.CreatedAt >= fromInclusive && record.CreatedAt < toExclusive)
                .OrderByDescending(record => record.CreatedAt)
                .ToList();

            foreach (var payment in payments)
            {
                foreach (var item in payment.Items)
                {
                    if (!IsProductOrServiceItem(item))
                        continue;

                    string title = string.IsNullOrWhiteSpace(payment.PlaceName)
                        ? "Продано сразу"
                        : $"Оплачено по {payment.PlaceName}";

                    string subtitle =
                        $"{item.Name} × {item.Quantity} = {item.TotalAmount} сом\n" +
                        $"Админ: {payment.EmployeeName}\n" +
                        $"Оплата: {BuildPaymentText(payment)}";

                    if (!string.IsNullOrWhiteSpace(payment.PlaceName))
                        subtitle += $"\nОформлено на {payment.PlaceName}";

                    if (!string.IsNullOrWhiteSpace(payment.Comment))
                        subtitle += $"\nКомментарий: {payment.Comment}";

                    rows.Add(new JournalRow
                    {
                        CreatedAt = payment.CreatedAt,
                        Title = title,
                        Subtitle = subtitle,
                        AccentText = $"{item.TotalAmount} сом"
                    });
                }
            }
        }

        private void AddAttachedProductRows(List<JournalRow> rows, DateTime fromInclusive, DateTime toExclusive)
        {
            var sessions = ActionLogService.GetAllGameSessions();

            foreach (var session in sessions)
            {
                foreach (var line in session.SaleLines)
                {
                    string itemType = line.ItemType == SaleItemType.Product ? "Товар" : "Услуга";

                    if (line.CreatedAt >= fromInclusive && line.CreatedAt < toExclusive)
                    {
                        rows.Add(new JournalRow
                        {
                            CreatedAt = line.CreatedAt,
                            Title = $"Оформлено на {session.PlaceName}",
                            Subtitle =
                                $"{itemType}: {line.ItemName} × {line.Quantity} = {line.TotalAmount} сом\n" +
                                $"Выдал: {SessionSaleSettlementService.GetCreatedByEmployeeName(line)}\n" +
                                (SessionSaleSettlementService.IsFinanciallyPaid(line)
                                    ? $"Оплату принял: {SessionSaleSettlementService.GetFinancialEmployeeName(line)}"
                                    : "Статус: долг клиента, ожидает ККМ"),
                            AccentText = $"{line.TotalAmount} сом"
                        });
                    }

                    if (line.SettlementSchemaVersion >=
                            SessionSaleSettlementService.CurrentSchemaVersion &&
                        SessionSaleSettlementService.IsFinanciallyPaid(line))
                    {
                        DateTime paidAt =
                            SessionSaleSettlementService.GetFinancialOccurredAt(line);
                        if (paidAt >= fromInclusive && paidAt < toExclusive)
                        {
                            rows.Add(new JournalRow
                            {
                                CreatedAt = paidAt,
                                Title = $"Оплачено на {session.PlaceName}",
                                Subtitle =
                                    $"{itemType}: {line.ItemName} × {line.Quantity} = {line.TotalAmount} сом\n" +
                                    $"Выдал: {SessionSaleSettlementService.GetCreatedByEmployeeName(line)}\n" +
                                    $"Оплату принял: {SessionSaleSettlementService.GetFinancialEmployeeName(line)}",
                                AccentText = $"{line.TotalAmount} сом"
                            });
                        }
                    }
                }
            }
        }

        private bool IsProductOrServiceItem(CheckoutItem item)
        {
            return item.Category == "Товар" ||
                   item.Category == "Услуга" ||
                   item.Category == "Товары и услуги";
        }

        private string BuildPaymentText(PaymentRecord payment)
        {
            if (payment.CashAmount > 0 && payment.MBankAmount <= 0)
                return $"Наличные {payment.CashAmount} сом";

            if (payment.MBankAmount > 0 && payment.CashAmount <= 0)
                return $"М Банк {payment.MBankAmount} сом";

            return $"Наличные {payment.CashAmount} сом, М Банк {payment.MBankAmount} сом";
        }

        private void RenderAcceptanceItems()
        {
            if (_auditSubSection == AuditSubSection.Cash)
            {
                RenderCashAcceptanceItems();
                return;
            }

            RenderProductAcceptanceItems();
        }

        private void RenderProductAcceptanceItems()
        {
            var range = GetDateRange();

            var batches = StockAuditService.GetAllBatches()
                .Select(batch => batch.ToList())
                .Where(batch => batch.Count > 0)
                .Where(batch =>
                {
                    DateTime createdAt = batch.Max(item => item.CreatedAt);
                    return createdAt >= range.FromInclusive && createdAt < range.ToExclusive;
                })
                .OrderByDescending(batch => batch.Max(item => item.CreatedAt))
                .ToList();

            if (batches.Count == 0)
            {
                AddEmptyText("За выбранный период приёмок товаров нет.");
                _countText.Text = "Записей: 0";
                return;
            }

            foreach (var batch in batches)
            {
                _itemsPanel.Children.Add(CreateAuditBatchCard(batch));
            }

            _countText.Text = $"Записей: {batches.Count}";
        }

        private void RenderCashAcceptanceItems()
        {
            var range = GetDateRange();

            var items = CashAcceptanceService.GetAll()
                .Where(item => item.CreatedAt >= range.FromInclusive && item.CreatedAt < range.ToExclusive)
                .OrderByDescending(item => item.CreatedAt)
                .ToList();

            if (items.Count == 0)
            {
                AddEmptyText("За выбранный период приёмок налички нет.");
                _countText.Text = "Записей: 0";
                return;
            }

            foreach (var item in items)
            {
                _itemsPanel.Children.Add(CreateCashAcceptanceCard(item));
            }

            _countText.Text = $"Записей: {items.Count}";
        }

        private Border CreateCashAcceptanceCard(CashAcceptanceItem item)
        {
            var panel = new StackPanel();

            panel.Children.Add(CreateTitleText(
                item.IsProvisional
                    ? "Приёмка налички: предварительно"
                    : "Приёмка налички"));

            string text =
                $"Дата: {item.CreatedAt:dd.MM.yyyy HH:mm}\n" +
                $"Передача: {item.ResponsibleEmployeeName} → {item.CheckedByEmployeeName}\n" +
                $"Проверил: {item.CheckedByEmployeeName}\n" +
                $"Ответственный: {item.ResponsibleEmployeeName}";

            panel.Children.Add(CreateMutedText(text));

            if (item.IsProvisional)
            {
                panel.Children.Add(CreateInfoAccentText(
                    "Ожидается завершение срока исправления. В кассовый разбор ещё не проведено."));
            }

            if (item.Difference < 0)
            {
                panel.Children.Add(CreateAccentText(
                    "Есть недостача наличных",
                    true
                ));
            }
            else if (item.Difference > 0)
            {
                panel.Children.Add(CreateInfoAccentText("Есть излишек наличных"));
            }
            else
            {
                panel.Children.Add(CreateGoodAccentText("Расхождений нет"));
            }

            return CreateCard(panel, Color.FromRgb(24, 32, 43));
        }

        private Border CreateAuditBatchCard(List<StockAuditItem> items)
        {
            var first = items.OrderBy(item => item.CreatedAt).First();
            DateTime createdAt = items.Max(item => item.CreatedAt);

            int changedCount = items.Count(item => item.Difference != 0);
            int shortageCount = items.Where(item => item.Difference < 0).Sum(item => Math.Abs(item.Difference));
            int shortageAmount = items.Where(item => item.Difference < 0).Sum(item => item.DifferenceAmount);
            int extraCount = items.Where(item => item.Difference > 0).Sum(item => item.Difference);

            var panel = new StackPanel();

            panel.Children.Add(CreateTitleText("Приёмка товаров"));

            string mainText =
                $"Дата: {createdAt:dd.MM.yyyy HH:mm}\n" +
                $"Передача: {first.ResponsibleEmployeeName} → {first.CheckedByEmployeeName}\n" +
                $"Проверил: {first.CheckedByEmployeeName}\n" +
                $"Ответственный: {first.ResponsibleEmployeeName}\n" +
                $"Изменённых позиций: {changedCount}";

            panel.Children.Add(CreateMutedText(mainText));

            if (shortageCount > 0)
            {
                panel.Children.Add(CreateAccentText(
                    $"Недостача товаров: {shortageCount} шт / {shortageAmount} сом",
                    true
                ));
            }
            else
            {
                panel.Children.Add(CreateGoodAccentText("Недостачи товаров нет"));
            }

            if (extraCount > 0)
            {
                panel.Children.Add(CreateInfoAccentText($"Излишки: {extraCount} шт"));
            }

            var changedLines = items
                .Where(item => item.Difference != 0)
                .OrderBy(item => item.ProductName)
                .ToList();

            if (changedLines.Count > 0)
            {
                panel.Children.Add(CreateMutedText("Позиции:"));

                foreach (var item in changedLines)
                {
                    bool isShortage = item.Difference < 0;
                    string type = isShortage ? "недостача" : "излишек";

                    panel.Children.Add(new TextBlock
                    {
                        Text =
                            $"• {item.ProductName}: {type} {Math.Abs(item.Difference)} шт / {item.DifferenceAmount} сом " +
                            $"(по программе {item.ExpectedQuantity}, фактически {item.ActualQuantity})",
                        Foreground = isShortage
                            ? new SolidColorBrush(Color.FromRgb(248, 113, 113))
                            : new SolidColorBrush(Color.FromRgb(96, 165, 250)),
                        FontSize = 14,
                        TextWrapping = TextWrapping.Wrap,
                        LineHeight = 21,
                        Margin = new Thickness(0, 4, 0, 0)
                    });
                }
            }

            return CreateCard(panel, Color.FromRgb(24, 32, 43));
        }

        private void RenderEmployeeLossItems()
        {
            var range = GetDateRange();

            var items = EmployeeLossService.GetByPeriod(range.FromInclusive, range.ToExclusive)
                .OrderByDescending(item => item.CreatedAt)
                .ToList();

            if (items.Count == 0)
            {
                AddEmptyText("За выбранный период потерь сотрудников нет.");
                _countText.Text = "Записей: 0";
                return;
            }

            _itemsPanel.Children.Add(CreateEmployeeLossSummaryCard(items, range.Title));
            _itemsPanel.Children.Add(CreateEmployeeLossEmployeeSummaryCard(items));

            foreach (var item in items)
            {
                _itemsPanel.Children.Add(CreateEmployeeLossCard(item));
            }

            int total = items.Sum(item => item.Amount);
            int unpaid = items.Where(item => !item.IsPaid).Sum(item => item.Amount);

            _countText.Text = $"Записей: {items.Count} • Всего потерь: {total} сом • Не оплачено: {unpaid} сом";
        }

        private Border CreateEmployeeLossSummaryCard(List<EmployeeLossItem> items, string periodTitle)
        {
            int cashShortage = items
                .Where(item => IsCashLoss(item))
                .Sum(item => item.Amount);

            int productShortage = items
                .Where(item => IsProductLoss(item))
                .Sum(item => item.Amount);

            int otherLosses = items
                .Where(item => !IsCashLoss(item) && !IsProductLoss(item))
                .Sum(item => item.Amount);

            int total = items.Sum(item => item.Amount);
            int unpaid = items.Where(item => !item.IsPaid).Sum(item => item.Amount);
            int paid = items.Where(item => item.IsPaid).Sum(item => item.Amount);

            var panel = new StackPanel();

            panel.Children.Add(new TextBlock
            {
                Text = $"Итог потерь за период: {periodTitle}",
                Foreground = Brushes.White,
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                TextWrapping = TextWrapping.Wrap
            });

            panel.Children.Add(new TextBlock
            {
                Text =
                    $"Недостача налички: {cashShortage} сом\n" +
                    $"Недостача товаров: {productShortage} сом\n" +
                    $"Другие потери: {otherLosses} сом\n" +
                    $"Всего: {total} сом\n" +
                    $"Не оплачено / к удержанию: {unpaid} сом\n" +
                    $"Оплачено: {paid} сом",
                Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                FontSize = 15,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 24,
                Margin = new Thickness(0, 8, 0, 0)
            });

            return CreateCard(panel, Color.FromRgb(30, 41, 59));
        }

        private Border CreateEmployeeLossEmployeeSummaryCard(List<EmployeeLossItem> items)
        {
            var panel = new StackPanel();

            panel.Children.Add(new TextBlock
            {
                Text = "К удержанию по сотрудникам",
                Foreground = Brushes.White,
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                TextWrapping = TextWrapping.Wrap
            });

            var groups = items
                .GroupBy(item => string.IsNullOrWhiteSpace(item.ResponsibleEmployeeName)
                    ? "Без имени"
                    : item.ResponsibleEmployeeName.Trim())
                .Select(group => new
                {
                    EmployeeName = group.Key,
                    Total = group.Sum(item => item.Amount),
                    Unpaid = group.Where(item => !item.IsPaid).Sum(item => item.Amount),
                    Paid = group.Where(item => item.IsPaid).Sum(item => item.Amount),
                    Cash = group.Where(item => !item.IsPaid && IsCashLoss(item)).Sum(item => item.Amount),
                    Product = group.Where(item => !item.IsPaid && IsProductLoss(item)).Sum(item => item.Amount),
                    Other = group.Where(item => !item.IsPaid && !IsCashLoss(item) && !IsProductLoss(item)).Sum(item => item.Amount),
                    Count = group.Count()
                })
                .OrderByDescending(group => group.Unpaid)
                .ThenBy(group => group.EmployeeName)
                .ToList();

            if (groups.Count == 0)
            {
                panel.Children.Add(CreateMutedText("За выбранный период записей нет."));
                return CreateCard(panel, Color.FromRgb(30, 41, 59));
            }

            foreach (var group in groups)
            {
                var employeePanel = new StackPanel
                {
                    Margin = new Thickness(0, 10, 0, 0)
                };

                employeePanel.Children.Add(new TextBlock
                {
                    Text = group.EmployeeName,
                    Foreground = Brushes.White,
                    FontSize = 16,
                    FontWeight = FontWeights.Bold,
                    TextWrapping = TextWrapping.Wrap
                });

                employeePanel.Children.Add(new TextBlock
                {
                    Text =
                        $"К удержанию: {group.Unpaid} сом\n" +
                        $"Оплачено: {group.Paid} сом\n" +
                        $"Всего потерь: {group.Total} сом / записей: {group.Count}\n" +
                        $"Неоплачено по типам: наличка {group.Cash} сом, товары {group.Product} сом, другое {group.Other} сом",
                    Foreground = group.Unpaid > 0
                        ? new SolidColorBrush(Color.FromRgb(248, 113, 113))
                        : new SolidColorBrush(Color.FromRgb(74, 222, 128)),
                    FontSize = 14,
                    FontWeight = group.Unpaid > 0 ? FontWeights.SemiBold : FontWeights.Normal,
                    TextWrapping = TextWrapping.Wrap,
                    LineHeight = 22,
                    Margin = new Thickness(0, 4, 0, 0)
                });

                panel.Children.Add(employeePanel);
            }

            return CreateCard(panel, Color.FromRgb(17, 35, 58));
        }

        private Border CreateEmployeeLossCard(EmployeeLossItem item)
        {
            var panel = new StackPanel();

            panel.Children.Add(CreateTitleText(item.Title));

            string status = item.IsPaid ? "Оплачено" : "Не оплачено / удержать из зарплаты";

            string text =
                $"Дата: {item.CreatedAt:dd.MM.yyyy HH:mm}\n" +
                $"Ответственный: {item.ResponsibleEmployeeName}\n" +
                $"Проверил: {item.CheckedByEmployeeName}\n" +
                $"Тип: {item.LossType}\n" +
                $"Статус: {status}";

            if (!string.IsNullOrWhiteSpace(item.Description))
                text += $"\n\n{item.Description}";

            if (!string.IsNullOrWhiteSpace(item.Note))
                text += $"\n\nПримечание: {item.Note}";

            panel.Children.Add(CreateMutedText(text));

            panel.Children.Add(new TextBlock
            {
                Text = item.IsPaid
                    ? $"Оплачено: {item.Amount} сом"
                    : $"К удержанию: {item.Amount} сом",
                Foreground = item.IsPaid
                    ? new SolidColorBrush(Color.FromRgb(74, 222, 128))
                    : new SolidColorBrush(Color.FromRgb(248, 113, 113)),
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 10, 0, 0)
            });

            return CreateCard(panel, Color.FromRgb(24, 32, 43));
        }

        private bool IsCashLoss(EmployeeLossItem item)
        {
            string text = $"{item.LossType} {item.Title} {item.Description}".ToLowerInvariant();

            return text.Contains("налич");
        }

        private bool IsProductLoss(EmployeeLossItem item)
        {
            string text = $"{item.LossType} {item.Title} {item.Description}".ToLowerInvariant();

            return text.Contains("товар");
        }

        private Border CreateJournalRowCard(JournalRow row)
        {
            var root = new Grid();
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var left = new StackPanel();

            left.Children.Add(CreateTitleText(row.Title));
            left.Children.Add(CreateMutedText($"{row.CreatedAt:dd.MM.yyyy HH:mm}\n{row.Subtitle}"));

            var right = new TextBlock
            {
                Text = row.AccentText,
                Foreground = new SolidColorBrush(Color.FromRgb(74, 222, 128)),
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(16, 0, 0, 0)
            };

            Grid.SetColumn(left, 0);
            Grid.SetColumn(right, 1);

            root.Children.Add(left);
            root.Children.Add(right);

            return CreateCard(root, Color.FromRgb(24, 32, 43));
        }

        private TextBlock CreateTitleText(string text)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = Brushes.White,
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                TextWrapping = TextWrapping.Wrap
            };
        }

        private TextBlock CreateMutedText(string text)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 22,
                Margin = new Thickness(0, 7, 0, 0)
            };
        }

        private TextBlock CreateAccentText(string text, bool isBad)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = isBad
                    ? new SolidColorBrush(Color.FromRgb(248, 113, 113))
                    : new SolidColorBrush(Color.FromRgb(74, 222, 128)),
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 10, 0, 0)
            };
        }

        private TextBlock CreateGoodAccentText(string text)
        {
            return CreateAccentText(text, false);
        }

        private TextBlock CreateInfoAccentText(string text)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = new SolidColorBrush(Color.FromRgb(96, 165, 250)),
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 0)
            };
        }

        private Border CreateCard(UIElement child, Color backgroundColor)
        {
            return new Border
            {
                Background = new SolidColorBrush(backgroundColor),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(14),
                Margin = new Thickness(0, 0, 0, 10),
                Child = child
            };
        }

        private void AddEmptyText(string text)
        {
            _itemsPanel.Children.Add(new TextBlock
            {
                Text = text,
                Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                FontSize = 16,
                Margin = new Thickness(8)
            });
        }

        private string FormatDuration(TimeSpan duration)
        {
            if (duration.TotalMinutes < 1)
                return "меньше 1 минуты";

            int hours = (int)duration.TotalHours;
            int minutes = duration.Minutes;

            if (hours <= 0)
                return $"{minutes} мин";

            return $"{hours} ч {minutes} мин";
        }

        private void DayButton_Click(object sender, RoutedEventArgs e)
        {
            var window = new DatePickerWindow(_selectedDay)
            {
                Owner = this
            };

            bool? result = window.ShowDialog();

            if (result != true)
                return;

            _selectedDay = window.SelectedDate;
            _periodMode = JournalPeriodMode.Day;
            Render();
        }

        private void MonthButton_Click(object sender, RoutedEventArgs e)
        {
            var window = new MonthPickerWindow(_selectedYear, _selectedMonth)
            {
                Owner = this
            };

            bool? result = window.ShowDialog();

            if (result != true)
                return;

            _selectedYear = window.SelectedYear;
            _selectedMonth = window.SelectedMonth;
            _periodMode = JournalPeriodMode.Month;
            Render();
        }

        private void PeriodButton_Click(object sender, RoutedEventArgs e)
        {
            var window = new PeriodPickerWindow(_periodStart, _periodEnd)
            {
                Owner = this
            };

            bool? result = window.ShowDialog();

            if (result != true)
                return;

            _periodStart = window.StartDate;
            _periodEnd = window.EndDate;
            _periodMode = JournalPeriodMode.CustomPeriod;
            Render();
        }

        private class JournalRow
        {
            public DateTime CreatedAt { get; set; }

            public string Title { get; set; } = "";

            public string Subtitle { get; set; } = "";

            public string AccentText { get; set; } = "";
        }
    }
}
