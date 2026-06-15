using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ClubTimerXbox.Models;
using ClubTimerXbox.Services;

namespace ClubTimerXbox
{
    public class TuyaSettingsWindow : Window
    {
        private readonly ComboBox _endpointComboBox = new ComboBox();
        private readonly TextBox _accessIdTextBox = new TextBox();
        private readonly PasswordBox _accessSecretBox = new PasswordBox();
        private readonly CheckBox _enabledCheckBox = new CheckBox();
        private readonly CheckBox _dryRunCheckBox = new CheckBox();
        private readonly Button _testButton = new Button();
        private readonly TextBlock _statusText = new TextBlock();
        private readonly StackPanel _devicesPanel = new StackPanel();
        private List<TuyaDevice> _lastRenderedDevices = new List<TuyaDevice>();

        private static readonly List<TuyaEndpointOption> EndpointOptions = new List<TuyaEndpointOption>
        {
            new TuyaEndpointOption("Central Europe", "https://openapi.tuyaeu.com"),
            new TuyaEndpointOption("Western Europe", "https://openapi-weaz.tuyaeu.com"),
            new TuyaEndpointOption("Western America", "https://openapi.tuyaus.com"),
            new TuyaEndpointOption("Eastern America", "https://openapi-ueaz.tuyaus.com"),
            new TuyaEndpointOption("China", "https://openapi.tuyacn.com"),
            new TuyaEndpointOption("India", "https://openapi.tuyain.com"),
            new TuyaEndpointOption("Singapore", "https://openapi-sg.iotbing.com")
        };

        public TuyaSettingsWindow()
        {
            Title = "Tuya розетки";
            Width = 760;
            Height = 720;
            MinWidth = 680;
            MinHeight = 600;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromRgb(16, 20, 28));

            Content = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = CreateContent()
            };

