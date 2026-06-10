using System;
using System.Windows;

namespace ClubTimerXbox
{
    public partial class PeriodPickerWindow : Window
    {
        public DateTime StartDate { get; private set; }

        public DateTime EndDate { get; private set; }

        public PeriodPickerWindow(DateTime startDate, DateTime endDate)
        {
            InitializeComponent();

            StartDate = startDate.Date;
            EndDate = endDate.Date;

            StartCalendar.SelectedDate = StartDate;
            StartCalendar.DisplayDate = StartDate;

            EndCalendar.SelectedDate = EndDate;
            EndCalendar.DisplayDate = EndDate;
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            if (StartCalendar.SelectedDate == null ||
                EndCalendar.SelectedDate == null)
            {
                MessageBox.Show("Выберите дату начала и дату конца.", "Период");
                return;
            }

            StartDate = StartCalendar.SelectedDate.Value.Date;
            EndDate = EndCalendar.SelectedDate.Value.Date;

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