using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ClubTimerXbox.Models;

namespace ClubTimerXbox
{
    public enum CashCheckoutResultType
    {
        None,
        Paid,
        TransferToPlace
    }

    internal enum CashCheckoutPaymentMode
    {
        Cash,
        MBank,
        Partial
    }

    public partial class CashCheckoutWindow : Window
    {
        private readonly List<CheckoutItem> _items;
        private readonly DispatcherTimer _selectedModeBlinkTimer = new DispatcherTimer();
        private bool _isUpdatingFields;
        private bool _hasInvalidPaymentInput;
        private bool _blinkStrong = true;
        private CashCheckoutPaymentMode _paymentMode = CashCheckoutPaymentMode.MBank;

        public string EmployeeName { get; }
        public string OperationTitle { get; }
        public string PlaceName { get; }
        public Guid? GameSessionId { get; }

        public int TotalAmount { get; }
        public int CashAmount { get; private set; }
        public int MBankAmount { get; private set; }

        public PaymentRecord? PaymentRecord { get; private set; }

        public CashCheckoutResultType ResultType { get; private set; } = CashCheckoutResultType.None;

        public CashCheckoutWindow(
            string employeeName,
            string operationTitle,
            List<CheckoutItem> items,
            string placeName = "",
            Guid? gameSessionId = null,
            bool allowTransferToPlace = false)
        {
            InitializeComponent();
            NumericInputHint.Apply(CashTextBox, "Наличные");
            NumericInputHint.Apply(MBankTextBox, "М Банк");
            DataObject.AddPastingHandler(CashTextBox, AmountTextBox_Pasting);
            DataObject.AddPastingHandler(MBankTextBox, AmountTextBox_Pasting);

            EmployeeName = employeeName;
            OperationTitle = operationTitle;
            PlaceName = placeName;
            GameSessionId = gameSessionId;
            _items = items ?? new List<CheckoutItem>();
            _selectedModeBlinkTimer.Interval = TimeSpan.FromMilliseconds(620);
            _selectedModeBlinkTimer.Tick += (_, _) => PulseSelectedPaymentMode();

            TransferToPlaceButton.Visibility = allowTransferToPlace
                ? Visibility.Visible
                : Visibility.Collapsed;

            foreach (var item in _items)
            {
                TotalAmount += item.TotalAmount;
            }

            Render();
            SetFullMBank();
            DialogKeyboard.Configure(this, CashTextBox, ConfirmPaymentButton);
        }

        private void Render()
        {
            EmployeeText.Text = $"Админ: {EmployeeName}";
            OperationText.Text = $"Операция: {OperationTitle}";

            if (string.IsNullOrWhiteSpace(PlaceName))
                PlaceText.Text = "";
            else
                PlaceText.Text = $"Место: {PlaceName}";

            ItemsPanel.Children.Clear();

            foreach (var item in _items)
            {
                var text = new TextBlock
                {
                    Text = $"{item.Name} × {item.Quantity} = {item.TotalAmount} сом",
                    Foreground = System.Windows.Media.Brushes.White,
                    FontSize = 16,
                    Margin = new Thickness(0, 0, 0, 7),
                    TextWrapping = TextWrapping.Wrap
                };

                ItemsPanel.Children.Add(text);
            }

            TotalText.Text = $"Итого: {TotalAmount} сом";
            UpdatePaymentModeVisuals();
        }

        private void SetFullCash()
        {
            SetAmounts(
                cashAmount: TotalAmount,
                mBankAmount: 0,
                mode: CashCheckoutPaymentMode.Cash
            );
        }

        private void SetFullMBank()
        {
            SetAmounts(
                cashAmount: 0,
                mBankAmount: TotalAmount,
                mode: CashCheckoutPaymentMode.MBank
            );
        }

        private void SetAmounts(int cashAmount, int mBankAmount, CashCheckoutPaymentMode mode, TextBox? editedBox = null)
        {
            if (cashAmount < 0)
                cashAmount = 0;

            if (mBankAmount < 0)
                mBankAmount = 0;

            _isUpdatingFields = true;

            CashAmount = cashAmount;
            MBankAmount = mBankAmount;
            _paymentMode = mode;
            _hasInvalidPaymentInput = false;

            // Keep the active editor's text/caret; update only its calculated counterpart.
            if (editedBox != CashTextBox || !TryParseAmount(CashTextBox.Text, out int typedCash) || typedCash != CashAmount)
            {
                CashTextBox.Text = CashAmount == 0 ? "" : CashAmount.ToString();
                if (editedBox == CashTextBox) CashTextBox.CaretIndex = CashTextBox.Text.Length;
            }
            if (editedBox != MBankTextBox || !TryParseAmount(MBankTextBox.Text, out int typedBank) || typedBank != MBankAmount)
            {
                MBankTextBox.Text = MBankAmount == 0 ? "" : MBankAmount.ToString();
                if (editedBox == MBankTextBox) MBankTextBox.CaretIndex = MBankTextBox.Text.Length;
            }

            _isUpdatingFields = false;

            UpdatePaymentCheckText();
            UpdatePaymentModeVisuals();
        }