            LoadSettings();
        }

        private UIElement CreateContent()
        {
            var root = new StackPanel
            {
                Margin = new Thickness(24)
            };

            root.Children.Add(new TextBlock
            {
                Text = "Tuya розетки",
                Foreground = Brushes.White,
                FontSize = 30,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 8)
            });

            root.Children.Add(new TextBlock
            {
                Text =
                    "Здесь подключаем Tuya Cloud и управляем списком розеток. " +
                    "По найденному устройству можно нажать правой кнопкой мыши: переименовать, скрыть или показать на главном экране.",
                Foreground = new SolidColorBrush(Color.FromRgb(170, 180, 195)),
                FontSize = 15,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 23,
                Margin = new Thickness(0, 0, 0, 18)
            });

            root.Children.Add(CreateCredentialsCard());
            root.Children.Add(CreateStatusCard());
            root.Children.Add(CreateDevicesCard());
            root.Children.Add(CreateButtons());

            return root;
        }

        private Border CreateCredentialsCard()
        {
            var panel = new StackPanel();

            panel.Children.Add(CreateSectionTitle("Доступ к Tuya Cloud"));

            _enabledCheckBox.Content = "Включить интеграцию Tuya";
            _enabledCheckBox.Foreground = Brushes.White;
            _enabledCheckBox.FontSize = 15;
            _enabledCheckBox.Margin = new Thickness(0, 0, 0, 10);
            panel.Children.Add(_enabledCheckBox);

            _dryRunCheckBox.Content = "Безопасный режим: команды не отправляются на розетки";
            _dryRunCheckBox.Foreground = Brushes.White;
            _dryRunCheckBox.FontSize = 15;
            _dryRunCheckBox.Margin = new Thickness(0, 0, 0, 14);
            panel.Children.Add(_dryRunCheckBox);

            foreach (var option in EndpointOptions)
                _endpointComboBox.Items.Add(option);

            _endpointComboBox.DisplayMemberPath = nameof(TuyaEndpointOption.Title);
            _endpointComboBox.Height = 36;
            _endpointComboBox.Margin = new Thickness(0, 5, 0, 12);
            panel.Children.Add(CreateLabeledControl("Дата-центр", _endpointComboBox));

            panel.Children.Add(CreateLabeledControl("Access ID", _accessIdTextBox));
            panel.Children.Add(CreateLabeledControl("Access Secret", _accessSecretBox));

            panel.Children.Add(new TextBlock
            {
                Text = "Access Secret не отправляй в чат. Вводи его только здесь, в локальном окне программы.",
                Foreground = new SolidColorBrush(Color.FromRgb(251, 191, 36)),
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 20,
                Margin = new Thickness(0, 6, 0, 0)
            });

            return CreateCard(panel);
        }

        private Border CreateStatusCard()
        {
            var panel = new StackPanel();
            panel.Children.Add(CreateSectionTitle("Проверка"));

            _testButton.Content = "Проверить подключение";
            _testButton.Height = 42;
            _testButton.FontSize = 15;
            _testButton.Margin = new Thickness(0, 0, 0, 12);
            _testButton.Click += async (_, _) => await TestConnectionAsync();

            panel.Children.Add(_testButton);

            _statusText.Text = "Пока не проверено.";
            _statusText.Foreground = new SolidColorBrush(Color.FromRgb(170, 180, 195));
            _statusText.FontSize = 15;
            _statusText.TextWrapping = TextWrapping.Wrap;
            _statusText.LineHeight = 23;
            panel.Children.Add(_statusText);

            return CreateCard(panel);
        }

        private Border CreateDevicesCard()
        {
            var panel = new StackPanel();
            panel.Children.Add(CreateSectionTitle("Найденные устройства"));
            panel.Children.Add(_devicesPanel);

            _devicesPanel.Children.Add(new TextBlock
            {
                Text = "После проверки здесь появятся розетки из Tuya.",
                Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                FontSize = 15,
                TextWrapping = TextWrapping.Wrap
            });

            return CreateCard(panel);
        }

        private UIElement CreateButtons()
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 8, 0, 0)
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

        private void LoadSettings()
        {
            var settings = TuyaSettingsStorageService.Current;

            _enabledCheckBox.IsChecked = settings.IsEnabled;
            _dryRunCheckBox.IsChecked = settings.DryRunMode;
            _accessIdTextBox.Text = settings.AccessId;
            _accessSecretBox.Password = settings.AccessSecret;

            int endpointIndex = EndpointOptions.FindIndex(option =>
                option.Endpoint.Equals(settings.Endpoint, StringComparison.OrdinalIgnoreCase));

            _endpointComboBox.SelectedIndex = endpointIndex >= 0 ? endpointIndex : 0;
        }

        private void SaveSettings()
        {
            TuyaSettingsStorageService.Save(BuildSettingsFromUi());
            SetStatus("Настройки Tuya сохранены.", Color.FromRgb(74, 222, 128));
        }

        private async Task TestConnectionAsync()
        {
            SaveSettings();

            _testButton.IsEnabled = false;
            SetStatus("Проверяем Tuya...", Color.FromRgb(251, 191, 36));
            _devicesPanel.Children.Clear();

            try
            {
                var result = await TuyaCloudService.TestConnectionAsync(TuyaSettingsStorageService.Current);
                SetStatus(result.Message, Color.FromRgb(74, 222, 128));
                RenderDevices(result.Devices);
            }
            catch (Exception ex)
            {
                SetStatus(
                    "Не удалось подключиться к Tuya.\n\n" +
                    ex.Message + "\n\n" +
                    "Чаще всего причина в дата-центре, Access ID/Secret, не привязанном аккаунте Tuya или не подключенных API-сервисах.",
                    Color.FromRgb(248, 113, 113));
                RenderDevices(new List<TuyaDevice>());
            }
            finally
            {
                _testButton.IsEnabled = true;
            }
        }

        private void RenderDevices(List<TuyaDevice> devices)
        {
            _lastRenderedDevices = new List<TuyaDevice>(devices);
            _devicesPanel.Children.Clear();

            if (devices.Count == 0)
            {
                _devicesPanel.Children.Add(new TextBlock
                {
                    Text = "Устройства не найдены.",
                    Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                    FontSize = 15
                });

                return;
            }

            foreach (var device in devices)
                _devicesPanel.Children.Add(CreateDeviceRow(device));
        }

        private Border CreateDeviceRow(TuyaDevice device)
        {
            var settings = TuyaSettingsStorageService.Current;
            string displayName = TuyaSettingsStorageService.GetDeviceDisplayName(settings, device);
            string cloudName = string.IsNullOrWhiteSpace(device.Name) ? device.Id : device.Name;
            bool isHidden = TuyaSettingsStorageService.IsDeviceHidden(settings, device.Id);
            string deviceTypeTitle = TuyaSettingsStorageService.GetDeviceTypeTitle(settings, device.Id);

            var panel = new StackPanel();

            panel.Children.Add(new TextBlock
            {
                Text = displayName,
                Foreground = isHidden ? new SolidColorBrush(Color.FromRgb(148, 163, 184)) : Brushes.White,
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                TextWrapping = TextWrapping.Wrap
            });

            string nameLine = displayName.Equals(cloudName, StringComparison.OrdinalIgnoreCase)
                ? ""
                : $"Tuya: {cloudName}\n";

            panel.Children.Add(new TextBlock
            {
                Text =
                    nameLine +
                    $"ID: {device.Id}\n" +
                    $"Тип: {deviceTypeTitle}\n" +
                    $"Категория: {Fallback(device.Category)}   Продукт: {Fallback(device.ProductName)}\n" +
                    $"Статус: {(device.Online ? "онлайн" : "офлайн")}   {FormatSwitchState(device.IsOn)}\n" +
                    $"Главный экран: {(isHidden ? "скрыта" : "показывается")}",
                Foreground = new SolidColorBrush(Color.FromRgb(170, 180, 195)),
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 19,
                Margin = new Thickness(0, 5, 0, 0)
            });

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 12, 0, 0)
            };

            var onButton = CreateDeviceCommandButton("Включить", Color.FromRgb(34, 197, 94));
            onButton.Click += async (_, _) => await SendDeviceCommandAsync(device, true);

            var offButton = CreateDeviceCommandButton("Выключить", Color.FromRgb(239, 68, 68));
            offButton.Margin = new Thickness(8, 0, 0, 0);
            offButton.Click += async (_, _) => await SendDeviceCommandAsync(device, false);

            buttons.Children.Add(onButton);
            buttons.Children.Add(offButton);
            panel.Children.Add(buttons);

            var card = CreateCard(panel, new Thickness(0, 0, 0, 8), Color.FromRgb(17, 24, 39));
            card.BorderThickness = new Thickness(1);
            card.BorderBrush = new SolidColorBrush(isHidden ? Color.FromRgb(71, 85, 105) : Color.FromRgb(37, 99, 235));
            card.ContextMenu = CreateDeviceContextMenu(device);

            return card;
        }

        private ContextMenu CreateDeviceContextMenu(TuyaDevice device)
        {
            bool isHidden = TuyaSettingsStorageService.IsDeviceHidden(TuyaSettingsStorageService.Current, device.Id);

            var menu = new ContextMenu();
            menu.Items.Add(CreateMenuItem("Переименовать", () => RenameDevice(device)));
            menu.Items.Add(CreateDeviceTypeMenu(device));
            menu.Items.Add(CreateMenuItem(isHidden ? "Показать на главном" : "Скрыть с главного", () => SetDeviceHidden(device, !isHidden)));

            return menu;
        }

        private MenuItem CreateDeviceTypeMenu(TuyaDevice device)
        {
            string currentType = TuyaSettingsStorageService.GetDeviceType(
                TuyaSettingsStorageService.Current,
                device.Id);

            var menu = new MenuItem
            {
                Header = "Тип устройства"
            };

            var tvItem = CreateMenuItem("ТВ розетка", () => SetDeviceType(device, TuyaDeviceTypes.TvSocket));
            tvItem.IsCheckable = true;
            tvItem.IsChecked = currentType == TuyaDeviceTypes.TvSocket;

            var applianceItem = CreateMenuItem("Прибор", () => SetDeviceType(device, TuyaDeviceTypes.Appliance));
            applianceItem.IsCheckable = true;
            applianceItem.IsChecked = currentType == TuyaDeviceTypes.Appliance;

            menu.Items.Add(tvItem);
            menu.Items.Add(applianceItem);

            return menu;
        }

        private void RenameDevice(TuyaDevice device)
        {
            var settings = BuildSettingsFromUi();
            var preference = TuyaSettingsStorageService.GetOrCreateDevicePreference(settings, device.Id, device.Name);
            string currentName = !string.IsNullOrWhiteSpace(preference.DisplayName)
                ? preference.DisplayName
                : TuyaSettingsStorageService.GetDeviceDisplayName(settings, device);

            string? newName = ShowRenameDialog(currentName);

            if (newName == null)
                return;

            preference.DisplayName = newName.Trim();
            TuyaSettingsStorageService.Save(settings);

            SetStatus(
                string.IsNullOrWhiteSpace(preference.DisplayName)
                    ? "Локальное имя устройства очищено."
                    : $"Устройство переименовано: {preference.DisplayName}.",
                Color.FromRgb(74, 222, 128));

            RenderDevices(_lastRenderedDevices);
        }

        private void SetDeviceHidden(TuyaDevice device, bool isHidden)
        {
            var settings = BuildSettingsFromUi();
            var preference = TuyaSettingsStorageService.GetOrCreateDevicePreference(settings, device.Id, device.Name);
            preference.IsHidden = isHidden;
            TuyaSettingsStorageService.Save(settings);

            SetStatus(
                $"{TuyaSettingsStorageService.GetDeviceDisplayName(settings, device)}: {(isHidden ? "скрыта с главного экрана" : "будет показана на главном экране")}.",
                Color.FromRgb(74, 222, 128));

            RenderDevices(_lastRenderedDevices);
        }

        private void SetDeviceType(TuyaDevice device, string deviceType)
        {
            var settings = BuildSettingsFromUi();
            var preference = TuyaSettingsStorageService.GetOrCreateDevicePreference(settings, device.Id, device.Name);
            preference.DeviceType = deviceType;
            TuyaSettingsStorageService.Save(settings);

            SetStatus(
                $"{TuyaSettingsStorageService.GetDeviceDisplayName(settings, device)}: тип изменён на {TuyaSettingsStorageService.GetDeviceTypeTitle(settings, device.Id)}.",
                Color.FromRgb(74, 222, 128));

            RenderDevices(_lastRenderedDevices);
        }

        private string? ShowRenameDialog(string currentName)
        {
            var dialog = new Window
            {
                Title = "Переименовать розетку",
                Width = 420,
                Height = 180,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                Background = new SolidColorBrush(Color.FromRgb(16, 20, 28))
            };

            var root = new StackPanel
            {
                Margin = new Thickness(18)
            };

            root.Children.Add(new TextBlock
            {
                Text = "Название в ClubTimerXbox",
                Foreground = Brushes.White,
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 10)
            });

            var textBox = new TextBox
            {
                Text = currentName,
                Height = 34,
                FontSize = 15,
                Margin = new Thickness(0, 0, 0, 14)
            };

            root.Children.Add(textBox);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            string? result = null;

            var okButton = new Button
            {
                Content = "Сохранить",
                Width = 110,
                Height = 36,
                FontSize = 14,
                Margin = new Thickness(0, 0, 8, 0)
            };

            okButton.Click += (_, _) =>
            {
                result = textBox.Text;
                dialog.DialogResult = true;
            };

            var cancelButton = new Button
            {
                Content = "Отмена",
                Width = 100,
                Height = 36,
                FontSize = 14
            };

            cancelButton.Click += (_, _) => dialog.DialogResult = false;

            buttons.Children.Add(okButton);
            buttons.Children.Add(cancelButton);
            root.Children.Add(buttons);

            dialog.Content = root;
            dialog.Loaded += (_, _) =>
            {
                textBox.Focus();
                textBox.SelectAll();
            };

            return dialog.ShowDialog() == true ? result : null;
        }

        private async Task SendDeviceCommandAsync(TuyaDevice device, bool turnOn)
        {
            SaveSettings();

            var settings = TuyaSettingsStorageService.Current;
            string action = turnOn ? "включить" : "выключить";
            string deviceName = TuyaSettingsStorageService.GetDeviceDisplayName(settings, device);

            if (!settings.IsEnabled)
            {
                SetStatus("Сначала включи интеграцию Tuya и сохрани настройки.", Color.FromRgb(251, 191, 36));
                MessageBox.Show(
                    "Сначала включи галочку 'Включить интеграцию Tuya'.",
                    "Tuya"
                );
                return;
            }

            if (settings.DryRunMode)
            {
                SetStatus(
                    $"Безопасный режим включен: команда '{action} {deviceName}' не отправлена. " +
                    "Сними галочку безопасного режима, если хочешь реально управлять розеткой.",
                    Color.FromRgb(251, 191, 36));
                MessageBox.Show(
                    "Включён безопасный режим.\n\nКоманда не отправлена на розетку. Сними галочку безопасного режима, если хочешь реально включать и выключать розетки.",
                    "Tuya"
                );
                return;
            }

            try
            {
                SetStatus($"Отправляем команду: {action} {deviceName}...", Color.FromRgb(251, 191, 36));
                await TuyaCloudService.SetSwitchAsync(settings, device.Id, turnOn);
                SetStatus($"{deviceName}: команда отправлена.", Color.FromRgb(74, 222, 128));
            }
            catch (Exception ex)
            {
                SetStatus(
                    $"Не удалось {action} {deviceName}.\n\n{ex.Message}",
                    Color.FromRgb(248, 113, 113));
            }
        }

        private TuyaSettings BuildSettingsFromUi()
        {
            var selectedEndpoint = _endpointComboBox.SelectedItem as TuyaEndpointOption;

            return new TuyaSettings
            {
                IsEnabled = _enabledCheckBox.IsChecked == true,
                DryRunMode = _dryRunCheckBox.IsChecked != false,
                Endpoint = selectedEndpoint?.Endpoint ?? "https://openapi.tuyaeu.com",
                AccessId = _accessIdTextBox.Text,
                AccessSecret = _accessSecretBox.Password,
                PlaceMappings = TuyaSettingsStorageService.Current.PlaceMappings,
                DevicePreferences = TuyaSettingsStorageService.Current.DevicePreferences,
                WorkModes = TuyaSettingsStorageService.Current.WorkModes,
                WorkModesInitialized = TuyaSettingsStorageService.Current.WorkModesInitialized,
                ActiveWorkModes = TuyaSettingsStorageService.Current.ActiveWorkModes
            };
        }

        private void SetStatus(string text, Color color)
        {
            _statusText.Text = text;
            _statusText.Foreground = new SolidColorBrush(color);
        }

        private static MenuItem CreateMenuItem(string title, Action action)
        {
            var item = new MenuItem
            {
                Header = title
            };

            item.Click += (_, _) => action();
            return item;
        }

        private static Button CreateDeviceCommandButton(string text, Color accent)
        {
            return new Button
            {
                Content = text,
                Width = 120,
                Height = 36,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                Background = new SolidColorBrush(accent),
                BorderBrush = Brushes.Transparent,
                Padding = new Thickness(10, 0, 10, 0)
            };
        }

        private static StackPanel CreateLabeledControl(string label, Control control)
        {
            var panel = new StackPanel
            {
                Margin = new Thickness(0, 0, 0, 10)
            };

            panel.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = new SolidColorBrush(Color.FromRgb(170, 180, 195)),
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 4)
            });

            control.Height = 36;
            control.FontSize = 15;
            panel.Children.Add(control);

            return panel;
        }

        private static TextBlock CreateSectionTitle(string text)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = Brushes.White,
                FontSize = 21,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 12)
            };
        }

        private static Border CreateCard(UIElement content)
        {
            return CreateCard(content, new Thickness(0, 0, 0, 14), Color.FromRgb(24, 32, 43));
        }

        private static Border CreateCard(UIElement content, Thickness margin, Color background)
        {
            return new Border
            {
                Background = new SolidColorBrush(background),
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(16),
                Margin = margin,
                Child = content
            };
        }

        private static string Fallback(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "-" : value;
        }

        private static string FormatSwitchState(bool? state)
        {
            if (!state.HasValue)
                return "состояние выключателя неизвестно";

            return state.Value ? "включено" : "выключено";
        }

        private class TuyaEndpointOption
        {
            public TuyaEndpointOption(string name, string endpoint)
            {
                Name = name;
                Endpoint = endpoint;
            }

            public string Name { get; }

            public string Endpoint { get; }

            public string Title => $"{Name} - {Endpoint}";
        }
    }
}
