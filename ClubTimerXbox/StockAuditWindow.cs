using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ClubTimerXbox.Models;
using ClubTimerXbox.Services;

namespace ClubTimerXbox
{
    public class StockAuditWindow : Window
    {
        private readonly StackPanel _contentPanel = new StackPanel();
        private readonly StackPanel _itemsPanel = new StackPanel();
        private readonly StackPanel _historyPanel = new StackPanel();

        private readonly List<AuditRow> _rows = new List<AuditRow>();

        private Border? _productsCard;
        private Border? _cashCard;

        private readonly TextBox _actualCashBox = new TextBox();
        private readonly TextBlock _cashExpectedText = new TextBlock();
        private readonly TextBlock _cashDifferenceText = new TextBlock();
        private readonly TextBlock _statusText = new TextBlock();

        private int _expectedCashAmount = 0;

        private enum ActiveSection
        {
            Products,
            Cash
        }

        private ActiveSection _activeSection = ActiveSection.Products;

        public StockAuditWindow()
        {
            Title = "Приёмка смены";
            Width = 980;
            Height = 780;
            MinWidth = 860;
            MinHeight = 660;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromRgb(16, 20, 28));

            Content = CreateContent();

            LoadRows();
            LoadHistory();
            RefreshStatusCards();

            if (ShiftAcceptanceService.Current.ProductsAccepted && !ShiftAcceptanceService.Current.CashAccepted)
                ShowCashSection();
            else
                ShowProductsSection();
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
                Text = "Приёмка смены",
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

            _statusText.Text = BuildAcceptanceStatusText();
            _statusText.Foreground = new SolidColorBrush(Color.FromRgb(170, 180, 195));
            _statusText.FontSize = 15;
            _statusText.TextWrapping = TextWrapping.Wrap;
            _statusText.LineHeight = 23;
            _statusText.Margin = new Thickness(0, 0, 0, 14);

            DockPanel.SetDock(_statusText, Dock.Top);
            root.Children.Add(_statusText);

            var cardsGrid = new Grid
            {
                Margin = new Thickness(0, 0, 0, 18)
            };

