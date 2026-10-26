using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ClubTimerXbox.Services;

namespace ClubTimerXbox
{
    public class EmployeeSalaryTakenWindow : AdaptiveDialogWindow
    {
        private const int CashReserveAmount = 1000;

        private readonly string _employeeName;
        private readonly DateTime _monthStart;
        private readonly Action? _onChanged;
        private readonly EmployeeSalaryAccessSession? _accessSession;
        private readonly string _operationId;
        private bool _isPaying;
        private readonly Func<(int Salary, int DebtCredit)>? _availabilityProvider;
        private readonly Func<int?>? _cashBalanceProvider;
        private readonly TextBlock _remainingText = new TextBlock();
        private readonly TextBlock _salaryBalanceText = new TextBlock();
        private readonly TextBlock _debtCreditText = new TextBlock();
        private readonly TextBlock _cashLimitText = new TextBlock();
        private readonly TextBox _amountBox = new TextBox();

        public EmployeeSalaryTakenWindow(
            string employeeName,
            DateTime monthStart,
            Action? onChanged = null,
            EmployeeSalaryAccessSession? accessSession = null)
            : this(employeeName, monthStart, onChanged, accessSession, null, null)
        {
        }

        internal EmployeeSalaryTakenWindow(
            string employeeName,
            DateTime monthStart,
            Action? onChanged,
            EmployeeSalaryAccessSession? accessSession,
            Func<(int Salary, int DebtCredit)>? availabilityProvider,
            Func<int?>? cashBalanceProvider)
            : base(430, 360)
        {
            _employeeName = employeeName;
            _monthStart = BusinessCalendarService
                .GetBusinessMonthByAnchor(monthStart)
                .StartInclusive;
            _onChanged = onChanged;
            _accessSession = accessSession;
            _operationId = accessSession?.PayoutOperationId ?? $"employee-salary:{Guid.NewGuid():N}";
            _availabilityProvider = availabilityProvider;
            _cashBalanceProvider = cashBalanceProvider;

            Title = accessSession == null ? "Взять аванс" : "Зарплата";
            SetAdaptiveContent(CreateContent());
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
                Text = $"{(_accessSession == null ? "Аванс" : "Зарплата")}: {_employeeName}",
                Foreground = Brushes.White,
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            });

