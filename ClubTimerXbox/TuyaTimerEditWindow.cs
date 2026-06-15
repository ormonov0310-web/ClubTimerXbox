using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ClubTimerXbox.Models;

namespace ClubTimerXbox
{
    public class TuyaTimerEditWindow : Window
    {
        private readonly ComboBox _hourComboBox = new ComboBox();
        private readonly ComboBox _minuteComboBox = new ComboBox();
        private readonly ComboBox _actionComboBox = new ComboBox();
        private readonly ComboBox _repeatComboBox = new ComboBox();
        private readonly bool _isNew;

        public TuyaScheduleTask Schedule { get; }

        public bool ShouldDelete { get; private set; }

        public TuyaTimerEditWindow(
            string deviceName,
            TuyaScheduleTask schedule,
            bool isNew)
        {
            _isNew = isNew;
            Schedule = schedule.Clone();

            Title = isNew ? "Добавить таймер" : "Изменить таймер";
            Width = 520;
            Height = 560;
            MinWidth = 480;
            MinHeight = 500;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromRgb(14, 18, 26));

            Content = CreateContent(deviceName);
        }

        private UIElement CreateContent(string deviceName)
        {
            var root = new StackPanel
            {
                Margin = new Thickness(24)
            };

            root.Children.Add(new TextBlock
            {
                Text = _isNew ? "Новый таймер" : "Параметры таймера",
                Foreground = Brushes.White,
                FontSize = 28,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 4)
            });

            root.Children.Add(new TextBlock
            {
                Text = deviceName,
                Foreground = new SolidColorBrush(Color.FromRgb(160, 170, 188)),
                FontSize = 15,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 18)
            });

            root.Children.Add(CreateCard());
            root.Children.Add(CreateButtonRow());

            return root;
        }

        private Border CreateCard()
        {
            var panel = new StackPanel();

            panel.Children.Add(CreateLabel("Действие"));
            _actionComboBox.Height = 42;
            _actionComboBox.FontSize = 16;
            _actionComboBox.Items.Add("Включить");
            _actionComboBox.Items.Add("Выключить");
            _actionComboBox.SelectedIndex = Schedule.TurnOn ? 0 : 1;
            panel.Children.Add(_actionComboBox);

            panel.Children.Add(CreateLabel("Время"));
            panel.Children.Add(CreateTimePicker());

            panel.Children.Add(CreateLabel("Повтор"));
            _repeatComboBox.Height = 42;
            _repeatComboBox.FontSize = 16;
            _repeatComboBox.Items.Add("Каждый день");
            _repeatComboBox.Items.Add("Только один раз");
            _repeatComboBox.SelectedIndex = Schedule.IsEveryDay ? 0 : 1;
            panel.Children.Add(_repeatComboBox);

            return new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(24, 30, 42)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(51, 65, 85)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(16),
                Child = panel
            };
        }

        private UIElement CreateTimePicker()
        {
            var grid = new Grid
            {
                Margin = new Thickness(0, 0, 0, 6)
            };

            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            for (int hour = 0; hour < 24; hour++)
                _hourComboBox.Items.Add(hour.ToString("00", CultureInfo.InvariantCulture));

            for (int minute = 0; minute < 60; minute++)
                _minuteComboBox.Items.Add(minute.ToString("00", CultureInfo.InvariantCulture));

            var time = ParseScheduleTime(Schedule.Time);
            _hourComboBox.SelectedIndex = time.Hours;
            _minuteComboBox.SelectedIndex = time.Minutes;

            _hourComboBox.Height = 44;
            _minuteComboBox.Height = 44;
            _hourComboBox.FontSize = 17;
            _minuteComboBox.FontSize = 17;

            var separator = new TextBlock
            {
                Text = ":",
                Foreground = Brushes.White,
                FontSize = 28,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            Grid.SetColumn(_hourComboBox, 0);
            Grid.SetColumn(separator, 1);
            Grid.SetColumn(_minuteComboBox, 2);

            grid.Children.Add(_hourComboBox);
            grid.Children.Add(separator);
            grid.Children.Add(_minuteComboBox);

            return grid;
        }

        private UIElement CreateButtonRow()
        {
            var panel = new DockPanel
            {
                Margin = new Thickness(0, 18, 0, 0),
                LastChildFill = false
            };

            if (!_isNew)
            {
                var deleteButton = CreateButton("Удалить", Color.FromRgb(153, 27, 27), 120);
                deleteButton.Click += (_, _) =>
                {
                    ShouldDelete = true;
                    DialogResult = true;
                    Close();
                };
                DockPanel.SetDock(deleteButton, Dock.Left);
                panel.Children.Add(deleteButton);
            }

            var cancelButton = CreateButton("Отмена", Color.FromRgb(51, 65, 85), 110);
            cancelButton.Click += (_, _) =>
            {
                DialogResult = false;
                Close();
            };
            DockPanel.SetDock(cancelButton, Dock.Right);
            panel.Children.Add(cancelButton);

            var saveButton = CreateButton("Сохранить", Color.FromRgb(37, 99, 235), 140);
            saveButton.Margin = new Thickness(0, 0, 10, 0);
            saveButton.Click += (_, _) =>
            {
                ApplySelection();
                DialogResult = true;
                Close();
            };
            DockPanel.SetDock(saveButton, Dock.Right);
            panel.Children.Add(saveButton);

            return panel;
        }

        private TextBlock CreateLabel(string text)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = new SolidColorBrush(Color.FromRgb(160, 170, 188)),
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 7)
            };
        }

        private Button CreateButton(string text, Color background, double width)
        {
            return new Button
            {
                Content = text,
                Width = width,
                Height = 44,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                Background = new SolidColorBrush(background),
                BorderBrush = Brushes.Transparent
            };
        }

        private void ApplySelection()
        {
            int hour = Math.Max(0, _hourComboBox.SelectedIndex);
            int minute = Math.Max(0, _minuteComboBox.SelectedIndex);

            Schedule.Time = $"{hour:00}:{minute:00}";
            Schedule.TurnOn = _actionComboBox.SelectedIndex != 1;
            Schedule.TimezoneId = string.IsNullOrWhiteSpace(Schedule.TimezoneId)
                ? "Asia/Bishkek"
                : Schedule.TimezoneId;

            if (_repeatComboBox.SelectedIndex == 1)
            {
                Schedule.Loops = "0000000";
                Schedule.Date = GetNearestDate(hour, minute);
            }
            else
            {
                Schedule.Loops = "1111111";
                Schedule.Date = "";
            }

            Schedule.AliasName = "";
        }

        private static TimeSpan ParseScheduleTime(string raw)
        {
            if (TimeSpan.TryParseExact(raw, @"hh\:mm", CultureInfo.InvariantCulture, out var exact))
                return exact;

            if (TimeSpan.TryParse(raw, CultureInfo.InvariantCulture, out var parsed))
                return new TimeSpan(parsed.Hours, parsed.Minutes, 0);

            return DateTime.Now.AddMinutes(5).TimeOfDay;
        }

        private static string GetNearestDate(int hour, int minute)
        {
            var now = DateTime.Now;
            var target = now.Date.AddHours(hour).AddMinutes(minute);

            if (target <= now)
                target = target.AddDays(1);

            return target.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        }
    }
}
