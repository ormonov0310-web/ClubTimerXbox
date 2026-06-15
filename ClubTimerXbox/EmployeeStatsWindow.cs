using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using ClubTimerXbox.Models;
using ClubTimerXbox.Services;

namespace ClubTimerXbox
{
    public class EmployeeStatsWindow : Window
    {
        private enum StatsSection
        {
            TakenHistory,
            Salary,
            Bonuses,
            Time,
            Income,
            ProductsServices,
            Losses
        }

        private readonly string _employeeName;
        private readonly TextBlock _monthText = new TextBlock();
        private readonly TextBlock _salaryValueText = new TextBlock();
        private readonly TextBlock _incomeValueText = new TextBlock();
        private readonly TextBlock _timeValueText = new TextBlock();
        private readonly TextBlock _lossValueText = new TextBlock();
        private readonly TextBlock _sectionInfoText = new TextBlock();
        private readonly StackPanel _contentPanel = new StackPanel();

        private Button _salaryButton = null!;
        private Button _bonusesButton = null!;
        private Button _timeButton = null!;
        private Button _incomeButton = null!;
        private Button _productsButton = null!;
        private Button _lossesButton = null!;

        private DateTime _monthStart = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        private StatsSection _section = StatsSection.Salary;

        public EmployeeStatsWindow(string employeeName)
        {
            _employeeName = employeeName;

            Title = $"Статистика сотрудника: {_employeeName}";
            Width = 980;
            Height = 780;
            MinWidth = 880;
            MinHeight = 660;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromRgb(16, 20, 28));

            Content = CreateContent();
            Render();
        }

