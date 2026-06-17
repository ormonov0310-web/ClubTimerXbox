using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ClubTimerXbox.Models;

namespace ClubTimerXbox
{
    public class ActivePlaceSelectWindow : Window
    {
        public ClubPlace? SelectedPlace { get; private set; }

        private readonly List<ClubPlace> _activePlaces;
        private readonly string _title;
        private readonly string _subtitle;
        private readonly StackPanel _placesPanel = new StackPanel();

        public ActivePlaceSelectWindow(
            List<ClubPlace> activePlaces,
            string title = "Оформить на ТВ.",
            string subtitle = "Выберите активный ТВ или руль, на который нужно оформить товар/услугу. Оплата будет добавлена при закрытии этого сеанса.")
        {
            _activePlaces = activePlaces;
            _title = title;
            _subtitle = subtitle;

            Title = "Выбрать место";
            Width = 520;
            Height = 520;
            MinWidth = 480;
            MinHeight = 460;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromRgb(16, 20, 28));

            Content = CreateContent();

            LoadPlaces();
        }

        private UIElement CreateContent()
        {
            var root = new DockPanel
            {
                Margin = new Thickness(22)
            };

            var titleText = new TextBlock
            {
                Text = _title,
                Foreground = Brushes.White,
                FontSize = 30,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 8)
            };

            DockPanel.SetDock(titleText, Dock.Top);
            root.Children.Add(titleText);

            var subtitleText = new TextBlock
            {
                Text = _subtitle,
                Foreground = new SolidColorBrush(Color.FromRgb(170, 180, 195)),
                FontSize = 15,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 23,
                Margin = new Thickness(0, 0, 0, 16)
            };

            DockPanel.SetDock(subtitleText, Dock.Top);
            root.Children.Add(subtitleText);

            var cancelButton = new Button
            {
                Content = "Отмена",
                Height = 42,
                FontSize = 16,
                Margin = new Thickness(0, 16, 0, 0)
            };

            cancelButton.Click += (_, _) =>
            {
                DialogResult = false;
                Close();
            };

            DockPanel.SetDock(cancelButton, Dock.Bottom);
            root.Children.Add(cancelButton);

            var scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = _placesPanel
            };

            root.Children.Add(scrollViewer);

            return root;
        }

        private void LoadPlaces()
        {
            _placesPanel.Children.Clear();

            if (_activePlaces.Count == 0)
            {
                _placesPanel.Children.Add(new TextBlock
                {
                    Text = "Нет активных мест. Сначала откройте ТВ или руль.",
                    Foreground = new SolidColorBrush(Color.FromRgb(248, 113, 113)),
                    FontSize = 17,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 12, 0, 0)
                });

                return;
            }

            foreach (var place in _activePlaces)
            {
                _placesPanel.Children.Add(CreatePlaceButton(place));
            }
        }

        private Button CreatePlaceButton(ClubPlace place)
        {
            string modeText = place.IsOpenMode ? "Открытый режим" : "Предоплата";
            string moneyText = place.IsOpenMode
                ? "оплата по факту"
                : $"оплачено: {place.PaidAmount} сом";

            var textBlock = new TextBlock
            {
                Text =
                    $"{place.Name}\n" +
                    $"{modeText} • {moneyText}",
                FontSize = 17,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 25
            };

            var button = new Button
            {
                Content = textBlock,
                MinHeight = 78,
                Margin = new Thickness(0, 0, 0, 10),
                Padding = new Thickness(14),
                HorizontalContentAlignment = HorizontalAlignment.Left
            };

            button.Click += (_, _) =>
            {
                SelectedPlace = place;
                DialogResult = true;
                Close();
            };

            return button;
        }
    }
}
