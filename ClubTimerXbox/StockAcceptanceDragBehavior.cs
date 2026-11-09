using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace ClubTimerXbox;

public sealed class StockAcceptanceDragBehavior : IDisposable
{
    public const string DataFormat = "ClubTimer.StockAcceptanceOrder.v1";
    public static readonly DependencyProperty IsDragHandleProperty = DependencyProperty.RegisterAttached(
        "IsDragHandle", typeof(bool), typeof(StockAcceptanceDragBehavior), new PropertyMetadata(false));
    public static bool GetIsDragHandle(DependencyObject element) => (bool)element.GetValue(IsDragHandleProperty);
    public static void SetIsDragHandle(DependencyObject element, bool value) => element.SetValue(IsDragHandleProperty, value);
    public sealed record Payload(StockItemListPanel Owner, Guid ProductId);
    private readonly StockItemListPanel _list;
    private readonly Func<Guid, bool> _available;
    private readonly Func<Guid, Guid, bool, bool> _move;
    private readonly Dictionary<FrameworkElement, Guid> _cards = new();
    private readonly DispatcherTimer _scrollTimer;
    private readonly StockAcceptanceDragVisuals _visuals;
    private readonly bool _transparentBackground;
    private FrameworkElement? _pressed;
    private Point _start;
    private Point _grab;
    private Point _pointer;
    private ScrollViewer? _scroll;
    private int _scrollDirection;
    private bool _disposed;

