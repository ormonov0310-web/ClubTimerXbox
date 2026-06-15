using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ClubTimerXbox.Models;

namespace ClubTimerXbox
{
    public class TuyaDeviceParameterWindow : Window
    {
        private readonly string _deviceName;
        private readonly List<TuyaScheduleTask> _schedules;
        private readonly List<TuyaWorkMode> _workModes;
        private readonly TuyaActiveWorkMode? _activeWorkMode;
        private readonly int _countdownSeconds;
        private readonly bool? _deviceIsOn;

        public TuyaWorkMode? SelectedWorkModeToRun { get; private set; }

        public TuyaWorkMode? SelectedWorkModeToCancel { get; private set; }

        public TuyaWorkMode? SelectedWorkModeToEdit { get; private set; }

        public TuyaWorkMode? SelectedWorkModeToDelete { get; private set; }

        public bool IsNewWorkMode { get; private set; }

        public TuyaScheduleTask? SelectedSchedule { get; private set; }

        public bool IsNewSchedule { get; private set; }

        public TuyaDeviceParameterWindow(
            string deviceName,
            IEnumerable<TuyaScheduleTask>? schedules = null,
            IEnumerable<TuyaWorkMode>? workModes = null,
            TuyaActiveWorkMode? activeWorkMode = null,
            int countdownSeconds = 0,
            bool? deviceIsOn = null)
        {
            _deviceName = deviceName;
            _schedules = schedules?.ToList() ?? new List<TuyaScheduleTask>();
            _workModes = workModes?.ToList() ?? new List<TuyaWorkMode>();
            _activeWorkMode = activeWorkMode;
            _countdownSeconds = Math.Max(0, countdownSeconds);
            _deviceIsOn = deviceIsOn;

            Title = "Параметры розетки";
            Width = 660;
            Height = 780;
            MinWidth = 580;
            MinHeight = 620;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromRgb(14, 18, 26));

