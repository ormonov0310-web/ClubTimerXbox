using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using ClubTimerXbox.Models;
using ClubTimerXbox.Services;

namespace ClubTimerXbox
{
    public class EmployeeStatsWindow : Window
    {
        private enum StatsSection
        {
            TakenHistory,
            Salary,
            Bonuses,
            Rating,
            Time,
            Income,
            ProductsServices,
            Losses
        }

        private readonly string _employeeName;
        private readonly TextBlock _monthText = new TextBlock();
        private readonly TextBlock _grossValueText = new TextBlock();
        private readonly TextBlock _salaryValueText = new TextBlock();
        private readonly TextBlock _timeValueText = new TextBlock();
        private readonly TextBlock _ratingValueText = new TextBlock();
        private readonly TextBlock _workValueText = new TextBlock();
        private readonly TextBlock _gameIncomeValueText = new TextBlock();
        private readonly TextBlock _productsIncomeValueText = new TextBlock();
        private readonly TextBlock _lossValueText = new TextBlock();
        private readonly TextBlock _sectionTitleText = new TextBlock();
        private readonly TextBlock _sectionInfoText = new TextBlock();
        private readonly StackPanel _contentPanel = new StackPanel();
        private readonly Canvas _ratingBoostCanvas = new Canvas
        {
            IsHitTestVisible = false,
            ClipToBounds = true
        };
        private readonly Dictionary<StatsSection, List<Border>> _summaryCards = new();
        private readonly DispatcherTimer _ratingBoostDelayTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        private readonly DispatcherTimer _ratingBorderAnimationTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(80)
        };
        private Border _ratingCard = null!;
        private int _currentOverallRating = 100;
        private bool _ratingBoostPlayed;

        private DateTime _monthStart = BusinessCalendarService
            .GetBusinessMonth(ClubClock.Current.LocalNow)
            .StartInclusive;
        private StatsSection _section = StatsSection.Salary;

        public EmployeeStatsWindow(string employeeName)
        {
            _employeeName = employeeName;

            Title = $"Статистика сотрудника: {_employeeName}";
            Width = 980;
            Height = 780;
            MinWidth = 880;
            MinHeight = 660;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromRgb(16, 20, 28));

