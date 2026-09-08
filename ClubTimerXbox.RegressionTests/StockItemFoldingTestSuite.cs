using ClubTimerXbox;
using ClubTimerXbox.Models;
using ClubTimerXbox.Services;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal sealed class StockItemFoldingTestSuite
{
    private int _passed;
    private static DateTime ExpiredSince => ClubClock.Current.UtcNow.AddDays(-2);

    public void Run()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var clock = ClubClock.UseForTesting(new ManualClubClock(new DateTime(2026, 9, 9, 12, 0, 0)));
                Test("expired zero stock starts folded", StartsFolded);
                Test("stock can expand and collapse without losing input", KeepsInput);
                Test("positive and negative stock remain visible", NonzeroVisible);
                Test("services never enter the empty-stock group", ServicesVisible);
                Test("purchase catalog keeps all zero-stock items visible", PurchaseVisible);
                Test("restock returns an item to the visible list", Restock);
                Test("group without empty stock has no toggle", NoEmptyGroup);
                Test("all zero stock still has an expandable group", AllEmpty);
                Test("reopening a list defaults to collapsed", Reopen);
                Test("hidden invalid input can be revealed for correction", RevealInput);
                Test("folding preserves popularity within each group", Popularity);
                Test("wrapped and stacked lists retain all item views", Layouts);
                Test("fresh zero remains visible until exactly 24 hours", GraceBoundary);
                Test("restocking before deadline cancels hiding", RestockBeforeDeadline);
                Test("restocking hidden product makes it visible", RestockAfterDeadline);
                Test("newest hidden stock precedes popular old stock", RecentlyHiddenFirst);
                Test("unsaved acceptance input stays in place at deadline", DraftStaysInPlace);
                Test("legacy zero without tracking is not silently hidden", UntrackedZeroVisible);
                RenderPreviews();
            }
            catch (Exception error) { failure = error; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
        Console.WriteLine($"PASS: {_passed} stock folding scenarios.");
    }

    private static Expander Section(StockItemListPanel list) => (Expander)list.Children[1];
    private static Panel Visible(StockItemListPanel list) => (Panel)list.Children[0];
    private static Panel Empty(StockItemListPanel list) => (Panel)Section(list).Content;
    private static Border Item(string title) => new() { Tag = title, Height = 44, Child = new TextBlock { Text = title } };

    private static void StartsFolded()
    {
        var list = new StockItemListPanel();
        list.AddItem(Item("available"), 5);
        list.AddItem(Item("empty"), 0, ExpiredSince);
        True(!list.IsOutOfStockExpanded);
        Equal(1, list.OutOfStockCount);
        Equal(Visibility.Visible, Section(list).Visibility);
    }

    private static void KeepsInput()
    {
        var list = new StockItemListPanel();
        var input = new TextBox { Text = "0" };
        var card = new Border { Child = input };
        list.AddItem(card, 0, ExpiredSince);
        list.IsOutOfStockExpanded = true;
        input.Text = "4";
        list.IsOutOfStockExpanded = false;
        list.IsOutOfStockExpanded = true;
        Equal("4", input.Text);
        True(ReferenceEquals(card, list.ItemViews.Single()));
        Equal(1, Empty(list).Children.Count);
    }

    private static void NonzeroVisible()
    {
        var list = new StockItemListPanel();
        list.AddItem(Item("low"), 1);
        list.AddItem(Item("error"), -1);
        Equal(2, Visible(list).Children.Count);
        Equal(0, list.OutOfStockCount);
    }

    private static void ServicesVisible()
    {
        var list = new StockItemListPanel(true);
        list.AddItem(Item("service"), null);
        Equal(1, Visible(list).Children.Count);
        Equal(0, list.OutOfStockCount);
    }

    private static void PurchaseVisible()
    {
        var list = new StockItemListPanel();
        list.AddItem(Item("empty popular"), null);
        list.AddItem(Item("empty other"), null);
        Equal(2, Visible(list).Children.Count);
        Equal(Visibility.Collapsed, Section(list).Visibility);
    }

    private static void Restock()
    {
        var list = new StockItemListPanel();
        list.AddItem(Item("water"), 0, ExpiredSince);
        list.IsOutOfStockExpanded = true;
        list.ClearItems();
        list.AddItem(Item("water"), 10);
        Equal(0, list.OutOfStockCount);
        Equal(1, Visible(list).Children.Count);
        Equal(1, list.ItemViews.Count);
    }

    private static void NoEmptyGroup()
    {
        var list = new StockItemListPanel();
        Equal(Visibility.Collapsed, Section(list).Visibility);
        list.AddItem(Item("water"), 2);
        Equal(Visibility.Collapsed, Section(list).Visibility);
    }

    private static void AllEmpty()
    {
        var list = new StockItemListPanel();
        list.AddItem(Item("water"), 0, ExpiredSince);
        list.AddItem(Item("cola"), 0, ExpiredSince);
        Equal(0, Visible(list).Children.Count);
        Equal(2, list.OutOfStockCount);
        list.IsOutOfStockExpanded = true;
        Equal(2, Empty(list).Children.Count);
    }

    private static void Reopen()
    {
        var list = new StockItemListPanel();
        list.AddItem(Item("water"), 0, ExpiredSince);
        list.IsOutOfStockExpanded = true;
        list.ClearItems();
        list.AddItem(Item("water"), 0, ExpiredSince);
        True(!list.IsOutOfStockExpanded);
    }

    private static void RevealInput()
    {
        var list = new StockItemListPanel();
        var input = new TextBox { Text = "invalid" };
        list.AddItem(new Border { Child = input }, 0, ExpiredSince);
        Layout(list, 620);
        list.RevealInput(input);
        True(list.IsOutOfStockExpanded);
        Equal("invalid", input.Text);
    }

    private static void Popularity()
    {
        var stock = new[]
        {
            new ProductStockItem { ProductName = "Cola", Quantity = 0 },
            new ProductStockItem { ProductName = "Fanta", Quantity = 2 },
            new ProductStockItem { ProductName = "Water", Quantity = 0 },
            new ProductStockItem { ProductName = "Pepsi", Quantity = 2 }
        };
        var popularity = new Dictionary<string, int> { ["Cola"] = 40, ["Pepsi"] = 30, ["Water"] = 20, ["Fanta"] = 10 };
        var list = new StockItemListPanel();
        foreach (var item in ProductPopularityService.OrderStock(stock, popularity))
            list.AddItem(Item(item.ProductName), item.Quantity, ExpiredSince);
        Equal("Pepsi,Fanta", string.Join(",", Visible(list).Children.Cast<Border>().Select(item => item.Tag)));
        Equal("Cola,Water", string.Join(",", Empty(list).Children.Cast<Border>().Select(item => item.Tag)));
        Equal("Cola,Pepsi,Water,Fanta", string.Join(",", ProductPopularityService.OrderPurchaseCatalog(stock, popularity).Select(item => item.ProductName)));
    }

    private static void Layouts()
    {
        foreach (bool wrap in new[] { false, true })
        {
            var list = new StockItemListPanel(wrap);
            list.AddItem(Item("available"), 3);
            list.AddItem(Item("empty"), 0, ExpiredSince);
            Layout(list, 620);
            double foldedHeight = list.DesiredSize.Height;
            list.IsOutOfStockExpanded = true;
            Layout(list, 620);
            True(list.DesiredSize.Height > foldedHeight);
            Equal(2, list.ItemViews.Count);
        }
    }

    private static void GraceBoundary()
    {
        var clock = new ManualClubClock(new DateTime(2026, 9, 9, 12, 0, 0));
        using var scope = ClubClock.UseForTesting(clock);
        var list = new StockItemListPanel();
        list.AddItem(Item("fresh zero"), 0, clock.UtcNow);
        Equal(0, list.OutOfStockCount);
        clock.Advance(TimeSpan.FromHours(24) - TimeSpan.FromSeconds(1));
        list.RefreshGroups();
        Equal(0, list.OutOfStockCount);
        clock.Advance(TimeSpan.FromSeconds(1));
        list.RefreshGroups();
        Equal(1, list.OutOfStockCount);
        Equal(1, list.ItemViews.Count);
    }

    private static void RestockBeforeDeadline()
    {
        var clock = new ManualClubClock(new DateTime(2026, 9, 9, 12, 0, 0));
        using var scope = ClubClock.UseForTesting(clock);
        var item = new ProductStockItem();
        StockVisibilityPolicy.EnsureTracking(item, clock.UtcNow);
        var list = new StockItemListPanel();
        list.AddTrackedItem(Item("restock"), () => ((int?)item.Quantity, item.ZeroStockSinceUtc));
        clock.Advance(TimeSpan.FromHours(10));
        StockVisibilityPolicy.SetQuantity(item, 1, clock.UtcNow);
        clock.Advance(TimeSpan.FromDays(3));
        list.RefreshGroups();
        True(item.ZeroStockSinceUtc == null);
        Equal(0, list.OutOfStockCount);
    }

    private static void RestockAfterDeadline()
    {
        var item = new ProductStockItem { ZeroStockSinceUtc = ExpiredSince };
        var list = new StockItemListPanel();
        list.AddTrackedItem(Item("hidden"), () => ((int?)item.Quantity, item.ZeroStockSinceUtc));
        Equal(1, list.OutOfStockCount);
        StockVisibilityPolicy.SetQuantity(item, 1, ClubClock.Current.UtcNow);
        list.RefreshGroups();
        Equal(0, list.OutOfStockCount);
        Equal(1, Visible(list).Children.Count);
    }

    private static void RecentlyHiddenFirst()
    {
        var list = new StockItemListPanel();
        list.AddItem(Item("old popular"), 0, ExpiredSince.AddDays(-4));
        list.AddItem(Item("just hidden unpopular"), 0, ClubClock.Current.UtcNow.AddDays(-1));
        list.IsOutOfStockExpanded = true;
        Equal("just hidden unpopular", ((Border)Empty(list).Children[0]).Tag);
        Equal("old popular", ((Border)Empty(list).Children[1]).Tag);
    }

    private static void DraftStaysInPlace()
    {
        var clock = new ManualClubClock(new DateTime(2026, 9, 9, 12, 0, 0));
        using var scope = ClubClock.UseForTesting(clock);
        var input = new TextBox { Text = "1" };
        DateTime since = clock.UtcNow;
        var list = new StockItemListPanel();
        list.AddTrackedItem(new Border { Child = input }, () => ((int?)0, (DateTime?)since), () => input.Text != "0");
        clock.Advance(TimeSpan.FromDays(1));
        list.RefreshGroups();
        Equal(0, list.OutOfStockCount);
        Equal("1", input.Text);
        input.Text = "0";
        list.RefreshGroups();
        Equal(1, list.OutOfStockCount);
    }

    private static void UntrackedZeroVisible()
    {
        var list = new StockItemListPanel();
        list.AddItem(Item("legacy"), 0);
        Equal(0, list.OutOfStockCount);
    }

    private static void Layout(FrameworkElement view, int width)
    {
        view.Measure(new Size(width, double.PositiveInfinity));
        view.Arrange(new Rect(0, 0, width, view.DesiredSize.Height));
        view.UpdateLayout();
    }

    private static void RenderPreviews()
    {
        string folder = Path.Combine(Directory.GetCurrentDirectory(), ".codex-build", "stock-folding");
        Directory.CreateDirectory(folder);
        foreach (bool expanded in new[] { false, true })
        {
            var list = new StockItemListPanel();
            foreach (var (name, quantity, since) in new[] {
                ("Кола", 8, ExpiredSince),
                ("Вода, закончилась сегодня", 0, ClubClock.Current.UtcNow),
                ("Фанта", 0, ExpiredSince),
                ("Сок, скрыт недавно", 0, ClubClock.Current.UtcNow.AddDays(-1)) })
            {
                var line = new DockPanel();
                var input = new TextBox { Text = quantity.ToString(), Width = 90, Height = 36, FontSize = 17 };
                DockPanel.SetDock(input, Dock.Right);
                line.Children.Add(input);
                line.Children.Add(new TextBlock { Text = $"{name}    По программе: {quantity} шт", Foreground = Brushes.White, FontSize = 17, VerticalAlignment = VerticalAlignment.Center });
                list.AddItem(new Border { Child = line, Background = new SolidColorBrush(Color.FromRgb(32, 43, 51)), Padding = new Thickness(14), Margin = new Thickness(0, 0, 0, 10), CornerRadius = new CornerRadius(8) }, quantity, since);
            }
            list.IsOutOfStockExpanded = expanded;
            var root = new Border { Child = list, Background = new SolidColorBrush(Color.FromRgb(16, 20, 28)), Padding = new Thickness(20) };
            root.Resources["Theme.TextBrush"] = Brushes.White;
            Layout(root, 620);
            var image = new RenderTargetBitmap(620, (int)Math.Ceiling(root.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            image.Render(root);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using var output = File.Create(Path.Combine(folder, expanded ? "expanded.png" : "collapsed.png"));
            encoder.Save(output);
        }
    }

    private void Test(string name, Action test) { test(); _passed++; Console.WriteLine("PASS: " + name); }
    private static void True(bool value) { if (!value) throw new Exception("Stock folding assertion failed"); }
    private static void Equal<T>(T expected, T actual) { if (!Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}"); }
}
