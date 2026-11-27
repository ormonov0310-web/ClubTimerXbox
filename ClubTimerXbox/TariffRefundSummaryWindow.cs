using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ClubTimerXbox.Models;

namespace ClubTimerXbox;

internal sealed class TariffRefundSummaryWindow : AdaptiveDialogWindow
{
    public TariffRefundSummaryWindow(string place, int gameAmount, int paidAmount, int refundAmount,
        TariffRefundBreakdown breakdown, int productsAmount, string incomeEmployee)
        : base(preferredWidth: 460, minimumWidth: 320)
    {
        Title = "Возврат по тарифу";
        var body = new StackPanel { Margin = new Thickness(18) };
        body.Children.Add(Label($"{place} · Возврат по тарифу", size: 20, bold: true));
        body.Children.Add(Label($"Игра и штрафные минуты: {Money(gameAmount)}"));

        var table = new Grid { Margin = new Thickness(0, 12, 0, 8) };
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.16, GridUnitType.Star) });
        for (int i = 0; i < 2; i++)
            table.ColumnDefinitions.Add(new ColumnDefinition());

        AddRow(table, "Канал", "Оплачено", "Вернуть", "Channel", bold: true);
        AddRow(table, "Наличные", PaymentText(breakdown.PaidCash), Money(breakdown.ReturnCash), "Cash");
        AddRow(table, "Безнал", PaymentText(breakdown.PaidCashless), Money(breakdown.ReturnCashless), "Cashless");
        AddRow(table, "Итого", Money(paidAmount), Money(refundAmount), "Total", bold: true);
        body.Children.Add(table);

        if (breakdown.PaidCash is not { } cash || breakdown.PaidCashless is not { } cashless)
            body.Children.Add(Label("Данные о каналах исходной оплаты не сохранились.", subdued: true));
        else if ((long)cash + cashless != paidAmount)
            body.Children.Add(Label("Сохранённые суммы по каналам не совпадают с общей оплатой.", subdued: true));

        if (productsAmount > 0)
            body.Children.Add(Label($"Товары и услуги оплачены отдельно: {Money(productsAmount)}"));

        body.Children.Add(Label($"Выручка за игру относится к: {incomeEmployee}", subdued: true));

        var ok = new Button
        {
            Name = "AcknowledgeRefund",
            Content = "ОК",
            IsDefault = true,
            MinWidth = 88,
            MinHeight = 36,
            Padding = new Thickness(12, 4, 12, 4),
            Margin = new Thickness(0, 10, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Right
        };
        ok.Click += (_, _) => DialogResult = true;
        body.Children.Add(ok);
        SetAdaptiveContent(body);
    }

    private static string Money(int amount) => $"{amount} сом";
    private static string PaymentText(int? amount) => amount is { } value ? Money(value) : "Неизвестно";

    private static TextBlock Label(string text, int size = 14, bool bold = false, bool subdued = false) =>
        new()
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            FontSize = size,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = subdued ? Brushes.LightGray : Brushes.White,
            Margin = new Thickness(0, 0, 0, 6)
        };

    private static void AddRow(Grid table, string channel, string paid, string refund, string name, bool bold = false)
    {
        int row = table.RowDefinitions.Count;
        table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var values = new[] { channel, paid, refund };
        var suffixes = new[] { "Label", "Paid", "Refund" };
        for (int column = 0; column < values.Length; column++)
        {
            var cell = Label(values[column], bold: bold);
            cell.Name = name + suffixes[column];
            cell.TextAlignment = column == 0 ? TextAlignment.Left : TextAlignment.Right;
            cell.Margin = new Thickness(column == 0 ? 0 : 6, 4, 0, 4);
            Grid.SetRow(cell, row);
            Grid.SetColumn(cell, column);
            table.Children.Add(cell);
        }
    }
}