        private UIElement CreateContent()
        {
            var root = new Grid
            {
                Margin = new Thickness(20)
            };

            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var topPanel = new DockPanel
            {
                Margin = new Thickness(0, 0, 0, 14)
            };

            var titleText = new TextBlock
            {
                Text = $"Статистика: {_employeeName}",
                Foreground = Brushes.White,
                FontSize = 30,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center
            };

            DockPanel.SetDock(titleText, Dock.Left);
            topPanel.Children.Add(titleText);

            var closeButton = new Button
            {
                Content = "Закрыть",
                Width = 120,
                Height = 40,
                FontSize = 16,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            closeButton.Click += (_, _) => Close();

            DockPanel.SetDock(closeButton, Dock.Right);
            topPanel.Children.Add(closeButton);

            Grid.SetRow(topPanel, 0);
            root.Children.Add(topPanel);

            var monthPanel = CreateMonthPanel();
            Grid.SetRow(monthPanel, 1);
            root.Children.Add(monthPanel);

            var sectionButtons = CreateSectionButtons();
            Grid.SetRow(sectionButtons, 2);
            root.Children.Add(sectionButtons);

            var summaryPanel = CreateSummaryPanel();
            Grid.SetRow(summaryPanel, 3);
            root.Children.Add(summaryPanel);

            var listTitle = new TextBlock
            {
                Text = "Список",
                Foreground = Brushes.White,
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 6)
            };

            Grid.SetRow(listTitle, 4);
            root.Children.Add(listTitle);

            _sectionInfoText.Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184));
            _sectionInfoText.FontSize = 14;
            _sectionInfoText.TextWrapping = TextWrapping.Wrap;
            _sectionInfoText.LineHeight = 21;
            _sectionInfoText.Margin = new Thickness(0, 0, 0, 10);

            Grid.SetRow(_sectionInfoText, 5);
            root.Children.Add(_sectionInfoText);

            var listBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(51, 65, 85)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(12),
                Child = new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Content = _contentPanel
                }
            };

            Grid.SetRow(listBorder, 6);
            root.Children.Add(listBorder);

            return root;
        }

        private UIElement CreateMonthPanel()
        {
            var grid = new Grid
            {
                Margin = new Thickness(0, 0, 0, 14)
            };

            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var prevButton = CreateArrowButton("<");
            prevButton.Click += (_, _) =>
            {
                _monthStart = _monthStart.AddMonths(-1);
                Render();
            };

            var nextButton = CreateArrowButton(">");
            nextButton.Click += (_, _) =>
            {
                _monthStart = _monthStart.AddMonths(1);
                Render();
            };

            _monthText.Foreground = Brushes.White;
            _monthText.FontSize = 26;
            _monthText.FontWeight = FontWeights.Bold;
            _monthText.HorizontalAlignment = HorizontalAlignment.Center;
            _monthText.VerticalAlignment = VerticalAlignment.Center;

            Grid.SetColumn(prevButton, 0);
            Grid.SetColumn(_monthText, 1);
            Grid.SetColumn(nextButton, 2);

            grid.Children.Add(prevButton);
            grid.Children.Add(_monthText);
            grid.Children.Add(nextButton);

            return grid;
        }

        private Button CreateArrowButton(string text)
        {
            return new Button
            {
                Content = text,
                Width = 58,
                Height = 42,
                FontSize = 24,
                FontWeight = FontWeights.Bold
            };
        }

        private UIElement CreateSummaryPanel()
        {
            var grid = new UniformGrid
            {
                Columns = 4,
                Margin = new Thickness(0, 0, 0, 14)
            };

            grid.Children.Add(CreateSummaryCard(
                "Взял",
                _salaryValueText,
                Color.FromRgb(74, 222, 128),
                () =>
                {
                    _section = StatsSection.TakenHistory;
                    Render();

                    var window = new EmployeeSalaryTakenWindow(
                        _employeeName,
                        _monthStart,
                        Render)
                    {
                        Owner = this
                    };

                    window.ShowDialog();
                    Render();
                }));
            grid.Children.Add(CreateSummaryCard("Осталось", _incomeValueText, Color.FromRgb(96, 165, 250)));
            grid.Children.Add(CreateSummaryCard("Премия/бонусы", _timeValueText, Color.FromRgb(250, 204, 21)));
            grid.Children.Add(CreateSummaryCard("Штрафы", _lossValueText, Color.FromRgb(248, 113, 113)));

            return grid;
        }

        private Border CreateSummaryCard(
            string title,
            TextBlock valueText,
            Color valueColor,
            Action? onClick = null)
        {
            var panel = new StackPanel();

            panel.Children.Add(new TextBlock
            {
                Text = title,
                Foreground = new SolidColorBrush(Color.FromRgb(170, 180, 195)),
                FontSize = 14
            });

            valueText.Foreground = new SolidColorBrush(valueColor);
            valueText.FontSize = 22;
            valueText.FontWeight = FontWeights.Bold;
            valueText.TextWrapping = TextWrapping.Wrap;
            valueText.Margin = new Thickness(0, 6, 0, 0);

            panel.Children.Add(valueText);

            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(24, 32, 43)),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(16),
                Margin = new Thickness(0, 0, 10, 0),
                Child = panel
            };

            if (onClick != null)
            {
                border.Cursor = Cursors.Hand;
                border.MouseLeftButtonUp += (_, _) => onClick();
            }

            return border;
        }

        private UIElement CreateSectionButtons()
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 14)
            };

            _salaryButton = CreateSectionButton("Зарплата", StatsSection.Salary);
            _bonusesButton = CreateSectionButton("Бонусы", StatsSection.Bonuses);
            _timeButton = CreateSectionButton("Время", StatsSection.Time);
            _incomeButton = CreateSectionButton("Выручка", StatsSection.Income);
            _productsButton = CreateSectionButton("Товары/услуги", StatsSection.ProductsServices);
            _lossesButton = CreateSectionButton("Штрафы", StatsSection.Losses);

            panel.Children.Add(_salaryButton);
            panel.Children.Add(_bonusesButton);
            panel.Children.Add(_timeButton);
            panel.Children.Add(_incomeButton);
            panel.Children.Add(_productsButton);
            panel.Children.Add(_lossesButton);

            return panel;
        }

        private Button CreateSectionButton(string text, StatsSection section)
        {
            var button = new Button
            {
                Content = text,
                Width = section == StatsSection.ProductsServices ? 170 : 130,
                Height = 40,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 8, 0)
            };

            button.Click += (_, _) =>
            {
                _section = section;
                Render();
            };

            return button;
        }

        private void Render()
        {
            var summary = EmployeeStatsService.GetSummary(_employeeName, _monthStart);
            var autoSalary = AutoSalaryService
                .BuildReport(_monthStart)
                .Employees
                .FirstOrDefault(employee => employee.EmployeeName == _employeeName);

            _monthText.Text = GetMonthTitle(_monthStart);
            _salaryValueText.Text = $"{autoSalary?.PaidAmount ?? 0} сом";
            _incomeValueText.Text = $"{autoSalary?.RemainingAmount ?? 0} сом";
            _timeValueText.Text = $"{(autoSalary?.ProductBonusAmount ?? 0) + (autoSalary?.BonusAmount ?? 0)} сом";
            _lossValueText.Text = $"{autoSalary?.LossesAmount ?? summary.MonthUnpaidLosses} сом";
            _sectionInfoText.Text = GetSectionInfoText(summary, autoSalary);

            UpdateSectionButtonStyles();

            _contentPanel.Children.Clear();

            if (_section == StatsSection.Salary)
            {
                RenderSalarySection(summary, autoSalary);
                return;
            }

            if (_section == StatsSection.TakenHistory)
            {
                RenderSalaryAdvanceHistory();
                return;
            }

            if (_section == StatsSection.Bonuses)
            {
                RenderBonusesSection(autoSalary);
                return;
            }

            if (_section == StatsSection.Time)
            {
                RenderTimeSection();
                return;
            }

            if (_section == StatsSection.Income)
            {
                RenderIncomeSection();
                return;
            }

            if (_section == StatsSection.ProductsServices)
            {
                RenderProductsSection();
                return;
            }

            RenderLossesSection();
        }

        private string GetSectionInfoText(EmployeeStatsSummary summary, AutoSalaryEmployeeResult? autoSalary)
        {
            if (_section == StatsSection.TakenHistory)
                return "История авансов и выдач зарплаты за выбранный месяц.";

            if (_section == StatsSection.Salary)
                return $"Осталось выдать: {autoSalary?.RemainingAmount ?? 0} сом.";

            if (_section == StatsSection.Bonuses)
                return $"Бонусы за выбранный месяц: {(autoSalary?.BonusAmount ?? 0) + (autoSalary?.ProductBonusAmount ?? 0)} сом.";

            if (_section == StatsSection.Time)
                return $"Общее время смен: {EmployeeStatsService.FormatTime(summary.MonthWorkTime)}.";

            if (_section == StatsSection.Income)
                return $"Игровая выручка: {summary.MonthGameIncome} сом. Общая выручка: {summary.MonthTotalIncome} сом.";

            if (_section == StatsSection.ProductsServices)
                return $"Товары/услуги: {summary.MonthProductsIncome} сом. Операций: {summary.ProductServiceOperationsCount}.";

            return $"К удержанию: {summary.MonthUnpaidLosses} сом. Оплачено: {summary.MonthPaidLosses} сом.";
        }

        private void UpdateSectionButtonStyles()
        {
            SetButtonActive(_salaryButton, _section == StatsSection.Salary || _section == StatsSection.TakenHistory);
            SetButtonActive(_bonusesButton, _section == StatsSection.Bonuses);
            SetButtonActive(_timeButton, _section == StatsSection.Time);
            SetButtonActive(_incomeButton, _section == StatsSection.Income);
            SetButtonActive(_productsButton, _section == StatsSection.ProductsServices);
            SetButtonActive(_lossesButton, _section == StatsSection.Losses);
        }

        private void SetButtonActive(Button button, bool isActive)
        {
            button.Background = new SolidColorBrush(isActive
                ? Color.FromRgb(37, 99, 235)
                : Color.FromRgb(51, 65, 85));
            button.Foreground = Brushes.White;
            button.FontWeight = isActive ? FontWeights.Bold : FontWeights.SemiBold;
        }

        private void RenderSalarySection(
            EmployeeStatsSummary summary,
            AutoSalaryEmployeeResult? autoSalary)
        {
            int timeAmount = autoSalary?.TimeAmount ?? 0;
            int gameAmount = autoSalary?.GameRevenueAmount ?? 0;
            int productBonus = autoSalary?.ProductBonusAmount ?? 0;
            int automaticBonuses = autoSalary?.BonusAmount ?? 0;
            int losses = autoSalary?.LossesAmount ?? summary.MonthUnpaidLosses;
            int paid = autoSalary?.PaidAmount ?? 0;
            int gross = autoSalary?.GrossAmount
                ?? timeAmount + gameAmount + productBonus + automaticBonuses;
            int remaining = autoSalary?.RemainingAmount
                ?? gross - paid - losses;

            _contentPanel.Children.Add(CreateCard(new StackPanel
            {
                Children =
                {
                    CreateBigLine("Зарплата за выбранный месяц"),
                    CreateLine($"Общее время: {EmployeeStatsService.FormatTime(summary.MonthWorkTime)}"),
                    CreateLine($"Заработал по времени: {timeAmount} сом"),
                    CreateLine(""),
                    CreateLine($"Общая игровая выручка: {summary.MonthGameIncome} сом"),
                    CreateLine($"Заработал по выручке: {gameAmount} сом"),
                    CreateLine(""),
                    CreateLine($"Товары/услуги: {summary.MonthProductsIncome} сом"),
                    CreateLine($"Бонус за товары/услуги: {productBonus} сом"),
                    CreateLine(""),
                    CreateLine($"Бонусы: {automaticBonuses} сом"),
                    CreateLine($"Всего начислено: {gross} сом"),
                    CreateLine($"Штрафы: -{losses} сом"),
                    CreateLine($"Взял: -{paid} сом"),
                    CreateBigLine($"Итог осталось: {remaining} сом")
                }
            }));

        }

        private void RenderBonusesSection(AutoSalaryEmployeeResult? autoSalary)
        {
            int productBonus = autoSalary?.ProductBonusAmount ?? 0;
            int automaticBonuses = autoSalary?.BonusAmount ?? 0;

            _contentPanel.Children.Add(CreateCard(new StackPanel
            {
                Children =
                {
                    CreateBigLine("Бонусы"),
                    CreateLine($"Бонус за товары/услуги: {productBonus} сом"),
                    CreateLine($"Бонусы по графику и плану: {automaticBonuses} сом"),
                    CreateBigLine($"Итого бонусы: {productBonus + automaticBonuses} сом")
                }
            }));

            var bonuses = autoSalary?.Bonuses ?? new List<AutoSalaryBonusItem>();
            if (bonuses.Count == 0)
            {
                _contentPanel.Children.Add(CreateMutedText("За выбранный месяц отдельных бонусов пока нет."));
                return;
            }

            foreach (var bonus in bonuses)
            {
                _contentPanel.Children.Add(CreateCard(new StackPanel
                {
                    Children =
                    {
                        CreateBigLine($"{bonus.Title}: +{bonus.Amount} сом"),
                        CreateLine(bonus.CreatedAt.ToString("dd.MM.yyyy HH:mm")),
                        CreateDescription(bonus.Description)
                    }
                }));
            }
        }

        private void RenderSalaryAdvanceHistory()
        {
            DateTime nextMonthStart = _monthStart.AddMonths(1);
            var records = CashService
                .GetSalaryRecordsByPeriod(_monthStart, nextMonthStart)
                .Where(record => record.RelatedEmployeeName.Equals(
                    _employeeName,
                    StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(record => record.CreatedAt)
                .ToList();

            _contentPanel.Children.Add(CreateBigLine("История авансов"));

            if (records.Count == 0)
            {
                _contentPanel.Children.Add(CreateMutedText("За выбранный месяц авансов и выдач пока нет."));
                return;
            }

            foreach (var record in records)
            {
                string source = record.EmployeeName == _employeeName
                    ? "Взял сам из кассы"
                    : $"Выдал: {record.EmployeeName}";

                var panel = new StackPanel
                {
                    Children =
                    {
                        CreateBigLine($"{record.CreatedAt:dd.MM.yyyy HH:mm} - {record.Amount} сом"),
                        CreateLine($"Способ: {record.PaymentMethod}"),
                        CreateLine(source)
                    }
                };

                if (!string.IsNullOrWhiteSpace(record.Description))
                    panel.Children.Add(CreateDescription(record.Description));

                _contentPanel.Children.Add(CreateCard(panel));
            }
        }

        private void RenderTimeSection()
        {
            var monthEnd = _monthStart.AddMonths(1);
            var shifts = EmployeeStatsService.GetShifts(_employeeName, _monthStart, monthEnd);

            if (shifts.Count == 0)
            {
                _contentPanel.Children.Add(CreateMutedText("Смен за выбранный месяц нет."));
                return;
            }

            foreach (var shift in shifts)
            {
                string endText = shift.ClosedAt == null
                    ? "смена открыта"
                    : shift.ClosedAt.Value.ToString("dd.MM.yyyy HH:mm");

                _contentPanel.Children.Add(CreateCard(new StackPanel
                {
                    Children =
                    {
                        CreateBigLine($"{shift.StartedAt:dd.MM.yyyy HH:mm}"),
                        CreateLine($"Конец: {endText}"),
                        CreateLine($"Длительность: {EmployeeStatsService.FormatTime(shift.Duration)}"),
                        CreateLine(shift.IsClosed ? "Статус: закрыта" : "Статус: активна")
                    }
                }));
            }
        }

        private void RenderIncomeSection()
        {
            var sessions = EmployeeStatsService.GetGameSessionsForMonth(_employeeName, _monthStart);

            if (sessions.Count == 0)
            {
                _contentPanel.Children.Add(CreateMutedText("Игровой выручки за выбранный месяц нет."));
                return;
            }

            foreach (var session in sessions)
            {
                _contentPanel.Children.Add(CreateCard(new StackPanel
                {
                    Children =
                    {
                        CreateBigLine($"{session.PlaceName} - {session.ClosedAt:dd.MM.yyyy HH:mm}"),
                        CreateLine($"Тариф: {session.TariffText}"),
                        CreateLine($"Игра: {session.GameAmount} сом"),
                        CreateLine($"Товары/услуги в сеансе: {session.ProductsAmount} сом"),
                        CreateLine($"Итого: {session.TotalAmount} сом"),
                        CreateLine($"Закрыл: {session.ClosedByEmployeeName}")
                    }
                }));
            }
        }

        private void RenderProductsSection()
        {
            var items = EmployeeStatsService.GetProductServicesForMonth(_employeeName, _monthStart);

            if (items.Count == 0)
            {
                _contentPanel.Children.Add(CreateMutedText("Товаров/услуг за выбранный месяц нет."));
                return;
            }

            foreach (var item in items)
            {
                string placeText = string.IsNullOrWhiteSpace(item.PlaceName)
                    ? "Без места"
                    : item.PlaceName;

                _contentPanel.Children.Add(CreateCard(new StackPanel
                {
                    Children =
                    {
                        CreateBigLine($"{item.CreatedAt:dd.MM.yyyy HH:mm} - {item.Amount} сом"),
                        CreateLine(item.Title),
                        CreateLine($"Место: {placeText}"),
                        CreateDescription(item.Description)
                    }
                }));
            }
        }

        private void RenderLossesSection()
        {
            var losses = EmployeeStatsService.GetShortagesForMonth(_employeeName, _monthStart);
            if (RenderCleanLossCards(losses))
                return;

            if (losses.Count == 0)
            {
                _contentPanel.Children.Add(CreateMutedText("Штрафов и потерь за выбранный месяц нет."));
                return;
            }

            foreach (var item in losses)
            {
                string status = item.IsPaid ? "Оплачено" : "Не оплачено / к удержанию";

                _contentPanel.Children.Add(CreateCard(new StackPanel
                {
                    Children =
                    {
                        CreateBigLine($"{item.CreatedAt:dd.MM.yyyy HH:mm} - {item.Amount} сом"),
                        CreateLine(item.Title),
                        CreateLine($"Тип: {item.LossType}"),
                        CreateLine($"Статус: {status}"),
                        CreateLine($"Проверил: {item.CheckedByEmployeeName}"),
                        item.PaidAt != null
                            ? CreateLine($"Оплачено: {item.PaidAt.Value:dd.MM.yyyy HH:mm}")
                            : CreateLine(""),
                        CreateDescription(item.Description)
                    }
                }));
            }
        }

        private bool RenderCleanLossCards(List<EmployeeShortageInfo> losses)
        {
            if (losses.Count == 0)
            {
                _contentPanel.Children.Add(CreateMutedText("Штрафов и потерь за выбранный месяц нет."));
                return true;
            }

            foreach (var item in losses)
            {
                string status = item.IsPaid ? "Оплачено" : "К удержанию из зарплаты";

                _contentPanel.Children.Add(CreateCard(new StackPanel
                {
                    Children =
                    {
                        CreateBigLine($"{item.CreatedAt:dd.MM.yyyy HH:mm} - {item.Amount} сом"),
                        CreateLine(item.Title),
                        CreateLine($"Тип: {item.LossType}"),
                        CreateLine($"Статус: {status}"),
                        CreateLine($"Проверил: {item.CheckedByEmployeeName}"),
                        item.PaidAt != null
                            ? CreateLine($"Оплачено: {item.PaidAt.Value:dd.MM.yyyy HH:mm}")
                            : CreateLine(""),
                        CreateDescription(item.Description)
                    }
                }));
            }

            return true;
        }

        private string GetMonthTitle(DateTime month)
        {
            var culture = new CultureInfo("ru-RU");
            string monthName = culture.DateTimeFormat.GetMonthName(month.Month);

            if (string.IsNullOrWhiteSpace(monthName))
                monthName = month.Month.ToString("00");

            monthName = char.ToUpper(monthName[0]) + monthName.Substring(1);

            return $"{monthName} {month.Year}";
        }

        private TextBlock CreateBigLine(string text)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = Brushes.White,
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 7)
            };
        }

        private TextBlock CreateLine(string text)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                FontSize = 15,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 22,
                Margin = new Thickness(0, 0, 0, 4)
            };
        }

        private TextBlock CreateDescription(string text)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = new SolidColorBrush(Color.FromRgb(170, 180, 195)),
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 21,
                Margin = new Thickness(0, 8, 0, 0)
            };
        }

        private TextBlock CreateMutedText(string text)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                FontSize = 15,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 22,
                Margin = new Thickness(0, 0, 0, 8)
            };
        }

        private Border CreateCard(UIElement content)
        {
            return new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(24, 32, 43)),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(16),
                Margin = new Thickness(0, 0, 0, 12),
                Child = content
            };
        }
    }
}
