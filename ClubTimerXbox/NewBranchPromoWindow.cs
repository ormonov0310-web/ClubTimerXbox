using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using ClubTimerXbox.Models;
using ClubTimerXbox.Services;

namespace ClubTimerXbox
{
    public class NewBranchPromoWindow : Window
    {
        private readonly StackPanel _promoDetailsPanel;
        private readonly ToggleButton _activationToggle;
        private readonly ToggleButton _oneMinuteEndTestToggle;
        private readonly DatePicker _startDatePicker;
        private readonly DatePicker _endDatePicker;
        private readonly TextBlock _effectiveEndText;

        public NewBranchPromoWindow()
        {
            Title = "Новый филиал";
            Width = 520;
            Height = 420;
            MinWidth = 480;
            MinHeight = 300;
            ResizeMode = ResizeMode.CanResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromRgb(16, 20, 28));

            var promo = NewBranchPromoService.Current;
            _activationToggle = CreatePhoneSwitch();
            _activationToggle.IsChecked = promo.IsEnabled;
            _oneMinuteEndTestToggle = CreatePhoneSwitch();
            _oneMinuteEndTestToggle.IsChecked = promo.IsOneMinuteEndTestEnabled;
            _startDatePicker = CreateDatePicker(promo.StartDate, isEnabled: false);
            _endDatePicker = CreateDatePicker(promo.LastDay, isEnabled: true);
            _effectiveEndText = CreateEffectiveEndText();
            _promoDetailsPanel = CreatePromoDetailsPanel();

            Content = CreateContent();
            UpdatePromoDetailsVisibility();
            UpdateEffectiveEndText();
        }

        private UIElement CreateContent()
        {
            var root = new StackPanel
            {
                Margin = new Thickness(24)
            };

            root.Children.Add(new TextBlock
            {
                Text = "Новый филиал",
                Foreground = Brushes.White,
                FontSize = 30,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 8)
            });

