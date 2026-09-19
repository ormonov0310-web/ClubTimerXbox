using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ClubTimerXbox.Services;

namespace ClubTimerXbox
{
    public enum EmployeeDebtFreezeWindowMode
    {
        Request,
        Pending,
        Rejected,
        Paused
    }

    public sealed class EmployeeDebtFreezeWindow : AdaptiveDialogWindow
    {
        private readonly EmployeeDebtFreezeWindowMode _mode;
        private readonly DateTime? _deadline;
        private readonly TextBlock _statusText = new();
        private readonly DispatcherTimer _timer = new()
        {
            Interval = TimeSpan.FromSeconds(1)
        };

        public int SelectedDurationDays { get; private set; }

        public bool ResumeRequested { get; private set; }

        public EmployeeDebtFreezeWindow(
            EmployeeDebtFreezeWindowMode mode,
            int durationDays = 0,
            DateTime? requestedAt = null,
            DateTime? deadline = null)
            : base(455, 430)
        {
            _mode = mode;
            _deadline = deadline;
            Title = mode switch
            {
                EmployeeDebtFreezeWindowMode.Request => "Запросить заморозку",
                EmployeeDebtFreezeWindowMode.Pending => "Запрос ожидается",
                EmployeeDebtFreezeWindowMode.Rejected => "Запрос отказан",
                _ => "Заморозка активна"
            };
            var panel = new StackPanel { Margin = new Thickness(22) };
            panel.Children.Add(new TextBlock
            {
                Text = BuildHeading(mode, durationDays, requestedAt),
                Foreground = Brushes.White,
                FontSize = 19,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12)
            });

            _statusText.Foreground = new SolidColorBrush(Color.FromRgb(209, 213, 219));
            _statusText.FontSize = 16;
            _statusText.TextWrapping = TextWrapping.Wrap;
            _statusText.Margin = new Thickness(0, 0, 0, 18);
            panel.Children.Add(_statusText);

            if (mode == EmployeeDebtFreezeWindowMode.Request)
            {
                _statusText.Text =
                    "До подтверждения кредитором автоматическое погашение продолжит работать. " +
                    "Срок начнётся в момент подтверждения.";
                panel.Children.Add(CreateDurationButtons());
            }
            else
            {
                panel.Children.Add(CreateFooterButtons());
                RefreshStatus();
                if (mode is EmployeeDebtFreezeWindowMode.Rejected or EmployeeDebtFreezeWindowMode.Paused)
                {
                    _timer.Tick += (_, _) => RefreshStatus();
                    _timer.Start();
                }
            }

            SetAdaptiveContent(panel);
            Closed += (_, _) => _timer.Stop();
        }

        private static string BuildHeading(
            EmployeeDebtFreezeWindowMode mode,
            int durationDays,
            DateTime? requestedAt)
        {
            return mode switch
            {
                EmployeeDebtFreezeWindowMode.Request => "На сколько дней остановить автооплату?",
                EmployeeDebtFreezeWindowMode.Pending =>
                    $"Запрошена заморозка на {durationDays} дн." +
                    (requestedAt.HasValue ? $"\nОтправлено {requestedAt:dd.MM.yyyy HH:mm}." : ""),
                EmployeeDebtFreezeWindowMode.Rejected => "Кредитор отклонил последний запрос.",
                _ => durationDays > 0
                    ? $"Автоматическое погашение заморожено на {durationDays} дн."
                    : "Автоматическое погашение заморожено."
            };
        }

        private UIElement CreateDurationButtons()
        {
            var root = new StackPanel();
            var container = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 0)
            };
            foreach (int days in EmployeeDebtFreezePolicy.AllowedDurations)
            {
                var button = new Button
                {
                    Content = $"{days} дн.",
                    Width = 88,
                    Height = 42,
                    Margin = new Thickness(4),
                    Background = new SolidColorBrush(Color.FromRgb(75, 85, 99)),
                    Foreground = Brushes.White,
                    FontWeight = FontWeights.SemiBold
                };
                button.Click += (_, _) =>
                {
                    SelectedDurationDays = days;
                    DialogResult = true;
                };
                container.Children.Add(button);
            }
            root.Children.Add(container);
            var cancel = new Button
            {
                Content = "Отмена",
                Width = 110,
                Height = 38,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 10, 4, 0)
            };
            cancel.Click += (_, _) => DialogResult = false;
            root.Children.Add(cancel);
            return root;
        }

        private UIElement CreateFooterButtons()
        {
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            var close = new Button
            {
                Content = "Закрыть",
                Width = 110,
                Height = 40,
                Margin = new Thickness(0, 0, 8, 0)
            };
            close.Click += (_, _) => DialogResult = false;
            buttons.Children.Add(close);

            if (_mode == EmployeeDebtFreezeWindowMode.Paused)
            {
                var resume = new Button
                {
                    Content = "Возобновить автооплату",
                    Width = 210,
                    Height = 40,
                    Background = new SolidColorBrush(Color.FromRgb(22, 101, 52)),
                    Foreground = Brushes.White,
                    FontWeight = FontWeights.SemiBold
                };
                resume.Click += (_, _) =>
                {
                    ResumeRequested = true;
                    DialogResult = true;
                };
                buttons.Children.Add(resume);
            }
            return buttons;
        }

        private void RefreshStatus()
        {
            DateTime now = ClubClock.Current.LocalNow;
            if (_mode == EmployeeDebtFreezeWindowMode.Pending)
            {
                _statusText.Text =
                    "Решение ещё не принято. До подтверждения автоматические удержания продолжаются.";
                return;
            }

            if (!_deadline.HasValue)
            {
                _statusText.Text = _mode == EmployeeDebtFreezeWindowMode.Paused
                    ? "Срок не задан. Автооплату можно возобновить вручную."
                    : "Новый запрос уже можно отправить.";
                return;
            }

            TimeSpan remaining = _deadline.Value - now;
            if (remaining <= TimeSpan.Zero)
            {
                _statusText.Text = _mode == EmployeeDebtFreezeWindowMode.Paused
                    ? "Срок завершён. Автооплата возобновится при ближайшем расчёте зарплаты."
                    : "Ограничение завершено. Новый запрос уже можно отправить.";
                return;
            }

            string time = FormatCountdown(remaining);
            _statusText.Text = _mode == EmployeeDebtFreezeWindowMode.Paused
                ? $"До автоматического возобновления: {time}."
                : $"Новый запрос можно отправить через {time}.";
        }

        private static string FormatCountdown(TimeSpan remaining)
        {
            int totalSeconds = Math.Max(0, (int)Math.Ceiling(remaining.TotalSeconds));
            int days = totalSeconds / 86400;
            int hours = totalSeconds % 86400 / 3600;
            int minutes = totalSeconds % 3600 / 60;
            int seconds = totalSeconds % 60;
            return days > 0
                ? $"{days} д {hours:00} ч {minutes:00} мин"
                : hours > 0
                    ? $"{hours:00} ч {minutes:00} мин {seconds:00} сек"
                    : $"{minutes:00}:{seconds:00}";
        }
    }
}
