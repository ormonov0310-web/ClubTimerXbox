using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using ClubTimerXbox.Models;

namespace ClubTimerXbox;

public sealed class DeferredCheckoutReminderBanner : Border
{
    private readonly TextBlock _message = new()
    {
        Name = "ReminderMessage",
        TextWrapping = TextWrapping.Wrap,
        Foreground = Brushes.White,
        FontSize = 14,
        FontWeight = FontWeights.SemiBold,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(0, 0, 10, 0)
    };

    internal event EventHandler? Dismissed;

    public DeferredCheckoutReminderBanner()
    {
        Visibility = Visibility.Collapsed;
        Background = new SolidColorBrush(Color.FromRgb(59, 45, 24));
        BorderBrush = Brushes.Goldenrod;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(6);
        Padding = new Thickness(12, 4, 6, 4);
        Margin = new Thickness(0, 0, 0, 10);

        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition());
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        layout.Children.Add(_message);

        var close = new Button { Content = "×", Width = 30, Height = 28, Padding = new Thickness(0) };
        AutomationProperties.SetName(close, "Скрыть напоминание");
        close.ToolTip = "Скрыть напоминание";
        close.Click += (_, _) => Dismissed?.Invoke(this, EventArgs.Empty);
        Grid.SetColumn(close, 1);
        layout.Children.Add(close);
        Child = layout;
    }

    internal void ShowOutstanding(IReadOnlyList<DeferredCheckout> reminders)
    {
        if (reminders.Count == 0)
        {
            Visibility = Visibility.Collapsed;
            return;
        }

        var first = reminders[0];
        string entry = $"{first.PlaceName} · Остановка {first.StopNumber} · {first.Total} сом";
        _message.Text = reminders.Count == 1
            ? $"Не оплачен ККМ: {entry}"
            : $"Не оплачено ККМ: {reminders.Count} · {entry}, ещё {reminders.Count - 1}";
        Visibility = Visibility.Visible;
    }
}
