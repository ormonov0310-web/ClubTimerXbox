using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ClubTimerXbox.Models;
using ClubTimerXbox.Services;

namespace ClubTimerXbox
{
    public class SettingsWindow : Window
    {
        private TextBox _tvCountTextBox = new TextBox();
        private TextBox _wheelCountTextBox = new TextBox();
        private TextBox _vipRoomCountTextBox = new TextBox();

        private TextBox _tvMainTariffTextBox = new TextBox();
        private TextBox _tvMiddleTariffTextBox = new TextBox();
        private TextBox _tvSmallTariffTextBox = new TextBox();

        private TextBox _wheelMainTariffTextBox = new TextBox();
        private TextBox _wheelMiddleTariffTextBox = new TextBox();
        private TextBox _wheelSmallTariffTextBox = new TextBox();

        private TextBlock _tvMainTimeText = new TextBlock();
        private TextBlock _tvMiddleTimeText = new TextBlock();
        private TextBlock _tvSmallTimeText = new TextBlock();

        private TextBlock _wheelMainTimeText = new TextBlock();
        private TextBlock _wheelMiddleTimeText = new TextBlock();
        private TextBlock _wheelSmallTimeText = new TextBlock();

        public SettingsWindow()
        {
            Title = "Настройки";
            Width = 820;
            Height = 700;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromRgb(16, 20, 28));

            var root = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = CreateContent()
            };

            Content = root;

