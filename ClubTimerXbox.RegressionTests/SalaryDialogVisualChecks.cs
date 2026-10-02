using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using ClubTimerXbox;
using ClubTimerXbox.Services;

internal static class SalaryDialogVisualChecks
{
    public static void Run(string outputFolder)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { RenderAll(outputFolder); }
            catch (Exception error) { failure = error; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null)
            throw new InvalidOperationException("Salary dialog visual check failed", failure);
    }

    private static void RenderAll(string folder)
    {
        Directory.CreateDirectory(folder);
        VerifyPayoutNoticeIsModeless(false);
        VerifyPayoutNoticeIsModeless(true);
        int rendered = 0;
        foreach (int width in new[] { 360, 500 })
        {
            Capture(new EmployeeActionWindow("Администратор с очень длинным именем"),
                width, $"menu-{width}", folder);
            Capture(new EmployeeSalaryAccessWindow(), width, $"pin-{width}", folder);
            Capture(new EmployeeCashPayoutNotice("Сотрудник с очень длинным именем", 140),
                width, $"cash-notice-{width}", folder,
                notice =>
                {
                    string[] texts = Descendants(notice).OfType<TextBlock>().Select(text => text.Text).ToArray();
                    if (!texts.Contains("Заберите из кассы") || !texts.Contains("140 сом наличными"))
                        throw new InvalidOperationException("Notice must show the actual cash amount");
                });
            rendered += 3;
            foreach (var sample in new[]
            {
                (Salary: 63, Credit: 77), (Salary: 63, Credit: 0),
                (Salary: -545, Credit: 100), (Salary: -545, Credit: 0),
                (Salary: 0, Credit: 77), (Salary: 0, Credit: 0), (Salary: 210, Credit: 0)
            })
            {
                var access = new EmployeeSalaryAccessSession
                {
                    EmployeeName = "Сотрудник с очень длинным именем",
                    DutyEmployeeName = "Администратор с очень длинным именем"
                };
                var window = new EmployeeSalaryTakenWindow(access.EmployeeName,
                    new DateTime(2026, 10, 1, 6, 0, 0), null, access,
                    () => sample, () => 5000);
                Capture(window, width, $"salary-{sample.Salary}-{sample.Credit}-{width}", folder,
                    shown => CheckSalaryLabels(shown, sample.Salary, sample.Credit));
                rendered++;
            }
        }
        Console.WriteLine($"PASS: {rendered} isolated salary dialog renders; no live data or payouts used.");
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowEnabled(IntPtr window);

    private static void VerifyPayoutNoticeIsModeless(bool fromDetails)
    {
        var root = new Window { Width = 640, Height = 480, Left = -10000, Top = -10000,
            ShowInTaskbar = false, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual };
        var source = new Window { Width = 200, Height = 200, Left = -10000, Top = -10000,
            ShowInTaskbar = false, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual };
        Window? details = null;
        EmployeeCashPayoutNotice? notice = null;
        try
        {
            root.Show();
            if (fromDetails)
            {
                details = new Window { Owner = root, Width = 200, Height = 200, Left = -10000, Top = -10000,
                    ShowInTaskbar = false, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual };
                details.Show();
            }
            source.Owner = details ?? root;
            source.Show();
            notice = EmployeeCashPayoutNotice.ShowAfterPayment(source, "Арген", 140);
            if (notice.WindowStartupLocation != WindowStartupLocation.CenterOwner)
                throw new InvalidOperationException("Payout notice must open centered on the surviving root window");
            notice.Left = -10000;
            notice.Top = -10000;
            source.Close();
            details?.Close();
            root.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            if (notice.Owner != root || !notice.IsVisible ||
                !IsWindowEnabled(new WindowInteropHelper(root).Handle) || notice.Topmost || notice.ShowActivated)
                throw new InvalidOperationException("Payout notice blocks work or closes with the paid form");
            var button = Descendants(notice).OfType<Button>().Single();
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (!notice.IsAcknowledged || notice.IsVisible || !root.IsVisible)
                throw new InvalidOperationException("Acknowledgment must close only the notice");
            notice = EmployeeCashPayoutNotice.ShowAfterPayment(root, "Арген", 140);
            if (notice.Owner != null || notice.WindowStartupLocation != WindowStartupLocation.CenterScreen)
                throw new InvalidOperationException("Without a surviving owner, the notice must open centered on screen");
            notice.Left = -10000;
            notice.Top = -10000;
            notice.Close();
            if (notice.IsAcknowledged)
                throw new InvalidOperationException("Closing is not an acknowledgment");
            Console.WriteLine($"PASS: cash notice from {(fromDetails ? "employee details" : "personal PIN")} is modeless, " +
                "uses centered placement, survives paid form, and acknowledges without a payout callback.");
        }
        finally
        {
            notice?.Close();
            source.Close();
            details?.Close();
            root.Close();
        }
    }

    private static void CheckSalaryLabels(Window window, int salary, int credit)
    {
        string[] labels = Descendants(window).OfType<TextBlock>()
            .Where(text => text.IsVisible)
            .Select(text => text.Text)
            .Where(text => new[] { "Доступно:", "ЗП остаток:", "Из долгов:",
                "Возврат долга:", "Можно получить:" }.Any(text.StartsWith))
            .ToArray();
        string[] expected = credit > 0
            ? new[] { $"Доступно: {Math.Max(0, salary) + credit} сом",
                $"ЗП остаток: {salary} сом", $"Из долгов: {credit} сом" }
            : new[] { $"Доступно: {salary} сом" };
        if (!labels.SequenceEqual(expected))
            throw new InvalidOperationException("Unexpected salary labels: " + string.Join(" | ", labels));
        if (salary < 0)
        {
            string negativeLabel = credit > 0 ? expected[1] : expected[0];
            var negative = Descendants(window).OfType<TextBlock>().Single(text => text.Text == negativeLabel);
            if (negative.Foreground is not SolidColorBrush brush || brush.Color != Color.FromRgb(248, 113, 113))
                throw new InvalidOperationException("Negative salary must remain visible in red");
        }
    }

    private static void Capture(Window window, int width, string name, string folder,
        Action<Window>? check = null)
    {
        window.ShowActivated = false;
        window.ShowInTaskbar = false;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -10000;
        window.Top = -10000;
        window.Width = width;
        window.MaxHeight = 480;
        ((ScrollViewer)window.Content).MaxHeight = 432;
        try
        {
            window.Show();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();
            check?.Invoke(window);
            var scroll = (ScrollViewer)window.Content;
            SaveImage((FrameworkElement)window.Content, Path.Combine(folder, name + ".png"));
            if (scroll.ExtentWidth > scroll.ViewportWidth + 1)
                throw new InvalidOperationException(name + " has horizontal overflow");
            foreach (TextBlock text in Descendants(window).OfType<TextBlock>())
            {
                if (text.Text.Length == 0)
                    continue;
                var measured = new TextBlock
                {
                    Text = text.Text, FontFamily = text.FontFamily, FontSize = text.FontSize,
                    FontWeight = text.FontWeight, TextWrapping = text.TextWrapping,
                    Padding = text.Padding
                };
                measured.Measure(new Size(Math.Max(1, text.ActualWidth + 2), double.PositiveInfinity));
                if (measured.DesiredSize.Height > text.ActualHeight + 2)
                    throw new InvalidOperationException(name + " has clipped text: " + text.Text +
                        $" actual={text.ActualWidth}x{text.ActualHeight} desired={measured.DesiredSize}");
            }
            SaveImage((FrameworkElement)window.Content, Path.Combine(folder, name + ".png"));
            scroll.ScrollToEnd();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();
            var lastButton = Descendants(scroll).OfType<Button>().Last();
            Point position = lastButton.TransformToAncestor(scroll).Transform(new Point(0, 0));
            if (position.Y < -1 || position.Y + lastButton.ActualHeight > scroll.ActualHeight + 1)
                throw new InvalidOperationException(name + " has unreachable action buttons");
            Console.WriteLine("PASS visual: " + name);
        }
        finally { window.Close(); }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }

    private static void SaveImage(FrameworkElement view, string path)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(view.ActualWidth),
            (int)Math.Ceiling(view.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(view);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
