using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using ClubTimerXbox.Models;

namespace ClubTimerXbox
{
    public partial class CashCheckoutWindow : Window
    {
        private readonly List<CheckoutItem> _items;
        private bool _isUpdatingFields;

        public string EmployeeName { get; }
        public string OperationTitle { get; }
        public string PlaceName { get; }
        public Guid? GameSessionId { get; }

        public int TotalAmount { get; }
        public int CashAmount { get; private set; }
        public int MBankAmount { get; private set; }

        public PaymentRecord? PaymentRecord { get; private set; }

        public CashCheckoutWindow(
            string employeeName,
            string operationTitle,
            List<CheckoutItem> items,
            string placeName = "",
            Guid? gameSessionId = null)
        {
            InitializeComponent();

            EmployeeName = employeeName;
            OperationTitle = operationTitle;
            PlaceName = placeName;
            GameSessionId = gameSessionId;
            _items = items ?? new List<CheckoutItem>();

            foreach (var item in _items)
            {
                TotalAmount += item.TotalAmount;
            }

            Render();
            SetFullMBank();
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
            FullCashButton.Content = $"Наличные\n{TotalAmount} сом";
            FullMBankButton.Content = $"М Банк\n{TotalAmount} сом";
        }

        private void SetFullCash()
        {
            SetAmounts(
                cashAmount: TotalAmount,
                mBankAmount: 0
            );
        }

        private void SetFullMBank()
        {
            SetAmounts(
                cashAmount: 0,
                mBankAmount: TotalAmount
            );
        }

        private void SetAmounts(int cashAmount, int mBankAmount)
        {
            if (cashAmount < 0)
                cashAmount = 0;

            if (mBankAmount < 0)
                mBankAmount = 0;

            _isUpdatingFields = true;

            CashAmount = cashAmount;
            MBankAmount = mBankAmount;

            CashTextBox.Text = CashAmount.ToString();
            MBankTextBox.Text = MBankAmount.ToString();

            _isUpdatingFields = false;

            UpdatePaymentCheckText();
        }

        private int ParseAmount(string text)
        {
            if (int.TryParse(text, out int value))
            {
                if (value < 0)
                    return 0;

                return value;
            }

            return 0;
        }

        private void CashTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingFields)
                return;

            int cash = ParseAmount(CashTextBox.Text);

            if (cash > TotalAmount)
                cash = TotalAmount;

            int mBank = TotalAmount - cash;

            SetAmounts(cash, mBank);
        }

        private void MBankTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingFields)
                return;

            int mBank = ParseAmount(MBankTextBox.Text);

            if (mBank > TotalAmount)
                mBank = TotalAmount;

            int cash = TotalAmount - mBank;

            SetAmounts(cash, mBank);
        }

        private void UpdatePaymentCheckText()
        {
            int totalPaid = CashAmount + MBankAmount;

            if (totalPaid == TotalAmount)
            {
                PaymentCheckText.Text =
                    $"Оплата сходится: наличные {CashAmount} сом, М Банк {MBankAmount} сом.";
                return;
            }

            PaymentCheckText.Text =
                $"Ошибка: оплачено {totalPaid} сом из {TotalAmount} сом.";
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

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}