            UpdateCalculatedTimes();
        }

        private UIElement CreateContent()
        {
            var mainPanel = new StackPanel
            {
                Margin = new Thickness(24)
            };

            mainPanel.Children.Add(new TextBlock
            {
                Text = "Настройки Club Timer Xbox",
                Foreground = Brushes.White,
                FontSize = 30,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 8)
            });

            mainPanel.Children.Add(new TextBlock
            {
                Text = "Укажи суммы тарифов. Система сама рассчитает, сколько минут и секунд даёт каждая сумма.",
                Foreground = new SolidColorBrush(Color.FromRgb(170, 180, 195)),
                FontSize = 15,
                Margin = new Thickness(0, 0, 0, 24)
            });

            mainPanel.Children.Add(CreatePlacesSection());
            mainPanel.Children.Add(CreateTvTariffSection());
            mainPanel.Children.Add(CreateWheelTariffSection());

            mainPanel.Children.Add(CreateFutureSection(
                "VIP комнаты",
                "Позже добавим отдельные тарифы для VIP-комнат."
            ));

            mainPanel.Children.Add(CreateFutureSection(
                "Сотрудники",
                "Позже здесь будут 3 сотрудника, вход по коду, смены и права доступа."
            ));

            mainPanel.Children.Add(CreateFutureSection(
                "Tuya розетки",
                "Дополнительная функция на будущее. Сначала сделаем основную систему клуба."
            ));

            var buttonsPanel = new StackPanel
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

            buttonsPanel.Children.Add(saveButton);
            buttonsPanel.Children.Add(closeButton);

            mainPanel.Children.Add(buttonsPanel);

            return mainPanel;
        }

        private Border CreatePlacesSection()
        {
            var settings = AppSettingsService.Current;

            var panel = new StackPanel();

            panel.Children.Add(CreateSectionTitle("Количество мест"));

            _tvCountTextBox = CreateTextBox(settings.TvCount.ToString());
            _wheelCountTextBox = CreateTextBox(settings.WheelCount.ToString());
            _vipRoomCountTextBox = CreateTextBox(settings.VipRoomCount.ToString());

            panel.Children.Add(CreateSimpleLabeledTextBox("Количество ТВ", _tvCountTextBox));
            panel.Children.Add(CreateSimpleLabeledTextBox("Количество рулей", _wheelCountTextBox));
            panel.Children.Add(CreateSimpleLabeledTextBox("Количество VIP-комнат", _vipRoomCountTextBox));

            return CreateCard(panel);
        }

        private Border CreateTvTariffSection()
        {
            var tariff = AppSettingsService.Current.TvTariff;

            var panel = new StackPanel();

            panel.Children.Add(CreateSectionTitle("Тарифы ТВ"));

            panel.Children.Add(CreateHintText("Основной тариф считается как 60 минут. Остальные суммы система переводит во время автоматически."));

            _tvMainTariffTextBox = CreateTextBox(tariff.OneHourPrice.ToString());
            _tvMiddleTariffTextBox = CreateTextBox(tariff.HalfHourPrice.ToString());
            _tvSmallTariffTextBox = CreateTextBox(tariff.FiveMinutesPrice.ToString());

            _tvMainTimeText = CreateTimeText();
            _tvMiddleTimeText = CreateTimeText();
            _tvSmallTimeText = CreateTimeText();

            ConnectAutoCalculation(_tvMainTariffTextBox);
            ConnectAutoCalculation(_tvMiddleTariffTextBox);
            ConnectAutoCalculation(_tvSmallTariffTextBox);

            panel.Children.Add(CreateTariffRow("Основной тариф, сом", _tvMainTariffTextBox, _tvMainTimeText));
            panel.Children.Add(CreateTariffRow("Средний тариф, сом", _tvMiddleTariffTextBox, _tvMiddleTimeText));
            panel.Children.Add(CreateTariffRow("Маленький тариф, сом", _tvSmallTariffTextBox, _tvSmallTimeText));

            return CreateCard(panel);
        }

        private Border CreateWheelTariffSection()
        {
            var tariff = AppSettingsService.Current.WheelTariff;

            var panel = new StackPanel();

            panel.Children.Add(CreateSectionTitle("Тарифы руля"));

            panel.Children.Add(CreateHintText("Например: 150 сом = 60 минут, 80 сом система сама покажет примерно 32 минуты."));

            _wheelMainTariffTextBox = CreateTextBox(tariff.OneHourPrice.ToString());
            _wheelMiddleTariffTextBox = CreateTextBox(tariff.HalfHourPrice.ToString());
            _wheelSmallTariffTextBox = CreateTextBox(tariff.FiveMinutesPrice.ToString());

            _wheelMainTimeText = CreateTimeText();
            _wheelMiddleTimeText = CreateTimeText();
            _wheelSmallTimeText = CreateTimeText();

            ConnectAutoCalculation(_wheelMainTariffTextBox);
            ConnectAutoCalculation(_wheelMiddleTariffTextBox);
            ConnectAutoCalculation(_wheelSmallTariffTextBox);

            panel.Children.Add(CreateTariffRow("Основной тариф, сом", _wheelMainTariffTextBox, _wheelMainTimeText));
            panel.Children.Add(CreateTariffRow("Средний тариф, сом", _wheelMiddleTariffTextBox, _wheelMiddleTimeText));
            panel.Children.Add(CreateTariffRow("Маленький тариф, сом", _wheelSmallTariffTextBox, _wheelSmallTimeText));

            return CreateCard(panel);
        }

        private void ConnectAutoCalculation(TextBox textBox)
        {
            textBox.TextChanged += (_, _) => UpdateCalculatedTimes();
        }

        private void UpdateCalculatedTimes()
        {
            UpdateTariffTimeTexts(
                _tvMainTariffTextBox,
                _tvMiddleTariffTextBox,
                _tvSmallTariffTextBox,
                _tvMainTimeText,
                _tvMiddleTimeText,
                _tvSmallTimeText
            );

            UpdateTariffTimeTexts(
                _wheelMainTariffTextBox,
                _wheelMiddleTariffTextBox,
                _wheelSmallTariffTextBox,
                _wheelMainTimeText,
                _wheelMiddleTimeText,
                _wheelSmallTimeText
            );
        }

        private void UpdateTariffTimeTexts(
            TextBox mainAmountTextBox,
            TextBox middleAmountTextBox,
            TextBox smallAmountTextBox,
            TextBlock mainTimeText,
            TextBlock middleTimeText,
            TextBlock smallTimeText)
        {
            int mainAmount = TryReadInt(mainAmountTextBox);
            int middleAmount = TryReadInt(middleAmountTextBox);
            int smallAmount = TryReadInt(smallAmountTextBox);

            mainTimeText.Text = CalculateTimeText(mainAmount, mainAmount);
            middleTimeText.Text = CalculateTimeText(mainAmount, middleAmount);
            smallTimeText.Text = CalculateTimeText(mainAmount, smallAmount);
        }

        private string CalculateTimeText(int mainAmount, int amount)
        {
            if (mainAmount <= 0 || amount <= 0)
                return "—";

            double seconds = amount * 3600.0 / mainAmount;
            int roundedSeconds = (int)Math.Floor(seconds);

            return TariffService.FormatMenuTime(roundedSeconds);
        }

        private int TryReadInt(TextBox textBox)
        {
            if (int.TryParse(textBox.Text.Trim(), out int value))
                return value;

            return 0;
        }

        private void SaveSettings()
        {
            try
            {
                int tvMain = ReadInt(_tvMainTariffTextBox, "ТВ основной тариф");
                int tvMiddle = ReadInt(_tvMiddleTariffTextBox, "ТВ средний тариф");
                int tvSmall = ReadInt(_tvSmallTariffTextBox, "ТВ маленький тариф");

                int wheelMain = ReadInt(_wheelMainTariffTextBox, "Руль основной тариф");
                int wheelMiddle = ReadInt(_wheelMiddleTariffTextBox, "Руль средний тариф");
                int wheelSmall = ReadInt(_wheelSmallTariffTextBox, "Руль маленький тариф");

                var settings = new ClubSettings
                {
                    TvCount = ReadInt(_tvCountTextBox, "Количество ТВ"),
                    WheelCount = ReadInt(_wheelCountTextBox, "Количество рулей"),
                    VipRoomCount = ReadInt(_vipRoomCountTextBox, "Количество VIP-комнат"),

                    TvTariff = new TariffSettings
                    {
                        OneHourPrice = tvMain,
                        HalfHourPrice = tvMiddle,
                        FiveMinutesPrice = tvSmall,
                        PricePerMinute = tvMain / 60.0
                    },

                    WheelTariff = new TariffSettings
                    {
                        OneHourPrice = wheelMain,
                        HalfHourPrice = wheelMiddle,
                        FiveMinutesPrice = wheelSmall,
                        PricePerMinute = wheelMain / 60.0
                    },

                    VipTariff = AppSettingsService.Current.VipTariff,
                    NewBranchPromo = AppSettingsService.Current.NewBranchPromo
                };

                AppSettingsService.Save(settings);

                MessageBox.Show(
                    "Настройки сохранены.\n\nГлавный экран сейчас обновится по новым тарифам и количеству мест.",
                    "Настройки сохранены"
                );

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Ошибка в настройках");
            }
        }

        private int ReadInt(TextBox textBox, string fieldName)
        {
            if (!int.TryParse(textBox.Text.Trim(), out int value))
                throw new Exception($"Поле \"{fieldName}\" должно быть целым числом.");

            if (value < 0)
                throw new Exception($"Поле \"{fieldName}\" не может быть меньше 0.");

            return value;
        }

        private Border CreateFutureSection(string title, string text)
        {
            var panel = new StackPanel();

            panel.Children.Add(CreateSectionTitle(title));

            panel.Children.Add(new TextBlock
            {
                Text = text,
                Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                FontSize = 16,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 24
            });

            return CreateCard(panel);
        }

        private TextBlock CreateSectionTitle(string title)
        {
            return new TextBlock
            {
                Text = title,
                Foreground = Brushes.White,
                FontSize = 22,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 14)
            };
        }

        private TextBlock CreateHintText(string text)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 14)
            };
        }

        private UIElement CreateSimpleLabeledTextBox(string label, TextBox textBox)
        {
            var grid = new Grid
            {
                Margin = new Thickness(0, 0, 0, 10)
            };

            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });

            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(180)
            });

            var labelText = new TextBlock
            {
                Text = label,
                Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                FontSize = 16,
                VerticalAlignment = VerticalAlignment.Center
            };

            Grid.SetColumn(labelText, 0);
            Grid.SetColumn(textBox, 1);

            grid.Children.Add(labelText);
            grid.Children.Add(textBox);

            return grid;
        }

        private UIElement CreateTariffRow(string label, TextBox textBox, TextBlock timeText)
        {
            var grid = new Grid
            {
                Margin = new Thickness(0, 0, 0, 10)
            };

            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });

            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(160)
            });

            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(160)
            });

            var labelText = new TextBlock
            {
                Text = label,
                Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                FontSize = 16,
                VerticalAlignment = VerticalAlignment.Center
            };

            Grid.SetColumn(labelText, 0);
            Grid.SetColumn(textBox, 1);
            Grid.SetColumn(timeText, 2);

            grid.Children.Add(labelText);
            grid.Children.Add(textBox);
            grid.Children.Add(timeText);

            return grid;
        }

        private TextBox CreateTextBox(string value)
        {
            return new TextBox
            {
                Text = value,
                FontSize = 16,
                Height = 34,
                Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(0, 0, 10, 0)
            };
        }

        private TextBlock CreateTimeText()
        {
            return new TextBlock
            {
                Text = "—",
                Foreground = new SolidColorBrush(Color.FromRgb(74, 222, 128)),
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        private Border CreateCard(UIElement content)
        {
            return new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(24, 32, 43)),
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(18),
                Margin = new Thickness(0, 0, 0, 14),
                Child = content
            };
        }
    }
}