            Content = CreateContent();
            Render();
            _ratingBoostDelayTimer.Tick += (_, _) =>
            {
                _ratingBoostDelayTimer.Stop();
                PlayRatingBoostIfNeeded();
            };
            _ratingBorderAnimationTimer.Tick += (_, _) =>
            {
                if (_currentOverallRating > 100 && _ratingCard != null)
                    _ratingCard.BorderBrush = CreateRotatingRatingBrush();
            };
            Loaded += (_, _) => ScheduleRatingBoost();
            Closed += (_, _) =>
            {
                _ratingBoostDelayTimer.Stop();
                _ratingBorderAnimationTimer.Stop();
            };
        }

        private UIElement CreateContent()
        {
            var root = new Grid
            {
                Margin = new Thickness(20)
            };

            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var topPanel = new DockPanel
            {
                Margin = new Thickness(0, 0, 0, 14)
            };

            var titleText = new TextBlock
            {
                Text = $"Статистика: {_employeeName}",
                Foreground = Brushes.White,
                FontSize = 30,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center
            };

            DockPanel.SetDock(titleText, Dock.Left);
            topPanel.Children.Add(titleText);

            var closeButton = new Button
            {
                Content = "Закрыть",
                Width = 120,
                Height = 40,
                FontSize = 16,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            closeButton.Click += (_, _) => Close();

            DockPanel.SetDock(closeButton, Dock.Right);
            topPanel.Children.Add(closeButton);

            Grid.SetRow(topPanel, 0);
            root.Children.Add(topPanel);

            var monthPanel = CreateMonthPanel();
            Grid.SetRow(monthPanel, 1);
            root.Children.Add(monthPanel);

            var summaryPanel = CreateSummaryPanel();
            Grid.SetRow(summaryPanel, 2);
            root.Children.Add(summaryPanel);

            _sectionTitleText.Foreground = Brushes.White;
            _sectionTitleText.FontSize = 18;
            _sectionTitleText.FontWeight = FontWeights.Bold;
            _sectionTitleText.Margin = new Thickness(0, 0, 0, 6);

            Grid.SetRow(_sectionTitleText, 3);
            root.Children.Add(_sectionTitleText);

            _sectionInfoText.Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184));
            _sectionInfoText.FontSize = 14;
            _sectionInfoText.TextWrapping = TextWrapping.Wrap;
            _sectionInfoText.LineHeight = 21;
            _sectionInfoText.Margin = new Thickness(0, 0, 0, 10);

            Grid.SetRow(_sectionInfoText, 4);
            root.Children.Add(_sectionInfoText);

            var listBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(51, 65, 85)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(12),
                Child = new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Content = _contentPanel
                }
            };

            Grid.SetRow(listBorder, 5);
            root.Children.Add(listBorder);

            Grid.SetRowSpan(_ratingBoostCanvas, 6);
            Panel.SetZIndex(_ratingBoostCanvas, 100);
            root.Children.Add(_ratingBoostCanvas);

            return root;
        }

        private UIElement CreateMonthPanel()
        {
            var grid = new Grid
            {
                Margin = new Thickness(0, 0, 0, 14)
            };

            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var prevButton = CreateArrowButton("<");
            prevButton.Click += (_, _) =>
            {
                _monthStart = _monthStart.AddMonths(-1);
                Render();
            };

            var nextButton = CreateArrowButton(">");
            nextButton.Click += (_, _) =>
            {
                _monthStart = _monthStart.AddMonths(1);
                Render();
            };

            _monthText.Foreground = Brushes.White;
            _monthText.FontSize = 26;
            _monthText.FontWeight = FontWeights.Bold;
            _monthText.HorizontalAlignment = HorizontalAlignment.Center;
            _monthText.VerticalAlignment = VerticalAlignment.Center;

            Grid.SetColumn(prevButton, 0);
            Grid.SetColumn(_monthText, 1);
            Grid.SetColumn(nextButton, 2);

            grid.Children.Add(prevButton);
            grid.Children.Add(_monthText);
            grid.Children.Add(nextButton);

            return grid;
        }

        private Button CreateArrowButton(string text)
        {
            return new Button
            {
                Content = text,
                Width = 58,
                Height = 42,
                FontSize = 24,
                FontWeight = FontWeights.Bold
            };
        }

        private UIElement CreateSummaryPanel()
        {
            var grid = new UniformGrid
            {
                Columns = 4,
                Margin = new Thickness(0, 0, 0, 14)
            };

            grid.Children.Add(CreateSummaryCard(
                "Общая",
                _grossValueText,
                Color.FromRgb(248, 250, 252),
                StatsSection.Salary));
            grid.Children.Add(CreateSummaryCard(
                "Взял",
                _salaryValueText,
                Color.FromRgb(96, 165, 250),
                StatsSection.TakenHistory,
                () =>
                {
                    var window = new EmployeeSalaryTakenWindow(
                        _employeeName,
                        _monthStart,
                        Render)
                    {
                        Owner = this
                    };

                    window.ShowDialog();
                    Render();
                }));
            grid.Children.Add(CreateSummaryCard(
                "Премии/бонусы",
                _timeValueText,
                Color.FromRgb(250, 204, 21),
                StatsSection.Bonuses));
            grid.Children.Add(CreateSummaryCard(
                "Штрафы",
                _lossValueText,
                Color.FromRgb(248, 113, 113),
                StatsSection.Losses));
            _ratingCard = CreateSummaryCard(
                "Рейтинг",
                _ratingValueText,
                Color.FromRgb(248, 250, 252),
                StatsSection.Rating);
            grid.Children.Add(_ratingCard);
            grid.Children.Add(CreateSummaryCard(
                "Время",
                _workValueText,
                Color.FromRgb(167, 139, 250),
                StatsSection.Time));
            grid.Children.Add(CreateSummaryCard(
                "Выручка",
                _gameIncomeValueText,
                Color.FromRgb(74, 222, 128),
                StatsSection.Income));
            grid.Children.Add(CreateSummaryCard(
                "Товары/услуги",
                _productsIncomeValueText,
                Color.FromRgb(45, 212, 191),
                StatsSection.ProductsServices));

            return grid;
        }

        private Border CreateSummaryCard(
            string title,
            TextBlock valueText,
            Color valueColor,
            StatsSection section,
            Action? onClick = null)
        {
            var panel = new StackPanel();
            panel.Children.Add(CreateSummaryTitle(title));

            ConfigureSummaryValue(valueText, valueColor, 22);

            panel.Children.Add(valueText);

            return CreateSummaryCardContainer(panel, section, onClick);
        }

        private static TextBlock CreateSummaryTitle(string title)
        {
            return new TextBlock
            {
                Text = title,
                Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                FontSize = 14,
                FontWeight = FontWeights.SemiBold
            };
        }

        private static void ConfigureSummaryValue(
            TextBlock valueText,
            Color valueColor,
            double fontSize)
        {
            valueText.Foreground = new SolidColorBrush(valueColor);
            valueText.FontSize = fontSize;
            valueText.FontWeight = FontWeights.Bold;
            valueText.TextWrapping = TextWrapping.Wrap;
            valueText.Margin = new Thickness(0, 6, 0, 0);
        }

        private Border CreateSummaryCardContainer(
            UIElement content,
            StatsSection section,
            Action? onClick = null)
        {

            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(24, 32, 43)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(51, 65, 85)),
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16),
                Margin = new Thickness(0, 0, 10, 10),
                MinHeight = 92,
                Cursor = Cursors.Hand,
                Child = content
            };

            if (!_summaryCards.TryGetValue(section, out var cards))
            {
                cards = new List<Border>();
                _summaryCards[section] = cards;
            }

            cards.Add(border);
            border.MouseLeftButtonUp += (_, _) =>
            {
                _section = section;
                Render();
                onClick?.Invoke();
            };

            return border;
        }

        private void Render()
        {
            var summary = EmployeeStatsService.GetSummary(_employeeName, _monthStart);
            var autoSalary = AutoSalaryService
                .BuildReport(_monthStart)
                .Employees
                .FirstOrDefault(employee => employee.EmployeeName == _employeeName);

            _monthText.Text = GetMonthTitle(_monthStart);
            _grossValueText.Text = $"{autoSalary?.GrossAmount ?? 0} сом";
            _salaryValueText.Text = $"{autoSalary?.PaidAmount ?? 0} сом";
            _timeValueText.Text = $"{(autoSalary?.ProductBonusAmount ?? 0) + (autoSalary?.BonusAmount ?? 0)} сом";
            int overallRating = autoSalary?.OverallRatingPercent ?? 100;
            _currentOverallRating = overallRating;
            _ratingValueText.Text = overallRating > 100
                ? $"{overallRating}% ↑"
                : $"{overallRating}%";
            _ratingValueText.Foreground = new SolidColorBrush(GetRatingColor(overallRating));
            UpdateRatingBorderAnimationState();
            _workValueText.Text = EmployeeStatsService.FormatTime(GetDisplayedWorkTime(summary, autoSalary));
            _gameIncomeValueText.Text = $"{summary.MonthGameIncome} сом";
            _productsIncomeValueText.Text = $"{summary.MonthProductsIncome} сом";
            _lossValueText.Text = $"{autoSalary?.LossesAmount ?? summary.MonthUnpaidLosses} сом";
            _sectionTitleText.Text = GetSectionTitle();
            _sectionInfoText.Text = GetSectionInfoText(summary, autoSalary);

            UpdateSummaryCardStyles();

            _contentPanel.Children.Clear();

            if (_section == StatsSection.Salary)
            {
                RenderSalarySection(summary, autoSalary);
                return;
            }

            if (_section == StatsSection.TakenHistory)
            {
                RenderSalaryAdvanceHistory();
                return;
            }

            if (_section == StatsSection.Bonuses)
            {
                RenderBonusesSection(autoSalary);
                return;
            }

            if (_section == StatsSection.Rating)
            {
                RenderRatingSection(autoSalary);
                return;
            }

            if (_section == StatsSection.Time)
            {
                RenderTimeSection();
                return;
            }

            if (_section == StatsSection.Income)
            {
                RenderIncomeSection();
                return;
            }

            if (_section == StatsSection.ProductsServices)
            {
                RenderProductsSection();
                return;
            }

            RenderLossesSection();
        }

        private string GetSectionTitle()
        {
            return _section switch
            {
                StatsSection.TakenHistory => "История выдач",
                StatsSection.Salary => "Зарплата",
                StatsSection.Bonuses => "Премии и бонусы",
                StatsSection.Rating => "Рейтинг",
                StatsSection.Time => "Время",
                StatsSection.Income => "Выручка",
                StatsSection.ProductsServices => "Товары и услуги",
                _ => "Штрафы"
            };
        }

        private string GetSectionInfoText(EmployeeStatsSummary summary, AutoSalaryEmployeeResult? autoSalary)
        {
            if (_section == StatsSection.TakenHistory)
                return "История авансов и выдач зарплаты за выбранный месяц.";

            if (_section == StatsSection.Salary)
                return $"Осталось выдать: {autoSalary?.RemainingAmount ?? 0} сом.";

            if (_section == StatsSection.Bonuses)
                return $"Бонусы за выбранный месяц: {(autoSalary?.BonusAmount ?? 0) + (autoSalary?.ProductBonusAmount ?? 0)} сом.";

            if (_section == StatsSection.Rating)
                return "Рейтинг времени и игр влияет только на будущие начисления сотрудника.";

            if (_section == StatsSection.Time)
                return $"Общее время смен: {EmployeeStatsService.FormatTime(GetDisplayedWorkTime(summary, autoSalary))}.";

            if (_section == StatsSection.Income)
                return $"Игровая выручка: {summary.MonthGameIncome} сом. Общая выручка: {summary.MonthTotalIncome} сом.";

            if (_section == StatsSection.ProductsServices)
                return $"Товары/услуги: {summary.MonthProductsIncome} сом. Операций: {summary.ProductServiceOperationsCount}.";

            return $"К удержанию: {summary.MonthUnpaidLosses} сом. Оплачено: {summary.MonthPaidLosses} сом.";
        }

        private void UpdateSummaryCardStyles()
        {
            foreach (var pair in _summaryCards)
            {
                bool isActive = pair.Key == _section;
                foreach (var card in pair.Value)
                {
                    bool isRatingBoost = pair.Key == StatsSection.Rating &&
                                         _currentOverallRating > 100;
                    card.Background = new SolidColorBrush(isActive
                        ? Color.FromRgb(30, 58, 88)
                        : Color.FromRgb(24, 32, 43));
                    card.BorderBrush = new SolidColorBrush(isRatingBoost
                        ? Color.FromRgb(74, 222, 128)
                        : isActive
                            ? Color.FromRgb(96, 165, 250)
                        : Color.FromRgb(51, 65, 85));
                    card.BorderThickness = new Thickness(2);
                }
            }
        }

        private void UpdateRatingBorderAnimationState()
        {
            if (_currentOverallRating > 100)
            {
                if (!_ratingBorderAnimationTimer.IsEnabled)
                    _ratingBorderAnimationTimer.Start();
                return;
            }

            _ratingBorderAnimationTimer.Stop();
        }

        private static Brush CreateRotatingRatingBrush()
        {
            double angle = Environment.TickCount64 % 3200 / 3200.0 * 360.0;
            var brush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0.5),
                EndPoint = new Point(1, 0.5),
                RelativeTransform = new RotateTransform(angle, 0.5, 0.5)
            };
            brush.GradientStops.Add(new GradientStop(Color.FromRgb(20, 83, 45), 0));
            brush.GradientStops.Add(new GradientStop(Color.FromRgb(134, 239, 172), 0.48));
            brush.GradientStops.Add(new GradientStop(Color.FromRgb(20, 83, 45), 1));
            return brush;
        }

        private void ScheduleRatingBoost()
        {
            if (_ratingBoostPlayed || _currentOverallRating <= 100)
                return;

            _ratingBoostDelayTimer.Stop();
            _ratingBoostDelayTimer.Start();
        }

        private void PlayRatingBoostIfNeeded()
        {
            if (_ratingBoostPlayed ||
                _currentOverallRating <= 100 ||
                _ratingCard.ActualWidth <= 0 ||
                _ratingCard.ActualHeight <= 0)
            {
                return;
            }

            _ratingBoostPlayed = true;
            Point origin = _ratingCard.TranslatePoint(
                new Point(_ratingCard.ActualWidth / 2, _ratingCard.ActualHeight / 2),
                _ratingBoostCanvas);

            PlayRatingCardPulse();
            AddRatingBoostRing(origin);
            AddRatingBoostLabel(origin);
            AddRatingBoostParticles(origin);
        }

        private void PlayRatingCardPulse()
        {
            var scale = new ScaleTransform(1, 1);
            _ratingCard.RenderTransformOrigin = new Point(0.5, 0.5);
            _ratingCard.RenderTransform = scale;

            var pulse = new DoubleAnimationUsingKeyFrames
            {
                Duration = TimeSpan.FromSeconds(1.5)
            };
            pulse.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            pulse.KeyFrames.Add(new EasingDoubleKeyFrame(
                1.055,
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(280)),
                new CubicEase { EasingMode = EasingMode.EaseOut }));
            pulse.KeyFrames.Add(new EasingDoubleKeyFrame(
                1,
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1180)),
                new CubicEase { EasingMode = EasingMode.EaseInOut }));

            scale.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, pulse.Clone());
        }

        private void AddRatingBoostRing(Point origin)
        {
            var ring = new Ellipse
            {
                Width = 32,
                Height = 32,
                Stroke = new SolidColorBrush(Color.FromRgb(74, 222, 128)),
                StrokeThickness = 3,
                Opacity = 0.9,
                RenderTransformOrigin = new Point(0.5, 0.5)
            };
            var scale = new ScaleTransform(1, 1);
            ring.RenderTransform = scale;
            Canvas.SetLeft(ring, origin.X - 16);
            Canvas.SetTop(ring, origin.Y - 16);
            _ratingBoostCanvas.Children.Add(ring);

            var duration = TimeSpan.FromMilliseconds(1320);
            scale.BeginAnimation(
                ScaleTransform.ScaleXProperty,
                new DoubleAnimation(1, 7, duration)
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
            scale.BeginAnimation(
                ScaleTransform.ScaleYProperty,
                new DoubleAnimation(1, 7, duration)
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
            var fade = new DoubleAnimation(0.9, 0, duration);
            fade.Completed += (_, _) => _ratingBoostCanvas.Children.Remove(ring);
            ring.BeginAnimation(OpacityProperty, fade);
        }

        private void AddRatingBoostLabel(Point origin)
        {
            var label = new Border
            {
                Width = 128,
                Height = 34,
                Background = new SolidColorBrush(Color.FromArgb(235, 22, 101, 52)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(134, 239, 172)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Child = new TextBlock
                {
                    Text = $"Отлично! +{_currentOverallRating - 100}%",
                    Foreground = Brushes.White,
                    FontSize = 14,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            var move = new TranslateTransform();
            label.RenderTransform = move;
            Canvas.SetLeft(label, origin.X - 64);
            Canvas.SetTop(label, origin.Y - 24);
            _ratingBoostCanvas.Children.Add(label);

            var duration = TimeSpan.FromSeconds(1.5);
            move.BeginAnimation(
                TranslateTransform.YProperty,
                new DoubleAnimation(0, -46, duration)
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
            var fade = new DoubleAnimationUsingKeyFrames { Duration = duration };
            fade.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            fade.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(90))));
            fade.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1050))));
            fade.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(duration)));
            fade.Completed += (_, _) => _ratingBoostCanvas.Children.Remove(label);
            label.BeginAnimation(OpacityProperty, fade);
        }

        private void AddRatingBoostParticles(Point origin)
        {
            Color[] colors =
            {
                Color.FromRgb(74, 222, 128),
                Color.FromRgb(250, 204, 21),
                Color.FromRgb(96, 165, 250),
                Color.FromRgb(248, 250, 252)
            };
            var random = new Random(_employeeName.GetHashCode() ^ _currentOverallRating);

            for (int index = 0; index < 24; index++)
            {
                double angle = (Math.PI * 2 * index / 24) +
                               ((random.NextDouble() - 0.5) * 0.28);
                double distance = 75 + (random.NextDouble() * 125);
                var particle = new Border
                {
                    Width = 4 + random.Next(0, 4),
                    Height = 7 + random.Next(0, 6),
                    Background = new SolidColorBrush(colors[index % colors.Length]),
                    CornerRadius = new CornerRadius(2),
                    RenderTransformOrigin = new Point(0.5, 0.5)
                };
                var transforms = new TransformGroup();
                var rotate = new RotateTransform(random.Next(0, 180));
                var move = new TranslateTransform();
                transforms.Children.Add(rotate);
                transforms.Children.Add(move);
                particle.RenderTransform = transforms;
                Canvas.SetLeft(particle, origin.X - (particle.Width / 2));
                Canvas.SetTop(particle, origin.Y - (particle.Height / 2));
                _ratingBoostCanvas.Children.Add(particle);

                var duration = TimeSpan.FromMilliseconds(1260 + random.Next(0, 240));
                var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
                move.BeginAnimation(
                    TranslateTransform.XProperty,
                    new DoubleAnimation(0, Math.Cos(angle) * distance, duration)
                    {
                        EasingFunction = easing
                    });
                move.BeginAnimation(
                    TranslateTransform.YProperty,
                    new DoubleAnimation(
                        0,
                        (Math.Sin(angle) * distance) + 18,
                        duration)
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    });
                rotate.BeginAnimation(
                    RotateTransform.AngleProperty,
                    new DoubleAnimation(
                        rotate.Angle,
                        rotate.Angle + random.Next(160, 480),
                        duration));

                var fade = new DoubleAnimationUsingKeyFrames { Duration = duration };
                fade.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                fade.KeyFrames.Add(new EasingDoubleKeyFrame(
                    1,
                    KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(duration.TotalMilliseconds * 0.55))));
                fade.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(duration)));
                fade.Completed += (_, _) => _ratingBoostCanvas.Children.Remove(particle);
                particle.BeginAnimation(OpacityProperty, fade);
            }
        }

        private void RenderSalarySection(
            EmployeeStatsSummary summary,
            AutoSalaryEmployeeResult? autoSalary)
        {
            int timeAmount = autoSalary?.TimeAmount ?? 0;
            int gameAmount = autoSalary?.GameRevenueAmount ?? 0;
            int productBonus = autoSalary?.ProductBonusAmount ?? 0;
            int automaticBonuses = autoSalary?.BonusAmount ?? 0;
            int losses = autoSalary?.LossesAmount ?? summary.MonthUnpaidLosses;
            int paid = autoSalary?.PaidAmount ?? 0;
            int gross = autoSalary?.GrossAmount
                ?? timeAmount + gameAmount + productBonus + automaticBonuses;
            int remaining = autoSalary?.RemainingAmount
                ?? gross - paid - losses;

            _contentPanel.Children.Add(CreateCard(new StackPanel
            {
                Children =
                {
                    CreateBigLine("Зарплата за выбранный месяц"),
                    CreateLine($"Общее время: {EmployeeStatsService.FormatTime(GetDisplayedWorkTime(summary, autoSalary))}"),
                    CreateLine($"Заработал по времени: {timeAmount} сом"),
                    CreateLine(""),
                    CreateLine($"Общая игровая выручка: {summary.MonthGameIncome} сом"),
                    CreateLine($"Заработал по выручке: {gameAmount} сом"),
                    CreateLine(""),
                    CreateLine($"Товары/услуги: {summary.MonthProductsIncome} сом"),
                    CreateLine($"Бонус за товары/услуги: {productBonus} сом"),
                    CreateLine(""),
                    CreateLine($"Бонусы: {automaticBonuses} сом"),
                    CreateLine($"Всего начислено: {gross} сом"),
                    CreateLine($"Штрафы: -{losses} сом"),
                    CreateLine($"Взял: -{paid} сом"),
                    CreateBigLine($"Итог осталось: {remaining} сом")
                }
            }));

        }

        private static TimeSpan GetDisplayedWorkTime(
            EmployeeStatsSummary summary,
            AutoSalaryEmployeeResult? autoSalary)
        {
            return autoSalary == null
                ? summary.MonthWorkTime
                : TimeSpan.FromHours(autoSalary.WorkHours);
        }

        private void RenderBonusesSection(AutoSalaryEmployeeResult? autoSalary)
        {
            int productBonus = autoSalary?.ProductBonusAmount ?? 0;
            int automaticBonuses = autoSalary?.BonusAmount ?? 0;

            _contentPanel.Children.Add(CreateCard(new StackPanel
            {
                Children =
                {
                    CreateBigLine("Бонусы"),
                    CreateLine($"Бонус за товары/услуги: {productBonus} сом"),
                    CreateLine($"Бонусы по графику и плану: {automaticBonuses} сом"),
                    CreateBigLine($"Итого бонусы: {productBonus + automaticBonuses} сом")
                }
            }));

            var bonuses = autoSalary?.Bonuses ?? new List<AutoSalaryBonusItem>();
            if (bonuses.Count == 0)
            {
                _contentPanel.Children.Add(CreateMutedText("За выбранный месяц отдельных бонусов пока нет."));
                return;
            }

            foreach (var bonus in bonuses)
            {
                _contentPanel.Children.Add(CreateCard(new StackPanel
                {
                    Children =
                    {
                        CreateBigLine($"{bonus.Title}: +{bonus.Amount} сом"),
                        CreateLine(bonus.CreatedAt.ToString("dd.MM.yyyy HH:mm")),
                        CreateDescription(bonus.Description)
                    }
                }));
            }
        }

        private void RenderRatingSection(AutoSalaryEmployeeResult? autoSalary)
        {
            DateTime now = ClubClock.Current.LocalNow;
            DateTime nextMonthStart = _monthStart.AddMonths(1);
            DateTime ratingAt = nextMonthStart <= now
                ? nextMonthStart.AddTicks(-1)
                : now;
            var snapshot = EmployeeRatingService.GetSnapshot(_employeeName, ratingAt);

            int overall = autoSalary?.OverallRatingPercent ?? snapshot.OverallPercent;
            int time = autoSalary?.TimeRatingPercent ?? snapshot.TimePercent;
            int revenue = autoSalary?.RevenueRatingPercent ?? snapshot.RevenuePercent;

            _contentPanel.Children.Add(CreateCard(new StackPanel
            {
                Children =
                {
                    CreateRatingLine("Рейтинг", overall, true),
                    CreateRatingLine("За время", time),
                    CreateRatingLine("За игры", revenue)
                }
            }));

            _contentPanel.Children.Add(CreateBigLine("События рейтинга"));

            var events = (autoSalary?.RatingEvents ?? snapshot.History
                    .Where(item =>
                        item.EffectiveFrom < nextMonthStart &&
                        item.EffectiveUntil > _monthStart)
                    .ToList())
                .OrderByDescending(item => IsRatingEventActive(item, now))
                .ThenByDescending(item => item.EffectiveFrom)
                .ToList();

            if (events.Count == 0)
            {
                _contentPanel.Children.Add(CreateMutedText(
                    "За выбранный месяц изменений рейтинга нет."));
                return;
            }

            foreach (var item in events)
            {
                bool isActive = IsRatingEventActive(item, now);
                string branch = item.Branch == EmployeeRatingBranch.Time
                    ? "время"
                    : "игры";
                int magnitude = item.ChangePercent > 0
                    ? item.ChangePercent
                    : Math.Abs(item.TargetPercent - item.BasePercentAtCreation);
                int difference = item.Direction == EmployeeRatingEffectDirection.Reward
                    ? magnitude
                    : -magnitude;
                string differenceText = difference > 0
                    ? $"+{difference}%"
                    : $"{difference}%";
                string title = difference == 0
                    ? $"100% за {branch}"
                    : $"{differenceText} за {branch}";

                var panel = new StackPanel
                {
                    Children =
                    {
                        CreateBigLine(title),
                        CreateLine($"Рейтинг установлен на {item.TargetPercent}%"),
                        CreateLine($"Причина: {GetRatingReason(item)}"),
                        CreateLine($"Дата события: {item.EffectiveFrom:dd.MM.yyyy HH:mm}"),
                        CreateLine($"Срок изменения: {FormatRatingDuration(item)}")
                    }
                };

                if (!string.IsNullOrWhiteSpace(item.Description) &&
                    !item.Description.Equals(item.Title, StringComparison.OrdinalIgnoreCase))
                {
                    panel.Children.Add(CreateDescription(item.Description));
                }

                panel.Children.Add(isActive
                    ? CreateRatingStatusLine(
                        $"Осталось: {FormatRatingRemaining(item.EffectiveUntil - now)}",
                        Color.FromRgb(250, 204, 21))
                    : CreateRatingStatusLine(
                        GetRatingEventStatus(item),
                        Color.FromRgb(148, 163, 184)));

                _contentPanel.Children.Add(CreateCard(panel));
            }
        }

        private static bool IsRatingEventActive(
            EmployeeRatingEvent item,
            DateTime now)
        {
            return item.Status == EmployeeRatingEventStatus.Active &&
                   item.EffectiveFrom <= now &&
                   now < item.EffectiveUntil;
        }

        private static string FormatRatingDuration(EmployeeRatingEvent item)
        {
            TimeSpan duration = item.ScheduledUntil - item.EffectiveFrom;
            if (duration.TotalHours < 24)
                return $"{Math.Max(1, (int)Math.Round(duration.TotalHours))} ч";
            return FormatDayCount(Math.Max(1, (int)Math.Round(duration.TotalDays)));
        }

        private static string GetRatingReason(EmployeeRatingEvent item)
        {
            return string.IsNullOrWhiteSpace(item.Title)
                ? "Причина не указана"
                : item.Title.Trim();
        }

        private static string GetRatingEventStatus(EmployeeRatingEvent item)
        {
            return item.Status switch
            {
                EmployeeRatingEventStatus.Forgiven =>
                    $"Снято владельцем: {item.EffectiveUntil:dd.MM.yyyy HH:mm}",
                EmployeeRatingEventStatus.CancelledAsError =>
                    $"Удалено как ошибочное: {item.EffectiveUntil:dd.MM.yyyy HH:mm}",
                _ => $"Срок завершён: {item.EffectiveUntil:dd.MM.yyyy HH:mm}"
            };
        }

        private static string FormatRatingRemaining(TimeSpan remaining)
        {
            if (remaining <= TimeSpan.Zero)
                return "срок завершён";

            int days = (int)remaining.TotalDays;
            int hours = remaining.Hours;
            int minutes = remaining.Minutes;
            var parts = new List<string>();

            if (days > 0)
                parts.Add(FormatDayCount(days));
            if (hours > 0 || days > 0)
                parts.Add($"{hours} ч");
            parts.Add($"{minutes} мин");

            return string.Join(" ", parts);
        }

        private static string FormatDayCount(int days)
        {
            int lastTwo = days % 100;
            int last = days % 10;
            string suffix = lastTwo is >= 11 and <= 14
                ? "дней"
                : last switch
                {
                    1 => "день",
                    2 or 3 or 4 => "дня",
                    _ => "дней"
                };
            return $"{days} {suffix}";
        }

        private TextBlock CreateRatingLine(
            string title,
            int percent,
            bool isMain = false)
        {
            return new TextBlock
            {
                Text = $"{title}: {percent}%",
                Foreground = new SolidColorBrush(GetRatingColor(percent)),
                FontSize = isMain ? 22 : 17,
                FontWeight = isMain ? FontWeights.Bold : FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, isMain ? 9 : 6)
            };
        }

        private TextBlock CreateRatingStatusLine(string text, Color color)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = new SolidColorBrush(color),
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 10, 0, 0)
            };
        }

        private static Color GetRatingColor(int percent)
        {
            if (percent >= 101)
                return Color.FromRgb(74, 222, 128);
            if (percent >= 92)
                return Color.FromRgb(229, 231, 235);
            if (percent >= 82)
                return Color.FromRgb(250, 204, 21);
            return Color.FromRgb(248, 113, 113);
        }

        private void RenderSalaryAdvanceHistory()
        {
            DateTime nextMonthStart = _monthStart.AddMonths(1);
            var records = CashService
                .GetSalaryRecordsByPeriod(_monthStart, nextMonthStart)
                .Where(record => record.RelatedEmployeeName.Equals(
                    _employeeName,
                    StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(record => record.CreatedAt)
                .ToList();

            _contentPanel.Children.Add(CreateBigLine("История авансов"));

            if (records.Count == 0)
            {
                _contentPanel.Children.Add(CreateMutedText("За выбранный месяц авансов и выдач пока нет."));
                return;
            }

            foreach (var record in records)
            {
                string source = record.EmployeeName == _employeeName
                    ? "Взял сам из кассы"
                    : $"Выдал: {record.EmployeeName}";

                var panel = new StackPanel
                {
                    Children =
                    {
                        CreateBigLine($"{record.CreatedAt:dd.MM.yyyy HH:mm} - {record.Amount} сом"),
                        CreateLine($"Способ: {record.PaymentMethod}"),
                        CreateLine(source)
                    }
                };

                if (!string.IsNullOrWhiteSpace(record.Description))
                    panel.Children.Add(CreateDescription(record.Description));

                _contentPanel.Children.Add(CreateCard(panel));
            }
        }

        private void RenderTimeSection()
        {
            var monthEnd = _monthStart.AddMonths(1);
            var shifts = EmployeeStatsService.GetShifts(_employeeName, _monthStart, monthEnd);

            if (shifts.Count == 0)
            {
                _contentPanel.Children.Add(CreateMutedText("Смен за выбранный месяц нет."));
                return;
            }

            foreach (var shift in shifts)
            {
                string endText = shift.ClosedAt == null
                    ? "смена открыта"
                    : shift.ClosedAt.Value.ToString("dd.MM.yyyy HH:mm");

                _contentPanel.Children.Add(CreateCard(new StackPanel
                {
                    Children =
                    {
                        CreateBigLine($"{shift.StartedAt:dd.MM.yyyy HH:mm}"),
                        CreateLine($"Конец: {endText}"),
                        CreateLine($"Длительность: {EmployeeStatsService.FormatTime(shift.Duration)}"),
                        CreateLine(shift.IsClosed ? "Статус: закрыта" : "Статус: активна")
                    }
                }));
            }
        }

        private void RenderIncomeSection()
        {
            var sessions = EmployeeStatsService.GetGameSessionsForMonth(_employeeName, _monthStart);

            if (sessions.Count == 0)
            {
                _contentPanel.Children.Add(CreateMutedText("Игровой выручки за выбранный месяц нет."));
                return;
            }

            foreach (var session in sessions)
            {
                _contentPanel.Children.Add(CreateCard(new StackPanel
                {
                    Children =
                    {
                        CreateBigLine($"{session.PlaceName} - {session.ClosedAt:dd.MM.yyyy HH:mm}"),
                        CreateLine($"Тариф: {session.TariffText}"),
                        CreateLine($"Игра: {session.GameAmount} сом"),
                        CreateLine($"Товары/услуги в сеансе: {session.ProductsAmount} сом"),
                        CreateLine($"Итого: {session.TotalAmount} сом"),
                        CreateLine($"Закрыл: {session.ClosedByEmployeeName}")
                    }
                }));
            }
        }

        private void RenderProductsSection()
        {
            var items = EmployeeStatsService.GetProductServicesForMonth(_employeeName, _monthStart);

            if (items.Count == 0)
            {
                _contentPanel.Children.Add(CreateMutedText("Товаров/услуг за выбранный месяц нет."));
                return;
            }

            foreach (var item in items)
            {
                string placeText = string.IsNullOrWhiteSpace(item.PlaceName)
                    ? "Без места"
                    : item.PlaceName;

                _contentPanel.Children.Add(CreateCard(new StackPanel
                {
                    Children =
                    {
                        CreateBigLine($"{item.CreatedAt:dd.MM.yyyy HH:mm} - {item.Amount} сом"),
                        CreateLine(item.Title),
                        CreateLine($"Место: {placeText}"),
                        CreateDescription(item.Description)
                    }
                }));
            }
        }

        private void RenderLossesSection()
        {
            var losses = EmployeeStatsService.GetShortagesForMonth(_employeeName, _monthStart);
            if (RenderCleanLossCards(losses))
                return;

            if (losses.Count == 0)
            {
                _contentPanel.Children.Add(CreateMutedText("Штрафов и потерь за выбранный месяц нет."));
                return;
            }

            foreach (var item in losses)
            {
                string status = item.IsPaid ? "Оплачено" : "Не оплачено / к удержанию";

                _contentPanel.Children.Add(CreateCard(new StackPanel
                {
                    Children =
                    {
                        CreateBigLine($"{item.CreatedAt:dd.MM.yyyy HH:mm} - {item.Amount} сом"),
                        CreateLine(item.Title),
                        CreateLine($"Тип: {item.LossType}"),
                        CreateLine($"Статус: {status}"),
                        CreateLine($"Проверил: {item.CheckedByEmployeeName}"),
                        item.PaidAt != null
                            ? CreateLine($"Оплачено: {item.PaidAt.Value:dd.MM.yyyy HH:mm}")
                            : CreateLine(""),
                        CreateDescription(item.Description)
                    }
                }));
            }
        }

        private bool RenderCleanLossCards(List<EmployeeShortageInfo> losses)
        {
            if (losses.Count == 0)
            {
                _contentPanel.Children.Add(CreateMutedText("Штрафов и потерь за выбранный месяц нет."));
                return true;
            }

            foreach (var item in losses)
            {
                string status = item.IsPaid ? "Оплачено" : "К удержанию из зарплаты";

                _contentPanel.Children.Add(CreateCard(new StackPanel
                {
                    Children =
                    {
                        CreateBigLine($"{item.CreatedAt:dd.MM.yyyy HH:mm} - {item.Amount} сом"),
                        CreateLine(item.Title),
                        CreateLine($"Тип: {item.LossType}"),
                        CreateLine($"Статус: {status}"),
                        CreateLine($"Проверил: {item.CheckedByEmployeeName}"),
                        item.PaidAt != null
                            ? CreateLine($"Оплачено: {item.PaidAt.Value:dd.MM.yyyy HH:mm}")
                            : CreateLine(""),
                        CreateDescription(item.Description)
                    }
                }));
            }

            return true;
        }

        private string GetMonthTitle(DateTime month)
        {
            var culture = new CultureInfo("ru-RU");
            string monthName = culture.DateTimeFormat.GetMonthName(month.Month);

            if (string.IsNullOrWhiteSpace(monthName))
                monthName = month.Month.ToString("00");

            monthName = char.ToUpper(monthName[0]) + monthName.Substring(1);

            return $"{monthName} {month.Year}";
        }

        private TextBlock CreateBigLine(string text)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = Brushes.White,
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 7)
            };
        }

        private TextBlock CreateLine(string text)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                FontSize = 15,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 22,
                Margin = new Thickness(0, 0, 0, 4)
            };
        }

        private TextBlock CreateDescription(string text)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = new SolidColorBrush(Color.FromRgb(170, 180, 195)),
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 21,
                Margin = new Thickness(0, 8, 0, 0)
            };
        }

        private TextBlock CreateMutedText(string text)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                FontSize = 15,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 22,
                Margin = new Thickness(0, 0, 0, 8)
            };
        }

        private Border CreateCard(UIElement content)
        {
            return new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(24, 32, 43)),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(16),
                Margin = new Thickness(0, 0, 0, 12),
                Child = content
            };
        }
    }
}
