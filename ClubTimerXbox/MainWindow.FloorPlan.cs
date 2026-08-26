using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ClubTimerXbox.Models;
using ClubTimerXbox.Services;

namespace ClubTimerXbox
{
    public partial class MainWindow
    {
        private const double FloorPlanCardWidth = 142;
        private const double FloorPlanCardHeight = 108;

        private readonly Dictionary<string, FloorPlanCardVisual> _floorPlanVisuals =
            new Dictionary<string, FloorPlanCardVisual>(StringComparer.OrdinalIgnoreCase);
        private readonly DispatcherTimer _floorPlanSelectionAnimationTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(90)
        };

        private FloorPlanLayoutState _floorPlanLayout = new FloorPlanLayoutState();
        private PlacesDisplayMode _placesDisplayMode = PlacesDisplayMode.Classic;
        private string? _selectedFloorPlanPlaceName;
        private bool _floorPlanEditorEnabled;
        private Border? _draggedFloorPlanCard;
        private string? _draggedFloorPlanPlaceName;
        private Point _floorPlanDragOffset;

        private void InitializeFloorPlanView()
        {
            _floorPlanLayout = FloorPlanLayoutService.LoadCurrent();
            _placesDisplayMode = _floorPlanLayout.DisplayMode;
            _floorPlanSelectionAnimationTimer.Tick += (_, _) =>
                UpdateSelectedFloorPlanBorder();
            UpdateFloorPlanEditorState();
        }

        private void PlacesViewButton_PreviewMouseRightButtonUp(
            object sender,
            MouseButtonEventArgs e)
        {
            var menu = new ContextMenu();
            menu.Items.Add(CreatePlacesDisplayModeMenuItem(
                "Классический",
                PlacesDisplayMode.Classic
            ));
            menu.Items.Add(CreatePlacesDisplayModeMenuItem(
                "Альтернативный",
                PlacesDisplayMode.Alternative
            ));
            menu.PlacementTarget = PlacesViewButton;
            menu.IsOpen = true;
            e.Handled = true;
        }

        private MenuItem CreatePlacesDisplayModeMenuItem(
            string title,
            PlacesDisplayMode mode)
        {
            var item = new MenuItem
            {
                Header = title,
                IsCheckable = true,
                IsChecked = _placesDisplayMode == mode
            };

            item.Click += (_, _) => SetPlacesDisplayMode(mode);
            return item;
        }

        private void SetPlacesDisplayMode(PlacesDisplayMode mode)
        {
            if (_placesDisplayMode == mode && !_isTuyaDevicesView)
                return;

            _placesDisplayMode = mode;
            _floorPlanLayout.DisplayMode = mode;
            FloorPlanLayoutService.SaveCurrent(_floorPlanLayout);

            if (mode != PlacesDisplayMode.Alternative)
                SetFloorPlanEditorEnabled(false);

            DrawPlaces();
        }

        private void ShowClassicPlacesHost()
        {
            _floorPlanSelectionAnimationTimer.Stop();
            ClassicPlacesScrollViewer.Visibility = Visibility.Visible;
            AlternativePlacesGrid.Visibility = Visibility.Collapsed;
        }

        private void ShowAlternativePlacesHost()
        {
            ClassicPlacesScrollViewer.Visibility = Visibility.Collapsed;
            AlternativePlacesGrid.Visibility = Visibility.Visible;
        }

        private void DrawAlternativePlaces()
        {
            ShowAlternativePlacesHost();
            EnsureFloorPlanPositions();
            EnsureFloorPlanCards();
            UpdateFloorPlanCards();

            if (!string.IsNullOrWhiteSpace(_selectedFloorPlanPlaceName) &&
                !_places.Any(place => place.Name.Equals(
                    _selectedFloorPlanPlaceName,
                    StringComparison.OrdinalIgnoreCase)))
            {
                _selectedFloorPlanPlaceName = null;
            }

            UpdateFloorPlanSelection();
            UpdateAlternativePlaceDetails();

            Dispatcher.BeginInvoke(
                DispatcherPriority.Loaded,
                new Action(PositionFloorPlanCards)
            );
        }

