using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ClubTimerXbox.Services;

namespace ClubTimerXbox
{
    public sealed class UpdateInstallProgressWindow : Window
    {
        private readonly TextBlock _statusText;
        private readonly TextBlock _downloadPercentText;
        private readonly TextBlock _readyPercentText;
        private readonly ProgressBar _downloadProgressBar;
        private readonly ProgressBar _readyProgressBar;
        private bool _isRunning;

        public UpdateInstallProgressWindow()
        {
            Title = "Скачивание обновления";
            Width = 520;
            Height = 360;
            MinWidth = 480;
            MinHeight = 320;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            Background = new SolidColorBrush(Color.FromRgb(11, 14, 20));

            var root = new StackPanel
            {
                Margin = new Thickness(26)
            };

            root.Children.Add(new TextBlock
            {
                Text = "Обновление ClubTimerXbox",
                Foreground = Brushes.White,
                FontSize = 26,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 8)
            });

            _statusText = new TextBlock
            {
                Text = "Готовим скачивание...",
                Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                FontSize = 15,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 22,
                Margin = new Thickness(0, 0, 0, 22)
            };
            root.Children.Add(_statusText);

            _downloadPercentText = new TextBlock();
            _downloadProgressBar = new ProgressBar();
            root.Children.Add(CreateProgressSection(
                "Скачивание",
                _downloadPercentText,
                _downloadProgressBar
            ));

            _readyPercentText = new TextBlock();
            _readyProgressBar = new ProgressBar();
            root.Children.Add(CreateProgressSection(
                "Готовность",
                _readyPercentText,
                _readyProgressBar
            ));

            root.Children.Add(new TextBlock
            {
                Text = "После 100% откроется установщик. Не закрывайте программу и не выключайте компьютер.",
                Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 20,
                Margin = new Thickness(0, 18, 0, 0)
            });

            Content = root;
            UpdateProgress(AppUpdateService.AppUpdateProgress.Downloading(0, "Готовим скачивание..."));
        }

        public async Task<AppUpdateService.InstallUpdateResult> RunAsync(
            Func<IProgress<AppUpdateService.AppUpdateProgress>, Task<AppUpdateService.InstallUpdateResult>> install)
        {
            _isRunning = true;

            try
            {
                var progress = new Progress<AppUpdateService.AppUpdateProgress>(UpdateProgress);
                var result = await install(progress);
                _statusText.Text = result.Message;
                return result;
            }
            finally
            {
                _isRunning = false;
            }
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (_isRunning)
            {
                e.Cancel = true;
                _statusText.Text = "Обновление уже началось. Дождитесь открытия установщика.";
                return;
            }

            base.OnClosing(e);
        }

        private Border CreateProgressSection(
            string title,
            TextBlock percentText,
            ProgressBar progressBar)
        {
            percentText.Text = "0%";
            percentText.Foreground = Brushes.White;
            percentText.FontSize = 16;
            percentText.FontWeight = FontWeights.SemiBold;

            var header = new DockPanel
            {
                Margin = new Thickness(0, 0, 0, 8)
            };
            header.Children.Add(new TextBlock
            {
                Text = title,
                Foreground = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                FontSize = 17,
                FontWeight = FontWeights.Bold
            });
            DockPanel.SetDock(percentText, Dock.Right);
            header.Children.Add(percentText);

            progressBar.Minimum = 0;
            progressBar.Maximum = 100;
            progressBar.Height = 16;
            progressBar.Value = 0;
            progressBar.Foreground = new SolidColorBrush(Color.FromRgb(96, 165, 250));
            progressBar.Background = new SolidColorBrush(Color.FromRgb(31, 41, 55));

            var panel = new StackPanel();
            panel.Children.Add(header);
            panel.Children.Add(progressBar);

            return new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(20, 24, 34)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(38, 44, 60)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14),
                Margin = new Thickness(0, 0, 0, 14),
                Child = panel
            };
        }

        private void UpdateProgress(AppUpdateService.AppUpdateProgress progress)
        {
            _statusText.Text = progress.Message;

            _downloadProgressBar.IsIndeterminate =
                progress.DownloadPercent == 0 &&
                progress.ReadyPercent == 0 &&
                progress.Message.Contains("Скачиваем", StringComparison.OrdinalIgnoreCase);
            _downloadProgressBar.Value = progress.DownloadPercent;
            _downloadPercentText.Text = $"{progress.DownloadPercent}%";

            _readyProgressBar.IsIndeterminate = false;
            _readyProgressBar.Value = progress.ReadyPercent;
            _readyPercentText.Text = $"{progress.ReadyPercent}%";
        }
    }
}
