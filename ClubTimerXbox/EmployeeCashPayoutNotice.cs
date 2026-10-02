using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ClubTimerXbox
{
    public sealed class EmployeeCashPayoutNotice : AdaptiveDialogWindow
    {
        internal bool IsAcknowledged { get; private set; }

        public EmployeeCashPayoutNotice(string employeeName, int amount) : base(380, 320)
        {
            if (amount <= 0)
                throw new ArgumentOutOfRangeException(nameof(amount));
            Title = "Выдача зарплаты";
            ShowInTaskbar = false;
            ShowActivated = false;
            WindowStyle = WindowStyle.ToolWindow;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            var panel = new StackPanel { Margin = new Thickness(20) };
            panel.Children.Add(new TextBlock
            {
                Text = employeeName, FontSize = 18, FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12)
            });
            panel.Children.Add(new TextBlock
            {
                Text = "Заберите из кассы", FontSize = 18, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 4)
            });
            panel.Children.Add(new TextBlock
            {
                Text = $"{amount} сом наличными", FontSize = 26, FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(74, 222, 128)),
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 18)
            });
            var taken = new Button
            {
                Content = "Забрал", MinWidth = 110, MinHeight = 38,
                Padding = new Thickness(16, 6, 16, 6),
                HorizontalAlignment = HorizontalAlignment.Right,
                Background = new SolidColorBrush(Color.FromRgb(22, 101, 52)),
                Foreground = Brushes.White, FontWeight = FontWeights.SemiBold
            };
            taken.Click += (_, _) =>
            {
                IsAcknowledged = true;
                Close();
            };
            panel.Children.Add(taken);
            SetAdaptiveContent(panel);
        }

        internal static EmployeeCashPayoutNotice ShowAfterPayment(
            Window source, string employeeName, int amount)
        {
            // The payout dialog closes immediately; own the notice by its surviving root window.
            Window? owner = source.Owner;
            while (owner?.Owner != null)
                owner = owner.Owner;
            var notice = new EmployeeCashPayoutNotice(employeeName, amount);
            if (owner?.IsVisible == true)
            {
                notice.Owner = owner;
                notice.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
            notice.Show();
            return notice;
        }
    }
}