    public StockAcceptanceDragBehavior(StockItemListPanel list, Func<Guid, bool> available,
        Func<Guid, Guid, bool, bool> move)
    {
        _list = list; _available = available; _move = move;
        _visuals = new StockAcceptanceDragVisuals(list,
            card => _cards.TryGetValue(card, out var id) && _available(id));
        _transparentBackground = list.Background == null;
        if (_transparentBackground) list.SetCurrentValue(Panel.BackgroundProperty, Brushes.Transparent);
        _list.AllowDrop = true;
        _list.PreviewMouseLeftButtonDown += MouseDown;
        _list.PreviewMouseMove += MouseMove;
        _list.PreviewMouseLeftButtonUp += MouseUp;
        _list.PreviewDragOver += DragOver;
        _list.PreviewDrop += Drop;
        _list.PreviewDragLeave += DragLeave;
        _list.QueryContinueDrag += ContinueDrag;
        _list.Unloaded += Unloaded;
        _scrollTimer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(80) };
        _scrollTimer.Tick += (_, _) =>
        {
            if (_scroll == null || !_visuals.IsDragging) { _scrollTimer.Stop(); return; }
            double before = _scroll.VerticalOffset;
            _scroll.ScrollToVerticalOffset(before + 18 * _scrollDirection);
            _scroll.UpdateLayout();
            double distance = _scroll.VerticalOffset - before;
            _pointer.Y += distance;
            _visuals.ShiftForScroll(distance);
            _visuals.Preview(_pointer, out _, out _);
        };
    }

    public void Register(FrameworkElement card, Guid id) => _cards.Add(card, id);
    public DataObject CreateData(Guid id) => new(DataFormat, new Payload(_list, id));

    public bool CanStartFrom(DependencyObject? source)
    {
        bool fromHandle = false;
        for (var node = source; node != null && node != _list; node = Parent(node))
        {
            if (node is TextBoxBase or PasswordBox or ButtonBase or Selector or Thumb) return false;
            fromHandle |= GetIsDragHandle(node);
            if (node is FrameworkElement card && _cards.TryGetValue(card, out var id)) return fromHandle && _available(id);
        }
        return false;
    }

    private FrameworkElement? Card(DependencyObject? source)
    {
        for (var node = source; node != null && node != _list; node = Parent(node))
            if (node is FrameworkElement card && _cards.TryGetValue(card, out var id)) return _available(id) ? card : null;
        return null;
    }

    private static DependencyObject? Parent(DependencyObject node) => node switch
    {
        Visual => VisualTreeHelper.GetParent(node),
        FrameworkContentElement element => element.Parent,
        _ => LogicalTreeHelper.GetParent(node)
    };

    private void MouseDown(object sender, MouseButtonEventArgs e)
    {
        _pressed = CanStartFrom(e.OriginalSource as DependencyObject) ? Card(e.OriginalSource as DependencyObject) : null;
        _start = e.GetPosition(_list);
        if (_pressed != null) _grab = e.GetPosition(_pressed);
    }
    private void MouseUp(object sender, MouseButtonEventArgs e) => _pressed = null;
    private void MouseMove(object sender, MouseEventArgs e)
    {
        if (_pressed == null || e.LeftButton != MouseButtonState.Pressed) return;
        var position = e.GetPosition(_list);
        if (Math.Abs(position.X - _start.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - _start.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var card = _pressed; _pressed = null;
        _visuals.Begin(card, _grab);
        try { DragDrop.DoDragDrop(card, CreateData(_cards[card]), DragDropEffects.Move); }
        finally { ClearDrag(); }
        e.Handled = true;
    }

    private bool TryPayload(IDataObject data, out Payload payload)
    {
        payload = null!;
        try
        {
            if (data.GetDataPresent(DataFormat, false) && data.GetData(DataFormat, false) is Payload value &&
                ReferenceEquals(value.Owner, _list) && _available(value.ProductId)) { payload = value; return true; }
        }
        catch { /* Ignore unrelated or unavailable OLE formats. */ }
        return false;
    }

    private void DragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormat, false)) return;
        e.Handled = true; e.Effects = DragDropEffects.None;
        if (!TryPayload(e.Data, out var payload)) { ClearDrag(); return; }
        if (!EnsureVisuals(payload)) return;
        _pointer = e.GetPosition(_list);
        if (_visuals.Preview(_pointer, out _, out _)) e.Effects = DragDropEffects.Move;
        UpdateScroll(e);
    }

    private bool EnsureVisuals(Payload payload)
    {
        var card = _cards.FirstOrDefault(pair => pair.Value == payload.ProductId).Key;
        if (card == null || !card.IsVisible) return false;
        if (_visuals.Source == card && !_visuals.IsDragging) return false;
        if (_visuals.Source != card) _visuals.Begin(card);
        return true;
    }

    private ScrollViewer? FindScroll()
    {
        for (var node = Parent(_list); node != null; node = Parent(node))
            if (node is ScrollViewer scroll) return scroll;
        return null;
    }

    private void UpdateScroll(DragEventArgs e)
    {
        _scroll = FindScroll();
        double y = _scroll == null ? 0 : e.GetPosition(_scroll).Y;
        _scrollDirection = _scroll == null ? 0 : y < 40 ? -1 : y > _scroll.ActualHeight - 40 ? 1 : 0;
        if (_scrollDirection == 0) _scrollTimer.Stop(); else _scrollTimer.Start();
    }

    private void Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormat, false)) return;
        e.Handled = true; e.Effects = DragDropEffects.None;
        _scrollTimer.Stop();
        _scrollDirection = 0;
        _scroll = null;
        try
        {
            if (TryPayload(e.Data, out var payload) && EnsureVisuals(payload) &&
                _visuals.TryDestination(out var target, out bool after) && _available(_cards[target]))
            {
                _visuals.StopFollowing();
                var positions = _visuals.CapturePositions();
                bool committed = _move(payload.ProductId, _cards[target], after);
                if (committed) e.Effects = DragDropEffects.Move;
                _visuals.Finish(committed, positions);
            }
        }
        finally { ClearDrag(); }
    }

    private void DragLeave(object sender, DragEventArgs e)
    {
        // WPF also raises leave while targets move beneath a held pointer. Only
        // a drop, explicit cancel or the end of DoDragDrop may finish the gesture.
        _scrollTimer.Stop();
        _scrollDirection = 0;
    }
    private void ContinueDrag(object sender, QueryContinueDragEventArgs e)
    {
        if (e.EscapePressed || e.Action == DragAction.Cancel) ClearDrag();
    }
    private void Unloaded(object sender, RoutedEventArgs e) => ClearDrag(immediate: true);
    private void ClearDrag(bool immediate = false)
    {
        _pressed = null; _scrollTimer.Stop(); _scrollDirection = 0; _scroll = null;
        if (immediate) _visuals.Dispose();
        else if (_visuals.IsDragging) _visuals.Finish(false);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; ClearDrag(immediate: true);
        _list.PreviewMouseLeftButtonDown -= MouseDown;
        _list.PreviewMouseMove -= MouseMove;
        _list.PreviewMouseLeftButtonUp -= MouseUp;
        _list.PreviewDragOver -= DragOver;
        _list.PreviewDrop -= Drop;
        _list.PreviewDragLeave -= DragLeave;
        _list.QueryContinueDrag -= ContinueDrag;
        _list.Unloaded -= Unloaded;
        if (_transparentBackground && ReferenceEquals(_list.Background, Brushes.Transparent))
            _list.SetCurrentValue(Panel.BackgroundProperty, null);
        _cards.Clear();
    }

}