            cardsGrid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });

            cardsGrid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });

            _productsCard = CreateTopStatusCard(
                title: "Товары",
                subtitle: "Проверка фактических остатков",
                isAccepted: ShiftAcceptanceService.Current.ProductsAccepted,
                isActive: true
            );

            _productsCard.MouseLeftButtonUp += (_, _) => ShowProductsSection();

            _cashCard = CreateTopStatusCard(
                title: "Наличка",
                subtitle: "Проверка денег в кассе",
                isAccepted: ShiftAcceptanceService.Current.CashAccepted,
                isActive: false
            );

            _cashCard.MouseLeftButtonUp += (_, _) => ShowCashSection();

            Grid.SetColumn(_productsCard, 0);
            cardsGrid.Children.Add(_productsCard);

            Grid.SetColumn(_cashCard, 1);
            cardsGrid.Children.Add(_cashCard);

            DockPanel.SetDock(cardsGrid, Dock.Top);
            root.Children.Add(cardsGrid);

            var scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = _contentPanel
            };

            root.Children.Add(scrollViewer);

            return root;
        }

        private Border CreateTopStatusCard(
            string title,
            string subtitle,
            bool isAccepted,
            bool isActive)
        {
            var panel = new StackPanel();

            panel.Children.Add(new TextBlock
            {
                Text = isAccepted ? $"{title} ✅" : $"{title} 🟡",
                Foreground = Brushes.White,
                FontSize = 23,
                FontWeight = FontWeights.Bold
            });

            panel.Children.Add(new TextBlock
            {
                Text = isAccepted ? "Принято" : subtitle,
                Foreground = isAccepted
                    ? new SolidColorBrush(Color.FromRgb(74, 222, 128))
                    : new SolidColorBrush(Color.FromRgb(251, 191, 36)),
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 7, 0, 0),
                TextWrapping = TextWrapping.Wrap
            });

            return new Border
            {
                Background = GetTopCardBackground(isAccepted, isActive),
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(18),
                Margin = title == "Товары"
                    ? new Thickness(0, 0, 8, 0)
                    : new Thickness(8, 0, 0, 0),
                Child = panel,
                Cursor = System.Windows.Input.Cursors.Hand
            };
        }

        private SolidColorBrush GetTopCardBackground(bool isAccepted, bool isActive)
        {
            if (isAccepted)
                return new SolidColorBrush(Color.FromRgb(22, 75, 50));

            if (isActive)
                return new SolidColorBrush(Color.FromRgb(70, 55, 20));

            return new SolidColorBrush(Color.FromRgb(45, 38, 25));
        }

        private void RefreshStatusCards()
        {
            UpdateTopStatusCard(
                card: _productsCard,
                title: "Товары",
                subtitle: "Проверка фактических остатков",
                isAccepted: ShiftAcceptanceService.Current.ProductsAccepted,
                isActive: _activeSection == ActiveSection.Products
            );

            UpdateTopStatusCard(
                card: _cashCard,
                title: "Наличка",
                subtitle: "Проверка денег в кассе",
                isAccepted: ShiftAcceptanceService.Current.CashAccepted,
                isActive: _activeSection == ActiveSection.Cash
            );

            _statusText.Text = BuildAcceptanceStatusText();
        }

        private void UpdateTopStatusCard(
            Border? card,
            string title,
            string subtitle,
            bool isAccepted,
            bool isActive)
        {
            if (card == null)
                return;

            card.Background = GetTopCardBackground(isAccepted, isActive);

            if (card.Child is not StackPanel panel)
                return;

            if (panel.Children.Count >= 1 && panel.Children[0] is TextBlock titleText)
                titleText.Text = isAccepted ? $"{title} ✅" : $"{title} 🟡";

            if (panel.Children.Count >= 2 && panel.Children[1] is TextBlock subtitleText)
            {
                subtitleText.Text = isAccepted ? "Принято" : subtitle;
                subtitleText.Foreground = isAccepted
                    ? new SolidColorBrush(Color.FromRgb(74, 222, 128))
                    : new SolidColorBrush(Color.FromRgb(251, 191, 36));
            }
        }

        private string BuildAcceptanceStatusText()
        {
            var state = ShiftAcceptanceService.Current;

            string newEmployee = string.IsNullOrWhiteSpace(state.NewEmployeeName)
                ? EmployeeService.CurrentEmployee?.Name ?? "Неизвестно"
                : state.NewEmployeeName;

            string responsible = string.IsNullOrWhiteSpace(state.ResponsibleEmployeeName)
                ? GetResponsibleEmployeeName()
                : state.ResponsibleEmployeeName;

            string displayNewEmployee = string.IsNullOrWhiteSpace(state.DisplayNewEmployeeName)
                ? newEmployee
                : state.DisplayNewEmployeeName;

            string displayResponsible = string.IsNullOrWhiteSpace(state.DisplayResponsibleEmployeeName)
                ? responsible
                : state.DisplayResponsibleEmployeeName;

            if (!state.IsRequired && state.IsCompleted && state.IsManualSelfAcceptance)
                return "Приёмка завершена. Товары и наличка уже приняты.";

            if (!state.IsRequired && state.IsCompleted)
                return $"Приёмка завершена. Передача: {displayResponsible} → {displayNewEmployee}.";

            if (state.IsManualSelfAcceptance)
            {
                return
                    $"Ручная самоприёмка: {newEmployee} проверяет кассу на себя.\n" +
                    "Эта проверка не мигает на главном экране и не блокирует статистику.";
            }

            return
                $"Передача смены: {displayResponsible} → {displayNewEmployee}.\n" +
                "Чтобы кнопка “Приёмка” перестала мигать, нужно принять две части: товары и наличку.";
        }

        private void ShowProductsSection()
        {
            _activeSection = ActiveSection.Products;
            RefreshStatusCards();

            _contentPanel.Children.Clear();

            _contentPanel.Children.Add(new TextBlock
            {
                Text = "Товары",
                Foreground = Brushes.White,
                FontSize = 26,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 8)
            });

            _contentPanel.Children.Add(new TextBlock
            {
                Text =
                    "Введите фактическое количество товаров. " +
                    "Если фактически меньше, чем по программе, недостача запишется на предыдущую смену.",
                Foreground = new SolidColorBrush(Color.FromRgb(170, 180, 195)),
                FontSize = 15,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 23,
                Margin = new Thickness(0, 0, 0, 14)
            });

            AddRepeatAcceptanceButtonIfAvailable(ActiveSection.Products);

            if (ShiftAcceptanceService.Current.ProductsAccepted)
            {
                _contentPanel.Children.Add(CreateInfoCard(
                    "Товары уже приняты ✅",
                    "Эта часть приёмки уже завершена. Можно перейти во вкладку “Наличка”.",
                    Color.FromRgb(22, 75, 50)
                ));
            }

            _contentPanel.Children.Add(_itemsPanel);

            var buttonsPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 18)
            };

            var acceptButton = new Button
            {
                Content = ShiftAcceptanceService.Current.ProductsAccepted
                    ? "Товары уже приняты"
                    : "Принять товары",
                Width = 180,
                Height = 44,
                FontSize = 16,
                FontWeight = FontWeights.SemiBold
            };

            acceptButton.Click += (_, _) => AcceptProducts();

            buttonsPanel.Children.Add(acceptButton);
            _contentPanel.Children.Add(buttonsPanel);

            _contentPanel.Children.Add(new TextBlock
            {
                Text = "История приёмок товаров",
                Foreground = Brushes.White,
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 14, 0, 12)
            });

            _contentPanel.Children.Add(_historyPanel);
        }

        private void ShowCashSection()
        {
            _activeSection = ActiveSection.Cash;
            RefreshStatusCards();

            _contentPanel.Children.Clear();

            _expectedCashAmount = CalculateExpectedCashAmount();

            _actualCashBox.Text = "";
            _actualCashBox.Width = 180;
            _actualCashBox.Height = 44;
            _actualCashBox.FontSize = 18;
            _actualCashBox.Padding = new Thickness(10, 5, 10, 5);
            _actualCashBox.TextChanged -= ActualCashBox_TextChanged;
            _actualCashBox.TextChanged += ActualCashBox_TextChanged;

            _cashExpectedText.Text = "Посчитайте фактическую наличку и введите сумму.";

            _cashExpectedText.Foreground = Brushes.White;
            _cashExpectedText.FontSize = 23;
            _cashExpectedText.FontWeight = FontWeights.Bold;

            _cashDifferenceText.Text = "";

            _contentPanel.Children.Add(new TextBlock
            {
                Text = "Наличка",
                Foreground = Brushes.White,
                FontSize = 26,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 8)
            });

            _contentPanel.Children.Add(new TextBlock
            {
                Text =
                    "Ожидаемую сумму до ввода не показываем. Сначала нужно честно посчитать деньги в кассе.",
                Foreground = new SolidColorBrush(Color.FromRgb(170, 180, 195)),
                FontSize = 15,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 23,
                Margin = new Thickness(0, 0, 0, 14)
            });

            AddRepeatAcceptanceButtonIfAvailable(ActiveSection.Cash);

            if (ShiftAcceptanceService.Current.CashAccepted)
            {
                _contentPanel.Children.Add(CreateInfoCard(
                    "Наличка уже принята ✅",
                    "Эта часть приёмки уже завершена.",
                    Color.FromRgb(22, 75, 50)
                ));
            }

            _contentPanel.Children.Add(CreateCashCard());
        }

        private void AddRepeatAcceptanceButtonIfAvailable(ActiveSection section)
        {
            string currentEmployeeName = EmployeeService.CurrentEmployee?.Name ?? "";
            bool canRepeat = section == ActiveSection.Products
                ? ShiftAcceptanceService.CanCorrectProductsAcceptance(currentEmployeeName)
                : ShiftAcceptanceService.CanCorrectCashAcceptance(currentEmployeeName);

            if (!canRepeat)
                return;

            var remaining = ShiftAcceptanceService.GetCashCorrectionRemaining();
            string remainingText = remaining == null
                ? ""
                : $" Осталось: {Math.Ceiling(remaining.Value.TotalMinutes)} мин.";

            var panel = new StackPanel();

            panel.Children.Add(new TextBlock
            {
                Text = section == ActiveSection.Products
                    ? $"Повторная приёмка товаров доступна.{remainingText}"
                    : $"Повторная приёмка налички доступна.{remainingText}",
                Foreground = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                FontSize = 15,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 22,
                Margin = new Thickness(0, 0, 0, 12)
            });

            var button = CreateRepeatAcceptanceButton(
                section == ActiveSection.Products
                    ? "Повторить товары"
                    : "Повторить наличку"
            );

            button.Click += (_, _) => StartRepeatAcceptance(section);

            panel.Children.Add(button);

            _contentPanel.Children.Add(CreateCard(panel, Color.FromRgb(42, 58, 78)));
        }

        private Button CreateRepeatAcceptanceButton(string text)
        {
            return new Button
            {
                Content = text,
                Width = 170,
                Height = 42,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Left
            };
        }

        private void StartRepeatAcceptance(ActiveSection section)
        {
            string currentEmployeeName = EmployeeService.CurrentEmployee?.Name ?? "";
            bool started = section == ActiveSection.Products
                ? ShiftAcceptanceService.StartProductsCorrection(currentEmployeeName)
                : ShiftAcceptanceService.StartCashCorrection(currentEmployeeName);

            if (!started)
            {
                MessageBox.Show(
                    "Время повторной приёмки уже прошло или повторная приёмка уже была выполнена.",
                    "Повторная приёмка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );

                RefreshStatusCards();
                if (section == ActiveSection.Products)
                    ShowProductsSection();
                else
                    ShowCashSection();
                return;
            }

            MessageBox.Show(
                "Эта часть снова переведена в статус незавершённой. Ответственный останется тот же, что и в первой приёмке.",
                "Повторная приёмка",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );

            if (section == ActiveSection.Products)
                ShowProductsSection();
            else
                ShowCashSection();
        }

        private Border CreateCashCard()
        {
            DetachFromParent(_cashExpectedText);
            DetachFromParent(_actualCashBox);
            DetachFromParent(_cashDifferenceText);

            var panel = new StackPanel();

            panel.Children.Add(_cashExpectedText);

            panel.Children.Add(new TextBlock
            {
                Text =
                    "После принятия система сравнит факт с программой и отправит разницу владельцу на разбор.",
                Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 21,
                Margin = new Thickness(0, 8, 0, 16)
            });

            var inputPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            inputPanel.Children.Add(new TextBlock
            {
                Text = "Фактически:",
                Foreground = Brushes.White,
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0)
            });

            inputPanel.Children.Add(_actualCashBox);

            inputPanel.Children.Add(new TextBlock
            {
                Text = "сом",
                Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                FontSize = 18,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0)
            });

            panel.Children.Add(inputPanel);

            _cashDifferenceText.Margin = new Thickness(0, 12, 0, 0);
            panel.Children.Add(_cashDifferenceText);

            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 18, 0, 0)
            };

            var acceptButton = new Button
            {
                Content = ShiftAcceptanceService.Current.CashAccepted
                    ? "Наличка уже принята"
                    : "Принять наличку",
                Width = 190,
                Height = 44,
                FontSize = 16,
                FontWeight = FontWeights.SemiBold
            };

            acceptButton.Click += (_, _) => AcceptCash();

            buttonPanel.Children.Add(acceptButton);
            panel.Children.Add(buttonPanel);

            return CreateCard(panel, Color.FromRgb(24, 32, 43));
        }

        private void ActualCashBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateCashDifferenceText();
        }

        private void DetachFromParent(UIElement element)
        {
            var parent = LogicalTreeHelper.GetParent(element);

            if (parent is Panel panel)
            {
                panel.Children.Remove(element);
                return;
            }

            if (parent is Border border && ReferenceEquals(border.Child, element))
            {
                border.Child = null;
                return;
            }

            if (parent is ContentControl contentControl && ReferenceEquals(contentControl.Content, element))
            {
                contentControl.Content = null;
            }
        }

        private void UpdateCashDifferenceText()
        {
            _cashDifferenceText.Text = "";
            _cashDifferenceText.FontSize = 16;
            _cashDifferenceText.FontWeight = FontWeights.SemiBold;
        }

        private int CalculateExpectedCashAmount()
        {
            try
            {
                var lastCashAcceptance = CashAcceptanceService.GetLastAcceptance();

                DateTime fromTime;
                int baseCashAmount;

                if (lastCashAcceptance != null)
                {
                    // Правильная логика:
                    // последняя принятая фактическая наличка
                    // + все новые наличные поступления после этой приёмки
                    // - все новые наличные расходы после этой приёмки.
                    fromTime = lastCashAcceptance.CreatedAt;
                    baseCashAmount = lastCashAcceptance.ActualCashAmount;
                }
                else
                {
                    // Если приёмки налички ещё ни разу не было,
                    // временно стартуем с начала текущего месяца.
                    fromTime = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
                    baseCashAmount = 0;
                }

                DateTime toTime = DateTime.Now.AddSeconds(1);

                int cashIncome = 0;

                if (PaymentService.Records != null)
                {
                    cashIncome = PaymentService.Records
                        .Where(record =>
                            record.CreatedAt > fromTime &&
                            record.CreatedAt < toTime)
                        .Sum(record => record.CashAmount);
                }

                int cashExpenses = 0;

                try
                {
                    cashExpenses = CashService.GetCashExpenseTotalByPeriod(fromTime, toTime);
                }
                catch
                {
                    // Если старый сервис расходов пока не готов или в нём старые данные,
                    // не даём окну приёмки падать.
                    cashExpenses = 0;
                }

                int expected = baseCashAmount + cashIncome - cashExpenses;

                if (expected < 0)
                    expected = 0;

                return expected;
            }
            catch
            {
                return 0;
            }
        }

        private void AcceptCash()
        {
            if (!CanAcceptPart("наличку", ShiftAcceptanceService.Current.CashAccepted))
                return;

            if (!int.TryParse(_actualCashBox.Text.Trim(), out int actualCash))
            {
                MessageBox.Show(
                    "Фактическая наличка должна быть числом.",
                    "Приёмка налички"
                );

                return;
            }

            if (actualCash < 0)
                actualCash = 0;

            string checkedBy = EmployeeService.CurrentEmployee?.Name ?? "Неизвестно";
            string responsible = GetResponsibleEmployeeName();
            string acceptanceKey = ShiftAcceptanceService.Current.AcceptanceKey;

            if (CashAcceptanceService.HasAcceptanceKey(acceptanceKey))
            {
                ShiftAcceptanceService.AcceptCash();
                MessageBox.Show(
                    "Наличка по этой передаче смены уже была принята ранее.\n\n" +
                    "Повторная запись и повторный штраф не созданы.",
                    "Приёмка смены",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
                FinishOrRefreshAfterPartAccepted();
                return;
            }

            int difference = actualCash - _expectedCashAmount;
            int originalCashShortage = 0;
            int coveredByCashlessExtra = 0;
            int finalCashShortage = 0;
            int correctedInputMistake = 0;
            int nettedReconciliation = 0;
            int remainingCashExtra = Math.Max(0, difference);
            var monthStart = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            var nextMonthStart = monthStart.AddMonths(1);

            CashAcceptanceService.AddItem(
                checkedByEmployeeName: checkedBy,
                responsibleEmployeeName: responsible,
                expectedCashAmount: _expectedCashAmount,
                actualCashAmount: actualCash,
                note: "Приёмка налички",
                acceptanceKey: acceptanceKey
            );

            if (difference < 0)
            {
                originalCashShortage = Math.Abs(difference);
                var reconciliation = CashReconciliationService.AddCashAcceptanceDifference(
                    checkedByEmployeeName: checkedBy,
                    responsibleEmployeeName: responsible,
                    expectedAmount: _expectedCashAmount,
                    actualAmount: actualCash,
                    note: "Приёмка налички"
                );
                nettedReconciliation = CashReconciliationService.NetOpenMoneyCorrections(
                    monthStart,
                    nextMonthStart,
                    "Система",
                    "Автозачёт после приёмки налички: встречные суммы нал/безнал закрыты как ошибка типа оплаты."
                );

                finalCashShortage = reconciliation.Status == CashReconciliationStatus.Resolved
                    ? 0
                    : reconciliation.Amount;
                coveredByCashlessExtra = originalCashShortage - finalCashShortage;
            }
            else if (difference > 0)
            {
                correctedInputMistake = CashReconciliationService.ResolveRecentCashAcceptanceInputMistakes(
                    checkedByEmployeeName: checkedBy,
                    amount: difference,
                    correctionWindow: TimeSpan.FromMinutes(15),
                    note:
                        $"Повторная приёмка налички: {checkedBy} ввёл {actualCash} сом. " +
                        "Зачтено как исправление ошибки ввода.",
                    fromInclusive: monthStart,
                    toExclusive: nextMonthStart
                );

                remainingCashExtra = difference - correctedInputMistake;

                if (remainingCashExtra > 0)
                {
                    var reconciliation = CashReconciliationService.AddCashAcceptanceDifference(
                        checkedByEmployeeName: checkedBy,
                        responsibleEmployeeName: responsible,
                        expectedAmount: _expectedCashAmount,
                        actualAmount: _expectedCashAmount + remainingCashExtra,
                        note: correctedInputMistake > 0
                            ? $"Приёмка налички. После исправления ошибки ввода осталось лишнее: {remainingCashExtra} сом."
                            : "Приёмка налички"
                    );
                    nettedReconciliation = CashReconciliationService.NetOpenMoneyCorrections(
                        monthStart,
                        nextMonthStart,
                        "Система",
                        "Автозачёт после приёмки налички: встречные суммы нал/безнал закрыты как ошибка типа оплаты."
                    );

                    remainingCashExtra = reconciliation.Status == CashReconciliationStatus.Resolved
                        ? 0
                        : reconciliation.Amount;
                }
            }

            ShiftAcceptanceService.AcceptCash();

            string message =
                "Наличка принята.\n\n" +
                $"Передача: {responsible} → {checkedBy}\n" +
                $"Должно быть: {_expectedCashAmount} сом\n" +
                $"Фактически: {actualCash} сом\n";

            if (difference < 0)
            {
                message += $"Недостача: {originalCashShortage} сом\n";

                if (coveredByCashlessExtra > 0)
                    message += $"Зачтено излишком безнала: {coveredByCashlessExtra} сом\n";
                else if (nettedReconciliation > 0)
                    message += $"Автозачёт разборов: {nettedReconciliation} сом\n";

                if (finalCashShortage > 0)
                    message += $"Активная разница: {finalCashShortage} сом\nОткройте на телефоне Разница кассы, чтобы закрыть её или оформить как потери.";
                else
                    message += "Недостача закрыта излишком безнала как ошибка типа оплаты.";
            }
            else if (difference > 0)
            {
                if (correctedInputMistake > 0)
                    message += $"Исправлена ошибка ввода прошлой приёмки: {correctedInputMistake} сом\n";

                if (nettedReconciliation > 0)
                    message += $"Автозачёт разборов: {nettedReconciliation} сом\n";

                if (remainingCashExtra > 0)
                    message += $"Излишек: {remainingCashExtra} сом\nРазница отправлена владельцу на разбор.";
                else
                    message += "Излишка нет: плюс ушёл на закрытие ошибки ввода прошлой приёмки.";
            }
            else
                message += "Разница: 0.";

            MessageBox.Show(message, "Приёмка налички");

            FinishOrRefreshAfterPartAccepted();
        }

        private void ResolveSmallCashlessShortagesAfterCashAcceptance(int actualCash)
        {
            var monthStart = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            var nextMonthStart = monthStart.AddMonths(1);
            var shortages = CashReconciliationService.GetOpenSmallCashlessShortages(
                monthStart,
                nextMonthStart
            );

            if (shortages.Count == 0)
                return;

            foreach (var shortage in shortages)
            {
                if (shortage.Status == CashReconciliationStatus.Resolved ||
                    shortage.Amount <= 0)
                {
                    continue;
                }

                DistributeCashlessShortage(
                    shortage.Amount,
                    shortage.ExpectedAmount,
                    shortage.ActualAmount
                );

                CashReconciliationService.Resolve(
                    shortage.Id,
                    "Система",
                    "RealShortage",
                    $"После приёмки налички фактическая касса {actualCash} сом не дала излишек для закрытия безнала. Оформлено как реальная недостача."
                );
            }
        }

        private void DistributeCashlessShortage(
            int shortageAmount,
            int expectedCashlessBalance,
            int actualCashless)
        {
            var monthStart = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            var nextMonthStart = monthStart.AddMonths(1);

            var groups = PaymentService.Records
                .Where(record =>
                    record.CreatedAt >= monthStart &&
                    record.CreatedAt < nextMonthStart &&
                    record.MBankAmount > 0)
                .GroupBy(record =>
                    string.IsNullOrWhiteSpace(record.EmployeeName)
                        ? "Неизвестно"
                        : record.EmployeeName)
                .Select(group => new
                {
                    EmployeeName = group.Key,
                    Amount = group.Sum(record => record.MBankAmount)
                })
                .Where(group => group.Amount > 0)
                .OrderByDescending(group => group.Amount)
                .ToList();

            if (groups.Count == 0)
            {
                AddCashlessShortageForEmployee(
                    "Неизвестно",
                    shortageAmount,
                    expectedCashlessBalance,
                    actualCashless
                );

                return;
            }

            int total = groups.Sum(group => group.Amount);
            int distributed = 0;

            for (int index = 0; index < groups.Count; index++)
            {
                int amount = index == groups.Count - 1
                    ? shortageAmount - distributed
                    : (int)Math.Round(shortageAmount * (groups[index].Amount / (double)total));

                if (amount <= 0)
                    continue;

                distributed += amount;

                AddCashlessShortageForEmployee(
                    groups[index].EmployeeName,
                    amount,
                    expectedCashlessBalance,
                    actualCashless
                );
            }
        }

        private void AddCashlessShortageForEmployee(
            string employeeName,
            int amount,
            int expectedCashlessBalance,
            int actualCashless)
        {
            string description =
                $"Автоматическая сверка безнала после приёмки налички.\n" +
                $"Ожидаемый остаток безнала: {expectedCashlessBalance} сом\n" +
                $"Фактический остаток: {actualCashless} сом\n" +
                $"Доля сотрудника: {amount} сом";

            CashService.AddShortage(
                checkedByEmployeeName: "Система",
                responsibleEmployeeName: employeeName,
                title: "Недостача безнала",
                description: description,
                amount: amount
            );

            EmployeeLossService.AddLoss(
                responsibleEmployeeName: employeeName,
                checkedByEmployeeName: "Система",
                lossType: "Недостача безнала",
                title: "Недостача безнала",
                description: description,
                amount: amount,
                note: "Автоматически создано после приёмки налички"
            );
        }

        private Border CreateInfoCard(string title, string subtitle, Color color)
        {
            var panel = new StackPanel();

            panel.Children.Add(new TextBlock
            {
                Text = title,
                Foreground = Brushes.White,
                FontSize = 20,
                FontWeight = FontWeights.Bold
            });

            panel.Children.Add(new TextBlock
            {
                Text = subtitle,
                Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 21,
                Margin = new Thickness(0, 8, 0, 0)
            });

            return CreateCard(panel, color);
        }

        private void LoadRows()
        {
            _itemsPanel.Children.Clear();
            _rows.Clear();

            foreach (var stockItem in ProductStockService.StockItems)
            {
                var saleItem = SaleItemService.FindByName(stockItem.ProductName);

                if (saleItem == null)
                    continue;

                var row = new AuditRow
                {
                    ProductName = stockItem.ProductName,
                    ExpectedQuantity = stockItem.Quantity,
                    SalePrice = saleItem.SalePrice,
                    ActualQuantityBox = new TextBox
                    {
                        Text = stockItem.Quantity.ToString(),
                        Width = 120,
                        Height = 40,
                        FontSize = 17,
                        Padding = new Thickness(10, 5, 10, 5)
                    },
                    DifferenceText = new TextBlock
                    {
                        Text = "Разница: 0",
                        Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                        FontSize = 14,
                        Margin = new Thickness(0, 6, 0, 0)
                    }
                };

                row.ActualQuantityBox.TextChanged += (_, _) => UpdateRowDifference(row);

                _rows.Add(row);
                _itemsPanel.Children.Add(CreateRowCard(row));
            }
        }

        private Border CreateRowCard(AuditRow row)
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
                Text = row.ProductName,
                Foreground = Brushes.White,
                FontSize = 20,
                FontWeight = FontWeights.Bold
            });

            leftPanel.Children.Add(new TextBlock
            {
                Text =
                    $"По программе: {row.ExpectedQuantity} шт\n" +
                    $"Цена продажи: {row.SalePrice} сом",
                Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                FontSize = 14,
                LineHeight = 21,
                Margin = new Thickness(0, 6, 0, 0)
            });

            leftPanel.Children.Add(row.DifferenceText);

            Grid.SetColumn(leftPanel, 0);
            root.Children.Add(leftPanel);

            var rightPanel = new StackPanel
            {
                Width = 170
            };

            rightPanel.Children.Add(new TextBlock
            {
                Text = "Фактически",
                Foreground = new SolidColorBrush(Color.FromRgb(170, 180, 195)),
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 4)
            });

            rightPanel.Children.Add(row.ActualQuantityBox);

            Grid.SetColumn(rightPanel, 1);
            root.Children.Add(rightPanel);

            return CreateCard(root, Color.FromRgb(24, 32, 43));
        }

        private void UpdateRowDifference(AuditRow row)
        {
            if (!int.TryParse(row.ActualQuantityBox.Text.Trim(), out int actualQuantity))
            {
                row.DifferenceText.Text = "Разница: неверное число";
                row.DifferenceText.Foreground = new SolidColorBrush(Color.FromRgb(248, 113, 113));
                return;
            }

            int difference = actualQuantity - row.ExpectedQuantity;
            int amount = Math.Abs(difference) * row.SalePrice;

            if (difference < 0)
            {
                row.DifferenceText.Text =
                    $"Недостача: {Math.Abs(difference)} шт / {amount} сом";

                row.DifferenceText.Foreground = new SolidColorBrush(Color.FromRgb(248, 113, 113));
                return;
            }

            if (difference > 0)
            {
                row.DifferenceText.Text =
                    $"Лишнее: {difference} шт / {amount} сом";

                row.DifferenceText.Foreground = new SolidColorBrush(Color.FromRgb(96, 165, 250));
                return;
            }

            row.DifferenceText.Text = "Разница: 0";
            row.DifferenceText.Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225));
        }

        private bool CanAcceptPart(string partName, bool alreadyAccepted)
        {
            if (alreadyAccepted)
            {
                string selfAcceptanceEmployeeName = EmployeeService.CurrentEmployee?.Name ?? "";

                if (ShiftAcceptanceService.CanStartManualSelfAcceptance(selfAcceptanceEmployeeName) &&
                    ShiftAcceptanceService.StartManualSelfAcceptance(selfAcceptanceEmployeeName))
                {
                    RefreshStatusCards();

                    if (_activeSection == ActiveSection.Products)
                        ShowProductsSection();
                    else
                        ShowCashSection();

                    return false;
                }

                MessageBox.Show(
                    $"Эта часть приёмки уже завершена: {partName}. Повторно принять нельзя.",
                    "Приёмка смены",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );

                return false;
            }

            string currentEmployeeName = EmployeeService.CurrentEmployee?.Name ?? "";

            if (!ShiftAcceptanceService.CanEmployeeAccept(currentEmployeeName))
            {
                string allowedEmployee = ShiftAcceptanceService.Current.NewEmployeeName;

                if (string.IsNullOrWhiteSpace(allowedEmployee))
                    allowedEmployee = "новый сотрудник текущей смены";

                MessageBox.Show(
                    $"Принять {partName} могут только следующие сотрудники:\n\n{allowedEmployee}",
                    "Приёмка смены",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );

                return false;
            }

            return true;
        }

        private void AcceptProducts()
        {
            if (!CanAcceptPart("товары", ShiftAcceptanceService.Current.ProductsAccepted))
                return;

            string checkedBy = EmployeeService.CurrentEmployee?.Name ?? "Неизвестно";
            string responsible = GetResponsibleEmployeeName();
            string acceptanceKey = ShiftAcceptanceService.Current.AcceptanceKey;

            if (StockAuditService.HasAcceptanceKey(acceptanceKey))
            {
                ShiftAcceptanceService.AcceptProducts();
                MessageBox.Show(
                    "Товары по этой передаче смены уже были приняты ранее.\n\n" +
                    "Повторная запись и повторный штраф не созданы.",
                    "Приёмка смены",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
                FinishOrRefreshAfterPartAccepted();
                return;
            }

            Guid batchId = Guid.NewGuid();

            int shortageCount = 0;
            int shortageAmount = 0;
            int changedCount = 0;

            var shortageDescriptions = new List<string>();

            foreach (var row in _rows)
            {
                if (!int.TryParse(row.ActualQuantityBox.Text.Trim(), out int actualQuantity))
                {
                    MessageBox.Show(
                        $"Проверьте поле товара: {row.ProductName}\n\n" +
                        "Фактическое количество должно быть числом.",
                        "Приёмка товаров"
                    );

                    return;
                }

                if (actualQuantity < 0)
                    actualQuantity = 0;

                int difference = actualQuantity - row.ExpectedQuantity;

                if (difference != 0)
                    changedCount++;

                if (difference < 0)
                {
                    int missingCount = Math.Abs(difference);
                    int missingAmount = missingCount * row.SalePrice;

                    shortageCount += missingCount;
                    shortageAmount += missingAmount;

                    shortageDescriptions.Add(
                        $"{row.ProductName}: недостача {missingCount} шт / {missingAmount} сом " +
                        $"(по программе {row.ExpectedQuantity}, фактически {actualQuantity})"
                    );
                }

                StockAuditService.AddAuditItem(
                    batchId: batchId,
                    checkedByEmployeeName: checkedBy,
                    responsibleEmployeeName: responsible,
                    productName: row.ProductName,
                    expectedQuantity: row.ExpectedQuantity,
                    actualQuantity: actualQuantity,
                    salePrice: row.SalePrice,
                    note: "Приёмка смены",
                    acceptanceKey: acceptanceKey
                );

                ProductStockService.SetQuantity(row.ProductName, actualQuantity);
            }

            if (shortageAmount > 0)
            {
                string shortageDescription =
                    $"Приёмка товаров: {responsible} → {checkedBy}\n" +
                    string.Join("\n", shortageDescriptions);

                CashService.AddShortage(
                    checkedByEmployeeName: checkedBy,
                    responsibleEmployeeName: responsible,
                    title: "Недостача товара",
                    description: shortageDescription,
                    amount: shortageAmount
                );

                EmployeeLossService.AddProductShortage(
                    responsibleEmployeeName: responsible,
                    checkedByEmployeeName: checkedBy,
                    description: shortageDescription,
                    amount: shortageAmount
                );
            }

            ShiftAcceptanceService.AcceptProducts();

            string message =
                $"Товары приняты.\n\n" +
                $"Передача: {responsible} → {checkedBy}\n\n" +
                $"Проверил: {checkedBy}\n" +
                $"Ответственная смена: {responsible}\n\n" +
                $"Изменённых позиций: {changedCount}";

            if (shortageCount > 0)
            {
                message +=
                    $"\nНедостача: {shortageCount} шт\n" +
                    $"Сумма недостачи: {shortageAmount} сом\n\n" +
                    "Недостача добавлена в кассу в раздел “Недостачи”.";
            }
            else
            {
                message += "\nНедостачи нет.";
            }

            MessageBox.Show(message, "Приёмка товаров");

            LoadRows();
            LoadHistory();

            FinishOrRefreshAfterPartAccepted();
        }

        private void FinishOrRefreshAfterPartAccepted()
        {
            RefreshStatusCards();

            if (!ShiftAcceptanceService.IsAcceptanceActive())
            {
                MessageBox.Show(
                    ShiftAcceptanceService.Current.IsManualSelfAcceptance
                        ? "Самоприёмка полностью завершена."
                        : "Приёмка смены полностью завершена.\n\nКнопка “Приёмка” на главном экране перестанет мигать.",
                    "Приёмка смены"
                );

                CloseSafelyAfterAcceptance();
                return;
            }

            if (!ShiftAcceptanceService.Current.CashAccepted)
            {
                ShowCashSection();
                return;
            }

            if (!ShiftAcceptanceService.Current.ProductsAccepted)
            {
                ShowProductsSection();
                return;
            }
        }

        private void CloseSafelyAfterAcceptance()
        {
            try
            {
                DialogResult = true;
            }
            catch
            {
                // Если окно было открыто через Show(), DialogResult вызывает ошибку.
                // Тогда просто закрываем окно обычным способом.
            }

            Close();
        }

        private string GetResponsibleEmployeeName()
        {
            var state = ShiftAcceptanceService.Current;

            if (!string.IsNullOrWhiteSpace(state.ResponsibleEmployeeName))
                return state.ResponsibleEmployeeName;

            string currentEmployeeName = EmployeeService.CurrentEmployee?.Name ?? "";

            var currentShift = ActionLogService.CurrentShift;

            if (currentShift != null)
            {
                var previousShift = ActionLogService.GetAllShifts()
                    .Where(shift =>
                        shift.IsClosed &&
                        shift.ClosedAt != null &&
                        shift.ClosedAt.Value <= currentShift.StartedAt)
                    .OrderByDescending(shift => shift.ClosedAt)
                    .FirstOrDefault();

                if (previousShift != null)
                    return previousShift.EmployeeName;
            }

            var fallbackShift = ActionLogService.GetAllShifts()
                .Where(shift =>
                    shift.IsClosed &&
                    shift.ClosedAt != null &&
                    shift.EmployeeName != currentEmployeeName)
                .OrderByDescending(shift => shift.ClosedAt)
                .FirstOrDefault();

            if (fallbackShift != null)
                return fallbackShift.EmployeeName;

            return "Предыдущая смена";
        }

        private void LoadHistory()
        {
            _historyPanel.Children.Clear();

            var batches = StockAuditService.GetAllBatches();

            if (batches.Count == 0)
            {
                _historyPanel.Children.Add(new TextBlock
                {
                    Text = "История приёмок товаров пока пустая.",
                    Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                    FontSize = 16,
                    Margin = new Thickness(0, 0, 0, 12)
                });

                return;
            }

            foreach (var batch in batches.Take(10))
            {
                _historyPanel.Children.Add(CreateHistoryBatchCard(batch.ToList()));
            }
        }

        private Border CreateHistoryBatchCard(List<StockAuditItem> items)
        {
            var first = items
                .OrderBy(item => item.CreatedAt)
                .First();

            string checkedBy = first.CheckedByEmployeeName;
            string responsible = first.ResponsibleEmployeeName;
            DateTime createdAt = items.Max(item => item.CreatedAt);

            int shortageCount = items
                .Where(item => item.Difference < 0)
                .Sum(item => Math.Abs(item.Difference));

            int shortageAmount = items
                .Where(item => item.Difference < 0)
                .Sum(item => item.DifferenceAmount);

            int extraCount = items
                .Where(item => item.Difference > 0)
                .Sum(item => item.Difference);

            var panel = new StackPanel();

            panel.Children.Add(new TextBlock
            {
                Text = $"{createdAt:dd.MM.yyyy HH:mm} • {responsible} → {checkedBy}",
                Foreground = Brushes.White,
                FontSize = 19,
                FontWeight = FontWeights.Bold
            });

            string summary = "Недостачи нет";

            if (shortageCount > 0)
                summary = $"Недостача: {shortageCount} шт / {shortageAmount} сом";

            if (extraCount > 0)
                summary += $" • Лишнее: {extraCount} шт";

            panel.Children.Add(new TextBlock
            {
                Text = summary,
                Foreground = shortageCount > 0
                    ? new SolidColorBrush(Color.FromRgb(248, 113, 113))
                    : new SolidColorBrush(Color.FromRgb(74, 222, 128)),
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 6, 0, 10)
            });

            foreach (var item in items)
            {
                if (item.Difference == 0)
                    continue;

                string label = item.Difference < 0
                    ? $"Недостача {Math.Abs(item.Difference)} шт"
                    : $"Лишнее {item.Difference} шт";

                panel.Children.Add(new TextBlock
                {
                    Text =
                        $"• {item.ProductName}: " +
                        $"по программе {item.ExpectedQuantity}, " +
                        $"фактически {item.ActualQuantity}, " +
                        $"{label}, сумма {item.DifferenceAmount} сом",
                    Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                    FontSize = 14,
                    TextWrapping = TextWrapping.Wrap,
                    LineHeight = 21,
                    Margin = new Thickness(0, 0, 0, 5)
                });
            }

            bool allMatched = items.All(item => item.Difference == 0);

            if (allMatched)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = "Все товары совпали с программой.",
                    Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                    FontSize = 14,
                    Margin = new Thickness(0, 0, 0, 5)
                });
            }

            return CreateCard(panel, Color.FromRgb(24, 32, 43));
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

        private class AuditRow
        {
            public string ProductName { get; set; } = "";
            public int ExpectedQuantity { get; set; }
            public int SalePrice { get; set; }
            public TextBox ActualQuantityBox { get; set; } = new TextBox();
            public TextBlock DifferenceText { get; set; } = new TextBlock();
        }
    }
}
