using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ClubTimerXbox.Models;
using ClubTimerXbox.Services;

namespace ClubTimerXbox
{
    public partial class CashReportWindow : Window
    {
        private readonly CashReportFilter _filter = new CashReportFilter();

        public CashReportWindow()
        {
            InitializeComponent();

            _filter.Section = CashReportSection.Games;
            _filter.PeriodMode = CashReportPeriodMode.Day;
            _filter.ViewMode = CashReportViewMode.Records;
            _filter.SelectedDay = DateTime.Today;
            _filter.SelectedYear = DateTime.Today.Year;
            _filter.SelectedMonth = DateTime.Today.Month;
            _filter.PeriodStart = DateTime.Today;
            _filter.PeriodEnd = DateTime.Today;

            Render();
        }

        private void Render()
        {
            NormalizeViewMode();
            UpdateButtonTexts();
            UpdateButtonStyles();

            var report = CashReportService.BuildReport(_filter);

            RenderSummary(report.Summary);
            RenderRows(report);
        }

        private void NormalizeViewMode()
        {
            if (_filter.PeriodMode == CashReportPeriodMode.Day &&
                _filter.ViewMode == CashReportViewMode.Days)
            {
                _filter.ViewMode = CashReportViewMode.Records;
            }

            if (_filter.Section == CashReportSection.Games)
            {
                if (_filter.ViewMode == CashReportViewMode.Items ||
                    _filter.ViewMode == CashReportViewMode.Categories)
                {
                    _filter.ViewMode = CashReportViewMode.Records;
                }
            }

            if (_filter.Section == CashReportSection.ProductsAndServices)
            {
                if (_filter.ViewMode == CashReportViewMode.Places ||
                    _filter.ViewMode == CashReportViewMode.Categories)
                {
                    _filter.ViewMode = CashReportViewMode.Records;
                }
            }

            if (_filter.Section == CashReportSection.Expenses)
            {
                if (_filter.ViewMode == CashReportViewMode.Places ||
                    _filter.ViewMode == CashReportViewMode.Items)
                {
                    _filter.ViewMode = CashReportViewMode.Records;
                }
            }
        }

        private void UpdateButtonTexts()
        {
            DayPeriodButton.Content = $"День: {_filter.SelectedDay:dd.MM.yyyy}";
            MonthPeriodButton.Content = $"Месяц: {GetMonthTitle(_filter.SelectedYear, _filter.SelectedMonth)}";
            CustomPeriodButton.Content = $"Период: {_filter.PeriodStart:dd.MM}–{_filter.PeriodEnd:dd.MM}";

            DaysViewButton.Visibility =
                _filter.PeriodMode == CashReportPeriodMode.Day
                    ? Visibility.Collapsed
                    : Visibility.Visible;

            if (_filter.Section == CashReportSection.Games)
            {
                MainGroupViewButton.Visibility = Visibility.Visible;
                MainGroupViewButton.Content = "ТВ/Рули";
            }
            else if (_filter.Section == CashReportSection.ProductsAndServices)
            {
                MainGroupViewButton.Visibility = Visibility.Visible;
                MainGroupViewButton.Content = "Товары";
            }
            else if (_filter.Section == CashReportSection.Expenses)
            {
                MainGroupViewButton.Visibility = Visibility.Visible;
                MainGroupViewButton.Content = "Категории";
            }
            else
            {
                MainGroupViewButton.Visibility = Visibility.Collapsed;
            }
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

        private void UpdateButtonStyles()
        {
            SetButtonActive(GamesSectionButton, _filter.Section == CashReportSection.Games);
            SetButtonActive(ProductsSectionButton, _filter.Section == CashReportSection.ProductsAndServices);
            SetButtonActive(EmployeesSectionButton, _filter.Section == CashReportSection.Employees);
            SetButtonActive(ExpensesSectionButton, _filter.Section == CashReportSection.Expenses);

            SetButtonActive(DayPeriodButton, _filter.PeriodMode == CashReportPeriodMode.Day);
            SetButtonActive(MonthPeriodButton, _filter.PeriodMode == CashReportPeriodMode.Month);
            SetButtonActive(CustomPeriodButton, _filter.PeriodMode == CashReportPeriodMode.CustomPeriod);

            SetButtonActive(RecordsViewButton, _filter.ViewMode == CashReportViewMode.Records);
            SetButtonActive(DaysViewButton, _filter.ViewMode == CashReportViewMode.Days);
            SetButtonActive(MainGroupViewButton,
                _filter.ViewMode == CashReportViewMode.Places ||
                _filter.ViewMode == CashReportViewMode.Items ||
                _filter.ViewMode == CashReportViewMode.Categories);
            SetButtonActive(EmployeesViewButton, _filter.ViewMode == CashReportViewMode.Employees);
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

        private void RenderSummary(CashReportSummary summary)
        {
            SummaryTitleText.Text = summary.Title;
            SummaryCountText.Text = $"Записей: {summary.RecordsCount}";

            SummaryTotalText.Text = $"{summary.TotalAmount} сом";
            SummaryCashText.Text = $"{summary.CashAmount} сом";
            SummaryMBankText.Text = $"{summary.MBankAmount} сом";
        }

        private void RenderRows(CashReportResult report)
        {
            RowsPanel.Children.Clear();

            if (report.Rows.Count == 0)
            {
                RowsPanel.Children.Add(new TextBlock
                {
                    Text = "За выбранный период записей нет.",
                    Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                    FontSize = 16,
                    Margin = new Thickness(8)
                });

                BottomInfoText.Text = "Записей: 0";
                return;
            }

            foreach (var row in report.Rows)
            {
                RowsPanel.Children.Add(CreateRowCard(row));
            }

            BottomInfoText.Text = $"Записей: {report.Rows.Count}";
        }

        private Border CreateRowCard(CashReportRow row)
        {
            var root = new Grid();

            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var left = new StackPanel();

            left.Children.Add(new TextBlock
            {
                Text = row.Title,
                Foreground = Brushes.White,
                FontSize = 17,
                FontWeight = FontWeights.Bold,
                TextWrapping = TextWrapping.Wrap
            });

            if (!string.IsNullOrWhiteSpace(row.Subtitle))
            {
                left.Children.Add(new TextBlock
                {
                    Text = row.Subtitle,
                    Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                    FontSize = 13,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 5, 0, 0)
                });
            }

            if (!string.IsNullOrWhiteSpace(row.TimeText))
            {
                left.Children.Add(new TextBlock
                {
                    Text = row.TimeText,
                    Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                    FontSize = 12,
                    Margin = new Thickness(0, 5, 0, 0)
                });
            }

            if (!string.IsNullOrWhiteSpace(row.EmployeeName))
            {
                left.Children.Add(new TextBlock
                {
                    Text = $"Админ: {row.EmployeeName}",
                    Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                    FontSize = 12,
                    Margin = new Thickness(0, 5, 0, 0)
                });
            }

            if (!string.IsNullOrWhiteSpace(row.PlaceName))
            {
                left.Children.Add(new TextBlock
                {
                    Text = $"Место: {row.PlaceName}",
                    Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                    FontSize = 12,
                    Margin = new Thickness(0, 3, 0, 0)
                });
            }

            var right = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Right
            };

            right.Children.Add(new TextBlock
            {
                Text = $"{row.TotalAmount} сом",
                Foreground = new SolidColorBrush(Color.FromRgb(74, 222, 128)),
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Right
            });

            right.Children.Add(new TextBlock
            {
                Text = $"Наличные: {row.CashAmount} сом",
                Foreground = Brushes.White,
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 5, 0, 0)
            });