            if (_accessSession != null)
                panel.Children.Add(new TextBlock
                {
                    Text = $"Администратор на смене: {_accessSession.DutyEmployeeName}",
                    Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                    FontSize = 15, TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 10)
                });

            _remainingText.Foreground = new SolidColorBrush(Color.FromRgb(96, 165, 250));
            _remainingText.FontSize = 18;
            _remainingText.FontWeight = FontWeights.Bold;
            _remainingText.TextWrapping = TextWrapping.Wrap;
            _remainingText.Margin = new Thickness(0, 0, 0, 10);
            panel.Children.Add(_remainingText);

            _salaryBalanceText.FontSize = 16;
            _salaryBalanceText.TextWrapping = TextWrapping.Wrap;
            _salaryBalanceText.Margin = new Thickness(0, 0, 0, 6);
            panel.Children.Add(_salaryBalanceText);

            _debtCreditText.Foreground = new SolidColorBrush(Color.FromRgb(74, 222, 128));
            _debtCreditText.FontSize = 16;
            _debtCreditText.FontWeight = FontWeights.SemiBold;
            _debtCreditText.TextWrapping = TextWrapping.Wrap;
            _debtCreditText.Margin = new Thickness(0, 0, 0, 10);
            panel.Children.Add(_debtCreditText);

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
            NumericInputHint.Apply(_amountBox, "Сколько хотите взять?");
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
            okButton.Click += (_, _) =>
            {
                if (_isPaying)
                    return;
                _isPaying = true;
                okButton.IsEnabled = false;
                try
                {
                    TakeAdvance();
                }
                catch (Exception error)
                {
                    MessageBox.Show(error.Message, "Выплата зарплаты",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                finally
                {
                    _isPaying = false;
                    okButton.IsEnabled = true;
                }
            };

            buttons.Children.Add(cancelButton);
            buttons.Children.Add(okButton);
            panel.Children.Add(buttons);
            DialogKeyboard.Configure(this, _amountBox, okButton);

            return panel;
        }

        private void Render()
        {
            var availability = GetAvailability();
            int remaining = Math.Max(0, availability.Salary) + availability.DebtCredit;
            bool hasDebtCredit = availability.DebtCredit > 0;
            int displayedTotal = hasDebtCredit ? remaining : availability.Salary;
            _remainingText.Text = $"Доступно: {displayedTotal} сом";
            _remainingText.Foreground = new SolidColorBrush(displayedTotal < 0
                ? Color.FromRgb(248, 113, 113)
                : displayedTotal > 0
                    ? Color.FromRgb(74, 222, 128)
                    : Color.FromRgb(229, 231, 235));
            _salaryBalanceText.Visibility = hasDebtCredit ? Visibility.Visible : Visibility.Collapsed;
            _salaryBalanceText.Text = hasDebtCredit ? $"ЗП остаток: {availability.Salary} сом" : "";
            _salaryBalanceText.Foreground = new SolidColorBrush(availability.Salary < 0
                ? Color.FromRgb(248, 113, 113)
                : Color.FromRgb(203, 213, 225));
            _debtCreditText.Visibility = hasDebtCredit
                ? Visibility.Visible
                : Visibility.Collapsed;
            _debtCreditText.Text = hasDebtCredit
                ? $"Из долгов: {availability.DebtCredit} сом"
                : "";

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
                    ? "Можно получить всю доступную сумму."
                    : maxAdvance > 0
                        ? "Сейчас доступна частичная выдача."
                        : "Сейчас аванс наличными недоступен.");

            _cashLimitText.Foreground = maxAdvance > 0
                ? new SolidColorBrush(Color.FromRgb(203, 213, 225))
                : new SolidColorBrush(Color.FromRgb(248, 113, 113));
        }

        private int GetRemainingAmount()
        {
            var availability = GetAvailability();
            return Math.Max(0, availability.Salary) + availability.DebtCredit;
        }

        private (int Salary, int DebtCredit) GetAvailability()
        {
            if (_availabilityProvider != null)
                return _availabilityProvider();
            var employee = _accessSession == null
                ? EmployeeService.FindByName(_employeeName)
                : EmployeeService.FindById(_accessSession.EmployeeId);
            var employeeSalary = AutoSalaryService
                .BuildReport(_monthStart)
                .Employees
                .FirstOrDefault(item => employee != null && item.EmployeeId == employee.EmployeeId);

            return (
                employeeSalary?.RemainingAmount ?? 0,
                employee == null || !BusinessCalendarService.GetBusinessMonth(_monthStart).Key.Equals(
                    BusinessCalendarService.GetBusinessMonth(ClubClock.Current.LocalNow).Key,
                    StringComparison.OrdinalIgnoreCase)
                    ? 0
                    : EmployeeDebtService.GetAvailableCredit(employee.EmployeeId));
        }

        private int? GetCurrentActualCashBalance()
        {
            if (_cashBalanceProvider != null)
                return _cashBalanceProvider();
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
            if (_accessSession != null)
                EmployeeSalaryAccessService.Validate(
                    _accessSession,
                    EmployeeService.FindById(_accessSession.EmployeeId),
                    EmployeeService.CurrentEmployee,
                    ActionLogService.CurrentShift);
            if (ShiftAcceptanceService.IsAcceptanceRequired())
                throw new InvalidOperationException("Сначала завершите приёмку смены.");
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

            var employee = (_accessSession == null
                ? EmployeeService.FindByName(_employeeName)
                : EmployeeService.FindById(_accessSession.EmployeeId))
                           ?? throw new InvalidOperationException("Сотрудник не найден.");
            EmployeePayoutResult payout = EmployeePayoutService.Pay(
                actorName: employee.Name,
                employeeId: employee.EmployeeId,
                amount: amount,
                paymentMethod: "Наличные",
                description: _accessSession == null
                    ? "Аванс наличными из кассы сотрудником"
                    : EmployeeSalaryAccessService.Describe(_accessSession),
                throughMonthKey: _monthStart.ToString("yyyy-MM"),
                operationId: _operationId
            );

            EmployeeCashPayoutNotice.ShowAfterPayment(this, employee.Name, payout.TotalAmount);

            // Finish the paid form even if a later refresh/notification fails.
            Close();

            _ = FirebaseEventService.PublishSalaryTakenCashAsync(
                _operationId,
                employee.Name,
                payout.TotalAmount);

            _onChanged?.Invoke();
        }
    }
}
