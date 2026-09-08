using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ClubTimerXbox.Services;

namespace ClubTimerXbox
{
    public sealed class StockItemListPanel : StackPanel
    {
        private readonly Panel _availableItems;
        private readonly Panel _emptyItems;
        private readonly Expander _emptySection;
        private readonly TextBlock _header;
        private readonly List<UIElement> _itemViews = new();
        private readonly List<Entry> _entries = new();
        private readonly DispatcherTimer _timer;

        private sealed record Entry(UIElement View,
            Func<(int? Quantity, DateTime? ZeroSinceUtc)> ReadState, Func<bool>? HasDraft);

        public StockItemListPanel(bool wrapItems = false)
        {
            _availableItems = wrapItems ? new WrapPanel() : new StackPanel();
            _emptyItems = wrapItems ? new WrapPanel() : new StackPanel();
            _header = new TextBlock
            {
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(6, 8, 6, 8)
            };
            _emptySection = new Expander
            {
                Header = _header,
                Content = _emptyItems,
                IsExpanded = false,
                Visibility = Visibility.Collapsed,
                Foreground = Brushes.White,
                Background = Brushes.Transparent,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 4, 0, 12)
            };
            _emptySection.SetResourceReference(Control.ForegroundProperty, "Theme.TextBrush");
            _emptySection.Expanded += (_, _) => UpdateHeader();
            _emptySection.Collapsed += (_, _) => UpdateHeader();
            Children.Add(_availableItems);
            Children.Add(_emptySection);
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (_, _) => RefreshGroups();
            Loaded += (_, _) => { RefreshGroups(); _timer.Start(); };
            Unloaded += (_, _) => _timer.Stop();
        }

        public IReadOnlyList<UIElement> ItemViews => _itemViews;
        public int OutOfStockCount => _emptyItems.Children.Count;
        public bool IsOutOfStockExpanded
        {
            get => _emptySection.IsExpanded;
            set => _emptySection.IsExpanded = value;
        }

        public void ClearItems()
        {
            _availableItems.Children.Clear();
            _emptyItems.Children.Clear();
            _itemViews.Clear();
            _entries.Clear();
            _emptySection.IsExpanded = false;
            UpdateHeader();
        }

        // Null means no stock restriction (services and the purchase catalog).
        public void AddItem(UIElement view, int? quantity, DateTime? zeroSinceUtc = null)
        {
            AddTrackedItem(view, () => (quantity, zeroSinceUtc));
        }

        public void AddTrackedItem(UIElement view,
            Func<(int? Quantity, DateTime? ZeroSinceUtc)> readState, Func<bool>? hasDraft = null)
        {
            _itemViews.Add(view);
            _entries.Add(new Entry(view, readState, hasDraft));
            RefreshGroups();
        }

        public void RefreshGroups()
        {
            DateTime now = ClubClock.Current.UtcNow;
            var states = _entries.Select(entry =>
            {
                var state = entry.ReadState();
                DateTime? deadline = StockVisibilityPolicy.FoldAfterUtc(state.Quantity, state.ZeroSinceUtc);
                bool hidden = deadline.HasValue && deadline.Value <= now;
                if (entry.View.IsKeyboardFocusWithin || entry.HasDraft?.Invoke() == true)
                    hidden = _emptyItems.Children.Contains(entry.View);
                return (entry.View, Deadline: deadline, Hidden: hidden);
            }).ToList();
            var visible = states.Where(item => !item.Hidden).Select(item => item.View).ToList();
            var hidden = states.Where(item => item.Hidden)
                .OrderByDescending(item => item.Deadline).Select(item => item.View).ToList();

            // Move existing controls only; a timer must never rebuild acceptance inputs.
            RemoveMissing(_availableItems, visible);
            RemoveMissing(_emptyItems, hidden);
            ArrangeItems(_availableItems, visible);
            ArrangeItems(_emptyItems, hidden);
            UpdateHeader();
        }

        private static void RemoveMissing(Panel panel, List<UIElement> desired)
        {
            foreach (UIElement child in panel.Children.Cast<UIElement>().ToArray())
                if (!desired.Contains(child)) panel.Children.Remove(child);
        }

        private static void ArrangeItems(Panel panel, List<UIElement> desired)
        {
            for (int i = 0; i < desired.Count; i++)
            {
                UIElement child = desired[i];
                if (panel.Children.IndexOf(child) == i) continue;
                panel.Children.Remove(child);
                panel.Children.Insert(i, child);
            }
        }

        public void RevealInput(FrameworkElement input)
        {
            if (_emptyItems.IsAncestorOf(input))
                _emptySection.IsExpanded = true;
            UpdateLayout();
            input.BringIntoView();
            input.Focus();
        }

        private void UpdateHeader()
        {
            _emptySection.Visibility = OutOfStockCount == 0 ? Visibility.Collapsed : Visibility.Visible;
            _header.Text = $"Нет в наличии ({OutOfStockCount}) · " +
                (_emptySection.IsExpanded ? "Свернуть" : "Развернуть");
            _emptySection.ToolTip = _emptySection.IsExpanded
                ? "Свернуть закончившиеся товары" : "Показать закончившиеся товары";
        }
    }
}