            Content = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = CreateContent()
            };
        }

        private UIElement CreateContent()
        {
            var root = new StackPanel
            {
                Margin = new Thickness(24)
            };

            root.Children.Add(new TextBlock
            {
                Text = _deviceName,
                Foreground = Brushes.White,
                FontSize = 28,
                FontWeight = FontWeights.Bold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 4)
            });

            root.Children.Add(new TextBlock
            {
                Text = "Режимы работы и сценарии розетки",
                Foreground = new SolidColorBrush(Color.FromRgb(160, 170, 188)),
                FontSize = 15,
                Margin = new Thickness(0, 0, 0, 18)
            });

            root.Children.Add(CreateWorkModeCard());
            root.Children.Add(CreateScenarioCard());

            return root;
        }

        private Border CreateWorkModeCard()
        {
            var panel = new StackPanel();

            panel.Children.Add(new TextBlock
            {
                Text = "Режим работы",
                Foreground = Brushes.White,
                FontSize = 22,
                FontWeight = FontWeights.Bold
            });

            panel.Children.Add(new TextBlock
            {
                Text = "Режим отправляется в розетку через Tuya countdown. Если закрыть ПК-приложение, розетка всё равно выполнит таймер сама.",
                Foreground = new SolidColorBrush(Color.FromRgb(160, 170, 188)),
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 20,
                Margin = new Thickness(0, 6, 0, 14)
            });

            if (_workModes.Count == 0)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = "Режимов пока нет.",
                    Foreground = new SolidColorBrush(Color.FromRgb(145, 155, 172)),
                    FontSize = 14,
                    Margin = new Thickness(0, 14, 0, 0)
                });
            }
            else
            {
                foreach (var mode in _workModes)
                    panel.Children.Add(CreateWorkModeRow(mode));
            }

            panel.Children.Add(CreateAddWorkModeButton());

            return CreateOuterCard(panel, Color.FromRgb(74, 222, 128));
        }

        private Border CreateScenarioCard()
        {
            var panel = new StackPanel();

            panel.Children.Add(new TextBlock
            {
                Text = "Сценарии Tuya",
                Foreground = Brushes.White,
                FontSize = 22,
                FontWeight = FontWeights.Bold
            });

            panel.Children.Add(new TextBlock
            {
                Text = "Таймеры сохраняются в Tuya. Розетка выполнит команду сама по расписанию.",
                Foreground = new SolidColorBrush(Color.FromRgb(160, 170, 188)),
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 20,
                Margin = new Thickness(0, 6, 0, 14)
            });

            if (_schedules.Count == 0)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = "Таймеров пока нет.",
                    Foreground = new SolidColorBrush(Color.FromRgb(145, 155, 172)),
                    FontSize = 14,
                    Margin = new Thickness(0, 14, 0, 0)
                });
            }
            else
            {
                foreach (var schedule in _schedules)
                    panel.Children.Add(CreateScheduleCard(schedule));
            }

            panel.Children.Add(CreateAddScheduleButton());

            return CreateOuterCard(panel, Color.FromRgb(56, 189, 248));
        }

        private Border CreateOuterCard(UIElement child, Color accent)
        {
            return new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(24, 30, 42)),
                BorderBrush = new SolidColorBrush(accent),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(16),
                Margin = new Thickness(0, 0, 0, 18),
                Child = child
            };
        }

        private Border CreateAddWorkModeButton()
        {
            var button = CreateWideButton("+ Добавить режим", Color.FromRgb(22, 163, 74));
            button.Margin = new Thickness(0, 12, 0, 0);

            button.MouseLeftButtonUp += (_, _) =>
            {
                SelectedWorkModeToEdit = new TuyaWorkMode
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = "Выключить через 10 минут",
                    ModeType = TuyaWorkModeTypes.TurnOffAfterMinutes,
                    Minutes = 10
                };
                IsNewWorkMode = true;
                DialogResult = true;
                Close();
            };

            return button;
        }

        private Border CreateAddScheduleButton()
        {
            var button = CreateWideButton("+ Добавить таймер", Color.FromRgb(37, 99, 235));
            button.Margin = new Thickness(0, 12, 0, 0);

            button.MouseLeftButtonUp += (_, _) =>
            {
                var defaultTime = DateTime.Now.AddMinutes(5);
                SelectedSchedule = new TuyaScheduleTask
                {
                    Time = defaultTime.ToString("HH:mm"),
                    Loops = "1111111",
                    TimezoneId = "Asia/Bishkek",
                    TurnOn = true,
                    AliasName = ""
                };
                IsNewSchedule = true;
                DialogResult = true;
                Close();
            };

            return button;
        }

        private Border CreateWideButton(string text, Color background)
        {
            return new Border
            {
                Background = new SolidColorBrush(background),
                CornerRadius = new CornerRadius(12),
                Height = 46,
                Cursor = Cursors.Hand,
                Child = new TextBlock
                {
                    Text = text,
                    Foreground = Brushes.White,
                    FontSize = 17,
                    FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
        }

        private Border CreateWorkModeRow(TuyaWorkMode mode)
        {
            bool isActive =
                _countdownSeconds > 0 &&
                _activeWorkMode != null &&
                _activeWorkMode.WorkModeId.Equals(mode.Id, StringComparison.OrdinalIgnoreCase);

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });

            var info = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center
            };

            info.Children.Add(new TextBlock
            {
                Text = mode.Name,
                Foreground = Brushes.White,
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                TextWrapping = TextWrapping.Wrap
            });

            info.Children.Add(new TextBlock
            {
                Text = isActive ? GetActiveText() : GetWorkModeDescription(mode),
                Foreground = isActive
                    ? new SolidColorBrush(Color.FromRgb(74, 222, 128))
                    : new SolidColorBrush(Color.FromRgb(160, 170, 188)),
                FontSize = 13,
                FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal,
                Margin = new Thickness(0, 4, 0, 0)
            });

            var actionButton = CreateSmallButton(
                isActive ? "Отменить" : "Запустить",
                isActive ? Color.FromRgb(180, 83, 9) : Color.FromRgb(37, 99, 235),
                110);

            actionButton.Click += (_, _) =>
            {
                if (isActive)
                    SelectedWorkModeToCancel = mode.Clone();
                else
                    SelectedWorkModeToRun = mode.Clone();

                DialogResult = true;
                Close();
            };

            Grid.SetColumn(info, 0);
            Grid.SetColumn(actionButton, 1);
            grid.Children.Add(info);
            grid.Children.Add(actionButton);

            var card = new Border
            {
                Background = new SolidColorBrush(isActive
                    ? Color.FromRgb(16, 48, 32)
                    : Color.FromRgb(31, 38, 52)),
                BorderBrush = new SolidColorBrush(isActive
                    ? Color.FromRgb(74, 222, 128)
                    : Color.FromRgb(51, 65, 85)),
                BorderThickness = new Thickness(isActive ? 2 : 1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(14),
                Margin = new Thickness(0, 12, 0, 0),
                Child = grid
            };

            card.ContextMenu = CreateWorkModeContextMenu(mode);

            return card;
        }

        private ContextMenu CreateWorkModeContextMenu(TuyaWorkMode mode)
        {
            var menu = new ContextMenu();

            var editItem = new MenuItem { Header = "Изменить" };
            editItem.Click += (_, _) =>
            {
                SelectedWorkModeToEdit = mode.Clone();
                IsNewWorkMode = false;
                DialogResult = true;
                Close();
            };

            var deleteItem = new MenuItem { Header = "Удалить" };
            deleteItem.Click += (_, _) =>
            {
                SelectedWorkModeToDelete = mode.Clone();
                DialogResult = true;
                Close();
            };

            menu.Items.Add(editItem);
            menu.Items.Add(deleteItem);

            return menu;
        }

        private Border CreateScheduleCard(TuyaScheduleTask schedule)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });

            var timeText = new TextBlock
            {
                Text = schedule.Time,
                Foreground = Brushes.White,
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center
            };

            var info = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center
            };

            info.Children.Add(new TextBlock
            {
                Text = schedule.TurnOn ? "Включить розетку" : "Выключить розетку",
                Foreground = Brushes.White,
                FontSize = 16,
                FontWeight = FontWeights.SemiBold
            });

            info.Children.Add(new TextBlock
            {
                Text = GetRepeatText(schedule),
                Foreground = new SolidColorBrush(Color.FromRgb(160, 170, 188)),
                FontSize = 13,
                Margin = new Thickness(0, 3, 0, 0)
            });

            var actionPill = new Border
            {
                Background = new SolidColorBrush(schedule.TurnOn
                    ? Color.FromRgb(20, 83, 45)
                    : Color.FromRgb(127, 29, 29)),
                CornerRadius = new CornerRadius(999),
                Padding = new Thickness(12, 6, 12, 6),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = schedule.TurnOn ? "Вкл" : "Выкл",
                    Foreground = Brushes.White,
                    FontWeight = FontWeights.Bold,
                    FontSize = 13
                }
            };

            Grid.SetColumn(timeText, 0);
            Grid.SetColumn(info, 1);
            Grid.SetColumn(actionPill, 2);

            grid.Children.Add(timeText);
            grid.Children.Add(info);
            grid.Children.Add(actionPill);

            var card = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(31, 38, 52)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(51, 65, 85)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(14),
                Margin = new Thickness(0, 12, 0, 0),
                Cursor = Cursors.Hand,
                Child = grid
            };

            card.MouseLeftButtonUp += (_, _) =>
            {
                SelectedSchedule = schedule.Clone();
                IsNewSchedule = false;
                DialogResult = true;
                Close();
            };

            return card;
        }

        private Button CreateSmallButton(string text, Color background, double width)
        {
            return new Button
            {
                Content = text,
                Width = width,
                Height = 38,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                Background = new SolidColorBrush(background),
                BorderBrush = Brushes.Transparent
            };
        }

        private string GetActiveText()
        {
            string action = _deviceIsOn == false ? "вкл" : "выкл";
            return $"Сейчас работает: через {FormatCountdown(_countdownSeconds)} {action}";
        }

        private static string GetWorkModeDescription(TuyaWorkMode mode)
        {
            return mode.ModeType switch
            {
                TuyaWorkModeTypes.TurnOnAfterMinutes => $"Через {mode.Minutes} мин. включить",
                TuyaWorkModeTypes.TurnOnForMinutes => $"Сейчас включить, через {mode.Minutes} мин. выключить",
                TuyaWorkModeTypes.TurnOffForMinutes => $"Сейчас выключить, через {mode.Minutes} мин. включить",
                _ => $"Через {mode.Minutes} мин. выключить"
            };
        }

        private static string GetRepeatText(TuyaScheduleTask schedule)
        {
            if (schedule.IsEveryDay)
                return "Повтор: каждый день";

            if (!string.IsNullOrWhiteSpace(schedule.Date) && schedule.Date.Length == 8)
                return $"Повтор: один раз, {schedule.Date[..4]}-{schedule.Date.Substring(4, 2)}-{schedule.Date.Substring(6, 2)}";

            return "Повтор: один раз";
        }

        private static string FormatCountdown(int seconds)
        {
            if (seconds <= 0)
                return "0 мин";

            int minutes = (int)Math.Ceiling(seconds / 60.0);

            return $"{minutes} мин";
        }
    }
}
