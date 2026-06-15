using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ClubTimerXbox.Models;

namespace ClubTimerXbox
{
    public class TuyaWorkModeEditWindow : Window
    {
        private readonly TextBox _nameTextBox = new TextBox();
        private readonly TextBox _minutesTextBox = new TextBox();
        private readonly ComboBox _typeComboBox = new ComboBox();
        private readonly bool _isNew;

        public TuyaWorkMode Mode { get; }

        public bool ShouldDelete { get; private set; }

        public TuyaWorkModeEditWindow(TuyaWorkMode mode, bool isNew)
        {
            _isNew = isNew;
            Mode = mode.Clone();

            Title = isNew ? "Добавить режим" : "Изменить режим";
            Width = 520;
            Height = 520;
            MinWidth = 480;
            MinHeight = 460;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromRgb(14, 18, 26));

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
                Text = _isNew ? "Новый режим" : "Режим работы",
                Foreground = Brushes.White,
                FontSize = 28,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 4)
            });

            root.Children.Add(new TextBlock
            {
                Text = "Режим отправляется в розетку через Tuya countdown и работает без открытого ПК-приложения.",
                Foreground = new SolidColorBrush(Color.FromRgb(160, 170, 188)),
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 21,
                Margin = new Thickness(0, 0, 0, 18)
            });

            root.Children.Add(CreateCard());
            root.Children.Add(CreateButtonRow());

            return root;
        }

        private Border CreateCard()
        {
            var panel = new StackPanel();

            panel.Children.Add(CreateLabel("Тип режима"));
            _typeComboBox.Height = 42;
            _typeComboBox.FontSize = 16;
            _typeComboBox.Items.Add("Включить через N минут");
            _typeComboBox.Items.Add("Выключить через N минут");
            _typeComboBox.Items.Add("Включить на N минут");
            _typeComboBox.Items.Add("Выключить на N минут");
            _typeComboBox.SelectedIndex = GetTypeIndex(Mode.ModeType);
            panel.Children.Add(_typeComboBox);

            panel.Children.Add(CreateLabel("Минуты"));
            _minutesTextBox.Height = 42;
            _minutesTextBox.FontSize = 16;
            _minutesTextBox.Text = Mode.Minutes.ToString();
            _minutesTextBox.Margin = new Thickness(0, 0, 0, 2);
            panel.Children.Add(_minutesTextBox);

            panel.Children.Add(CreateLabel("Название"));
            _nameTextBox.Height = 42;
            _nameTextBox.FontSize = 16;
            _nameTextBox.Text = Mode.Name;
            panel.Children.Add(_nameTextBox);

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
                if (!ApplySelection())
                    return;

                DialogResult = true;
                Close();
            };
            DockPanel.SetDock(saveButton, Dock.Right);
            panel.Children.Add(saveButton);

            return panel;
        }

        private bool ApplySelection()
        {
            if (!int.TryParse(_minutesTextBox.Text.Trim(), out int minutes) ||
                minutes < 1 ||
                minutes > 1440)
            {
                MessageBox.Show("Минуты должны быть от 1 до 1440.", "Режим работы");
                return false;
            }

            Mode.Minutes = minutes;
            Mode.ModeType = GetTypeFromIndex(_typeComboBox.SelectedIndex);

            Mode.Name = _nameTextBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(Mode.Name))
            {
                Mode.Name = GetDefaultName(Mode.ModeType, Mode.Minutes);
            }

            return true;
        }

        private static int GetTypeIndex(string modeType)
        {
            return modeType switch
            {
                TuyaWorkModeTypes.TurnOnAfterMinutes => 0,
                TuyaWorkModeTypes.TurnOffAfterMinutes => 1,
                TuyaWorkModeTypes.TurnOnForMinutes => 2,
                TuyaWorkModeTypes.TurnOffForMinutes => 3,
                _ => 1
            };
        }

        private static string GetTypeFromIndex(int index)
        {
            return index switch
            {
                0 => TuyaWorkModeTypes.TurnOnAfterMinutes,
                2 => TuyaWorkModeTypes.TurnOnForMinutes,
                3 => TuyaWorkModeTypes.TurnOffForMinutes,
                _ => TuyaWorkModeTypes.TurnOffAfterMinutes
            };
        }

        private static string GetDefaultName(string modeType, int minutes)
        {
            return modeType switch
            {
                TuyaWorkModeTypes.TurnOnAfterMinutes => $"Включить через {minutes} минут",
                TuyaWorkModeTypes.TurnOnForMinutes => $"Включить на {minutes} минут",
                TuyaWorkModeTypes.TurnOffForMinutes => $"Выключить на {minutes} минут",
                _ => $"Выключить через {minutes} минут"
            };
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
    }
}
