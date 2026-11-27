using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ClubTimerXbox;
using ClubTimerXbox.Models;

internal static class TariffRefundTests
{
    public static void Run()
    {
        int comparisons = 0;
        foreach (int cash in new[] { 0, 1, 30, 60, 100 })
        foreach (int cashless in new[] { 0, 1, 20, 70, 100 })
        foreach (int refund in Enumerable.Range(0, 201))
        {
            var details = TariffRefundBreakdown.FromOriginalPayment(cash, cashless, refund);
            Require((details.ReturnCash, details.ReturnCashless) == LegacySplit(cash, cashless, refund),
                "return amounts must be identical to the prior distribution");
            comparisons++;
        }

        Exception? failure = null;
        var ui = new Thread(() =>
        {
            try
            {
                CheckDialog(100, 0, 60, 100, 0, "100 сом", "0 сом", "60 сом", "0 сом");
                CheckDialog(0, 100, 60, 100, 0, "0 сом", "100 сом", "0 сом", "60 сом");
                CheckDialog(40, 60, 70, 100, 90, "40 сом", "60 сом", "40 сом", "30 сом");
                CheckDialog(0, 0, 60, 100, 0, "Неизвестно", "Неизвестно", "0 сом", "60 сом",
                    "Данные о каналах исходной оплаты не сохранились.");
                CheckDialog(20, 30, 25, 100, 0, "20 сом", "30 сом", "20 сом", "5 сом",
                    "Сохранённые суммы по каналам не совпадают с общей оплатой.");
            }
            catch (Exception error) { failure = error; }
        });
        ui.SetApartmentState(ApartmentState.STA);
        ui.Start();
        ui.Join();
        if (failure != null)
            ExceptionDispatchInfo.Capture(failure).Throw();
        Console.WriteLine($"Tariff refund: {comparisons} unchanged split comparisons, 5 isolated WPF dialogs passed.");
    }

    private static void CheckDialog(int cash, int cashless, int amount, int paid, int goods,
        string cashPaidText, string cashlessPaidText, string cashRefundText, string cashlessRefundText, string? note = null)
    {
        var details = TariffRefundBreakdown.FromOriginalPayment(cash, cashless, amount);
        Guid gameId = Guid.NewGuid();
        var receipt = MainWindow.CreateGameRefundPaymentRecord("Администратор", "ТВ 7", gameId, amount,
            details.ReturnCash, details.ReturnCashless);
        Require(receipt.GameSessionId == gameId && receipt.TotalAmount == -amount &&
            receipt.CashAmount + receipt.MBankAmount == receipt.TotalAmount &&
            $"{-receipt.CashAmount} сом" == cashRefundText && $"{-receipt.MBankAmount} сом" == cashlessRefundText,
            "same split used for signed receipt and positive display, without posting it");

        var window = new TariffRefundSummaryWindow("Телевизор с длинным названием для проверки на небольшом экране",
            paid - amount, paid, amount, details, goods, "Администратор с длинным именем для проверки")
        {
            Width = 320, MaxHeight = 260, Left = -10000, Top = -10000,
            ShowInTaskbar = false, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual
        };
        Exception? failure = null;
        window.ContentRendered += (_, _) => window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            try
            {
                var scroll = (ScrollViewer)window.Content;
                Require(window.SizeToContent == SizeToContent.Height && scroll.ScrollableHeight > 0 &&
                    scroll.ExtentWidth <= scroll.ViewportWidth + 1, "compact window must scroll vertically without overflow");
                var labels = Descendants(scroll).OfType<TextBlock>().ToArray();
                string Cell(string name) => labels.Single(label => label.Name == name).Text;
                Require(Cell("CashPaid") == cashPaidText && Cell("CashlessPaid") == cashlessPaidText &&
                    Cell("CashRefund") == cashRefundText && Cell("CashlessRefund") == cashlessRefundText &&
                    Cell("TotalPaid") == $"{paid} сом" && Cell("TotalRefund") == $"{amount} сом",
                    "payment and refund must be shown per channel with correct totals");
                Require(note == null || labels.Any(label => label.Text == note), "legacy evidence note not shown");
                Require(goods == 0 || labels.Any(label => label.Text == $"Товары и услуги оплачены отдельно: {goods} сом"),
                    "separately paid goods must not imply an offset against the refund");
                foreach (var label in labels)
                {
                    var probe = new TextBlock
                    {
                        Text = label.Text, FontSize = label.FontSize, FontFamily = label.FontFamily,
                        FontWeight = label.FontWeight, TextWrapping = label.TextWrapping
                    };
                    probe.Measure(new Size(label.ActualWidth + 1, double.PositiveInfinity));
                    Require(probe.DesiredSize.Height <= label.ActualHeight + 2, "clipped WPF label: " + label.Text);
                }
                var ok = Descendants(scroll).OfType<Button>().Single();
                ok.BringIntoView();
                window.UpdateLayout();
                Require(ok.IsDefault && scroll.VerticalOffset > 0, "dialog acknowledgement must be reachable by scrolling");
                ok.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            catch (Exception error) { failure = error; window.Close(); }
        }));
        var result = window.ShowDialog();
        if (failure != null)
            ExceptionDispatchInfo.Capture(failure).Throw();
        Require(result == true, "OK acknowledgement did not dismiss the summary");
    }

    // Frozen pre-change implementation, including the legacy cashless fallback.
    private static (int Cash, int Cashless) LegacySplit(int cashPaid, int mBankPaid, int refundAmount)
    {
        if (refundAmount <= 0) return (0, 0);
        if (cashPaid + mBankPaid <= 0) return (0, refundAmount);
        int cashRefund = Math.Min(cashPaid, refundAmount);
        int mBankRefund = refundAmount - cashRefund;
        if (mBankRefund > mBankPaid)
        {
            int extra = mBankRefund - mBankPaid;
            mBankRefund = mBankPaid;
            cashRefund += extra;
        }
        return (cashRefund, mBankRefund);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static void Require(bool condition, string detail)
    {
        if (!condition)
            throw new InvalidOperationException("Tariff refund: " + detail);
    }
}
