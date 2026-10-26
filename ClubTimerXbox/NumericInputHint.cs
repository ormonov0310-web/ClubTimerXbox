using System;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace ClubTimerXbox
{
    public static class NumericInputHint
    {
        public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
            "Enabled", typeof(bool), typeof(NumericInputHint),
            new PropertyMetadata(false, OnEnabledChanged));

        private static readonly DependencyProperty AdornmentProperty = DependencyProperty.RegisterAttached(
            "Adornment", typeof(ZeroAdornment), typeof(NumericInputHint));

        public static bool GetEnabled(DependencyObject element) => (bool)element.GetValue(EnabledProperty);
        public static void SetEnabled(DependencyObject element, bool value) => element.SetValue(EnabledProperty, value);

        public static void Apply(TextBox input, string label)
        {
            SetEnabled(input, true);
            if (string.IsNullOrEmpty(AutomationProperties.GetName(input)))
                AutomationProperties.SetName(input, label);
        }

        private static void OnEnabledChanged(DependencyObject element, DependencyPropertyChangedEventArgs args)
        {
            if (element is not TextBox input) return;
            if ((bool)args.NewValue)
            {
                input.Loaded += OnLoaded;
                input.Unloaded += OnUnloaded;
                input.TextChanged += OnTextChanged;
                input.SizeChanged += OnSizeChanged;
                Refresh(input);
            }
            else
            {
                input.Loaded -= OnLoaded;
                input.Unloaded -= OnUnloaded;
                input.TextChanged -= OnTextChanged;
                input.SizeChanged -= OnSizeChanged;
                Remove(input);
            }
        }

        private static void OnLoaded(object sender, RoutedEventArgs args) => Refresh((TextBox)sender);
        private static void OnUnloaded(object sender, RoutedEventArgs args) => Remove((TextBox)sender);
        private static void OnTextChanged(object sender, TextChangedEventArgs args) => Refresh((TextBox)sender);
        private static void OnSizeChanged(object sender, SizeChangedEventArgs args) => Refresh((TextBox)sender);

        private static void Refresh(TextBox input)
        {
            if (!input.IsLoaded) return;
            var adornment = input.GetValue(AdornmentProperty) as ZeroAdornment;
            if (adornment == null)
            {
                var layer = AdornerLayer.GetAdornerLayer(input);
                if (layer == null) return;
                adornment = new ZeroAdornment(input, layer);
                input.SetValue(AdornmentProperty, adornment);
                layer.Add(adornment);
            }
            adornment.Visibility = input.Text.Length == 0 ? Visibility.Visible : Visibility.Hidden;
            adornment.InvalidateVisual();
        }

        private static void Remove(TextBox input)
        {
            if (input.GetValue(AdornmentProperty) is not ZeroAdornment adornment) return;
            adornment.Layer.Remove(adornment);
            input.ClearValue(AdornmentProperty);
        }

        private sealed class ZeroAdornment : Adorner
        {
            public AdornerLayer Layer { get; }
            private readonly TextBox _input;

            public ZeroAdornment(TextBox input, AdornerLayer layer) : base(input)
            {
                _input = input;
                Layer = layer;
                IsHitTestVisible = false;
                Focusable = false;
            }

            protected override void OnRender(DrawingContext drawingContext)
            {
                if (_input.Text.Length != 0) return;
                double left = _input.Padding.Left + _input.BorderThickness.Left;
                double top = _input.Padding.Top + _input.BorderThickness.Top;
                double width = Math.Max(1, _input.ActualWidth - left - _input.Padding.Right - _input.BorderThickness.Right);
                var text = new FormattedText("0", CultureInfo.CurrentUICulture, _input.FlowDirection,
                    new Typeface(_input.FontFamily, _input.FontStyle, _input.FontWeight, _input.FontStretch),
                    _input.FontSize, Brushes.SlateGray, VisualTreeHelper.GetDpi(_input).PixelsPerDip)
                {
                    MaxTextWidth = width,
                    TextAlignment = _input.TextAlignment
                };
                if (_input.VerticalContentAlignment == VerticalAlignment.Center)
                    top = Math.Max(top, (_input.ActualHeight - text.Height) / 2);
                drawingContext.PushClip(new RectangleGeometry(new Rect(_input.RenderSize)));
                drawingContext.DrawText(text, new Point(left, top));
                drawingContext.Pop();
            }
        }
    }
}
