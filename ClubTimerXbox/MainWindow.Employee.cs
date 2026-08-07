using System.Windows;
using ClubTimerXbox.Services;

using System;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace ClubTimerXbox
{
    public partial class MainWindow
    {
        private readonly DispatcherTimer _employeeRatingLikeDelayTimer = new()
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        private string _scheduledRatingLikeEmployeeName = "";

        private void InitializeEmployeeRatingLikeAnimation()
        {
            _employeeRatingLikeDelayTimer.Tick += (_, _) =>
            {
                _employeeRatingLikeDelayTimer.Stop();
                PlayEmployeeRatingLikeIfEligible();
            };
        }

        private void ScheduleEmployeeRatingLike()
        {
            _employeeRatingLikeDelayTimer.Stop();
            ResetEmployeeRatingLikeVisual();

            _scheduledRatingLikeEmployeeName =
                EmployeeService.CurrentEmployee?.Name.Trim() ?? "";
            if (!string.IsNullOrWhiteSpace(_scheduledRatingLikeEmployeeName))
                _employeeRatingLikeDelayTimer.Start();
        }

        private void PlayEmployeeRatingLikeIfEligible()
        {
            string currentEmployeeName =
                EmployeeService.CurrentEmployee?.Name.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(currentEmployeeName) ||
                !currentEmployeeName.Equals(
                    _scheduledRatingLikeEmployeeName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            int overallRating = EmployeeRatingService
                .GetSnapshot(currentEmployeeName, ClubClock.Current.LocalNow)
                .OverallPercent;
            if (overallRating <= 100)
                return;

            var hostOpacity = new DoubleAnimationUsingKeyFrames
            {
                Duration = TimeSpan.FromSeconds(2.2)
            };
            hostOpacity.KeyFrames.Add(new DiscreteDoubleKeyFrame(
                0,
                KeyTime.FromTimeSpan(TimeSpan.Zero)));
            hostOpacity.KeyFrames.Add(new EasingDoubleKeyFrame(
                1,
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(160)),
                new CubicEase { EasingMode = EasingMode.EaseOut }));
            hostOpacity.KeyFrames.Add(new DiscreteDoubleKeyFrame(
                1,
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1550))));
            hostOpacity.KeyFrames.Add(new EasingDoubleKeyFrame(
                0,
                KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2.2)),
                new CubicEase { EasingMode = EasingMode.EaseIn }));
            EmployeeRatingLikeHost.BeginAnimation(OpacityProperty, hostOpacity);

            var scale = new DoubleAnimationUsingKeyFrames
            {
                Duration = TimeSpan.FromMilliseconds(900)
            };
            scale.KeyFrames.Add(new EasingDoubleKeyFrame(
                0.55,
                KeyTime.FromTimeSpan(TimeSpan.Zero)));
            scale.KeyFrames.Add(new EasingDoubleKeyFrame(
                1.22,
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(260)),
                new BackEase { Amplitude = 0.35, EasingMode = EasingMode.EaseOut }));
            scale.KeyFrames.Add(new EasingDoubleKeyFrame(
                1,
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(700)),
                new CubicEase { EasingMode = EasingMode.EaseInOut }));
            EmployeeRatingLikeGlyphScale.BeginAnimation(
                System.Windows.Media.ScaleTransform.ScaleXProperty,
                scale);
            EmployeeRatingLikeGlyphScale.BeginAnimation(
                System.Windows.Media.ScaleTransform.ScaleYProperty,
                scale.Clone());

            EmployeeRatingLikeGlyphRotate.BeginAnimation(
                System.Windows.Media.RotateTransform.AngleProperty,
                new DoubleAnimation(-12, 0, TimeSpan.FromMilliseconds(620))
                {
                    EasingFunction = new BackEase
                    {
                        Amplitude = 0.2,
                        EasingMode = EasingMode.EaseOut
                    }
                });
            EmployeeRatingLikeGlyphMove.BeginAnimation(
                System.Windows.Media.TranslateTransform.YProperty,
                new DoubleAnimation(6, -2, TimeSpan.FromMilliseconds(620))
                {
                    EasingFunction = new CubicEase
                    {
                        EasingMode = EasingMode.EaseOut
                    }
                });

            var ringScale = new DoubleAnimation(
                0.5,
                1.75,
                TimeSpan.FromMilliseconds(820))
            {
                EasingFunction = new CubicEase
                {
                    EasingMode = EasingMode.EaseOut
                }
            };
            EmployeeRatingLikeRingScale.BeginAnimation(
                System.Windows.Media.ScaleTransform.ScaleXProperty,
                ringScale);
            EmployeeRatingLikeRingScale.BeginAnimation(
                System.Windows.Media.ScaleTransform.ScaleYProperty,
                ringScale.Clone());
            EmployeeRatingLikeRing.BeginAnimation(
                OpacityProperty,
                new DoubleAnimation(0.9, 0, TimeSpan.FromMilliseconds(820)));
        }

        private void ResetEmployeeRatingLikeVisual()
        {
            EmployeeRatingLikeHost.BeginAnimation(OpacityProperty, null);
            EmployeeRatingLikeHost.Opacity = 0;
            EmployeeRatingLikeGlyphScale.BeginAnimation(
                System.Windows.Media.ScaleTransform.ScaleXProperty,
                null);
            EmployeeRatingLikeGlyphScale.BeginAnimation(
                System.Windows.Media.ScaleTransform.ScaleYProperty,
                null);
            EmployeeRatingLikeGlyphScale.ScaleX = 0.55;
            EmployeeRatingLikeGlyphScale.ScaleY = 0.55;
            EmployeeRatingLikeGlyphRotate.BeginAnimation(
                System.Windows.Media.RotateTransform.AngleProperty,
                null);
            EmployeeRatingLikeGlyphRotate.Angle = -12;
            EmployeeRatingLikeGlyphMove.BeginAnimation(
                System.Windows.Media.TranslateTransform.YProperty,
                null);
            EmployeeRatingLikeGlyphMove.Y = 6;
            EmployeeRatingLikeRing.BeginAnimation(OpacityProperty, null);
            EmployeeRatingLikeRing.Opacity = 0;
            EmployeeRatingLikeRingScale.BeginAnimation(
                System.Windows.Media.ScaleTransform.ScaleXProperty,
                null);
            EmployeeRatingLikeRingScale.BeginAnimation(
                System.Windows.Media.ScaleTransform.ScaleYProperty,
                null);
            EmployeeRatingLikeRingScale.ScaleX = 0.5;
            EmployeeRatingLikeRingScale.ScaleY = 0.5;
        }

        private void UpdateCurrentEmployeeText()
        {
            if (EmployeeService.CurrentEmployee == null)
            {
                CurrentEmployeeButton.Content = "Смена: не выбрана";
                return;
            }

            CurrentEmployeeButton.Content = $"Смена: {EmployeeService.CurrentEmployee.Name}";
        }

        private void CurrentEmployeeButton_Click(object sender, RoutedEventArgs e)
        {
            string currentName = EmployeeService.CurrentEmployee?.Name ?? "не выбрана";

            var actionWindow = new EmployeeActionWindow(currentName)
            {
                Owner = this
            };

            bool? result = actionWindow.ShowDialog();

            if (result != true)
                return;

            if (actionWindow.SelectedAction == EmployeeActionResult.ChangeEmployee)
            {
                ChangeEmployee();
                return;
            }

            if (actionWindow.SelectedAction == EmployeeActionResult.ShowStatistics)
            {
                ConfirmAndShowCurrentEmployeeInfo();
            }
        }

        private void ChangeEmployee()
        {
            string oldEmployeeName = EmployeeService.CurrentEmployee?.Name ?? "Неизвестно";

            var loginWindow = new LoginWindow
            {
                Owner = this
            };

            bool? result = loginWindow.ShowDialog();

            if (result == true)
            {
                string newEmployeeName = EmployeeService.CurrentEmployee?.Name ?? "Неизвестно";

                // Умный журнал:
                // закрывает старую смену и открывает новую.
                ActionLogService.SwitchShift(newEmployeeName);

                UpdateCurrentEmployeeText();
                _ = FirebaseEventService.PublishEmployeeChangedAsync(
                    oldEmployeeName,
                    newEmployeeName
                );

                MessageBox.Show(
                    $"Смена изменена.\n\n" +
                    $"Было: {oldEmployeeName}\n" +
                    $"Стало: {newEmployeeName}\n\n" +
                    $"Активные места не сброшены. Таймеры продолжают работать.",
                    "Смена сотрудника"
                );

                ScheduleEmployeeRatingLike();
            }
        }

        private void ConfirmAndShowCurrentEmployeeInfo()
        {
            if (EmployeeService.CurrentEmployee == null)
            {
                MessageBox.Show("Смена не выбрана.", "Сотрудник");
                return;
            }

            if (IsStockAcceptanceBlockingEmployeeStats())
            {
                MessageBox.Show(
                    "Сначала завершите приёмку.\n\n" +
                    "После приёмки можно открыть статистику сотрудника и взять аванс.",
                    "Приёмка смены",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );

                return;
            }

            string employeeName = EmployeeService.CurrentEmployee.Name;

            var confirmWindow = new EmployeePinConfirmWindow(employeeName)
            {
                Owner = this
            };

            bool? result = confirmWindow.ShowDialog();

            if (result == true)
            {
                ShowCurrentEmployeeInfo();
            }
        }

        private void ShowCurrentEmployeeInfo()
        {
            if (EmployeeService.CurrentEmployee == null)
            {
                MessageBox.Show("Смена не выбрана.", "Сотрудник");
                return;
            }

            if (IsStockAcceptanceBlockingEmployeeStats())
            {
                MessageBox.Show(
                    "Сначала завершите приёмку.",
                    "Приёмка смены",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );

                return;
            }

            var statsWindow = new EmployeeStatsWindow(EmployeeService.CurrentEmployee.Name)
            {
                Owner = this
            };

            statsWindow.ShowDialog();
        }

        private bool IsStockAcceptanceBlockingEmployeeStats()
        {
            ActionLogService.EnsureAcceptanceForCurrentShift();
            return ShiftAcceptanceService.IsAcceptanceRequired();
        }
    }
}
