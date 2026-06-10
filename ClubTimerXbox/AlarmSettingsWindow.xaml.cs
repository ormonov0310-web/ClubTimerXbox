using System.Windows;
using System.Windows.Controls;
using ClubTimerXbox.Models;
using ClubTimerXbox.Services;

namespace ClubTimerXbox
{
    public partial class AlarmSettingsWindow : Window
    {
        public AlarmSettingsWindow()
        {
            InitializeComponent();
            LoadSettings();
        }

        private void LoadSettings()
        {
            var settings = AlarmSettingsService.Current;

            AlarmEnabledCheckBox.IsChecked = settings.IsEnabled;

            SelectComboBoxByTag(
                TriggerSecondsComboBox,
                settings.TriggerBeforeEndSeconds.ToString()
            );

            SelectComboBoxByTag(
                SoundComboBox,
                settings.SoundName
            );

            SelectComboBoxByTag(
                DurationComboBox,
                settings.SoundDurationSeconds.ToString()
            );
        }

        private void SelectComboBoxByTag(ComboBox comboBox, string tagValue)
        {
            foreach (var item in comboBox.Items)
            {
                if (item is ComboBoxItem comboBoxItem &&
                    comboBoxItem.Tag?.ToString() == tagValue)
                {
                    comboBox.SelectedItem = comboBoxItem;
                    return;
                }
            }

            if (comboBox.Items.Count > 0)
                comboBox.SelectedIndex = 0;
        }

        private string GetSelectedTag(ComboBox comboBox)
        {
            if (comboBox.SelectedItem is ComboBoxItem item)
                return item.Tag?.ToString() ?? "";

            return "";
        }

        private void TestSoundButton_Click(object sender, RoutedEventArgs e)
        {
            string soundName = GetSelectedTag(SoundComboBox);
            AlarmSoundService.PlayOnce(soundName);
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            int triggerSeconds = int.Parse(GetSelectedTag(TriggerSecondsComboBox));
            int durationSeconds = int.Parse(GetSelectedTag(DurationComboBox));
            string soundName = GetSelectedTag(SoundComboBox);

            var settings = new AlarmSettings
            {
                IsEnabled = AlarmEnabledCheckBox.IsChecked == true,
                TriggerBeforeEndSeconds = triggerSeconds,
                SoundName = soundName,
                SoundDurationSeconds = durationSeconds
            };

            AlarmSettingsService.Save(settings);

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