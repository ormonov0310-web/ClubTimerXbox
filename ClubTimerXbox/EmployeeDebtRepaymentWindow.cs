using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ClubTimerXbox.Models;

namespace ClubTimerXbox
{
    public sealed class EmployeeDebtRepaymentWindow : AdaptiveDialogWindow
    {
        private readonly Func<EmployeeDebtManualRepaymentQuote> _quoteProvider;
        private readonly Action<int, long> _submitPayment;
        private readonly TextBox _amountBox = new();
        private readonly TextBlock _availableText = new();
        private readonly TextBlock _debtText = new();
        private readonly TextBlock _maximumText = new();
        private readonly TextBlock _salaryPreviewText = new();
        private readonly TextBlock _debtPreviewText = new();
        private readonly TextBlock _errorText = new();
        private readonly Button _okButton = new();
        private EmployeeDebtManualRepaymentQuote _quote = new();

        public EmployeeDebtRepaymentWindow(
            Func<EmployeeDebtManualRepaymentQuote> quoteProvider,
            Action<int, long> submitPayment)
            : base(510, 450)
        {
            _quoteProvider = quoteProvider;
            _submitPayment = submitPayment;
            Title = "Погасить долг из зарплаты";

            var panel = new StackPanel { Margin = new Thickness(22) };
            panel.Children.Add(new TextBlock
            {
                Text = "Ручное погашение долга",
                Foreground = Brushes.White,
                FontSize = 21,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 14)
            });
            ConfigureInfoText(_availableText);
            ConfigureInfoText(_debtText);
            ConfigureInfoText(_maximumText);
            _maximumText.Margin = new Thickness(0, 0, 0, 16);
            panel.Children.Add(_availableText);
            panel.Children.Add(_debtText);
            panel.Children.Add(_maximumText);

            panel.Children.Add(new TextBlock
            {
                Text = "Сумма погашения",
                Foreground = new SolidColorBrush(Color.FromRgb(156, 163, 175)),
                FontSize = 15,
                Margin = new Thickness(0, 0, 0, 6)
            });
            NumericInputHint.Apply(_amountBox, "Сумма погашения");
            _amountBox.Height = 42;
            _amountBox.FontSize = 20;
            _amountBox.Padding = new Thickness(10, 5, 10, 5);
            _amountBox.Margin = new Thickness(0, 0, 0, 14);
            _amountBox.TextChanged += (_, _) => UpdatePreview();
            panel.Children.Add(_amountBox);

            ConfigurePreviewText(_salaryPreviewText);
            ConfigurePreviewText(_debtPreviewText);
            panel.Children.Add(_salaryPreviewText);
            panel.Children.Add(_debtPreviewText);

            _errorText.Foreground = new SolidColorBrush(Color.FromRgb(248, 113, 113));
            _errorText.FontSize = 14;
            _errorText.TextWrapping = TextWrapping.Wrap;
            _errorText.Margin = new Thickness(0, 8, 0, 8);
            panel.Children.Add(_errorText);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 8, 0, 0)
            };
            var cancel = new Button
            {
                Content = "Отмена",
                Width = 105,
                Height = 40,
                Margin = new Thickness(0, 0, 8, 0)
            };
            cancel.Click += (_, _) => DialogResult = false;
            _okButton.Content = "ОК";
            _okButton.Width = 105;
            _okButton.Height = 40;
            _okButton.Background = new SolidColorBrush(Color.FromRgb(22, 101, 52));
            _okButton.Foreground = Brushes.White;
            _okButton.FontWeight = FontWeights.SemiBold;
            _okButton.Click += (_, _) => Accept();
            buttons.Children.Add(cancel);
            buttons.Children.Add(_okButton);
            panel.Children.Add(buttons);
            SetAdaptiveContent(panel);

            Loaded += (_, _) =>
            {
                RefreshQuote();
            };
            DialogKeyboard.Configure(this, _amountBox, _okButton);
        }

        private static void ConfigureInfoText(TextBlock text)
        {
            text.Foreground = new SolidColorBrush(Color.FromRgb(209, 213, 219));
            text.FontSize = 16;
            text.TextWrapping = TextWrapping.Wrap;
            text.Margin = new Thickness(0, 0, 0, 7);
        }

        private static void ConfigurePreviewText(TextBlock text)
        {
            text.Foreground = Brushes.White;
            text.FontSize = 17;
            text.FontWeight = FontWeights.SemiBold;
            text.TextWrapping = TextWrapping.Wrap;
            text.Margin = new Thickness(0, 0, 0, 7);
        }

        private bool RefreshQuote()
        {
            try
            {
                _quote = _quoteProvider();
                _availableText.Text = $"Свободные деньги: {_quote.AvailableSalaryAmount} сом";
                _debtText.Text = $"Остаток долга: {_quote.DebtRemainingAmount} сом";
                _maximumText.Text = _quote.MaximumManualPaymentAmount > 0
                    ? $"Можно оплатить вручную не более " +
                      $"{_quote.MaximumManualPaymentAmount} сом. " +
                      $"Для автооплаты останется минимум " +
                      $"{_quote.AutomaticReserveAmount} сом."
                    : _quote.DebtRemainingAmount <= 0
                        ? "Долг уже закрыт."
                        : _quote.AvailableSalaryAmount <= 0
                            ? "Свободных денег для ручного погашения сейчас нет."
                            : $"Остаток {_quote.AutomaticReserveAmount} сом " +
                              "оставлен для автооплаты.";
                UpdatePreview();
                return true;
            }
            catch (Exception ex)
            {
                _errorText.Text = ex.Message;
                _okButton.IsEnabled = false;
                return false;
            }
        }

        private void UpdatePreview()
        {
            bool parsed = int.TryParse(_amountBox.Text.Trim(), out int amount);
            amount = parsed ? Math.Max(0, amount) : 0;
            int salaryAfter = Math.Max(0, _quote.AvailableSalaryAmount - amount);
            int debtAfter = Math.Max(0, _quote.DebtRemainingAmount - amount);
            _salaryPreviewText.Text =
                $"Свободные деньги: {_quote.AvailableSalaryAmount} → {salaryAfter} сом";
            _debtPreviewText.Text =
                $"Долг: {_quote.DebtRemainingAmount} → {debtAfter} сом";

            bool valid = parsed && amount > 0 &&
                         amount <= _quote.MaximumManualPaymentAmount &&
                         amount <= _quote.AvailableSalaryAmount &&
                         amount <= _quote.DebtRemainingAmount;
            _okButton.IsEnabled = valid;
            SolidColorBrush previewColor = valid || amount == 0
                ? new SolidColorBrush(Color.FromRgb(229, 231, 235))
                : new SolidColorBrush(Color.FromRgb(248, 113, 113));
            _salaryPreviewText.Foreground = previewColor;
            _debtPreviewText.Foreground = previewColor;
        }

        private void Accept()
        {
            _errorText.Text = "";
            if (!RefreshQuote())
                return;
            if (!int.TryParse(_amountBox.Text.Trim(), out int amount) || amount <= 0)
            {
                _errorText.Text = "Введите целую сумму больше 0.";
                return;
            }
            if (amount > _quote.MaximumManualPaymentAmount)
            {
                _errorText.Text =
                    $"Данные обновились. Сейчас можно оплатить не более " +
                    $"{_quote.MaximumManualPaymentAmount} сом.";
                UpdatePreview();
                return;
            }

            try
            {
                _submitPayment(amount, _quote.Revision);
                DialogResult = true;
            }
            catch (Exception ex)
            {
                _errorText.Text = ex.Message;
                RefreshQuote();
                UpdatePreview();
            }
        }
    }
}