            right.Children.Add(new TextBlock
            {
                Text = $"М Банк: {row.MBankAmount} сом",
                Foreground = Brushes.White,
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 3, 0, 0)
            });

            Grid.SetColumn(left, 0);
            Grid.SetColumn(right, 1);

            root.Children.Add(left);
            root.Children.Add(right);

            return new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(24, 32, 43)),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(14),
                Margin = new Thickness(0, 0, 0, 10),
                Child = root
            };
        }

        private void GamesSectionButton_Click(object sender, RoutedEventArgs e)
        {
            _filter.Section = CashReportSection.Games;
            _filter.ViewMode = CashReportViewMode.Records;
            Render();
        }

        private void ProductsSectionButton_Click(object sender, RoutedEventArgs e)
        {
            _filter.Section = CashReportSection.ProductsAndServices;
            _filter.ViewMode = CashReportViewMode.Records;
            Render();
        }

        private void EmployeesSectionButton_Click(object sender, RoutedEventArgs e)
        {
            _filter.Section = CashReportSection.Employees;
            _filter.ViewMode = CashReportViewMode.Employees;
            Render();
        }

        private void ExpensesSectionButton_Click(object sender, RoutedEventArgs e)
        {
            _filter.Section = CashReportSection.Expenses;
            _filter.ViewMode = CashReportViewMode.Records;
            Render();
        }

        private void DayPeriodButton_Click(object sender, RoutedEventArgs e)
        {
            var window = new DatePickerWindow(_filter.SelectedDay)
            {
                Owner = this
            };

            bool? result = window.ShowDialog();

            if (result != true)
                return;

            _filter.SelectedDay = window.SelectedDate;
            _filter.PeriodMode = CashReportPeriodMode.Day;
            _filter.ViewMode = CashReportViewMode.Records;

            Render();
        }

        private void MonthPeriodButton_Click(object sender, RoutedEventArgs e)
        {
            var window = new MonthPickerWindow(_filter.SelectedYear, _filter.SelectedMonth)
            {
                Owner = this
            };

            bool? result = window.ShowDialog();

            if (result != true)
                return;

            _filter.SelectedYear = window.SelectedYear;
            _filter.SelectedMonth = window.SelectedMonth;
            _filter.PeriodMode = CashReportPeriodMode.Month;

            Render();
        }

        private void CustomPeriodButton_Click(object sender, RoutedEventArgs e)
        {
            var window = new PeriodPickerWindow(_filter.PeriodStart, _filter.PeriodEnd)
            {
                Owner = this
            };

            bool? result = window.ShowDialog();

            if (result != true)
                return;

            _filter.PeriodStart = window.StartDate;
            _filter.PeriodEnd = window.EndDate;
            _filter.PeriodMode = CashReportPeriodMode.CustomPeriod;

            Render();
        }

        private void RecordsViewButton_Click(object sender, RoutedEventArgs e)
        {
            _filter.ViewMode = CashReportViewMode.Records;
            Render();
        }

        private void DaysViewButton_Click(object sender, RoutedEventArgs e)
        {
            _filter.ViewMode = CashReportViewMode.Days;
            Render();
        }

        private void MainGroupViewButton_Click(object sender, RoutedEventArgs e)
        {
            if (_filter.Section == CashReportSection.Games)
                _filter.ViewMode = CashReportViewMode.Places;
            else if (_filter.Section == CashReportSection.ProductsAndServices)
                _filter.ViewMode = CashReportViewMode.Items;
            else if (_filter.Section == CashReportSection.Expenses)
                _filter.ViewMode = CashReportViewMode.Categories;
            else
                _filter.ViewMode = CashReportViewMode.Records;

            Render();
        }

        private void EmployeesViewButton_Click(object sender, RoutedEventArgs e)
        {
            _filter.ViewMode = CashReportViewMode.Employees;
            Render();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}