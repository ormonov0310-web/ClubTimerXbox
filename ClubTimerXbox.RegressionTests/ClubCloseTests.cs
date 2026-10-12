using ClubTimerXbox;
using ClubTimerXbox.Services;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

internal static class ClubCloseTests
{
    public static void Run()
    {
        var original = JsonNode.Parse("""
            {"isOpen":true,"currentEmployeeName":"employee","cash":{"actualCashlessBalanceMonth":34478},
             "places":[{"isBusy":true,"paidAmount":300,"saleLines":[{"amount":100}]}],"reportsByMonth":{"old":{"total":123}}}
            """)!.AsObject();
        var closed = FirebaseSyncService.BuildClosedSnapshot(original);
        if (closed["isOpen"]!.GetValue<bool>() || closed["places"]![0]!["isBusy"]!.GetValue<bool>())
            throw new Exception("Closed state cannot show occupied TVs");
        if (!original["places"]![0]!["isBusy"]!.GetValue<bool>())
            throw new Exception("Closing presentation must not end stored games");
        if (!JsonNode.DeepEquals(original["cash"], closed["cash"]) ||
            !JsonNode.DeepEquals(original["reportsByMonth"], closed["reportsByMonth"]))
            throw new Exception("Closing must retain money and history");
        Console.WriteLine("Club closing: 3 projection checks passed.");
    }

    public static void Render(string folder)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Directory.CreateDirectory(folder);
                foreach (int width in new[] { 320, 440 })
                foreach (int state in new[] { 0, 1, 2 })
                {
                    var window = new ClubCloseProgressWindow
                    {
                        Width = width, Left = -10000, Top = -10000,
                        ShowInTaskbar = false, ShowActivated = false,
                        WindowStartupLocation = WindowStartupLocation.Manual
                    };
                    try
                    {
                        if (state == 0) window.SetRemaining(5);
                        else window.Finish(state == 1);
                        window.Show();
                        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                        window.UpdateLayout();
                        var scroll = (ScrollViewer)window.Content;
                        if (scroll.ExtentWidth > scroll.ViewportWidth + 1) throw new Exception("Horizontal overflow");
                        foreach (var text in Descendants(scroll).OfType<TextBlock>())
                        {
                            var measured = new TextBlock { Text = text.Text, FontSize = text.FontSize,
                                FontWeight = text.FontWeight, FontFamily = text.FontFamily, TextWrapping = text.TextWrapping };
                            measured.Measure(new Size(text.ActualWidth + 1, double.PositiveInfinity));
                            if (measured.DesiredSize.Height > text.ActualHeight + 2) throw new Exception("Clipped close text");
                        }
                        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(scroll.ActualWidth),
                            (int)Math.Ceiling(scroll.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                        bitmap.Render(scroll);
                        var encoder = new PngBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create(bitmap));
                        using var output = File.Create(Path.Combine(folder, $"close-{width}-{state}.png"));
                        encoder.Save(output);
                    }
                    finally { window.Finish(true); window.Close(); }
                }
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        if (failure != null) throw failure;
        Console.WriteLine("Club closing: 6 isolated renders passed, no live data/cloud used.");
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
}