        private static bool TryParseAmount(string text, out int value)
        {
            value = 0;
            // Either payment leg may be empty: its explicit business meaning is zero.
            return string.IsNullOrWhiteSpace(text) || int.TryParse(text.Trim(), out value) && value >= 0;
        }

        private void ShowInvalidPaymentInput()
        {
            _hasInvalidPaymentInput = true;
            PaymentCheckText.Text = "Введите целую неотрицательную сумму.";
        }

        private void CashTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingFields)
                return;

            if (!TryParseAmount(CashTextBox.Text, out int cash))
            {
                ShowInvalidPaymentInput();
                return;
            }

            if (cash > TotalAmount)
                cash = TotalAmount;

            int mBank = TotalAmount - cash;

            SetAmounts(cash, mBank, CashCheckoutPaymentMode.Partial, CashTextBox);
        }

        private void MBankTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingFields)
                return;

            if (!TryParseAmount(MBankTextBox.Text, out int mBank))
            {
                ShowInvalidPaymentInput();
                return;
            }

            if (mBank > TotalAmount)
                mBank = TotalAmount;

            int cash = TotalAmount - mBank;

            SetAmounts(cash, mBank, CashCheckoutPaymentMode.Partial, MBankTextBox);
        }

        private void AmountTextBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (sender is TextBox textBox)
                textBox.SelectAll();
        }

        private void AmountTextBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not TextBox textBox)
                return;

            if (textBox.IsKeyboardFocusWithin)
                return;

            e.Handled = true;
            textBox.Focus();
        }

        private void AmountTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            foreach (char character in e.Text)
            {
                if (!char.IsDigit(character))
                {
                    e.Handled = true;
                    return;
                }
            }
            if (sender is TextBox input)
                e.Handled = !TryParseAmount(TextAfterInsertion(input, e.Text), out _);
        }

        private static string TextAfterInsertion(TextBox input, string text) =>
            input.Text.Remove(input.SelectionStart, input.SelectionLength).Insert(input.SelectionStart, text);

        private static void AmountTextBox_Pasting(object sender, DataObjectPastingEventArgs e)
        {
            if (sender is not TextBox input ||
                !e.SourceDataObject.GetDataPresent(DataFormats.UnicodeText) ||
                e.SourceDataObject.GetData(DataFormats.UnicodeText) is not string text ||
                !TryParseAmount(TextAfterInsertion(input, text), out _))
                e.CancelCommand();
        }

        private void UpdatePaymentCheckText()
        {
            int totalPaid = CashAmount + MBankAmount;

            if (totalPaid == TotalAmount)
            {
                if (_paymentMode == CashCheckoutPaymentMode.Partial)
                {
                    PaymentCheckText.Text =
                        $"Частичная оплата: наличные {CashAmount} сом, М Банк {MBankAmount} сом.";
                }
                else
                {
                    PaymentCheckText.Text =
                        $"Оплата сходится: наличные {CashAmount} сом, М Банк {MBankAmount} сом.";
                }

                return;
            }

            PaymentCheckText.Text =
                $"Ошибка: оплачено {totalPaid} сом из {TotalAmount} сом.";
        }

        private void UpdatePaymentModeVisuals()
        {
            FullCashButton.Content = _paymentMode == CashCheckoutPaymentMode.Cash
                ? $"✓ Наличные\n{TotalAmount} сом"
                : $"Наличные\n{TotalAmount} сом";

            FullMBankButton.Content = _paymentMode == CashCheckoutPaymentMode.MBank
                ? $"✓ М Банк\n{TotalAmount} сом"
                : $"М Банк\n{TotalAmount} сом";

            ResetQuickButtonVisual(FullCashButton, Color.FromRgb(22, 163, 74));
            ResetQuickButtonVisual(FullMBankButton, Color.FromRgb(37, 99, 235));

            PartialPaymentCard.BorderBrush = new SolidColorBrush(Color.FromRgb(51, 65, 85));
            PartialPaymentCard.BorderThickness = new Thickness(1);
            PaymentModeText.Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184));

            if (_paymentMode == CashCheckoutPaymentMode.Partial)
            {
                _selectedModeBlinkTimer.Stop();
                FullCashButton.Opacity = 0.72;
                FullMBankButton.Opacity = 0.72;
                PartialPaymentCard.BorderBrush = new SolidColorBrush(Color.FromRgb(251, 191, 36));
                PartialPaymentCard.BorderThickness = new Thickness(2);
                PaymentModeText.Foreground = new SolidColorBrush(Color.FromRgb(251, 191, 36));
                PaymentModeText.Text = $"Выбрана частичная оплата: {CashAmount} сом нал / {MBankAmount} сом М Банк.";
                return;
            }

            PaymentModeText.Text = _paymentMode == CashCheckoutPaymentMode.Cash
                ? $"Выбрано: вся сумма наличными, {TotalAmount} сом."
                : $"Выбрано: вся сумма через М Банк, {TotalAmount} сом.";

            _blinkStrong = true;
            PulseSelectedPaymentMode();
            _selectedModeBlinkTimer.Start();
        }

        private static void ResetQuickButtonVisual(Button button, Color color)
        {
            button.Background = new SolidColorBrush(color);
            button.BorderBrush = new SolidColorBrush(color);
            button.BorderThickness = new Thickness(2);
            button.Opacity = 0.78;
        }

        private void PulseSelectedPaymentMode()
        {
            if (_paymentMode == CashCheckoutPaymentMode.Partial)
                return;

            Button selectedButton = _paymentMode == CashCheckoutPaymentMode.Cash
                ? FullCashButton
                : FullMBankButton;

            Button otherButton = _paymentMode == CashCheckoutPaymentMode.Cash
                ? FullMBankButton
                : FullCashButton;

            Color selectedColor = _paymentMode == CashCheckoutPaymentMode.Cash
                ? Color.FromRgb(22, 163, 74)
                : Color.FromRgb(37, 99, 235);

            otherButton.Opacity = 0.72;
            otherButton.BorderThickness = new Thickness(2);

            selectedButton.Opacity = _blinkStrong ? 1.0 : 0.78;
            selectedButton.BorderBrush = new SolidColorBrush(_blinkStrong
                ? Color.FromRgb(253, 224, 71)
                : selectedColor);
            selectedButton.BorderThickness = _blinkStrong
                ? new Thickness(4)
                : new Thickness(2);

            _blinkStrong = !_blinkStrong;
        }

        private void FullCashButton_Click(object sender, RoutedEventArgs e)
        {
            SetFullCash();
        }

        private void FullMBankButton_Click(object sender, RoutedEventArgs e)
        {
            SetFullMBank();
        }

        private void ConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            if (_hasInvalidPaymentInput ||
                !TryParseAmount(CashTextBox.Text, out int enteredCash) ||
                !TryParseAmount(MBankTextBox.Text, out int enteredBank) ||
                enteredCash != CashAmount || enteredBank != MBankAmount)
            {
                MessageBox.Show("Проверьте введённые суммы наличных и М Банк.", "Касса");
                return;
            }

            if (TotalAmount <= 0)
            {
                MessageBox.Show("Сумма оплаты должна быть больше 0.", "Касса");
                return;
            }

            if (CashAmount + MBankAmount != TotalAmount)
            {
                MessageBox.Show(
                    "Сумма наличных и М Банк должна совпадать с итогом.",
                    "Касса"
                );
                return;
            }

            PaymentRecord = new PaymentRecord
            {
                EmployeeName = EmployeeName,
                OperationTitle = OperationTitle,
                PlaceName = PlaceName,
                GameSessionId = GameSessionId,
                Items = _items,
                TotalAmount = TotalAmount,
                CashAmount = CashAmount,
                MBankAmount = MBankAmount,
                Comment = ""
            };

            ResultType = CashCheckoutResultType.Paid;
            DialogResult = true;
            Close();
        }

        private void TransferToPlaceButton_Click(object sender, RoutedEventArgs e)
        {
            if (TotalAmount <= 0)
            {
                MessageBox.Show("Сумма переноса должна быть больше 0.", "Касса");
                return;
            }

            ResultType = CashCheckoutResultType.TransferToPlace;
            PaymentRecord = null;
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            ResultType = CashCheckoutResultType.None;
            DialogResult = false;
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            _selectedModeBlinkTimer.Stop();
            base.OnClosed(e);
        }
    }
}
