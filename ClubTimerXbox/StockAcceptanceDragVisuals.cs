using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace ClubTimerXbox;

internal sealed class StockAcceptanceDragVisuals : IDisposable
{
    private readonly StockItemListPanel _list;
    private readonly Func<FrameworkElement, bool> _available;
    private readonly Dictionary<FrameworkElement, Motion> _motions = new();
    private FrameworkElement? _source;
    private double _sourceOpacity;
    private Point _grab;
    private GhostAdorner? _ghost;
    private AdornerLayer? _layer;
    private AnimationClock? _landing;
    private bool _finishing;
    private List<Slot> _slots = new();
    private List<Slot> _preview = new();
    private double _targetY;
    private bool _following;
    private TimeSpan? _lastFrame;

    private sealed record Slot(FrameworkElement Card, Rect Bounds, double CardTop);
    public bool IsDragging => _source != null && !_finishing;
    public FrameworkElement? Source => _source;

    public StockAcceptanceDragVisuals(StockItemListPanel list, Func<FrameworkElement, bool> available)
    {
        _list = list;
        _available = available;
    }

    public void Begin(FrameworkElement source, Point? grab = null)
    {
        Reset();
        _list.UpdateLayout();
        _source = source;
        _sourceOpacity = source.Opacity;
        _grab = grab ?? new Point(source.ActualWidth / 2, source.ActualHeight / 2);
        _slots = Slots();
        _preview = _slots.ToList();
        var origin = LayoutPosition(source);
        _targetY = origin.Y;
        _layer = AdornerLayer.GetAdornerLayer(_list);
        if (_layer == null || source.ActualWidth <= 0 || source.ActualHeight <= 0) return;

        var dpi = VisualTreeHelper.GetDpi(source);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(source.ActualWidth * dpi.DpiScaleX),
            (int)Math.Ceiling(source.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
            context.DrawRectangle(new VisualBrush(source) { Stretch = Stretch.Fill }, null,
                new Rect(0, 0, source.ActualWidth, source.ActualHeight));
        bitmap.Render(drawing);
        bitmap.Freeze();
        _ghost = new GhostAdorner(_list, bitmap, source.RenderSize)
        {
            X = origin.X, Y = origin.Y, Opacity = 0.88
        };
        _layer.Add(_ghost);
        source.SetCurrentValue(UIElement.OpacityProperty, 0d);
        if (SystemParameters.ClientAreaAnimation)
            _ghost.BeginAnimation(GhostAdorner.LiftProperty, Animation(1, 1.035, 130));
        else _ghost.Lift = 1.035;
    }

    public bool Preview(Point pointer, out FrameworkElement target, out bool after)
    {
        if (IsDragging && _source != null)
        {
            _targetY = pointer.Y - _grab.Y;
            var viewport = ViewportBounds(_list);
            if (viewport.HasValue)
            {
                double minimumTop = viewport.Value.Top + 8 - _source.ActualHeight / 2;
                _targetY = Math.Clamp(_targetY, minimumTop, Math.Max(minimumTop, viewport.Value.Bottom - _source.ActualHeight / 2 - 8));
            }
            if (_ghost == null || !SystemParameters.ClientAreaAnimation)
            {
                if (_ghost != null) _ghost.Y = _targetY;
                UpdatePreviewOrder();
            }
            else if (Math.Abs(_targetY - _ghost.Y) > 0.15 && !_following)
            {
                _following = true;
                _lastFrame = null;
                CompositionTarget.Rendering += FollowPointer;
            }
        }
        return TryDestination(out target, out after);
    }

    public bool TryDestination(out FrameworkElement target, out bool after)
    {
        target = null!;
        after = false;
        if (!IsDragging || _preview.Count < 2) return false;
        int index = _preview.FindIndex(slot => slot.Card == _source);
        if (index < 0) return false;
        after = index == _preview.Count - 1;
        target = _preview[after ? index - 1 : index + 1].Card;
        return true;
    }

    private void FollowPointer(object? sender, EventArgs e)
    {
        if (!IsDragging || _ghost == null) { StopFollowing(); return; }
        var time = ((RenderingEventArgs)e).RenderingTime;
        double elapsed = _lastFrame.HasValue ? Math.Clamp((time - _lastFrame.Value).TotalSeconds, 0, 0.05) : 1d / 60;
        _lastFrame = time;
        _ghost.Y += (_targetY - _ghost.Y) * (1 - Math.Exp(-elapsed / 0.07));
        if (Math.Abs(_targetY - _ghost.Y) <= 0.15)
        {
            _ghost.Y = _targetY;
            StopFollowing();
        }
        UpdatePreviewOrder();
    }

    // The leading edge crosses a neighbor's reserved midpoint, not its moving
    // animation position. The reserved slot stays selected until another crossing.
    private void UpdatePreviewOrder()
    {
        if (_source == null || _preview.Count < 2) return;
        double lift = _ghost?.Lift ?? 1;
        double topEdge = (_ghost?.Y ?? _targetY) - _source.ActualHeight * (lift - 1) / 2;
        double bottomEdge = topEdge + _source.ActualHeight * lift;
        int index = _preview.FindIndex(slot => slot.Card == _source);
        bool changed = false;
        while (index > 0 && topEdge <= PreviewCenter(index - 1))
        {
            (_preview[index], _preview[index - 1]) = (_preview[index - 1], _preview[index]);
            index--; changed = true;
        }
        while (index >= 0 && index + 1 < _preview.Count && bottomEdge >= PreviewCenter(index + 1))
        {
            (_preview[index], _preview[index + 1]) = (_preview[index + 1], _preview[index]);
            index++; changed = true;
        }
        if (!changed || _ghost == null) return;
        double top = _slots[0].Bounds.Top;
        foreach (var slot in _preview)
        {
            if (slot.Card != _source) MotionFor(slot.Card).Move(top - slot.Bounds.Top);
            top += slot.Bounds.Height;
        }
    }

    private double PreviewCenter(int index) => _slots[0].Bounds.Top +
        _preview.Take(index).Sum(slot => slot.Bounds.Height) + _preview[index].CardTop + _preview[index].Card.ActualHeight / 2;

    public void ShiftForScroll(double distance)
    {
        if (_ghost != null) _ghost.Y += distance;
        _targetY += distance;
    }

    public void StopFollowing()
    {
        if (_following) CompositionTarget.Rendering -= FollowPointer;
        _following = false;
        _lastFrame = null;
    }

    public Dictionary<FrameworkElement, Point> CapturePositions() => _slots
        .ToDictionary(slot => slot.Card, slot => slot.Card.TranslatePoint(new Point(), _list));

    public void Finish(bool committed, IReadOnlyDictionary<FrameworkElement, Point>? positions = null)
    {
        if (!IsDragging) return;
        StopFollowing();
        _finishing = true;
        _list.UpdateLayout();
        foreach (var pair in _motions)
        {
            if (committed && positions != null && positions.TryGetValue(pair.Key, out var position))
                pair.Value.Jump(position.Y - LayoutPosition(pair.Key).Y);
            pair.Value.Move(0);
        }
        if (_ghost == null || !SystemParameters.ClientAreaAnimation)
        {
            Dispose();
            return;
        }
        var destination = LayoutPosition(_source!);
        _ghost.X = destination.X;
        _ghost.BeginAnimation(GhostAdorner.YProperty, Animation(_ghost.Y, destination.Y, 190));
        _ghost.BeginAnimation(GhostAdorner.LiftProperty, Animation(_ghost.Lift, 1, 190));
        // The controllable clock owns cleanup even when no mouse event follows the drop.
        _landing = new DoubleAnimation(_ghost.Opacity, 1, TimeSpan.FromMilliseconds(200)).CreateClock();
        _landing.Completed += Landed;
        _ghost.ApplyAnimationClock(UIElement.OpacityProperty, _landing);
    }

    private void Landed(object? sender, EventArgs e)
    {
        if (ReferenceEquals(sender, _landing)) Dispose();
    }

    private List<Slot> Slots()
    {
        if (_source == null || VisualTreeHelper.GetParent(_source) is not Panel parent) return new();
        return parent.Children.OfType<FrameworkElement>().Where(card => _available(card) && card.IsVisible)
            .Select(card =>
            {
                var slot = LayoutInformation.GetLayoutSlot(card);
                var bounds = parent.TransformToAncestor(_list).TransformBounds(slot);
                return new Slot(card, bounds, VisualTreeHelper.GetOffset(card).Y - slot.Top);
            }).OrderBy(slot => slot.Bounds.Top).ToList();
    }

    private Point LayoutPosition(FrameworkElement card)
    {
        var parent = (Visual)VisualTreeHelper.GetParent(card);
        return parent.TransformToAncestor(_list).Transform((Point)VisualTreeHelper.GetOffset(card));
    }

    private Motion MotionFor(FrameworkElement card)
    {
        if (!_motions.TryGetValue(card, out var motion)) _motions.Add(card, motion = new Motion(card));
        return motion;
    }

    private static DoubleAnimation Animation(double from, double to, int milliseconds) => new(from, to,
        TimeSpan.FromMilliseconds(milliseconds))
    {
        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.HoldEnd
    };

    private static Rect? ViewportBounds(UIElement owner)
    {
        for (DependencyObject? node = owner; node != null; node = VisualTreeHelper.GetParent(node))
            if (node is ScrollViewer scroll)
                return new Rect(scroll.TranslatePoint(new Point(), owner), scroll.RenderSize);
        return null;
    }

    public void Dispose() => Reset();

    private void Reset()
    {
        StopFollowing();
        if (_landing != null)
        {
            _landing.Completed -= Landed;
            _landing.Controller?.Remove();
            _landing = null;
        }
        if (_ghost != null)
        {
            _ghost.BeginAnimation(GhostAdorner.XProperty, null);
            _ghost.BeginAnimation(GhostAdorner.YProperty, null);
            _ghost.BeginAnimation(GhostAdorner.LiftProperty, null);
            _ghost.BeginAnimation(UIElement.OpacityProperty, null);
            _layer?.Remove(_ghost);
        }
        _ghost = null;
        _layer = null;
        if (_source != null) _source.SetCurrentValue(UIElement.OpacityProperty, _sourceOpacity);
        foreach (var motion in _motions.Values) motion.Dispose();
        _motions.Clear();
        _slots.Clear();
        _preview.Clear();
        _source = null;
        _finishing = false;
    }

    private sealed class Motion : IDisposable
    {
        private readonly FrameworkElement _view;
        private readonly Transform _original;
        private readonly TranslateTransform _offset = new();
        private readonly TransformGroup _group = new();
        private double _target;

        public Motion(FrameworkElement view)
        {
            _view = view;
            _original = view.RenderTransform;
            _group.Children.Add(_original);
            _group.Children.Add(_offset);
            view.SetCurrentValue(UIElement.RenderTransformProperty, _group);
        }

        public void Jump(double value)
        {
            _offset.BeginAnimation(TranslateTransform.YProperty, null);
            _offset.Y = _target = value;
        }

        public void Move(double value)
        {
            if (Math.Abs(_target - value) < 0.1) return;
            _target = value;
            if (SystemParameters.ClientAreaAnimation)
                _offset.BeginAnimation(TranslateTransform.YProperty, Animation(_offset.Y, value, 180));
            else Jump(value);
        }

        public void Dispose()
        {
            _offset.BeginAnimation(TranslateTransform.YProperty, null);
            if (ReferenceEquals(_view.RenderTransform, _group))
                _view.SetCurrentValue(UIElement.RenderTransformProperty, _original);
        }
    }

    private sealed class GhostAdorner : Adorner
    {
        public static readonly DependencyProperty XProperty = PositionProperty(nameof(X), 0);
        public static readonly DependencyProperty YProperty = PositionProperty(nameof(Y), 0);
        public static readonly DependencyProperty LiftProperty = PositionProperty(nameof(Lift), 1);
        private static DependencyProperty PositionProperty(string name, double initial) => DependencyProperty.Register(
            name, typeof(double), typeof(GhostAdorner), new FrameworkPropertyMetadata(initial, FrameworkPropertyMetadataOptions.AffectsRender));
        private readonly ImageSource _image;
        private readonly Size _size;
        public double X { get => (double)GetValue(XProperty); set => SetValue(XProperty, value); }
        public double Y { get => (double)GetValue(YProperty); set => SetValue(YProperty, value); }
        public double Lift { get => (double)GetValue(LiftProperty); set => SetValue(LiftProperty, value); }

        public GhostAdorner(UIElement owner, ImageSource image, Size size) : base(owner)
        {
            _image = image; _size = size; IsHitTestVisible = false;
        }

        protected override void OnRender(DrawingContext context)
        {
            var viewport = ViewportBounds(AdornedElement);
            if (viewport.HasValue)
            {
                var clip = viewport.Value;
                clip.Inflate(12, 0);
                context.PushClip(new RectangleGeometry(clip));
            }
            var bounds = new Rect(new Point(X, Y), _size);
            context.PushTransform(new ScaleTransform(Lift, Lift, bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2));
            context.DrawImage(_image, bounds);
            context.DrawRoundedRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(180, 125, 211, 252)), 1.5), bounds, 14, 14);
            context.Pop();
            if (viewport.HasValue) context.Pop();
        }
    }
}
