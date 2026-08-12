using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ClubTimerXbox.Services;

namespace ClubTimerXbox
{
    public class EmployeeSalaryTakenWindow : Window
    {
        private const int CashReserveAmount = 1000;

        private readonly string _employeeName;
        private readonly DateTime _monthStart;
        private readonly Action? _onChanged;
        private readonly TextBlock _remainingText = new TextBlock();
        private readonly TextBlock _cashLimitText = new TextBlock();
        private readonly TextBox _amountBox = new TextBox();

        public EmployeeSalaryTakenWindow(
            string employeeName,
            DateTime monthStart,
            Action? onChanged = null)
        {
            _employeeName = employeeName;
            _monthStart = BusinessCalendarService
                .GetBusinessMonthByAnchor(monthStart)
                .StartInclusive;
            _onChanged = onChanged;

            Title = "Взять аванс";
            Width = 430;
            Height = 365;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            Background = new SolidColorBrush(Color.FromRgb(16, 20, 28));

            Content = CreateContent();
            Render();
        }

        private UIElement CreateContent()
        {
            var panel = new StackPanel
            {
                Margin = new Thickness(18)
            };

            panel.Children.Add(new TextBlock
            {
                Text = $"Аванс: {_employeeName}",
                Foreground = Brushes.White,
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 10)
            });

            _remainingText.Foreground = new SolidColorBrush(Color.FromRgb(96, 165, 250));
            _remainingText.FontSize = 18;
            _remainingText.FontWeight = FontWeights.Bold;
            _remainingText.Margin = new Thickness(0, 0, 0, 10);
            panel.Children.Add(_remainingText);

            _cashLimitText.Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225));
            _cashLimitText.FontSize = 15;
            _cashLimitText.TextWrapping = TextWrapping.Wrap;
            _cashLimitText.Margin = new Thickness(0, 0, 0, 14);
            panel.Children.Add(_cashLimitText);

            panel.Children.Add(new TextBlock
            {
                Text = "Сколько хотите взять?",
                Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                FontSize = 15,
                Margin = new Thickness(0, 0, 0, 6)
            });

            _amountBox.Height = 38;
            _amountBox.FontSize = 18;
            _amountBox.Padding = new Thickness(10, 4, 10, 4);
            _amountBox.Margin = new Thickness(0, 0, 0, 14);
            panel.Children.Add(_amountBox);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var cancelButton = new Button
            {
                Content = "Отмена",
                Width = 100,
                Height = 38,
                Margin = new Thickness(0, 0, 8, 0)
            };
            cancelButton.Click += (_, _) => Close();

            var okButton = new Button
            {
                Content = "ОК",
                Width = 100,
                Height = 38,
                Background = new SolidColorBrush(Color.FromRgb(22, 101, 52)),
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold
            };
            okButton.Click += (_, _) => TakeAdvance();

            buttons.Children.Add(cancelButton);
            buttons.Children.Add(okButton);
            panel.Children.Add(buttons);

            return panel;
        }

        private void Render()
        {
            int remaining = GetRemainingAmount();
            _remainingText.Text = $"Осталось: {remaining} сом";

            int? currentCash = GetCurrentActualCashBalance();

            if (!currentCash.HasValue)
            {
                _cashLimitText.Text =
                    "Наличка в кассе ещё не принята. Сначала завершите приёмку налички.";
                _cashLimitText.Foreground = new SolidColorBrush(Color.FromRgb(248, 113, 113));
                return;
            }

            int availableCash = GetAvailableCashForAdvance(currentCash.Value);
            int maxAdvance = Math.Min(remaining, availableCash);

            _cashLimitText.Text =
                $"Наличные для аванса: " +
                $"{EmployeeCashPrivacyService.GetAvailableCashBand(currentCash.Value, CashReserveAmount)}\n" +
                (maxAdvance >= remaining && remaining > 0
                    ? "Можно получить всю оставшуюся зарплату."
                    : maxAdvance > 0
                        ? "Сейчас доступна частичная выдача."
                        : "Сейчас аванс наличными недоступен.");

            _cashLimitText.Foreground = maxAdvance > 0
                ? new SolidColorBrush(Color.FromRgb(203, 213, 225))
                : new SolidColorBrush(Color.FromRgb(248, 113, 113));
        }

        private int GetRemainingAmount()
        {
            var employeeSalary = AutoSalaryService
                .BuildReport(_monthStart)
                .Employees
                .FirstOrDefault(employee => employee.EmployeeName == _employeeName);

            return employeeSalary?.RemainingAmount ?? 0;
        }

        private int? GetCurrentActualCashBalance()
        {
            DateTime nextMonthStart = _monthStart.AddMonths(1);
            return CashBalanceSummaryService.CalculateActualCashBalanceByPeriod(
                _monthStart,
                nextMonthStart);
        }

        private static int GetAvailableCashForAdvance(int currentCash)
        {
            return Math.Max(0, currentCash - CashReserveAmount);
        }

        private void TakeAdvance()
        {
            int remaining = GetRemainingAmount();

            if (remaining <= 0)
            {
                MessageBox.Show(
                    "У вас нет остатка зарплаты для аванса.",
                    "Взять аванс",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            if (!BusinessCalendarService.GetBusinessMonth(_monthStart).Key.Equals(
                    BusinessCalendarService.GetBusinessMonth(ClubClock.Current.LocalNow).Key,
                    StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(
                    "Аванс можно взять только за текущий месяц.",
                    "Взять аванс",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            int? currentCash = GetCurrentActualCashBalance();

            if (!currentCash.HasValue)
            {
                MessageBox.Show(
                    "Сначала завершите приёмку налички.\n\n" +
                    "Система должна знать фактическую сумму в кассе, иначе аванс наличными брать нельзя.",
                    "Взять аванс",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            int availableCash = GetAvailableCashForAdvance(currentCash.Value);

            if (availableCash <= 0)
            {
                MessageBox.Show(
                    "Сейчас аванс наличными недоступен.",
                    "Взять аванс",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(_amountBox.Text.Trim(), out int amount) || amount <= 0)
            {
                MessageBox.Show("Введите сумму больше 0.", "Взять аванс");
                return;
            }

            if (amount > remaining)
            {
                MessageBox.Show(
                    $"Нельзя взять больше остатка: {remaining} сом.",
                    "Взять аванс");
                return;
            }

            if (amount > availableCash)
            {
                MessageBox.Show(
                    "Указанная сумма сейчас недоступна.\n\n" +
                    "Введите меньшую сумму.",
                    "Взять аванс",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            string operationId = $"employee-salary:{Guid.NewGuid():N}";
            BusinessAccountingService.PaySalaryFifo(
                ownerName: _employeeName,
                employeeName: _employeeName,
                amount: amount,
                paymentMethod: "Наличные",
                description: "Аванс наличными из кассы сотрудником",
                throughMonthKey: _monthStart.ToString("yyyy-MM"),
                operationId: operationId
            );

            _ = FirebaseEventService.PublishSalaryTakenCashAsync(
                operationId,
                _employeeName,
                amount);

            _onChanged?.Invoke();
            Close();
        }
    }
}
