using ClubTimerXbox;
using ClubTimerXbox.Models;
using ClubTimerXbox.Services;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

internal static class StockAcceptanceOrderTests
{
    public static void Run()
    {
        int passed = 0;
        void Check(bool value, string title)
        {
            if (!value) throw new Exception("Acceptance order: " + title);
            passed++;
        }
        var cola = new ProductStockItem { ProductName = "Cola", Quantity = 8 };
        var tea = new ProductStockItem { ProductName = "Tea", Quantity = 6 };
        var juice = new ProductStockItem { ProductName = "Juice", Quantity = 4 };
        var stock = new List<ProductStockItem> { cola, tea, juice };
        var popularity = new Dictionary<string, int> { ["Cola"] = 1, ["Tea"] = 100, ["Juice"] = 20 };
        var priority = StockAcceptanceOrderPolicy.Priority(stock, Array.Empty<Guid>(), popularity);
        Check(priority.SequenceEqual(new[] { tea.Id, juice.Id, cola.Id }), "default popularity retained");
        priority = StockAcceptanceOrderPolicy.MoveAvailable(priority, stock, cola.Id, tea.Id, false);
        Check(priority.SequenceEqual(new[] { cola.Id, tea.Id, juice.Id }), "cola moved first");
        Check(ProductPopularityService.OrderStock(stock, popularity).First() == tea, "warehouse popularity unchanged");
        Check(ProductPopularityService.OrderPurchaseCatalog(stock, popularity).First() == tea, "procurement popularity unchanged");
        popularity["Juice"] = 1000;
        Check(StockAcceptanceOrderPolicy.Priority(stock, priority, popularity).SequenceEqual(priority), "later popularity cannot override manual order");
        cola.Quantity = 0;
        Check(StockAcceptanceOrderPolicy.Order(stock, priority).Select(item => item.Id)
            .SequenceEqual(new[] { tea.Id, juice.Id, cola.Id }), "sold-out cola yields visible first place");
        var moved = StockAcceptanceOrderPolicy.MoveAvailable(priority, stock, juice.Id, tea.Id, false);
        Check(moved.SequenceEqual(new[] { cola.Id, juice.Id, tea.Id }), "absent cola retains reserved first slot");
        cola.Quantity = 8;
        Check(StockAcceptanceOrderPolicy.Order(stock, moved).First() == cola, "restock restores original first place");
        cola.ProductName = "Renamed Cola";
        Check(StockAcceptanceOrderPolicy.Priority(stock, moved, popularity).First() == cola.Id, "rename preserves stable ID order");
        cola.Quantity = 0;
        Check(StockAcceptanceOrderPolicy.MoveAvailable(moved, stock, cola.Id, tea.Id, true).SequenceEqual(moved), "absent items do not steal available positions");
        Check(StockAcceptanceOrderPolicy.MoveAvailable(moved, stock, tea.Id, tea.Id, true).SequenceEqual(moved), "self-drop is no-op");
        cola.Quantity = 8;
        var water = new ProductStockItem { ProductName = "Water", Quantity = 10 };
        Check(StockAcceptanceOrderPolicy.Priority(stock.Append(water), moved, popularity).Last() == water.Id, "new goods append after known priorities");
        Check(!StockAcceptanceOrderPolicy.Priority(stock.Where(item => item != tea), moved, popularity).Contains(tea.Id), "deleted IDs ignored");
        Check(StockAcceptanceOrderPolicy.MoveAvailable(priority, stock, cola.Id, juice.Id, true).Last() == cola.Id, "downward drop supported");

        string folder = Path.Combine(Path.GetTempPath(), "ClubTimer-acceptance-order-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string path = Path.Combine(folder, "order.json");
            var store = new StockAcceptanceOrderStore(path);
            Check(store.Load("club_a").Count == 0, "missing file uses default");
            store.Save("club_a", priority); store.Save("club_b", priority.AsEnumerable().Reverse().ToArray());
            Check(new StockAcceptanceOrderStore(path).Load("club_a").SequenceEqual(priority), "order survives reopening store");
            Check(store.Load("club_b").SequenceEqual(priority.AsEnumerable().Reverse()), "club preferences isolated");
            var copy = store.Load("club_a"); copy.Clear();
            Check(store.Load("club_a").Count == 3, "read result cannot mutate stored order");
            byte[] original = File.ReadAllBytes(path);
            bool rejected = false;
            try { store.Save("club_a", new[] { cola.Id, cola.Id }); } catch (ArgumentException) { rejected = true; }
            Check(rejected && original.SequenceEqual(File.ReadAllBytes(path)), "invalid save preserves file");
            File.WriteAllText(path, "{broken"); original = File.ReadAllBytes(path);
            rejected = false;
            try { store.Save("club_a", priority); } catch (JsonException) { rejected = true; }
            Check(rejected && original.SequenceEqual(File.ReadAllBytes(path)), "corruption is not overwritten");
            File.WriteAllText(path, "{\"SchemaVersion\":2,\"Clubs\":{}}");
            rejected = false;
            try { store.Load("club_a"); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "unknown schema rejected");
            File.WriteAllText(path, "{}");
            rejected = false;
            try { store.Load("club_a"); } catch (JsonException) { rejected = true; }
            Check(rejected, "incomplete file rejected");
        }
        finally
        {
            if (!Path.GetFullPath(folder).StartsWith(Path.GetFullPath(Path.GetTempPath()) + "ClubTimer-acceptance-order-", StringComparison.OrdinalIgnoreCase))
                throw new Exception("Unsafe test cleanup path.");
            Directory.Delete(folder, true);
        }

        // Apply committed purchases without invalidating the stock objects held by open lists.
        var originalCola = cola;
        var purchase = StockPurchasePostingPolicy.Create(Guid.NewGuid(),
            new[] { new StockPurchaseItem { ProductName = cola.ProductName, Quantity = 2, PurchasePrice = 100 } },
            200, 0, "", new DateTime(2026, 11, 9, 17, 0, 0), stock);
        var updatedStock = StockPurchasePostingPolicy.ApplyInventory(purchase, stock, DateTime.UtcNow);
        StockPurchasePostingPolicy.CommitIncomingInventory(stock, updatedStock);
        Check(ReferenceEquals(originalCola, stock.First(item => item.Id == cola.Id)) && cola.Quantity == 10,
            "restock preserves references used by open acceptance/stock views");

        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { RunUi(Check); RunMotionUi(Check); }
            catch (Exception error) { failure = error; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
        Console.WriteLine($"Acceptance manual order: {passed} checks passed; isolated controls/temp preferences, no live club files/cloud.");
    }

    private static void RunUi(Action<bool, string> check)
    {
        var clock = new ManualClubClock(new DateTime(2026, 11, 9, 17, 0, 0));
        using var scope = ClubClock.UseForTesting(clock);
        var cola = new ProductStockItem { ProductName = "Кола", Quantity = 8 };
        var tea = new ProductStockItem { ProductName = "Чай", Quantity = 5 };
        var empty = new ProductStockItem { ProductName = "Сок", Quantity = 0, ZeroStockSinceUtc = clock.UtcNow.AddDays(-2) };
        var stock = new[] { cola, tea, empty };
        var order = stock.Select(item => item.Id).ToList();
        var list = new StockItemListPanel();
        var cards = new Dictionary<Guid, Border>();
        var fields = new Dictionary<Guid, TextBox>();
        var window = new Window
        {
            Width = 880, Height = 650, Left = -32000, Top = -32000, ShowActivated = false,
            ShowInTaskbar = false, WindowStyle = WindowStyle.None
        };
        int saves = 0;
        bool failSave = false;
        using var behavior = new StockAcceptanceDragBehavior(list,
            id => stock.Any(item => item.Id == id && item.Quantity > 0),
            (source, target, after) =>
            {
                var updated = StockAcceptanceOrderPolicy.MoveAvailable(order, stock, source, target, after);
                if (updated.SequenceEqual(order) || failSave) return false;
                order = updated; saves++; list.RefreshGroups(forceItemOrder: true); return true;
            });
        foreach (var item in stock)
        {
            var input = new TextBox { Text = item.Quantity.ToString(), Width = 120, Height = 40, FontSize = 17 };
            var card = StockAcceptanceRowView.Create(item.ProductName, item.Quantity, 100, input,
                new TextBlock { Text = "Разница: 0", Foreground = Brushes.LightGray });
            cards[item.Id] = card; fields[item.Id] = input; behavior.Register(card, item.Id);
            list.AddTrackedItem(card, () => ((int?)item.Quantity, item.ZeroStockSinceUtc));
        }
        list.SetItemPriority(view =>
        {
            var item = stock.Single(item => ReferenceEquals(cards[item.Id], view));
            return order.IndexOf(item.Id) + (item.Quantity > 0 ? 0 : order.Count);
        });
        window.Content = new AdornerDecorator { Child = new ScrollViewer { Content = list } };
        window.Show(); window.UpdateLayout();
        try
        {
            var grid = (Grid)cards[cola.Id].Child;
            var grip = (Border)grid.Children[0];
            check(behavior.CanStartFrom(grip) && behavior.CanStartFrom(grip.Child), "six-dot grip and its glyph start reorder");
            check(!behavior.CanStartFrom(cards[cola.Id]), "card background cannot start reorder");
            check(!behavior.CanStartFrom(((StackPanel)grid.Children[1]).Children[0]), "product name cannot start reorder");
            check(!behavior.CanStartFrom(fields[cola.Id]), "numeric input never starts reorder");
            check(!behavior.CanStartFrom(((Grid)cards[empty.Id].Child).Children[0]), "out-of-stock grip cannot drag");
            var pressed = typeof(StockAcceptanceDragBehavior).GetField("_pressed", BindingFlags.Instance | BindingFlags.NonPublic)!;
            cards[cola.Id].RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                { RoutedEvent = Mouse.PreviewMouseDownEvent });
            check(pressed.GetValue(behavior) == null, "routed press on card does not arm native drag");
            grip.Child.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                { RoutedEvent = Mouse.PreviewMouseDownEvent });
            check(ReferenceEquals(pressed.GetValue(behavior), cards[cola.Id]), "routed press on six dots arms only that card");
            list.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                { RoutedEvent = Mouse.PreviewMouseUpEvent });
            fields[cola.Id].Text = "7"; fields[cola.Id].Select(0, 1);
            var target = cards[cola.Id];
            var hover = DragArgs(behavior.CreateData(tea.Id), target, false, DragDrop.PreviewDragOverEvent);
            target.RaiseEvent(hover);
            check(saves == 0 && hover.Effects == DragDropEffects.Move, "hover previews but never saves");
            Pump(500);
            var drop = DragArgs(behavior.CreateData(tea.Id), target, false, DragDrop.PreviewDropEvent);
            target.RaiseEvent(drop);
            check(saves == 1 && order[0] == tea.Id && drop.Effects == DragDropEffects.Move, "routed drop saves one move");
            var visible = (Panel)list.Children[0];
            check(ReferenceEquals(visible.Children[0], cards[tea.Id]), "existing card actually changes position");
            list.SetItemPriority(null);
            check(ReferenceEquals(visible.Children[0], cards[tea.Id]), "last applied order survives deferred priority evaluation");
            list.SetItemPriority(view =>
            {
                var item = stock.Single(item => ReferenceEquals(cards[item.Id], view));
                return order.IndexOf(item.Id) + (item.Quantity > 0 ? 0 : order.Count);
            });
            check(fields[cola.Id].Text == "7" && fields[cola.Id].SelectionLength == 1 && list.ItemViews.Count == 3,
                "drag preserves quantity text selection and all rows");
            Pump(260);
            failSave = true;
            cards[tea.Id].RaiseEvent(DragArgs(behavior.CreateData(cola.Id), cards[tea.Id], false, DragDrop.PreviewDragOverEvent));
            Pump(500);
            var failed = DragArgs(behavior.CreateData(cola.Id), cards[tea.Id], false, DragDrop.PreviewDropEvent);
            cards[tea.Id].RaiseEvent(failed);
            check(saves == 1 && order[0] == tea.Id && failed.Effects == DragDropEffects.None, "failed persistence keeps visual order");
            using var foreign = new StockAcceptanceDragBehavior(new StockItemListPanel(), _ => true, (_, _, _) => true);
            var external = DragArgs(foreign.CreateData(cola.Id), cards[tea.Id], false, DragDrop.PreviewDropEvent);
            cards[tea.Id].RaiseEvent(external);
            check(saves == 1 && external.Effects == DragDropEffects.None, "another list cannot reorder this club");
            list.IsOutOfStockExpanded = true; fields[empty.Id].Text = "3";
            list.IsOutOfStockExpanded = false; list.IsOutOfStockExpanded = true;
            check(fields[empty.Id].Text == "3", "expander preserves absent counts after reorder");
            cola.Quantity = 0; cola.ZeroStockSinceUtc = clock.UtcNow;
            list.RefreshGroups();
            check(list.OutOfStockCount == 1 && ReferenceEquals(visible.Children[0], cards[tea.Id]), "zero stock retains existing grace period");
            clock.Advance(TimeSpan.FromDays(1)); list.RefreshGroups();
            check(list.OutOfStockCount == 2, "zero stock folds at existing 24 hour deadline");
            cola.Quantity = 8; cola.ZeroStockSinceUtc = null; list.RefreshGroups();
            check(list.OutOfStockCount == 1 && fields[cola.Id].Text == "7", "restock restores same edited card");
            Render();
        }
        finally { window.Close(); }
    }

    private static void RunMotionUi(Action<bool, string> check)
    {
        var products = Enumerable.Range(1, 4).Select(index => new ProductStockItem
            { ProductName = index == 4 ? "Чай в большой бутылке 1 литр" : "Напиток " + index, Quantity = 10 }).ToArray();
        var order = products.Select(item => item.Id).ToList();
        var list = new StockItemListPanel();
        var cards = new Dictionary<Guid, Border>();
        var fields = new Dictionary<Guid, TextBox>();
        var originals = new Dictionary<Guid, Transform>();
        int saves = 0;
        using var behavior = new StockAcceptanceDragBehavior(list, id => products.Any(item => item.Id == id),
            (source, target, after) =>
            {
                var next = StockAcceptanceOrderPolicy.MoveAvailable(order, products, source, target, after);
                if (next.SequenceEqual(order)) return false;
                order = next; saves++; list.RefreshGroups(forceItemOrder: true); return true;
            });
        foreach (var product in products)
        {
            var input = new TextBox { Text = "9", Width = 120, Height = 40, FontSize = 17 };
            var card = StockAcceptanceRowView.Create(product.ProductName, 10, 100, input,
                new TextBlock { Text = "Недостача: 1 шт / 100 сом", Foreground = Brushes.OrangeRed });
            card.RenderTransform = new ScaleTransform(1, 1);
            fields.Add(product.Id, input); cards.Add(product.Id, card); originals.Add(product.Id, card.RenderTransform);
            behavior.Register(card, product.Id); list.AddItem(card, product.Quantity);
        }
        list.SetItemPriority(view => order.IndexOf(cards.Single(pair => pair.Value == view).Key));
        var viewport = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var root = new AdornerDecorator { Child = new Border { Background = new SolidColorBrush(Color.FromRgb(16, 20, 28)),
            Padding = new Thickness(16), Child = viewport } };
        var window = new Window { Width = 720, Height = 700, Left = -32000, Top = -32000,
            ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None, Content = root };
        window.Show(); window.UpdateLayout();
        try
        {
            Pump(30);
            var source = cards[products[^1].Id];
            var first = cards[products[0].Id];
            double initialY = source.TranslatePoint(new Point(), list).Y;
            check(list.InputHitTest(source.TranslatePoint(new Point(20, source.ActualHeight + 6), list)) != null,
                "spaces between cards remain a continuous drop surface");
            fields[products[^1].Id].Select(0, 1);
            var destination = first.TranslatePoint(new Point(25, first.ActualHeight * 0.3), list);
            byte[] baseline = MotionFrame(root, "motion-before.png");
            list.RaiseEvent(DragArgs(behavior.CreateData(products[^1].Id), list, false, DragDrop.PreviewDragOverEvent, destination));
            check(saves == 0 && order[^1] == products[^1].Id && ((Panel)list.Children[0]).Children[0] == first,
                "motion preview never changes stored or physical order");
            check(source.Opacity == 0 && (AdornerLayer.GetAdornerLayer(list)?.GetAdorners(list)?.Length ?? 0) == 1,
                "drag uses one lifted visual copy rather than duplicate editable controls");
            Pump(55);
            var ghost = AdornerLayer.GetAdornerLayer(list)!.GetAdorners(list)!.Single();
            double intermediate = GhostValue(ghost, "Y");
            double fixedX = GhostValue(ghost, "X");
            check(ghost.Opacity > 0.8 && ghost.Opacity < 1 && (!SystemParameters.ClientAreaAnimation ||
                intermediate > destination.Y - source.ActualHeight / 2 && intermediate < initialY),
                "lifted card is translucent and follows vertically with bounded lag");
            byte[] during = MotionFrame(root, "motion-during.png");
            list.RefreshGroups();
            check(source.Opacity == 0 && order[^1] == products[^1].Id && saves == 0,
                "normal grouping refresh preserves active visual-only drag and unsaved order");
            Pump(440);
            double settled = Offset(first);
            Console.WriteLine($"Acceptance motion: system animation={SystemParameters.ClientAreaAnimation}; followingY={intermediate:F2}, settledNeighbor={settled:F2} DIP.");
            check(settled > 50, "neighbor smoothly yields a reserved slot after leading-edge crossing");
            check(baseline.Where((value, index) => value != during[index]).Take(1000).Count() >= 1000,
                "captured motion frame is visibly different from the list at rest");
            list.RaiseEvent(DragArgs(behavior.CreateData(products[^1].Id), list, false, DragDrop.PreviewDragOverEvent, destination));
            Pump(25);
            check(Math.Abs(Offset(first) - settled) < 0.5 && saves == 0,
                "stationary pointer does not oscillate between animated target rows");
            var originalGhost = AdornerLayer.GetAdornerLayer(list)!.GetAdorners(list)!.Single();
            bool keptGesture = true;
            for (int index = 0; index < 12; index++)
            {
                list.RaiseEvent(DragArgs(behavior.CreateData(products[^1].Id), list, false, DragDrop.PreviewDragLeaveEvent, new Point(-100, -100)));
                list.RaiseEvent(DragArgs(behavior.CreateData(products[^1].Id), list, false, DragDrop.PreviewDragOverEvent, destination));
                keptGesture &= ReferenceEquals(originalGhost, AdornerLayer.GetAdornerLayer(list)!.GetAdorners(list)!.Single());
            }
            check(keptGesture && source.Opacity == 0 && saves == 0,
                "child drag-leave/re-enter cannot cancel and restart a held gesture");
            list.RaiseEvent(DragArgs(behavior.CreateData(products[^1].Id), list, false, DragDrop.PreviewDragOverEvent,
                new Point(destination.X + 300, destination.Y)));
            Pump(35);
            check(Math.Abs(GhostValue(ghost, "X") - fixedX) < 0.01, "floating card never follows horizontal mouse movement");
            MotionFrame(root, "motion-preview.png");
            double inFlightY = GhostValue(ghost, "Y");
            var drop = DragArgs(behavior.CreateData(products[^1].Id), list, false, DragDrop.PreviewDropEvent,
                new Point(destination.X, viewport.ActualHeight - 20));
            list.RaiseEvent(drop);
            check(saves == 1 && order[0] == products[^1].Id && drop.Effects == DragDropEffects.Move,
                "release commits the reserved slot once without selecting again from release coordinates");
            check(Math.Abs(inFlightY - source.TranslatePoint(new Point(), list).Y) > 5 || !SystemParameters.ClientAreaAnimation,
                "released floating card can still be in flight when its landing slot is already chosen");
            Pump(60); MotionFrame(root, "motion-landing.png");
            Pump(240);
            check(source.Opacity == 1 && cards.All(pair => ReferenceEquals(pair.Value.RenderTransform, originals[pair.Key])) &&
                (AdornerLayer.GetAdornerLayer(list)?.GetAdorners(list)?.Length ?? 0) == 0,
                "landing removes ghost animation clocks and restores original transforms and opacity");
            check(fields.All(pair => pair.Value.Text == "9") && fields[products[^1].Id].SelectionLength == 1,
                "animation never changes edited count text or selection");

            var prior = order.ToArray();
            var nextSource = cards[products[0].Id];
            var neighbor = cards[products[1].Id];
            double midpoint = neighbor.TranslatePoint(new Point(0, neighbor.ActualHeight / 2), list).Y;
            double edgePointerY = midpoint - nextSource.ActualHeight / 2;
            list.RaiseEvent(DragArgs(behavior.CreateData(products[0].Id), list, false, DragDrop.PreviewDragOverEvent, new Point(25, edgePointerY - 9)));
            Pump(500);
            check(Math.Abs(Offset(neighbor)) < 0.5, "neighbor keeps its place before floating bottom edge crosses its midpoint");
            list.RaiseEvent(DragArgs(behavior.CreateData(products[0].Id), list, false, DragDrop.PreviewDragOverEvent, new Point(25, edgePointerY + 9)));
            Pump(500);
            check(Offset(neighbor) < -50, "neighbor yields once floating bottom edge passes its midpoint");
            var heldGhost = AdornerLayer.GetAdornerLayer(list)!.GetAdorners(list)!.Single();
            check(GhostValue(heldGhost, "Y") + nextSource.ActualHeight / 2 < midpoint,
                "handoff happens before the floating center arrives at the neighbor midpoint");
            check(GhostValue(heldGhost, "Lift") > 1.02 && heldGhost.Opacity == 0.88,
                "held card lifts toward the viewer and stays slightly translucent");
            double reservedOffset = Offset(neighbor);
            for (int index = 0; index < 12; index++)
            {
                list.RaiseEvent(DragArgs(behavior.CreateData(products[0].Id), list, false, DragDrop.PreviewDragLeaveEvent, new Point()));
                list.RaiseEvent(DragArgs(behavior.CreateData(products[0].Id), list, false, DragDrop.PreviewDragOverEvent, new Point(25, edgePointerY + 9)));
            }
            Pump(260);
            check(ReferenceEquals(heldGhost, AdornerLayer.GetAdornerLayer(list)!.GetAdorners(list)!.Single()) &&
                Math.Abs(Offset(neighbor) - reservedOffset) < 0.5 && saves == 1,
                "midpoint handoff remains stable through repeated native-style target transitions");
            var cancel = (QueryContinueDragEventArgs)Activator.CreateInstance(typeof(QueryContinueDragEventArgs),
                BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { true, DragDropKeyStates.LeftMouseButton }, null)!;
            cancel.RoutedEvent = DragDrop.QueryContinueDragEvent;
            list.RaiseEvent(cancel);
            Pump(280);
            check(saves == 1 && order.SequenceEqual(prior) && cards.All(pair => pair.Value.Opacity == 1 &&
                ReferenceEquals(pair.Value.RenderTransform, originals[pair.Key])), "cancel returns every visual without saving");

            var cancelPoint = source.TranslatePoint(new Point(25, source.ActualHeight * 0.3), list);
            list.RaiseEvent(DragArgs(behavior.CreateData(products[1].Id), list, false, DragDrop.PreviewDragOverEvent, cancelPoint));
            Pump(35);
            behavior.Dispose();
            check(cards.All(pair => pair.Value.Opacity == 1 && ReferenceEquals(pair.Value.RenderTransform, originals[pair.Key])) &&
                (AdornerLayer.GetAdornerLayer(list)?.GetAdorners(list)?.Length ?? 0) == 0 && saves == 1,
                "closing or disposing during a drag cleans visuals immediately without a save");
        }
        finally { window.Close(); }
    }

    private static double Offset(FrameworkElement view) => view.RenderTransform is TransformGroup group
        ? group.Children.OfType<TranslateTransform>().LastOrDefault()?.Y ?? 0 : 0;

    private static double GhostValue(Adorner ghost, string property) =>
        (double)ghost.GetType().GetProperty(property)!.GetValue(ghost)!;

    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame);
    }

    private static byte[] MotionFrame(FrameworkElement root, string name)
    {
        root.UpdateLayout();
        int width = (int)Math.Ceiling(root.ActualWidth), height = (int)Math.Ceiling(root.ActualHeight);
        var image = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        image.Render(root);
        string folder = Path.Combine(Directory.GetCurrentDirectory(), ".codex", "local-notes", "acceptance-order-2026-11-09");
        Directory.CreateDirectory(folder);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using (var file = File.Create(Path.Combine(folder, name))) encoder.Save(file);
        var pixels = new byte[width * height * 4]; image.CopyPixels(pixels, width * 4, 0); return pixels;
    }

    private static DragEventArgs DragArgs(IDataObject data, FrameworkElement target, bool after, RoutedEvent routed, Point? point = null)
    {
        var constructor = typeof(DragEventArgs).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
            .Single(item => item.GetParameters().Any(parameter => parameter.ParameterType == typeof(Point)));
        var arguments = constructor.GetParameters().Select(parameter =>
            parameter.ParameterType == typeof(IDataObject) ? (object)data :
            parameter.ParameterType == typeof(DependencyObject) ? target :
            parameter.ParameterType == typeof(Point) ? point ?? new Point(5, after ? target.ActualHeight - 2 : 2) :
            parameter.ParameterType.IsEnum ? Enum.ToObject(parameter.ParameterType, parameter.Name?.Contains("allowed", StringComparison.OrdinalIgnoreCase) == true ? 2 : 0) :
            (object)(parameter.Name?.Contains("allowed", StringComparison.OrdinalIgnoreCase) == true ? 2 : 0)).ToArray();
        var result = (DragEventArgs)constructor.Invoke(arguments); result.RoutedEvent = routed; return result;
    }

    private static void Render()
    {
        string folder = Path.Combine(Directory.GetCurrentDirectory(), ".codex", "local-notes", "acceptance-order-2026-11-09");
        Directory.CreateDirectory(folder);
        foreach (double scale in new[] { 1d, 1.5d })
        {
            var root = new Border
            {
                Child = StockAcceptanceRowView.Create("Очень длинное название напитка — Кола в большой бутылке 1 литр", 123, 120,
                    new TextBox { Text = "122", Width = 120, Height = 40, FontSize = 17 },
                    new TextBlock { Text = "Недостача: 1 шт / 120 сом", Foreground = Brushes.OrangeRed }),
                Background = new SolidColorBrush(Color.FromRgb(16, 20, 28)), Padding = new Thickness(12)
            };
            double width = 800 / scale;
            root.Measure(new Size(width, double.PositiveInfinity)); root.Arrange(new Rect(0, 0, width, root.DesiredSize.Height)); root.UpdateLayout();
            var image = new RenderTargetBitmap(800, (int)Math.Ceiling(root.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            image.Render(root); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
            using var stream = File.Create(Path.Combine(folder, $"row-{scale}.png")); encoder.Save(stream);
        }
    }
}
