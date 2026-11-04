using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ClubTimerXbox.Models;
using ClubTimerXbox.Services;

namespace ClubTimerXbox
{
    public class PenaltyWindow : AdaptiveDialogWindow
    {
        public int PenaltyMinutes { get; private set; }

        private readonly ClubPlace _place;
        private readonly TextBox _minutesTextBox = new TextBox();
        private readonly TextBlock _newTimeText = new TextBlock();
        private readonly TextBlock _currentInfoText = new TextBlock();
        private readonly Button _okButton = new Button();

        public PenaltyWindow(ClubPlace place) : base(540, 360)
        {
            _place = place;

            Title = "Добавить штраф";
            SetAdaptiveContent(CreateContent());

            UpdateCalculation();
        }

        private UIElement CreateContent()
        {
            var root = new StackPanel
            {
                Margin = new Thickness(24)
            };

            root.Children.Add(new TextBlock
            {
                Text = $"Добавить штраф — {_place.Name}",
                Foreground = Brushes.White,
                FontSize = 26,
                FontWeight = FontWeights.Bold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 14)
            });

            root.Children.Add(CreateCurrentInfoCard());
            root.Children.Add(CreateMinutesRow());
            root.Children.Add(CreateInfoCard(_place.IsOpenMode ? "К оплате после штрафа" : "Останется времени", _newTimeText));
            root.Children.Add(CreateButtonsPanel());

            return root;
        }

        private Border CreateCurrentInfoCard()
        {
            var panel = new StackPanel();

            panel.Children.Add(new TextBlock
            {
                Text = "Текущие данные",
                Foreground = new SolidColorBrush(Color.FromRgb(170, 180, 195)),
                FontSize = 14
            });

            _currentInfoText.Foreground = Brushes.White;
            _currentInfoText.FontSize = 18;
            _currentInfoText.FontWeight = FontWeights.SemiBold;
            _currentInfoText.Margin = new Thickness(0, 6, 0, 0);
            _currentInfoText.LineHeight = 27;
            _currentInfoText.TextWrapping = TextWrapping.Wrap;
            panel.Children.Add(_currentInfoText);

            return new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(24, 32, 43)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16),
                Margin = new Thickness(0, 0, 0, 14),
                Child = panel
            };
        }

        private UIElement CreateMinutesRow()
        {
            var panel = new StackPanel
            {
                Margin = new Thickness(0, 0, 0, 16)
            };

            var label = new TextBlock
            {
                Text = "Штраф, минут",
                Foreground = Brushes.White,
                FontSize = 18,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6),
                VerticalAlignment = VerticalAlignment.Center
            };

            _minutesTextBox.Text = "5";
            NumericInputHint.Apply(_minutesTextBox, "Штраф, минут");
            _minutesTextBox.FontSize = 20;
            _minutesTextBox.Height = 42;
            _minutesTextBox.Padding = new Thickness(10, 5, 10, 5);

            _minutesTextBox.TextChanged += (_, _) => UpdateCalculation();

            _minutesTextBox.PreviewMouseWheel += (_, e) =>
            {
                int current = 0;

                if (int.TryParse(_minutesTextBox.Text.Trim(), out int parsed))
                    current = parsed;

                current = (int)System.Math.Clamp((long)current + (e.Delta > 0 ? 1 : -1), 1, int.MaxValue);

                _minutesTextBox.Text = current.ToString();
                _minutesTextBox.CaretIndex = _minutesTextBox.Text.Length;

                e.Handled = true;
            };

            panel.Children.Add(label);
            panel.Children.Add(_minutesTextBox);

            return panel;
        }

        private Border CreateInfoCard(string title, TextBlock valueText)
        {
            var panel = new StackPanel();

            panel.Children.Add(new TextBlock
            {
                Text = title,
                Foreground = new SolidColorBrush(Color.FromRgb(170, 180, 195)),
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap
            });

            valueText.Foreground = Brushes.White;
            valueText.FontSize = 24;
            valueText.FontWeight = FontWeights.Bold;
            valueText.Margin = new Thickness(0, 5, 0, 0);
            valueText.TextWrapping = TextWrapping.Wrap;

            panel.Children.Add(valueText);

            return new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(24, 32, 43)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16),
                Margin = new Thickness(0, 0, 0, 12),
                Child = panel
            };
        }

        private UIElement CreateButtonsPanel()
        {
            var buttonsPanel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Margin = new Thickness(0, 18, 0, 0)
            };

            var cancelButton = new Button
            {
                Content = "Отмена",
                Height = 44,
                FontSize = 16,
                Margin = new Thickness(0, 0, 0, 8)
            };

            cancelButton.Click += (_, _) =>
            {
                DialogResult = false;
                Close();
            };

            _okButton.Content = "ОК";
            _okButton.Height = 44;
            _okButton.FontSize = 16;

            _okButton.Click += (_, _) =>
            {
                if (!ReadMinutes(out int minutes))
                {
                    MessageBox.Show("Введите минуты числом.", "Добавить штраф");
                    return;
                }

                if (minutes <= 0)
                {
                    MessageBox.Show("Минуты штрафа должны быть больше 0.", "Добавить штраф");
                    return;
                }

                if (!SessionPenaltyPolicy.TryGetPrice(_place.ActivePricePerMinute, minutes, out _))
                {
                    MessageBox.Show("Сумма штрафа слишком велика.", "Добавить штраф");
                    return;
                }

                PenaltyMinutes = minutes;

                DialogResult = true;
                Close();
            };

            buttonsPanel.Children.Add(cancelButton);
            buttonsPanel.Children.Add(_okButton);
            DialogKeyboard.Configure(this, _minutesTextBox, _okButton);

            return buttonsPanel;
        }

        private void UpdateCalculation()
        {
            var now = System.DateTime.Now;
            _currentInfoText.Text = _place.IsOpenMode
                ? $"Начислено: {TariffService.CalculateOpenModePrice(_place.AccruedAmountBeforeCurrentSegment, _place.ActivePricePerMinute, _place.StartTime, now)} сом"
                : $"Осталось: {TariffService.FormatTime(SessionPenaltyPolicy.GetPrepaidRemainingSeconds(_place, now))}\nОплачено: {_place.PaidAmount} сом";
            var preview = new ClubPlace
            {
                IsBusy = _place.IsBusy, IsOpenMode = _place.IsOpenMode,
                IsCalculating = _place.IsCalculating,
                IsTimeExpiredAwaitingAcknowledgement = _place.IsTimeExpiredAwaitingAcknowledgement,
                StartTime = _place.StartTime, RemainingSeconds = _place.RemainingSeconds,
                ActivePricePerMinute = _place.ActivePricePerMinute,
                AccruedAmountBeforeCurrentSegment = _place.AccruedAmountBeforeCurrentSegment
            };
            _okButton.IsEnabled = ReadMinutes(out int minutes) &&
                SessionPenaltyPolicy.TryApply(preview, minutes, now, out _);
            if (!_okButton.IsEnabled)
            {
                _newTimeText.Text = "—";
                return;
            }

            if (_place.IsOpenMode)
            {
                _newTimeText.Text = $"{TariffService.CalculateOpenModePrice(preview.AccruedAmountBeforeCurrentSegment, preview.ActivePricePerMinute, preview.StartTime, now)} сом";
            }
            else
            {
                _newTimeText.Text = TariffService.FormatTime(preview.RemainingSeconds);
            }
        }

        private bool ReadMinutes(out int minutes)
        {
            return int.TryParse(_minutesTextBox.Text.Trim(), out minutes);
        }
    }
}
