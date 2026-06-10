using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ClubTimerXbox.Services;

namespace ClubTimerXbox
{
    public class EmployeeStatsWindow : Window
    {
        private readonly string _employeeName;

        public EmployeeStatsWindow(string employeeName)
        {
            _employeeName = employeeName;

            Title = $"Статистика сотрудника: {_employeeName}";
            Width = 940;
            Height = 780;
            MinWidth = 840;
            MinHeight = 660;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromRgb(16, 20, 28));

            Content = CreateContent();
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
                Text = $"Статистика: {_employeeName}",
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

            var tabs = new TabControl
            {
                Background = new SolidColorBrush(Color.FromRgb(16, 20, 28)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(51, 65, 85))
            };

            tabs.Items.Add(CreateTab("Обзор", CreateOverviewContent()));
            tabs.Items.Add(CreateTab("Журнал", CreateJournalContent()));
            tabs.Items.Add(CreateTab("Время", CreateTimeContent()));
            tabs.Items.Add(CreateTab("Выручка", CreateIncomeContent()));
            tabs.Items.Add(CreateTab("Недостачи", CreateShortagesContent()));
            tabs.Items.Add(CreateTab("Зарплата", CreateSalaryContent()));

            root.Children.Add(tabs);

            return root;
        }

        private TabItem CreateTab(string title, UIElement content)
        {
            return new TabItem
            {
                Header = title,
                Content = new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Content = content
                }
            };
        }

        private UIElement CreateOverviewContent()
        {
            var summary = EmployeeStatsService.GetSummary(_employeeName);

            var panel = new StackPanel
            {
                Margin = new Thickness(16)
            };

            panel.Children.Add(CreateHeader("Обзор"));

            panel.Children.Add(CreateCard(new StackPanel
            {
                Children =
                {
                    CreateBigLine("Сегодня"),
                    CreateLine($"Время: {EmployeeStatsService.FormatTime(summary.TodayWorkTime)}"),
                    CreateLine($"Выручка: {summary.TodayTotalIncome} сом"),
                    CreateLine($"Игры: {summary.TodayGameIncome} сом"),
                    CreateLine($"Товары/услуги: {summary.TodayProductsIncome} сом"),
                    CreateLine($"Недостачи: {summary.TodayShortages} сом")
                }
            }));

            panel.Children.Add(CreateCard(new StackPanel
            {
                Children =
                {
                    CreateBigLine("Текущий месяц"),
                    CreateLine($"Время: {EmployeeStatsService.FormatTime(summary.MonthWorkTime)}"),
                    CreateLine($"Выручка: {summary.MonthTotalIncome} сом"),
                    CreateLine($"Игры: {summary.MonthGameIncome} сом"),
                    CreateLine($"Товары/услуги: {summary.MonthProductsIncome} сом"),
                    CreateLine($"Недостачи: {summary.MonthShortages} сом"),
                    CreateLine($"Закрытых игровых сессий: {summary.ClosedGameSessionsCount}"),
                    CreateLine($"Операций товаров/услуг: {summary.ProductServiceOperationsCount}"),
                    CreateLine($"Записей недостач: {summary.ShortageCount}")
                }
            }));

            return panel;
        }

        private UIElement CreateJournalContent()
        {
            var journalItems = EmployeeStatsService.GetJournalForCurrentMonth(_employeeName);

            var panel = new StackPanel
            {
                Margin = new Thickness(16)
            };

            panel.Children.Add(CreateHeader("Журнал сотрудника"));
            panel.Children.Add(CreateMutedText("Показаны действия сотрудника за текущий месяц."));

            if (journalItems.Count == 0)
            {
                panel.Children.Add(CreateMutedText("Действий за текущий месяц пока нет."));
                return panel;
            }

            foreach (var group in journalItems.GroupBy(item => item.CreatedAt.Date).OrderByDescending(group => group.Key))
            {
                panel.Children.Add(CreateSubHeader($"{group.Key:dd.MM.yyyy}"));

                foreach (var item in group.OrderByDescending(x => x.CreatedAt))
                {
                    panel.Children.Add(CreateCard(new StackPanel
                    {
                        Children =
                        {
                            CreateBigLine($"{item.CreatedAt:HH:mm} • {item.Type}"),
                            CreateLine(item.Title),
                            item.Amount > 0
                                ? CreateLine($"Сумма: {item.Amount} сом")
                                : CreateLine(""),
                            CreateDescription(item.Description)
                        }
                    }));
                }
            }

            return panel;
        }

        private UIElement CreateTimeContent()
        {
            var shifts = EmployeeStatsService.GetShifts(_employeeName);

            var panel = new StackPanel
            {
                Margin = new Thickness(16)
            };

            panel.Children.Add(CreateHeader("Время работы"));

            if (shifts.Count == 0)
            {
                panel.Children.Add(CreateMutedText("Смены этого сотрудника пока не найдены."));
                return panel;
            }

            foreach (var shift in shifts.Take(40))
            {
                string endText = shift.ClosedAt == null
                    ? "смена открыта"
                    : shift.ClosedAt.Value.ToString("dd.MM.yyyy HH:mm");

                panel.Children.Add(CreateCard(new StackPanel
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

            return panel;
        }

        private UIElement CreateIncomeContent()
        {
            var summary = EmployeeStatsService.GetSummary(_employeeName);
            var days = EmployeeStatsService.GetDailyIncomeForCurrentMonth(_employeeName);
            var sessions = EmployeeStatsService.GetGameSessionsForCurrentMonth(_employeeName);

            var panel = new StackPanel
            {
                Margin = new Thickness(16)
            };

            panel.Children.Add(CreateHeader("Выручка"));

            panel.Children.Add(CreateCard(new StackPanel
            {
                Children =
                {
                    CreateBigLine("Итого за месяц"),
                    CreateLine($"Игры: {summary.MonthGameIncome} сом"),
                    CreateLine($"Товары/услуги: {summary.MonthProductsIncome} сом"),
                    CreateLine($"Всего: {summary.MonthTotalIncome} сом")
                }
            }));

            panel.Children.Add(CreateSubHeader("По дням"));

            if (days.Count == 0)
            {
                panel.Children.Add(CreateMutedText("Выручки за текущий месяц пока нет."));
            }
            else
            {
                foreach (var day in days)
                {
                    panel.Children.Add(CreateCard(new StackPanel
                    {
                        Children =
                        {
                            CreateBigLine($"{day.Date:dd.MM.yyyy}"),
                            CreateLine($"Игры: {day.GameIncome} сом"),
                            CreateLine($"Товары/услуги: {day.ProductsIncome} сом"),
                            CreateLine($"Итого: {day.TotalIncome} сом")
                        }
                    }));
                }
            }

            panel.Children.Add(CreateSubHeader("Игровые сессии за месяц"));

            if (sessions.Count == 0)
            {
                panel.Children.Add(CreateMutedText("Закрытых игровых сессий пока нет."));
            }
            else
            {
                foreach (var session in sessions.Take(40))
                {
                    panel.Children.Add(CreateCard(new StackPanel
                    {
                        Children =
                        {
                            CreateBigLine($"{session.PlaceName} • {session.ClosedAt:dd.MM.yyyy HH:mm}"),
                            CreateLine($"Тариф: {session.TariffText}"),
                            CreateLine($"Игра: {session.GameAmount} сом"),
                            CreateLine($"Товары/услуги: {session.ProductsAmount} сом"),
                            CreateLine($"Итого: {session.TotalAmount} сом"),
                            CreateLine($"Закрыл: {session.ClosedByEmployeeName}")
                        }
                    }));
                }
            }

            return panel;
        }

        private UIElement CreateShortagesContent()
        {
            var shortages = EmployeeStatsService.GetShortagesForCurrentMonth(_employeeName);
            var summary = EmployeeStatsService.GetSummary(_employeeName);

            var panel = new StackPanel
            {
                Margin = new Thickness(16)
            };

            panel.Children.Add(CreateHeader("Долги / штрафы / недостачи"));

            panel.Children.Add(CreateCard(new StackPanel
            {
                Children =
                {
                    CreateBigLine("Итого за месяц"),
                    CreateLine($"Недостачи: {summary.MonthShortages} сом"),
                    CreateLine($"Количество записей: {summary.ShortageCount}")
                }
            }));

            if (shortages.Count == 0)
            {
                panel.Children.Add(CreateMutedText("Недостач за текущий месяц нет."));
                return panel;
            }

            foreach (var item in shortages)
            {
                panel.Children.Add(CreateCard(new StackPanel
                {
                    Children =
                    {
                        CreateBigLine($"{item.CreatedAt:dd.MM.yyyy HH:mm} • {item.Amount} сом"),
                        CreateLine(item.Title),
                        CreateLine($"Проверил/оформил: {item.CheckedByEmployeeName}"),
                        CreateDescription(item.Description)
                    }
                }));
            }

            return panel;
        }

        private UIElement CreateSalaryContent()
        {
            var summary = EmployeeStatsService.GetSummary(_employeeName);

            var panel = new StackPanel
            {
                Margin = new Thickness(16)
            };

            panel.Children.Add(CreateHeader("Зарплата"));

            panel.Children.Add(CreateCard(new StackPanel
            {
                Children =
                {
                    CreateBigLine("Будущий расчёт зарплаты"),
                    CreateLine($"Время за месяц: {EmployeeStatsService.FormatTime(summary.MonthWorkTime)}"),
                    CreateLine($"Выручка за месяц: {summary.MonthTotalIncome} сом"),
                    CreateLine($"Недостачи за месяц: {summary.MonthShortages} сом"),
                    CreateMutedText("Формулу зарплаты добавим позже в настройках владельца: ставка за час, процент от выручки, бонусы, штрафы и недостачи.")
                }
            }));

            return panel;
        }

        private TextBlock CreateHeader(string text)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = Brushes.White,
                FontSize = 26,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 14)
            };
        }

        private TextBlock CreateSubHeader(string text)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = Brushes.White,
                FontSize = 22,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 18, 0, 12)
            };
        }

        private TextBlock CreateBigLine(string text)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = Brushes.White,
                FontSize = 19,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 8)
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