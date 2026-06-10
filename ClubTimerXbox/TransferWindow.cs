using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ClubTimerXbox.Models;

namespace ClubTimerXbox
{
    public class TransferWindow : Window
    {
        public ClubPlace? SelectedPlace { get; private set; }

        private readonly ListBox _placesListBox = new ListBox();

        public TransferWindow(List<ClubPlace> freePlaces)
        {
            Title = "Пересадить клиента";
            Width = 420;
            Height = 520;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromRgb(16, 20, 28));

            var root = new Grid
            {
                Margin = new Thickness(16)
            };

            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var titleText = new TextBlock
            {
                Text = "Выберите свободное место",
                Foreground = Brushes.White,
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 12)
            };

            Grid.SetRow(titleText, 0);
            root.Children.Add(titleText);

            _placesListBox.Background = new SolidColorBrush(Color.FromRgb(24, 32, 43));
            _placesListBox.Foreground = Brushes.White;
            _placesListBox.BorderThickness = new Thickness(0);
            _placesListBox.FontSize = 20;

            foreach (var place in freePlaces)
            {
                var item = new ListBoxItem
                {
                    Content = $"{place.Name}  •  {FormatPrice(place.PricePerMinute)}",
                    Tag = place,
                    Padding = new Thickness(12),
                    Margin = new Thickness(0, 4, 0, 4)
                };

                _placesListBox.Items.Add(item);
            }

            Grid.SetRow(_placesListBox, 1);
            root.Children.Add(_placesListBox);

            var buttonsPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 16, 0, 0)
            };

            var cancelButton = new Button
            {
                Content = "Отмена",
                Width = 110,
                Height = 42,
                Margin = new Thickness(0, 0, 8, 0)
            };

            cancelButton.Click += (_, _) =>
            {
                DialogResult = false;
                Close();
            };

            var okButton = new Button
            {
                Content = "ОК",
                Width = 110,
                Height = 42
            };

            okButton.Click += (_, _) =>
            {
                if (_placesListBox.SelectedItem is not ListBoxItem selectedItem)
                {
                    MessageBox.Show("Сначала выберите свободное место.", "Пересадка");
                    return;
                }

                SelectedPlace = selectedItem.Tag as ClubPlace;

                DialogResult = true;
                Close();
            };

            buttonsPanel.Children.Add(cancelButton);
            buttonsPanel.Children.Add(okButton);

            Grid.SetRow(buttonsPanel, 2);
            root.Children.Add(buttonsPanel);

            Content = root;
        }

        private static string FormatPrice(double pricePerMinute)
        {
            if (pricePerMinute % 1 == 0)
                return $"{pricePerMinute:0} сом/мин";

            return $"{pricePerMinute:0.##} сом/мин";
        }
    }
}