        private void EnsureFloorPlanCards()
        {
            bool needsRebuild = _floorPlanVisuals.Count != _places.Count ||
                                _places.Any(place =>
                                    !_floorPlanVisuals.TryGetValue(place.Name, out FloorPlanCardVisual? visual) ||
                                    !ReferenceEquals(visual.Place, place));

            if (!needsRebuild)
                return;

            FloorPlanCanvas.Children.Clear();
            _floorPlanVisuals.Clear();

            foreach (ClubPlace place in _places)
            {
                FloorPlanCardVisual visual = CreateFloorPlanCard(place);
                _floorPlanVisuals[place.Name] = visual;
                FloorPlanCanvas.Children.Add(visual.Card);
            }
        }

        private FloorPlanCardVisual CreateFloorPlanCard(ClubPlace place)
        {
            var titleText = new TextBlock
            {
                Text = place.Name,
                Foreground = Brushes.White,
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center
            };

            var additionalPositionsText = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(17, 24, 39)),
                FontSize = 11,
                FontWeight = FontWeights.ExtraBold,
                VerticalAlignment = VerticalAlignment.Center
            };

            var additionalPositionsBadge = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(251, 191, 36)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(253, 224, 71)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(5, 2, 5, 2),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed,
                Child = additionalPositionsText
            };

            var titleGrid = new Grid();
            titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            titleGrid.Children.Add(titleText);
            Grid.SetColumn(additionalPositionsBadge, 1);
            titleGrid.Children.Add(additionalPositionsBadge);

            var statusText = new TextBlock
            {
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 6, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            var timeText = new TextBlock
            {
                Foreground = Brushes.White,
                FontSize = 27,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 7, 0, 0)
            };

            var stack = new StackPanel();
            stack.Children.Add(titleGrid);
            stack.Children.Add(statusText);
            stack.Children.Add(timeText);

            var card = new Border
            {
                Width = FloorPlanCardWidth,
                Height = FloorPlanCardHeight,
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12, 10, 12, 10),
                BorderThickness = new Thickness(1),
                Background = GetCardBackground(place),
                BorderBrush = GetCardBorderBrush(place),
                Cursor = Cursors.Hand,
                Child = stack
            };

            card.ContextMenu = CreateContextMenu(place);
            card.ContextMenuOpening += (_, _) =>
            {
                card.ContextMenu = CreateContextMenu(place);
            };
            card.MouseLeftButtonDown += (_, e) =>
                FloorPlanCard_MouseLeftButtonDown(card, place, e);
            card.MouseMove += (_, e) => FloorPlanCard_MouseMove(card, e);
            card.MouseLeftButtonUp += (_, e) => FloorPlanCard_MouseLeftButtonUp(card, e);
            card.LostMouseCapture += (_, _) => EndFloorPlanDrag(card, save: true);

            return new FloorPlanCardVisual(
                place,
                card,
                statusText,
                timeText,
                additionalPositionsBadge,
                additionalPositionsText
            );
        }

        private void UpdateFloorPlanCards()
        {
            foreach (ClubPlace place in _places)
            {
                if (!_floorPlanVisuals.TryGetValue(place.Name, out FloorPlanCardVisual? visual))
                    continue;

                visual.StatusText.Text = GetStatusText(place);
                visual.StatusText.Foreground = GetStatusBrush(place);
                visual.TimeText.Text = GetTimeText(place);
                visual.Card.Background = GetCardBackground(place);
                visual.Card.Cursor = _floorPlanEditorEnabled ? Cursors.SizeAll : Cursors.Hand;

                int additionalAmount = GetActiveSessionPendingCheckoutTotal(place.Name);
                visual.AdditionalPositionsText.Text = $"+{additionalAmount}";
                visual.AdditionalPositionsBadge.Visibility = additionalAmount > 0
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
        }

        private void FloorPlanCard_MouseLeftButtonDown(
            Border card,
            ClubPlace place,
            MouseButtonEventArgs e)
        {
            _selectedFloorPlanPlaceName = place.Name;
            UpdateFloorPlanSelection();
            UpdateAlternativePlaceDetails();
            e.Handled = true;

            if (!_floorPlanEditorEnabled)
                return;

            _draggedFloorPlanCard = card;
            _draggedFloorPlanPlaceName = place.Name;
            _floorPlanDragOffset = e.GetPosition(card);
            Panel.SetZIndex(card, 20);
            card.CaptureMouse();
        }

        private void FloorPlanCanvas_MouseLeftButtonDown(
            object sender,
            MouseButtonEventArgs e)
        {
            _selectedFloorPlanPlaceName = null;
            UpdateFloorPlanSelection();
            UpdateAlternativePlaceDetails();
            e.Handled = true;
        }

        private void FloorPlanCard_MouseMove(Border card, MouseEventArgs e)
        {
            if (!_floorPlanEditorEnabled ||
                !ReferenceEquals(_draggedFloorPlanCard, card) ||
                e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            Point pointer = e.GetPosition(FloorPlanCanvas);
            double left = Math.Clamp(
                pointer.X - _floorPlanDragOffset.X,
                0,
                Math.Max(0, FloorPlanCanvas.ActualWidth - FloorPlanCardWidth)
            );
            double top = Math.Clamp(
                pointer.Y - _floorPlanDragOffset.Y,
                0,
                Math.Max(0, FloorPlanCanvas.ActualHeight - FloorPlanCardHeight)
            );

            Canvas.SetLeft(card, left);
            Canvas.SetTop(card, top);
            e.Handled = true;
        }

        private void FloorPlanCard_MouseLeftButtonUp(Border card, MouseButtonEventArgs e)
        {
            if (!ReferenceEquals(_draggedFloorPlanCard, card))
                return;

            EndFloorPlanDrag(card, save: true);
            e.Handled = true;
        }

        private void EndFloorPlanDrag(Border card, bool save)
        {
            if (!ReferenceEquals(_draggedFloorPlanCard, card))
                return;

            string? placeName = _draggedFloorPlanPlaceName;
            _draggedFloorPlanCard = null;
            _draggedFloorPlanPlaceName = null;
            Panel.SetZIndex(card, 0);

            if (card.IsMouseCaptured)
                card.ReleaseMouseCapture();

            if (save && !string.IsNullOrWhiteSpace(placeName))
            {
                SaveFloorPlanCardPosition(
                    placeName,
                    Canvas.GetLeft(card),
                    Canvas.GetTop(card)
                );
            }
        }

        private void EnsureFloorPlanPositions()
        {
            if (_places.Count == 0)
                return;

            bool changed = false;
            int columns = Math.Min(4, Math.Max(1, _places.Count));
            int rows = (int)Math.Ceiling(_places.Count / (double)columns);

            for (int index = 0; index < _places.Count; index++)
            {
                ClubPlace place = _places[index];
                if (FindFloorPlanPosition(place.Name) != null)
                    continue;

                int column = index % columns;
                int row = index / columns;
                _floorPlanLayout.Positions.Add(new FloorPlanPlacePosition
                {
                    PlaceName = place.Name,
                    X = columns <= 1 ? 0.5 : column / (double)(columns - 1),
                    Y = rows <= 1 ? 0.5 : row / (double)(rows - 1)
                });
                changed = true;
            }

            if (changed)
                FloorPlanLayoutService.SaveCurrent(_floorPlanLayout);
        }

        private FloorPlanPlacePosition? FindFloorPlanPosition(string placeName)
        {
            return _floorPlanLayout.Positions.FirstOrDefault(position =>
                position.PlaceName.Equals(placeName, StringComparison.OrdinalIgnoreCase));
        }

        private void PositionFloorPlanCards()
        {
            if (FloorPlanCanvas.ActualWidth <= 0 || FloorPlanCanvas.ActualHeight <= 0)
                return;

            double availableWidth = Math.Max(0, FloorPlanCanvas.ActualWidth - FloorPlanCardWidth);
            double availableHeight = Math.Max(0, FloorPlanCanvas.ActualHeight - FloorPlanCardHeight);

            foreach ((string placeName, FloorPlanCardVisual visual) in _floorPlanVisuals)
            {
                if (ReferenceEquals(_draggedFloorPlanCard, visual.Card))
                    continue;

                FloorPlanPlacePosition? position = FindFloorPlanPosition(placeName);
                if (position == null)
                    continue;

                Canvas.SetLeft(visual.Card, availableWidth * position.X);
                Canvas.SetTop(visual.Card, availableHeight * position.Y);
            }
        }

        private void SaveFloorPlanCardPosition(string placeName, double left, double top)
        {
            FloorPlanPlacePosition? position = FindFloorPlanPosition(placeName);
            if (position == null)
                return;

            double availableWidth = Math.Max(1, FloorPlanCanvas.ActualWidth - FloorPlanCardWidth);
            double availableHeight = Math.Max(1, FloorPlanCanvas.ActualHeight - FloorPlanCardHeight);
            position.X = Math.Clamp(left / availableWidth, 0, 1);
            position.Y = Math.Clamp(top / availableHeight, 0, 1);
            FloorPlanLayoutService.SaveCurrent(_floorPlanLayout);
        }

        private void FloorPlanCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            PositionFloorPlanCards();
        }

        private void FloorPlanEditorButton_Click(object sender, RoutedEventArgs e)
        {
            SetFloorPlanEditorEnabled(!_floorPlanEditorEnabled);
        }

        private void SetFloorPlanEditorEnabled(bool enabled)
        {
            if (!enabled && _draggedFloorPlanCard != null)
                EndFloorPlanDrag(_draggedFloorPlanCard, save: true);

            _floorPlanEditorEnabled = enabled;
            UpdateFloorPlanEditorState();
            UpdateFloorPlanCards();
            UpdateFloorPlanSelection();
        }

        private void UpdateFloorPlanEditorState()
        {
            if (FloorPlanEditorButton == null)
                return;

            FloorPlanEditorButton.Content = _floorPlanEditorEnabled ? "Готово" : "Редактор";
            FloorPlanEditorButton.Background = new SolidColorBrush(
                _floorPlanEditorEnabled
                    ? Color.FromRgb(22, 101, 52)
                    : Color.FromRgb(37, 48, 68)
            );
            FloorPlanResetButton.Visibility = _floorPlanEditorEnabled
                ? Visibility.Visible
                : Visibility.Collapsed;
            FloorPlanEditorStatusText.Text = _floorPlanEditorEnabled
                ? "Редактирование"
                : "Расстановка заблокирована";
            FloorPlanEditorStatusText.Foreground = new SolidColorBrush(
                _floorPlanEditorEnabled
                    ? Color.FromRgb(74, 222, 128)
                    : Color.FromRgb(148, 163, 184)
            );
        }

        private void FloorPlanResetButton_Click(object sender, RoutedEventArgs e)
        {
            MessageBoxResult result = MessageBox.Show(
                "Вернуть автоматическую расстановку мест?",
                "План клуба",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question
            );

            if (result != MessageBoxResult.Yes)
                return;

            var currentNames = _places
                .Select(place => place.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            _floorPlanLayout.Positions.RemoveAll(position =>
                currentNames.Contains(position.PlaceName));
            EnsureFloorPlanPositions();
            PositionFloorPlanCards();
        }

        private void UpdateFloorPlanSelection()
        {
            bool hasSelection = !string.IsNullOrWhiteSpace(_selectedFloorPlanPlaceName) &&
                                _floorPlanVisuals.ContainsKey(_selectedFloorPlanPlaceName);

            if (hasSelection && AlternativePlacesGrid.Visibility == Visibility.Visible)
            {
                if (!_floorPlanSelectionAnimationTimer.IsEnabled)
                    _floorPlanSelectionAnimationTimer.Start();
            }
            else
            {
                _floorPlanSelectionAnimationTimer.Stop();
            }

            foreach ((string placeName, FloorPlanCardVisual visual) in _floorPlanVisuals)
            {
                bool isSelected = placeName.Equals(
                    _selectedFloorPlanPlaceName,
                    StringComparison.OrdinalIgnoreCase
                );

                visual.Card.BorderThickness = new Thickness(isSelected ? 2 : 1);
                visual.Card.BorderBrush = isSelected
                    ? CreateFloorPlanSelectionBorderBrush()
                    : _floorPlanEditorEnabled
                        ? new SolidColorBrush(Color.FromRgb(251, 191, 36))
                        : GetCardBorderBrush(visual.Place);
            }
        }

        private void UpdateSelectedFloorPlanBorder()
        {
            if (string.IsNullOrWhiteSpace(_selectedFloorPlanPlaceName) ||
                !_floorPlanVisuals.TryGetValue(
                    _selectedFloorPlanPlaceName,
                    out FloorPlanCardVisual? visual))
            {
                _floorPlanSelectionAnimationTimer.Stop();
                return;
            }

            visual.Card.BorderBrush = CreateFloorPlanSelectionBorderBrush();
        }

        private static Brush CreateFloorPlanSelectionBorderBrush()
        {
            double angle = Environment.TickCount64 % 3200 / 3200.0 * 360.0;
            var brush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0.5),
                EndPoint = new Point(1, 0.5),
                RelativeTransform = new RotateTransform(angle, 0.5, 0.5)
            };
            brush.GradientStops.Add(new GradientStop(Color.FromRgb(29, 78, 216), 0));
            brush.GradientStops.Add(new GradientStop(Color.FromRgb(147, 197, 253), 0.5));
            brush.GradientStops.Add(new GradientStop(Color.FromRgb(29, 78, 216), 1));
            return brush;
        }

        private void UpdateAlternativePlaceDetails()
        {
            ClubPlace? place = _places.FirstOrDefault(item => item.Name.Equals(
                _selectedFloorPlanPlaceName,
                StringComparison.OrdinalIgnoreCase
            ));

            if (place == null)
            {
                AlternativeDetailTypeText.Text = "";
                AlternativeDetailTitleText.Text = "";
                AlternativeDetailStatusText.Text = "";
                AlternativeDetailTimeText.Text = "";
                AlternativeDetailMoneyText.Text = "";
                AlternativeDetailEmployeeText.Text = "";
                AlternativeDetailSalesText.Text = "";
                AlternativeDetailSalesText.Visibility = Visibility.Collapsed;
                AlternativeDetailsCard.ContextMenu = null;
                AlternativeDetailsCard.SetResourceReference(
                    Border.BackgroundProperty,
                    "Theme.HeaderBrush"
                );
                AlternativeDetailsCard.SetResourceReference(
                    Border.BorderBrushProperty,
                    "Theme.BorderBrush"
                );
                return;
            }

            AlternativeDetailTypeText.Text = place.Type == PlaceType.Wheel
                ? "РУЛЬ"
                : "ТЕЛЕВИЗОР";
            AlternativeDetailTitleText.Text = place.Name;
            AlternativeDetailStatusText.Text = GetStatusText(place);
            AlternativeDetailStatusText.Foreground = GetStatusBrush(place);
            AlternativeDetailTimeText.Text = GetTimeText(place);
            AlternativeDetailMoneyText.Text = GetMoneyText(place);
            AlternativeDetailEmployeeText.Text = GetEmployeeText(place);

            string salesText = GetActiveSalesText(place);
            AlternativeDetailSalesText.Text = salesText;
            AlternativeDetailSalesText.Visibility = string.IsNullOrWhiteSpace(salesText)
                ? Visibility.Collapsed
                : Visibility.Visible;
            AlternativeDetailsCard.Background = GetCardBackground(place);
            AlternativeDetailsCard.BorderBrush = GetCardBorderBrush(place);
            AlternativeDetailsCard.ContextMenu = CreateContextMenu(place);
            AlternativeDetailsCard.ContextMenuOpening -= AlternativeDetailsCard_ContextMenuOpening;
            AlternativeDetailsCard.ContextMenuOpening += AlternativeDetailsCard_ContextMenuOpening;
        }

        private void AlternativeDetailsCard_ContextMenuOpening(
            object sender,
            ContextMenuEventArgs e)
        {
            ClubPlace? place = _places.FirstOrDefault(item => item.Name.Equals(
                _selectedFloorPlanPlaceName,
                StringComparison.OrdinalIgnoreCase
            ));

            if (place != null)
                AlternativeDetailsCard.ContextMenu = CreateContextMenu(place);
        }

        private sealed class FloorPlanCardVisual
        {
            public FloorPlanCardVisual(
                ClubPlace place,
                Border card,
                TextBlock statusText,
                TextBlock timeText,
                Border additionalPositionsBadge,
                TextBlock additionalPositionsText)
            {
                Place = place;
                Card = card;
                StatusText = statusText;
                TimeText = timeText;
                AdditionalPositionsBadge = additionalPositionsBadge;
                AdditionalPositionsText = additionalPositionsText;
            }

            public ClubPlace Place { get; }
            public Border Card { get; }
            public TextBlock StatusText { get; }
            public TextBlock TimeText { get; }
            public Border AdditionalPositionsBadge { get; }
            public TextBlock AdditionalPositionsText { get; }
        }
    }
}
