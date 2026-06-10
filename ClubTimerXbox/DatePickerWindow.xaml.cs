using System;
using System.Windows;

namespace ClubTimerXbox
{
    public partial class DatePickerWindow : Window
    {
        public DateTime SelectedDate { get; private set; }

        public DatePickerWindow(DateTime selectedDate)
        {
            InitializeComponent();

            SelectedDate = selectedDate.Date;
            DateCalendar.SelectedDate = SelectedDate;
            DateCalendar.DisplayDate = SelectedDate;
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            if (DateCalendar.SelectedDate == null)
                return;

            SelectedDate = DateCalendar.SelectedDate.Value.Date;

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