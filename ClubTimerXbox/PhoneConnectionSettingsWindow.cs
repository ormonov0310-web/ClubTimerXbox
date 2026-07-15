using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using ClubTimerXbox.Services;

namespace ClubTimerXbox
{
    public class PhoneConnectionSettingsWindow : Window
    {
        private readonly Border _statusCard = new Border();
        private readonly TextBlock _statusTitle = new TextBlock();
        private readonly TextBlock _statusMessage = new TextBlock();
        private readonly TextBlock _identityText = new TextBlock();
        private readonly TextBlock _firebaseAccountText = new TextBlock();
        private readonly TextBlock _operationText = new TextBlock();
        private readonly TextBlock _channelListText = new TextBlock();
        private readonly ComboBox _channelComboBox = new ComboBox();
        private readonly PasswordBox _ownerCodeBox = new PasswordBox();
        private readonly Button _checkButton = new Button();
        private readonly Button _firebaseLoginButton = new Button();
        private readonly Button _refreshChannelsButton = new Button();
        private readonly Button _saveChannelButton = new Button();
        private readonly DispatcherTimer _refreshTimer = new DispatcherTimer();
        private IReadOnlyList<FirebaseChannelOption> _channelOptions =
            Array.Empty<FirebaseChannelOption>();
        private bool _operationRunning;
        private bool _channelListLoading;

        public PhoneConnectionSettingsWindow()
        {
            Title = "Связь с телефоном";
            Width = 680;
            Height = 720;
            MinWidth = 560;
            MinHeight = 560;
            ResizeMode = ResizeMode.CanResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromRgb(16, 20, 28));

            Content = CreateContent();
            RefreshUi();
            UpdateChannelControlsEnabled();

            Loaded += async (_, _) =>
            {
                await CheckBindingAsync();
                await LoadChannelCatalogAsync();
            };
            Closed += (_, _) => _refreshTimer.Stop();

            _refreshTimer.Interval = TimeSpan.FromSeconds(1);
            _refreshTimer.Tick += (_, _) => RefreshUi();
            _refreshTimer.Start();
        }

        private UIElement CreateContent()
        {
            var root = new StackPanel
            {
                Margin = new Thickness(24)
            };

            root.Children.Add(new TextBlock
            {
                Text = "Связь с телефоном",
                Foreground = Brushes.White,
                FontSize = 30,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 18)
            });

            ConfigureStatusCard();
            root.Children.Add(_statusCard);

            _identityText.Foreground = new SolidColorBrush(Color.FromRgb(226, 232, 240));
            _identityText.FontSize = 15;
            _identityText.LineHeight = 23;
            _identityText.TextWrapping = TextWrapping.Wrap;
            _identityText.Margin = new Thickness(0, 0, 0, 8);
            root.Children.Add(_identityText);

