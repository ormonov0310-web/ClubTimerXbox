using System;
using System.Windows;
using System.Windows.Controls;
using ClubTimerXbox.Services;

namespace ClubTimerXbox
{
    public partial class MonthPickerWindow : Window
    {
        public int SelectedYear { get; private set; }

        public int SelectedMonth { get; private set; }

        public MonthPickerWindow(int selectedYear, int selectedMonth)
        {
            InitializeComponent();

            SelectedYear = selectedYear;
            SelectedMonth = selectedMonth;

            YearTextBox.Text = SelectedYear.ToString();

            foreach (var item in MonthComboBox.Items)
            {
                if (item is ComboBoxItem comboBoxItem &&
                    comboBoxItem.Tag?.ToString() == SelectedMonth.ToString())
                {
                    MonthComboBox.SelectedItem = comboBoxItem;
                    break;
                }
            }

            if (MonthComboBox.SelectedItem == null)
                MonthComboBox.SelectedIndex = BusinessCalendarService
                    .GetBusinessDate(ClubClock.Current.LocalNow)
                    .Month - 1;
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(YearTextBox.Text.Trim(), out int year))
            {
                MessageBox.Show("Введите правильный год.", "Месяц");
                return;
            }

            if (year < 2000 || year > 2100)
            {
                MessageBox.Show("Год должен быть от 2000 до 2100.", "Месяц");
                return;
            }

            if (MonthComboBox.SelectedItem is not ComboBoxItem item)
            {
                MessageBox.Show("Выберите месяц.", "Месяц");
                return;
            }

            if (!int.TryParse(item.Tag?.ToString(), out int month))
            {
                MessageBox.Show("Месяц выбран неправильно.", "Месяц");
                return;
            }

            SelectedYear = year;
            SelectedMonth = month;

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
