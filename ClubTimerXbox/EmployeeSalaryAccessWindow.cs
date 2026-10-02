using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ClubTimerXbox.Models;
using ClubTimerXbox.Services;

namespace ClubTimerXbox
{
    public sealed class EmployeeSalaryAccessWindow : AdaptiveDialogWindow
    {
        private readonly PasswordBox _pin = new();
        private readonly TextBlock _error = new();
        private static int _failedAttempts;
        private static long _retryAfter;
        public Employee? IdentifiedEmployee { get; private set; }

        public EmployeeSalaryAccessWindow() : base(440, 360)
        {
            Title = "Зарплата по личному коду";
            var panel = new StackPanel { Margin = new Thickness(22) };
            panel.Children.Add(new TextBlock
            {
                Text = "Код сотрудника", FontSize = 22, FontWeight = FontWeights.Bold,
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16)
            });
            _pin.FontSize = 24;
            _pin.MinHeight = 44;
            _pin.Padding = new Thickness(10, 5, 10, 5);
            _pin.KeyDown += (_, e) => { if (e.Key == Key.Enter) Confirm(); };
            panel.Children.Add(_pin);
            _error.Foreground = new SolidColorBrush(Color.FromRgb(248, 113, 113));
            _error.TextWrapping = TextWrapping.Wrap;
            _error.Margin = new Thickness(0, 12, 0, 12);
            panel.Children.Add(_error);
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            var cancel = new Button { Content = "Отмена", MinWidth = 100, MinHeight = 38,
                Margin = new Thickness(0, 0, 8, 0) };
            cancel.Click += (_, _) => DialogResult = false;
            var open = new Button { Content = "Открыть", MinWidth = 100, MinHeight = 38 };
            open.Click += (_, _) => Confirm();
            buttons.Children.Add(cancel);
            buttons.Children.Add(open);
            panel.Children.Add(buttons);
            SetAdaptiveContent(panel);
            Loaded += (_, _) => _pin.Focus();
        }

        private void Confirm()
        {
            if (Environment.TickCount64 < _retryAfter)
            {
                _error.Text = "Слишком много попыток. Попробуйте немного позже.";
                _pin.Clear();
                return;
            }
            Employee? employee = EmployeeSalaryAccessService.Identify(
                EmployeeService.Employees, _pin.Password);
            _pin.Clear();
            if (employee == null)
            {
                _failedAttempts++;
                if (_failedAttempts >= 5)
                {
                    _failedAttempts = 0;
                    _retryAfter = Environment.TickCount64 + 30000;
                }
                _error.Text = "Не удалось определить сотрудника. Проверьте личный код.";
                _pin.Focus();
                return;
            }
            _failedAttempts = 0;
            IdentifiedEmployee = employee;
            DialogResult = true;
        }
    }
}