            root.Children.Add(new TextBlock
            {
                Text = "Настройки временной акции нового клуба.",
                Foreground = new SolidColorBrush(Color.FromRgb(170, 180, 195)),
                FontSize = 15,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 22)
            });

            var row = new Grid
            {
                Background = new SolidColorBrush(Color.FromRgb(23, 27, 38))
            };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var rowBorder = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(38, 44, 62)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(16),
                Child = row
            };

            var title = new TextBlock
            {
                Text = "Активация акции",
                Foreground = Brushes.White,
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(title, 0);
            row.Children.Add(title);

            _activationToggle.Checked += (_, _) => UpdatePromoDetailsVisibility();
            _activationToggle.Unchecked += (_, _) => UpdatePromoDetailsVisibility();
            Grid.SetColumn(_activationToggle, 1);
            row.Children.Add(_activationToggle);

            root.Children.Add(rowBorder);
            root.Children.Add(_promoDetailsPanel);
            root.Children.Add(CreateButtonsPanel());

            return new ScrollViewer
            {
                Content = root,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
        }

        private StackPanel CreatePromoDetailsPanel()
        {
            var panel = new StackPanel
            {
                Visibility = Visibility.Collapsed,
                Margin = new Thickness(0, 16, 0, 0)
            };

            panel.Children.Add(CreateDateRow(
                "Дата старта",
                _startDatePicker
            ));

            panel.Children.Add(CreateDateRow(
                "Последний день акции",
                _endDatePicker,
                new Thickness(0, 12, 0, 0)
            ));

            _endDatePicker.SelectedDateChanged += (_, _) => UpdateEffectiveEndText();
            panel.Children.Add(_effectiveEndText);
            panel.Children.Add(CreatePromoTariffsButton());
            panel.Children.Add(CreateOneMinuteEndTestRow());

            return panel;
        }

        private Button CreatePromoTariffsButton()
        {
            var button = new Button
            {
                Content = "Тарифы",
                Height = 44,
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 16, 0, 0)
            };

            button.Click += (_, _) =>
            {
                var window = new NewBranchPromoTariffsWindow
                {
                    Owner = this
                };

                window.ShowDialog();
            };

            return button;
        }

        private Border CreateOneMinuteEndTestRow()
        {
            var row = new Grid
            {
                Background = new SolidColorBrush(Color.FromRgb(23, 27, 38))
            };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var textPanel = new StackPanel
            {
                Margin = new Thickness(0, 0, 16, 0)
            };

            textPanel.Children.Add(new TextBlock
            {
                Text = "Тест отключения акции через 1 минуту",
                Foreground = Brushes.White,
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap
            });

            textPanel.Children.Add(new TextBlock
            {
                Text = "После сохранения акционные пункты исчезнут из ПКМ через 60 секунд.",
                Foreground = new SolidColorBrush(Color.FromRgb(170, 180, 195)),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0)
            });

            Grid.SetColumn(textPanel, 0);
            row.Children.Add(textPanel);

            Grid.SetColumn(_oneMinuteEndTestToggle, 1);
            row.Children.Add(_oneMinuteEndTestToggle);

            return new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(23, 27, 38)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(38, 44, 62)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(16),
                Margin = new Thickness(0, 12, 0, 0),
                Child = row
            };
        }

        private StackPanel CreateButtonsPanel()
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 18, 0, 0)
            };

            var saveButton = new Button
            {
                Content = "Сохранить",
                Width = 130,
                Height = 42,
                FontSize = 16,
                Margin = new Thickness(0, 0, 8, 0)
            };
            saveButton.Click += (_, _) => SaveSettings();

            var closeButton = new Button
            {
                Content = "Закрыть",
                Width = 130,
                Height = 42,
                FontSize = 16
            };
            closeButton.Click += (_, _) => Close();

            panel.Children.Add(saveButton);
            panel.Children.Add(closeButton);

            return panel;
        }

        private void UpdatePromoDetailsVisibility()
        {
            _promoDetailsPanel.Visibility = _activationToggle.IsChecked == true
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void UpdateEffectiveEndText()
        {
            var settings = BuildPromoSettings();
            DateTime effectiveEndAt = NewBranchPromoService.GetEffectiveEndAt(settings);

            _effectiveEndText.Text =
                $"Акция действует до {effectiveEndAt:dd.MM.yyyy HH:mm}. " +
                "Последний день работает полностью, после полуночи есть запас до 06:00.";
        }

        private void SaveSettings()
        {
            var settings = BuildPromoSettings();
            NewBranchPromoService.Save(settings);

            MessageBox.Show("Настройки акции сохранены.", "Новый филиал");
            DialogResult = true;
            Close();
        }

        private NewBranchPromoSettings BuildPromoSettings()
        {
            var current = NewBranchPromoService.Current;
            DateTime startDate = (_startDatePicker.SelectedDate ?? DateTime.Today).Date;
            DateTime lastDay = (_endDatePicker.SelectedDate ?? startDate).Date;

            return new NewBranchPromoSettings
            {
                IsEnabled = _activationToggle.IsChecked == true,
                StartDate = startDate,
                LastDay = lastDay,
                GraceEndHour = 6,
                TvPromoMinutes = current.TvPromoMinutes,
                TvPromoPrice = current.TvPromoPrice,
                OpenModeDiscountPercent = current.OpenModeDiscountPercent,
                IsOneMinuteEndTestEnabled = _oneMinuteEndTestToggle.IsChecked == true,
                OneMinuteEndTestEndsAt = _oneMinuteEndTestToggle.IsChecked == true
                    ? DateTime.Now.AddMinutes(1)
                    : null
            };
        }

        private static TextBlock CreateEffectiveEndText()
        {
            return new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(170, 180, 195)),
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 14, 0, 0)
            };
        }

        private static Border CreateDateRow(string label, DatePicker picker, Thickness? margin = null)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var text = new TextBlock
            {
                Text = label,
                Foreground = Brushes.White,
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 16, 0)
            };
            Grid.SetColumn(text, 0);
            row.Children.Add(text);

            Grid.SetColumn(picker, 1);
            row.Children.Add(picker);

            return new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(23, 27, 38)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(38, 44, 62)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(16),
                Margin = margin ?? new Thickness(0),
                Child = row
            };
        }

        private static DatePicker CreateDatePicker(DateTime date, bool isEnabled)
        {
            return new DatePicker
            {
                SelectedDate = date,
                DisplayDate = date,
                IsEnabled = isEnabled,
                Width = 160,
                FontSize = 15,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        private static ToggleButton CreatePhoneSwitch()
        {
            var toggle = new ToggleButton
            {
                Width = 66,
                Height = 34,
                VerticalAlignment = VerticalAlignment.Center,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Template = CreateSwitchTemplate()
            };

            return toggle;
        }

        private static ControlTemplate CreateSwitchTemplate()
        {
            var template = new ControlTemplate(typeof(ToggleButton));

            var track = new FrameworkElementFactory(typeof(Border));
            track.Name = "Track";
            track.SetValue(Border.WidthProperty, 66.0);
            track.SetValue(Border.HeightProperty, 34.0);
            track.SetValue(Border.CornerRadiusProperty, new CornerRadius(17));
            track.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(75, 85, 99)));

            var canvas = new FrameworkElementFactory(typeof(Canvas));

            var thumb = new FrameworkElementFactory(typeof(Ellipse));
            thumb.Name = "Thumb";
            thumb.SetValue(Shape.WidthProperty, 28.0);
            thumb.SetValue(Shape.HeightProperty, 28.0);
            thumb.SetValue(Shape.FillProperty, Brushes.White);
            thumb.SetValue(Canvas.LeftProperty, 3.0);
            thumb.SetValue(Canvas.TopProperty, 3.0);

            canvas.AppendChild(thumb);
            track.AppendChild(canvas);
            template.VisualTree = track;

            var checkedTrigger = new Trigger
            {
                Property = ToggleButton.IsCheckedProperty,
                Value = true
            };
            checkedTrigger.Setters.Add(new Setter(
                Border.BackgroundProperty,
                new SolidColorBrush(Color.FromRgb(34, 197, 94)),
                "Track"
            ));
            checkedTrigger.Setters.Add(new Setter(Canvas.LeftProperty, 35.0, "Thumb"));

            template.Triggers.Add(checkedTrigger);

            return template;
        }
    }
}
