using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ClubTimerXbox;

public static class StockAcceptanceRowView
{
    public static Border Create(string name, int expected, int price, TextBox actual, TextBlock difference)
    {
        var root = new Grid();
        root.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        root.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var grip = new Border
        {
            Width = 28, Height = 36, Background = Brushes.Transparent,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0),
            Cursor = Cursors.SizeNS, ToolTip = "Перетащить товар",
            Child = new TextBlock
            {
                Text = "⋮⋮", FontSize = 20, Foreground = Brushes.Gray,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            }
        };
        StockAcceptanceDragBehavior.SetIsDragHandle(grip, true);
        AutomationProperties.SetName(grip, "Перетащить товар");
        root.Children.Add(grip);
        var left = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
        left.Children.Add(new TextBlock
        {
            Text = name, Foreground = Brushes.White, FontSize = 20, FontWeight = FontWeights.Bold,
            TextWrapping = TextWrapping.Wrap
        });
        left.Children.Add(new TextBlock
        {
            Text = $"По программе: {expected} шт\nЦена продажи: {price} сом", FontSize = 14,
            Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)), LineHeight = 21,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0)
        });
        difference.TextWrapping = TextWrapping.Wrap;
        left.Children.Add(difference); Grid.SetColumn(left, 1); root.Children.Add(left);
        var right = new StackPanel { Width = 170 };
        right.Children.Add(new TextBlock
        {
            Text = "Фактически", FontSize = 13, Foreground = new SolidColorBrush(Color.FromRgb(170, 180, 195)),
            Margin = new Thickness(0, 0, 0, 4)
        });
        right.Children.Add(actual); Grid.SetColumn(right, 2); root.Children.Add(right);
        return new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(24, 32, 43)), CornerRadius = new CornerRadius(14),
            Padding = new Thickness(16), Margin = new Thickness(0, 0, 0, 12), Child = root
        };
    }
}