            _firebaseAccountText.Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184));
            _firebaseAccountText.FontSize = 14;
            _firebaseAccountText.TextWrapping = TextWrapping.Wrap;
            _firebaseAccountText.Margin = new Thickness(0, 0, 0, 16);
            root.Children.Add(_firebaseAccountText);

            var actionButtons = new WrapPanel
            {
                Margin = new Thickness(0, 0, 0, 24)
            };

            ConfigureButton(_checkButton, "Проверить связь", 180);
            _checkButton.Margin = new Thickness(0, 0, 8, 8);
            _checkButton.Click += async (_, _) => await CheckBindingAsync();
            actionButtons.Children.Add(_checkButton);

            ConfigureButton(_firebaseLoginButton, "Войти в Firebase", 210);
            _firebaseLoginButton.Margin = new Thickness(0, 0, 8, 8);
            _firebaseLoginButton.Click += async (_, _) => await OpenFirebaseLoginAsync();
            actionButtons.Children.Add(_firebaseLoginButton);

            root.Children.Add(actionButtons);

            root.Children.Add(new Border
            {
                Height = 1,
                Background = new SolidColorBrush(Color.FromRgb(51, 65, 85)),
                Margin = new Thickness(0, 0, 0, 22)
            });

            root.Children.Add(new TextBlock
            {
                Text = "Канал приложения",
                Foreground = Brushes.White,
                FontSize = 22,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 16)
            });

            root.Children.Add(CreateLabel("Канал клуба"));
            ConfigureChannelComboBox();
            root.Children.Add(_channelComboBox);

            _channelListText.Foreground =
                new SolidColorBrush(Color.FromRgb(148, 163, 184));
            _channelListText.FontSize = 14;
            _channelListText.TextWrapping = TextWrapping.Wrap;
            _channelListText.Margin = new Thickness(0, 8, 0, 12);
            _channelListText.Text =
                "Загружаем каналы, созданные на телефоне...";
            root.Children.Add(_channelListText);

            ConfigureButton(_refreshChannelsButton, "Обновить список", 180);
            _refreshChannelsButton.HorizontalAlignment = HorizontalAlignment.Left;
            _refreshChannelsButton.Margin = new Thickness(0, 0, 0, 18);
            _refreshChannelsButton.Click += async (_, _) =>
                await LoadChannelCatalogAsync();
            root.Children.Add(_refreshChannelsButton);

            root.Children.Add(CreateLabel("Код владельца"));
            _ownerCodeBox.Height = 42;
            _ownerCodeBox.FontSize = 18;
            _ownerCodeBox.Padding = new Thickness(10, 4, 10, 4);
            _ownerCodeBox.PasswordChar = '●';
            _ownerCodeBox.Margin = new Thickness(0, 0, 0, 12);
            root.Children.Add(_ownerCodeBox);

            _operationText.FontSize = 14;
            _operationText.TextWrapping = TextWrapping.Wrap;
            _operationText.Margin = new Thickness(0, 0, 0, 12);
            root.Children.Add(_operationText);

            ConfigureButton(_saveChannelButton, "Привязать выбранный канал", 280);
            _saveChannelButton.HorizontalAlignment = HorizontalAlignment.Left;
            _saveChannelButton.Click += async (_, _) => await SaveChannelAsync();
            root.Children.Add(_saveChannelButton);

            var closeButton = new Button
            {
                Content = "Закрыть",
                Height = 44,
                MinWidth = 140,
                FontSize = 16,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 26, 0, 0)
            };
            closeButton.Click += (_, _) => Close();
            root.Children.Add(closeButton);

            return new ScrollViewer
            {
                Content = root,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
        }

        private void ConfigureStatusCard()
        {
            var panel = new StackPanel();

            _statusTitle.Foreground = Brushes.White;
            _statusTitle.FontSize = 20;
            _statusTitle.FontWeight = FontWeights.Bold;
            panel.Children.Add(_statusTitle);

            _statusMessage.Foreground = new SolidColorBrush(Color.FromRgb(226, 232, 240));
            _statusMessage.FontSize = 14;
            _statusMessage.LineHeight = 21;
            _statusMessage.TextWrapping = TextWrapping.Wrap;
            _statusMessage.Margin = new Thickness(0, 6, 0, 0);
            panel.Children.Add(_statusMessage);

            _statusCard.BorderThickness = new Thickness(1);
            _statusCard.CornerRadius = new CornerRadius(8);
            _statusCard.Padding = new Thickness(16);
            _statusCard.Margin = new Thickness(0, 0, 0, 16);
            _statusCard.Child = panel;
        }

        private async Task CheckBindingAsync()
        {
            if (_operationRunning)
                return;

            SetOperationRunning(true);
            _operationText.Text = "";

            try
            {
                await FirebaseChannelBindingService.EnsureCurrentBindingAsync(force: true);
            }
            finally
            {
                SetOperationRunning(false);
                RefreshUi();
            }
        }

        private async Task OpenFirebaseLoginAsync()
        {
            if (_operationRunning)
                return;

            var loginWindow = new FirebaseLoginWindow
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            if (loginWindow.ShowDialog() != true)
                return;

            FirebaseChannelBindingService.ResetRuntimeStatus();
            await CheckBindingAsync();
            await LoadChannelCatalogAsync();
        }

        private async Task SaveChannelAsync()
        {
            if (_operationRunning)
                return;

            if (_channelComboBox.SelectedItem is not FirebaseChannelOption selectedChannel ||
                !selectedChannel.IsSelectable)
            {
                ShowOperationError(
                    "Выберите текущий или свободный канал из списка."
                );
                return;
            }

            if (!OwnerAccessService.IsValidCode(_ownerCodeBox.Password))
            {
                ShowOperationError("Неверный код владельца.");
                _ownerCodeBox.SelectAll();
                _ownerCodeBox.Focus();
                return;
            }

            SetOperationRunning(true);
            _operationText.Foreground = new SolidColorBrush(Color.FromRgb(250, 204, 21));
            _operationText.Text = "Проверяем и привязываем канал...";

            bool saved = false;

            try
            {
                FirebaseChannelSwitchResult result =
                    await FirebaseChannelBindingService.TrySwitchCurrentChannelAsync(
                        selectedChannel.ClubId
                    );

                if (!result.Success)
                {
                    ShowOperationError(result.Message);
                    return;
                }

                _ownerCodeBox.Clear();
                _operationText.Foreground = new SolidColorBrush(Color.FromRgb(74, 222, 128));
                _operationText.Text = result.Message;
                saved = true;
            }
            finally
            {
                SetOperationRunning(false);
                RefreshUi();
            }

            if (saved)
                await LoadChannelCatalogAsync();
        }

        private void RefreshUi()
        {
            var identity = PcIdentityService.Current;
            FirebaseChannelBindingStatus status = FirebaseChannelBindingService.CurrentStatus;

            _identityText.Text =
                $"Клуб: {(string.IsNullOrWhiteSpace(identity.ClubName) ? "не выбран" : identity.ClubName)}\n" +
                $"Канал: {(string.IsNullOrWhiteSpace(identity.ClubId) ? "не выбран" : identity.ClubId)}\n" +
                $"ПК: {Environment.MachineName}\n" +
                $"Installation ID: {ShortId(identity.InstallationId)}";

            _firebaseAccountText.Text = string.IsNullOrWhiteSpace(FirebaseAuthService.CurrentEmail)
                ? "Firebase: вход не выполнен"
                : $"Firebase: {FirebaseAuthService.CurrentEmail}";

            _firebaseLoginButton.Content = string.IsNullOrWhiteSpace(FirebaseAuthService.CurrentEmail)
                ? "Войти в Firebase"
                : "Сменить Firebase аккаунт";

            ApplyStatusStyle(status);
        }

        private void ApplyStatusStyle(FirebaseChannelBindingStatus status)
        {
            Color background;
            Color border;

            switch (status.State)
            {
                case FirebaseChannelBindingState.Bound:
                    _statusTitle.Text = "Канал подключен";
                    background = Color.FromRgb(20, 83, 45);
                    border = Color.FromRgb(34, 197, 94);
                    break;

                case FirebaseChannelBindingState.Conflict:
                    _statusTitle.Text = "Канал занят";
                    background = Color.FromRgb(87, 28, 28);
                    border = Color.FromRgb(248, 113, 113);
                    break;

                case FirebaseChannelBindingState.Checking:
                    _statusTitle.Text = "Проверяем канал";
                    background = Color.FromRgb(30, 58, 95);
                    border = Color.FromRgb(96, 165, 250);
                    break;

                case FirebaseChannelBindingState.Unassigned:
                    _statusTitle.Text = "Канал не выбран";
                    background = Color.FromRgb(51, 45, 22);
                    border = Color.FromRgb(250, 204, 21);
                    break;

                case FirebaseChannelBindingState.AuthenticationRequired:
                    _statusTitle.Text = "Нужен вход Firebase";
                    background = Color.FromRgb(51, 45, 22);
                    border = Color.FromRgb(250, 204, 21);
                    break;

                case FirebaseChannelBindingState.Offline:
                    _statusTitle.Text = "Связь временно недоступна";
                    background = Color.FromRgb(51, 45, 22);
                    border = Color.FromRgb(245, 158, 11);
                    break;

                default:
                    _statusTitle.Text = "Канал ожидает проверки";
                    background = Color.FromRgb(30, 41, 59);
                    border = Color.FromRgb(100, 116, 139);
                    break;
            }

            _statusMessage.Text = status.Message;
            _statusCard.Background = new SolidColorBrush(background);
            _statusCard.BorderBrush = new SolidColorBrush(border);
        }

        private void SetOperationRunning(bool value)
        {
            _operationRunning = value;
            _checkButton.IsEnabled = !value;
            _firebaseLoginButton.IsEnabled = !value;
            _saveChannelButton.IsEnabled = !value;
            _ownerCodeBox.IsEnabled = !value;
            UpdateChannelControlsEnabled();
        }

        private void ShowOperationError(string message)
        {
            _operationText.Foreground = new SolidColorBrush(Color.FromRgb(248, 113, 113));
            _operationText.Text = string.IsNullOrWhiteSpace(message)
                ? "Не удалось сохранить канал."
                : message;
        }

        private void ConfigureChannelComboBox()
        {
            _channelComboBox.Height = 46;
            _channelComboBox.FontSize = 17;
            _channelComboBox.Padding = new Thickness(10, 3, 10, 3);
            _channelComboBox.Margin = new Thickness(0);
            _channelComboBox.MaxDropDownHeight = 320;
            _channelComboBox.DisplayMemberPath = nameof(FirebaseChannelOption.DisplayText);
            _channelComboBox.IsTextSearchEnabled = false;
            _channelComboBox.SelectionChanged += (_, _) =>
                UpdateChannelControlsEnabled();

            var itemStyle = new Style(typeof(ComboBoxItem));
            itemStyle.Setters.Add(
                new Setter(
                    IsEnabledProperty,
                    new Binding(nameof(FirebaseChannelOption.IsSelectable))
                )
            );
            itemStyle.Setters.Add(
                new Setter(
                    PaddingProperty,
                    new Thickness(10, 8, 10, 8)
                )
            );

            var occupiedTrigger = new DataTrigger
            {
                Binding = new Binding(nameof(FirebaseChannelOption.Availability)),
                Value = FirebaseChannelAvailability.Occupied
            };
            occupiedTrigger.Setters.Add(
                new Setter(
                    ForegroundProperty,
                    new SolidColorBrush(Color.FromRgb(148, 163, 184))
                )
            );
            itemStyle.Triggers.Add(occupiedTrigger);

            _channelComboBox.ItemContainerStyle = itemStyle;
        }

        private async Task LoadChannelCatalogAsync()
        {
            if (_operationRunning || _channelListLoading)
                return;

            _channelListLoading = true;
            UpdateChannelControlsEnabled();

            string selectedClubId =
                (_channelComboBox.SelectedItem as FirebaseChannelOption)?.ClubId ??
                PcIdentityService.Current.ClubId;

            _channelListText.Foreground =
                new SolidColorBrush(Color.FromRgb(250, 204, 21));
            _channelListText.Text = "Обновляем список каналов...";

            try
            {
                FirebaseChannelCatalogResult result =
                    await FirebaseChannelBindingService.GetChannelCatalogAsync();

                if (!result.Success)
                {
                    _channelListText.Foreground =
                        new SolidColorBrush(Color.FromRgb(248, 113, 113));
                    _channelListText.Text = result.Message;
                    return;
                }

                _channelOptions = result.Channels;
                _channelComboBox.ItemsSource = _channelOptions;

                FirebaseChannelOption? selected = _channelOptions.FirstOrDefault(channel =>
                    channel.IsSelectable &&
                    channel.ClubId.Equals(
                        selectedClubId,
                        StringComparison.OrdinalIgnoreCase
                    )
                );

                selected ??= _channelOptions.FirstOrDefault(channel =>
                    channel.IsCurrent && channel.IsSelectable
                );

                _channelComboBox.SelectedItem = selected;

                _channelListText.Foreground =
                    new SolidColorBrush(Color.FromRgb(148, 163, 184));
                _channelListText.Text =
                    result.Message +
                    " Занятые каналы показаны серым и недоступны для выбора.";
            }
            finally
            {
                _channelListLoading = false;
                UpdateChannelControlsEnabled();
            }
        }

        private void UpdateChannelControlsEnabled()
        {
            bool enabled = !_operationRunning && !_channelListLoading;
            _channelComboBox.IsEnabled = enabled && _channelOptions.Count > 0;
            _refreshChannelsButton.IsEnabled = enabled;
            _saveChannelButton.IsEnabled =
                enabled &&
                _channelComboBox.SelectedItem is FirebaseChannelOption selected &&
                selected.IsSelectable;
        }

        private static void ConfigureButton(Button button, string text, double minWidth)
        {
            button.Content = text;
            button.Height = 44;
            button.MinWidth = minWidth;
            button.Padding = new Thickness(16, 0, 16, 0);
            button.FontSize = 16;
            button.FontWeight = FontWeights.SemiBold;
        }

        private static TextBlock CreateLabel(string text)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = Brushes.White,
                FontSize = 15,
                Margin = new Thickness(0, 0, 0, 7)
            };
        }

        private static string ShortId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "не создан";

            string clean = value.Trim();
            return clean.Length <= 12 ? clean : clean[..12];
        }
    }
}
