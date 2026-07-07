using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ClubTimerXbox.Models;
using ClubTimerXbox.Services;

namespace ClubTimerXbox
{
    public class NewBranchPromoTariffsWindow : Window
    {
        private readonly TextBox _tvPromoMinutesTextBox;
        private readonly TextBox _tvPromoPriceTextBox;
        private readonly TextBox _openModeDiscountTextBox;

        public NewBranchPromoTariffsWindow()
        {
            Title = "Тарифы акции";
            Width = 720;
            Height = 560;
            MinWidth = 520;
            MinHeight = 360;
            ResizeMode = ResizeMode.CanResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromRgb(16, 20, 28));

            var promo = NewBranchPromoService.Current;
            _tvPromoMinutesTextBox = CreateTextBox(promo.TvPromoMinutes.ToString());
            _tvPromoPriceTextBox = CreateTextBox(promo.TvPromoPrice.ToString());
            _openModeDiscountTextBox = CreateTextBox(promo.OpenModeDiscountPercent.ToString());

            Content = CreateContent();
        }

        private UIElement CreateContent()
        {
            var root = new StackPanel
            {
                Margin = new Thickness(24)
            };

            root.Children.Add(new TextBlock
            {
                Text = "Тарифы акции",
                Foreground = Brushes.White,
                FontSize = 30,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 8)
            });

            root.Children.Add(new TextBlock
            {
                Text = "Акция применяется только к обычным ТВ нового филиала.",
                Foreground = new SolidColorBrush(Color.FromRgb(170, 180, 195)),
                FontSize = 15,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 22)
            });

            root.Children.Add(CreateTvPromoCard());
            root.Children.Add(CreateButtonsPanel());

            return new ScrollViewer
            {
                Content = root,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
        }

        private Border CreateTvPromoCard()
        {
            var panel = new StackPanel();

            panel.Children.Add(new TextBlock
            {
                Text = "ТВ",
                Foreground = Brushes.White,
                FontSize = 22,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 14)
            });

            panel.Children.Add(CreateLabeledTextBox("Акционное время, минут", _tvPromoMinutesTextBox));
            panel.Children.Add(CreateLabeledTextBox("Акционная цена, сом", _tvPromoPriceTextBox));
            panel.Children.Add(CreateLabeledTextBox("Скидка открытого режима, %", _openModeDiscountTextBox));

            panel.Children.Add(new TextBlock
            {
                Text = "Обычные тарифы 60/30/5 минут остаются в меню без изменений. Добавить время тоже работает по обычной цене.",
                Foreground = new SolidColorBrush(Color.FromRgb(170, 180, 195)),
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 0)
            });

            return CreateCard(panel);
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

        private void SaveSettings()
        {
            try
            {
                var current = NewBranchPromoService.Current;
                var settings = new NewBranchPromoSettings
                {
                    IsEnabled = current.IsEnabled,
                    StartDate = current.StartDate,
                    LastDay = current.LastDay,
                    GraceEndHour = current.GraceEndHour,
                    TvPromoMinutes = ReadPositiveInt(_tvPromoMinutesTextBox, "Акционное время"),
                    TvPromoPrice = ReadPositiveInt(_tvPromoPriceTextBox, "Акционная цена"),
                    OpenModeDiscountPercent = ReadPercent(_openModeDiscountTextBox, "Скидка открытого режима"),
                    IsOneMinuteEndTestEnabled = current.IsOneMinuteEndTestEnabled,
                    OneMinuteEndTestEndsAt = current.OneMinuteEndTestEndsAt
                };

                NewBranchPromoService.Save(settings);

                MessageBox.Show("Тарифы акции сохранены.", "Тарифы акции");
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Ошибка в тарифах акции");
            }
        }

        private static int ReadPositiveInt(TextBox textBox, string fieldName)
        {
            if (!int.TryParse(textBox.Text.Trim(), out int value))
                throw new Exception($"Поле \"{fieldName}\" должно быть целым числом.");

            if (value <= 0)
                throw new Exception($"Поле \"{fieldName}\" должно быть больше 0.");

            return value;
        }

        private static int ReadPercent(TextBox textBox, string fieldName)
        {
            int value = ReadPositiveInt(textBox, fieldName);

            if (value > 100)
                throw new Exception($"Поле \"{fieldName}\" не может быть больше 100.");

            return value;
        }

        private static UIElement CreateLabeledTextBox(string label, TextBox textBox)
        {
            var grid = new Grid
            {
                Margin = new Thickness(0, 0, 0, 10)
            };

            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });

            var labelText = new TextBlock
            {
                Text = label,
                Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                FontSize = 16,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 16, 0)
            };

            Grid.SetColumn(labelText, 0);
            Grid.SetColumn(textBox, 1);

            grid.Children.Add(labelText);
            grid.Children.Add(textBox);

            return grid;
        }

        private static TextBox CreateTextBox(string value)
        {
            return new TextBox
            {
                Text = value,
                FontSize = 16,
                Height = 34,
                Padding = new Thickness(8, 4, 8, 4)
            };
        }

        private static Border CreateCard(UIElement content)
        {
            return new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(23, 27, 38)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(38, 44, 62)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(16),
                Margin = new Thickness(0, 0, 0, 12),
                Child = content
            };
        }
    }
}
