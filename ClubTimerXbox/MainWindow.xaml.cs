using System;
using System.Collections.Generic;
using System.Linq;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ClubTimerXbox.Models;
using ClubTimerXbox.Services;

namespace ClubTimerXbox
{
    public partial class MainWindow : Window
    {
        private readonly DispatcherTimer _mainTimer = new DispatcherTimer();
        private readonly List<ClubPlace> _places = new List<ClubPlace>();

        // Чтобы предупреждение за 1 минуту не повторялось каждую секунду.
        private readonly HashSet<string> _oneMinuteWarningShownPlaceNames = new HashSet<string>();

        // Открытые неблокирующие окна будильника по местам.
        private readonly Dictionary<string, WarningAlarmWindow> _activeAlarmWindows =
            new Dictionary<string, WarningAlarmWindow>();

        // Мигание кнопки "Приёмка", если новый админ ещё не принял товары и наличку.
        private readonly DispatcherTimer _stockAuditBlinkTimer = new DispatcherTimer();
        private bool _stockAuditBlinkState = false;

        // Совместимость со старым именем.
        public MainWindow()
        {
            InitializeComponent();

            UpdateCurrentEmployeeText();

            CreatePlaces();
            RestoreActivePlacesFromStorage();
            DrawPlaces();
            UpdateMainCashText();
            UpdateStockAuditButtonState();

            _mainTimer.Interval = TimeSpan.FromSeconds(1);
            _mainTimer.Tick += MainTimer_Tick;
            _mainTimer.Start();

            _stockAuditBlinkTimer.Interval = TimeSpan.FromMilliseconds(650);
            _stockAuditBlinkTimer.Tick += StockAuditBlinkTimer_Tick;
            _stockAuditBlinkTimer.Start();

            Closing += (_, _) => HandleWindowClosing();
        }

        private void HandleWindowClosing()
        {
            _stockAuditBlinkTimer.Stop();

            CloseAllAlarmWindows();
            SaveActivePlacesToStorage();

            // Важно:
            // активные игровые сеансы остаются,
            // но рабочая смена сотрудника должна закрыться,
            // чтобы время сотрудника не накручивалось после закрытия программы.
            ActionLogService.CloseCurrentShift();
        }

        private void RestoreActivePlacesFromStorage()
        {
            var savedPlaces = ActiveSessionStorageService.Load();

            if (savedPlaces.Count == 0)
                return;

            foreach (var saved in savedPlaces)
            {
                var place = _places.Find(item =>
                    item.Name == saved.Name &&
                    item.Type == saved.Type
                );

                if (place == null)
                    continue;

                place.IsBusy = saved.IsBusy;
                place.IsOpenMode = saved.IsOpenMode;
                place.IsCalculating = saved.IsCalculating;
                place.PaidAmount = saved.PaidAmount;
                place.StartTime = saved.StartTime;
                place.TotalMinutes = saved.TotalMinutes;
                place.RemainingSeconds = saved.RemainingSeconds;
                place.PricePerMinute = saved.PricePerMinute;
                place.ActivePricePerMinute = saved.ActivePricePerMinute;
                place.AccruedAmountBeforeCurrentSegment = saved.AccruedAmountBeforeCurrentSegment;
                place.StartedByEmployeeName = saved.StartedByEmployeeName;
                place.IncomeEmployeeName = saved.IncomeEmployeeName;

                if (place.IsBusy && !place.IsOpenMode && !place.IsCalculating)
                {
                    int passedSeconds = (int)(DateTime.Now - saved.LastSavedAt).TotalSeconds;

                    if (passedSeconds < 0)
                        passedSeconds = 0;

                    place.RemainingSeconds -= passedSeconds;

                    if (place.RemainingSeconds <= 0)
                    {
                        place.RemainingSeconds = 0;
                        place.IsCalculating = true;
                        place.StartTime = null;
                    }
                }
            }
        }

        private void SaveActivePlacesToStorage()
        {
            var activePlaces = new List<SavedActivePlace>();

            foreach (var place in _places)
            {
                if (!place.IsBusy)
                    continue;

                activePlaces.Add(new SavedActivePlace
                {
                    Name = place.Name,
                    Type = place.Type,

                    IsBusy = place.IsBusy,
                    IsOpenMode = place.IsOpenMode,
                    IsCalculating = place.IsCalculating,

                    PaidAmount = place.PaidAmount,

                    StartTime = place.StartTime,

                    TotalMinutes = place.TotalMinutes,
                    RemainingSeconds = place.RemainingSeconds,

                    LastSavedAt = DateTime.Now,

                    PricePerMinute = place.PricePerMinute,
                    ActivePricePerMinute = place.ActivePricePerMinute,

                    AccruedAmountBeforeCurrentSegment = place.AccruedAmountBeforeCurrentSegment,

                    StartedByEmployeeName = place.StartedByEmployeeName,
                    IncomeEmployeeName = place.IncomeEmployeeName
                });
            }

            if (activePlaces.Count == 0)
            {
                ActiveSessionStorageService.Clear();
                return;
            }

            ActiveSessionStorageService.Save(activePlaces);
        }

        private void ReloadPlacesFromSettings()
        {
            bool hasBusyPlaces = _places.Exists(place => place.IsBusy);

            if (hasBusyPlaces)
            {
                MessageBox.Show(
                    "Есть активные клиенты.\n\n" +
                    "Чтобы не потерять текущие таймеры и расчёты, количество мест сейчас не будет перестроено.\n" +
                    "Новые тарифы будут применяться для новых запусков после освобождения мест.",
                    "Настройки применены частично"
                );

                UpdatePlacesTariffsOnly();
                DrawPlaces();
                SaveActivePlacesToStorage();
                return;
            }

            CreatePlaces();
            DrawPlaces();
            SaveActivePlacesToStorage();

            MessageBox.Show(
                "Настройки применены.\n\n" +
                "Главный экран обновлён по новым тарифам и количеству мест.",
                "Настройки"
            );
        }

        private void UpdatePlacesTariffsOnly()
        {
            var settings = AppSettingsService.Current;
            double tvPricePerMinute = TariffService.GetPricePerMinute(settings.TvTariff);
            double wheelPricePerMinute = TariffService.GetPricePerMinute(settings.WheelTariff);

            foreach (var place in _places)
            {
                if (place.Type == PlaceType.Wheel)
                {
                    place.PricePerMinute = wheelPricePerMinute;

                    if (!place.IsBusy)
                        place.ActivePricePerMinute = wheelPricePerMinute;
                }
                else
                {
                    place.PricePerMinute = tvPricePerMinute;

                    if (!place.IsBusy)
                        place.ActivePricePerMinute = tvPricePerMinute;
                }
            }
        }

        private void CreatePlaces()
        {
            _places.Clear();
            _oneMinuteWarningShownPlaceNames.Clear();

            var settings = AppSettingsService.Current;
            double tvPricePerMinute = TariffService.GetPricePerMinute(settings.TvTariff);
            double wheelPricePerMinute = TariffService.GetPricePerMinute(settings.WheelTariff);

            for (int i = 1; i <= settings.TvCount; i++)
            {
                _places.Add(new ClubPlace
                {
                    Name = $"ТВ {i}",
                    Type = PlaceType.NormalTv,
                    PricePerMinute = tvPricePerMinute,
                    ActivePricePerMinute = tvPricePerMinute
                });
            }

            for (int i = 1; i <= settings.WheelCount; i++)
            {
                _places.Add(new ClubPlace
                {
                    Name = $"Руль {i}",
                    Type = PlaceType.Wheel,
                    PricePerMinute = wheelPricePerMinute,
                    ActivePricePerMinute = wheelPricePerMinute
                });
            }
        }

        private void DrawPlaces()
        {
            PlacesItemsControl.Items.Clear();

            foreach (var place in _places)
            {
                PlacesItemsControl.Items.Add(CreatePlaceCard(place));
            }
        }

        private Border CreatePlaceCard(ClubPlace place)
        {
            var titleText = new TextBlock
            {
                Text = place.Name,
                Foreground = Brushes.White,
                FontSize = 22,
                FontWeight = FontWeights.Bold
            };

            var statusText = new TextBlock
            {
                Text = GetStatusText(place),
                Foreground = GetStatusBrush(place),
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 10, 0, 0)
            };

            var timeText = new TextBlock
            {
                Text = GetTimeText(place),
                Foreground = Brushes.White,
                FontSize = 34,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 12, 0, 0)
            };

            var moneyText = new TextBlock
            {
                Text = GetMoneyText(place),
                Foreground = new SolidColorBrush(Color.FromRgb(170, 180, 195)),
                FontSize = 15,
                Margin = new Thickness(0, 8, 0, 0),
                TextWrapping = TextWrapping.Wrap
            };

            var employeeText = new TextBlock
            {
                Text = GetEmployeeText(place),
                Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                FontSize = 13,
                Margin = new Thickness(0, 6, 0, 0),
                TextWrapping = TextWrapping.Wrap
            };

            var saleText = new TextBlock
            {
                Text = GetActiveSalesText(place),
                Foreground = new SolidColorBrush(Color.FromRgb(251, 191, 36)),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 6, 0, 0),
                TextWrapping = TextWrapping.Wrap
            };

            var stack = new StackPanel();
            stack.Children.Add(titleText);
            stack.Children.Add(statusText);
            stack.Children.Add(timeText);
            stack.Children.Add(moneyText);
            stack.Children.Add(employeeText);

            if (!string.IsNullOrWhiteSpace(saleText.Text))
                stack.Children.Add(saleText);

            var card = new Border
            {
                Background = GetCardBackground(place),
                CornerRadius = new CornerRadius(18),
                Padding = new Thickness(18),
                Margin = new Thickness(8),
                MinHeight = 205,
                Child = stack
            };

            card.ContextMenu = CreateContextMenu(place);

            return card;
        }

        private ContextMenu CreateContextMenu(ClubPlace place)
        {
            var menu = new ContextMenu();
            var tariff = GetTariffForPlace(place);

            AddTariffMenuItem(menu, place, tariff.OneHourPrice);
            AddTariffMenuItem(menu, place, tariff.HalfHourPrice);
            AddTariffMenuItem(menu, place, tariff.FiveMinutesPrice);

            menu.Items.Add(new Separator());
            menu.Items.Add(CreateMenuItem("Открытый режим", () => StartOpenMode(place)));

            menu.Items.Add(new Separator());
            menu.Items.Add(CreateMenuItem("Добавить время", () => OpenAddTimeWindow(place)));
            menu.Items.Add(CreateMenuItem("Добавить штраф", () => OpenPenaltyWindow(place)));
            menu.Items.Add(CreateMenuItem("Пересадить", () => MoveClient(place)));

            menu.Items.Add(new Separator());
            menu.Items.Add(CreateMenuItem("Остановить", () => StopPlace(place)));

            menu.Items.Add(new Separator());
            menu.Items.Add(CreateMenuItem("Настройки будильника", OpenAlarmSettingsWindow));

            return menu;
        }

        private void AddTariffMenuItem(ContextMenu menu, ClubPlace place, int amount)
        {
            if (amount <= 0)
                return;

            var tariff = GetTariffForPlace(place);
            int seconds = TariffService.CalculateSecondsByAmount(tariff, amount);
            string timeText = TariffService.FormatMenuTime(seconds);

            menu.Items.Add(CreateMenuItem(
                $"{timeText} — {amount} сом",
                () => StartPrepaid(place, seconds, amount)
            ));
        }

        private TariffSettings GetTariffForPlace(ClubPlace place)
        {
            var settings = AppSettingsService.Current;

            if (place.Type == PlaceType.Wheel)
                return settings.WheelTariff;

            return settings.TvTariff;
        }

        private MenuItem CreateMenuItem(string title, Action action)
        {
            var item = new MenuItem
            {
                Header = title
            };

            item.Click += (_, _) => action();

            return item;
        }

        private string GetCurrentEmployeeName()
        {
            return EmployeeService.CurrentEmployee?.Name ?? "Неизвестно";
        }

        private void StartPrepaid(ClubPlace place, int seconds, int paidAmount)
        {
            if (place.IsBusy)
            {
                MessageBox.Show($"{place.Name} уже занят.", "Внимание");
                return;
            }

            if (seconds <= 0)
            {
                MessageBox.Show("Этот тариф даёт 0 секунд. Проверьте настройки.", "Ошибка тарифа");
                return;
            }

            string employeeName = GetCurrentEmployeeName();
            string tariffText = $"{TariffService.FormatMenuTime(seconds)} — {paidAmount} сом";

            var checkoutItems = new List<CheckoutItem>
            {
                new CheckoutItem
                {
                    Name = $"Игровое время: {place.Name} — {tariffText}",
                    Quantity = 1,
                    UnitPrice = paidAmount,
                    Category = "Игры"
                }
            };

            var checkoutWindow = new CashCheckoutWindow(
                employeeName: employeeName,
                operationTitle: "Предоплаченный тариф",
                items: checkoutItems,
                placeName: place.Name
            )
            {
                Owner = this
            };

            bool? checkoutResult = checkoutWindow.ShowDialog();

            if (checkoutResult != true || checkoutWindow.PaymentRecord == null)
                return;

            PaymentService.AddPayment(checkoutWindow.PaymentRecord);

            _oneMinuteWarningShownPlaceNames.Remove(place.Name);

            place.IsBusy = true;
            place.IsOpenMode = false;
            place.IsCalculating = false;
            place.PaidAmount = paidAmount;
            place.StartTime = DateTime.Now;
            place.TotalMinutes = seconds / 60;
            place.RemainingSeconds = seconds;
            place.ActivePricePerMinute = place.PricePerMinute;
            place.AccruedAmountBeforeCurrentSegment = 0;

            place.StartedByEmployeeName = employeeName;
            place.IncomeEmployeeName = employeeName;

            ActionLogService.StartGameSession(
                placeName: place.Name,
                employeeName: employeeName,
                isOpenMode: false,
                tariffText: tariffText,
                paidAmount: paidAmount
            );

            DrawPlaces();
            SaveActivePlacesToStorage();
        }

        private void StartOpenMode(ClubPlace place)
        {
            if (place.IsBusy)
            {
                MessageBox.Show($"{place.Name} уже занят.", "Внимание");
                return;
            }

            string employeeName = GetCurrentEmployeeName();

            _oneMinuteWarningShownPlaceNames.Remove(place.Name);

            place.IsBusy = true;
            place.IsOpenMode = true;
            place.IsCalculating = false;
            place.PaidAmount = 0;
            place.StartTime = DateTime.Now;
            place.TotalMinutes = 0;
            place.RemainingSeconds = 0;
            place.ActivePricePerMinute = place.PricePerMinute;
            place.AccruedAmountBeforeCurrentSegment = 0;

            place.StartedByEmployeeName = employeeName;
            place.IncomeEmployeeName = null;

            ActionLogService.StartGameSession(
                placeName: place.Name,
                employeeName: employeeName,
                isOpenMode: true,
                tariffText: "Открытый режим",
                paidAmount: 0
            );

            DrawPlaces();
            SaveActivePlacesToStorage();
        }

        private void OpenAddTimeWindow(ClubPlace place)
        {
            if (!place.IsBusy)
            {
                MessageBox.Show("Сначала запустите место.", "Добавить время");
                return;
            }

            if (place.IsCalculating)
            {
                MessageBox.Show("Это место сейчас в расчёте.", "Добавить время");
                return;
            }

            if (place.IsOpenMode)
            {
                MessageBox.Show("В открытом режиме время добавлять не нужно. Он сам считает по факту.", "Открытый режим");
                return;
            }

            var window = new AddTimeWindow(place)
            {
                Owner = this
            };

            bool? result = window.ShowDialog();

            if (result != true)
                return;

            string employeeName = GetCurrentEmployeeName();

            var checkoutItems = new List<CheckoutItem>
            {
                new CheckoutItem
                {
                    Name = $"Добавить время: {place.Name} — {window.MinutesToAdd} мин.",
                    Quantity = 1,
                    UnitPrice = window.PriceToAdd,
                    Category = "Игры"
                }
            };

            var checkoutWindow = new CashCheckoutWindow(
                employeeName: employeeName,
                operationTitle: "Добавить время",
                items: checkoutItems,
                placeName: place.Name
            )
            {
                Owner = this
            };

            bool? checkoutResult = checkoutWindow.ShowDialog();

            if (checkoutResult != true || checkoutWindow.PaymentRecord == null)
                return;

            PaymentService.AddPayment(checkoutWindow.PaymentRecord);

            AddTime(place, window.MinutesToAdd, window.PriceToAdd);
        }

        private void AddTime(ClubPlace place, int minutes, int price)
        {
            string employeeName = GetCurrentEmployeeName();

            place.TotalMinutes += minutes;
            place.RemainingSeconds += minutes * 60;
            place.PaidAmount += price;

            if (place.RemainingSeconds > 60)
                _oneMinuteWarningShownPlaceNames.Remove(place.Name);

            ActionLogService.AddExtraToActiveSession(
                placeName: place.Name,
                type: "Добавлено время",
                employeeName: employeeName,
                description:
                    $"Добавлено время: {minutes} мин, сумма {price} сом. " +
                    $"Итого оплачено: {place.PaidAmount} сом.",
                amount: price
            );

            DrawPlaces();
            SaveActivePlacesToStorage();
        }

        private void OpenPenaltyWindow(ClubPlace place)
        {
            if (!place.IsBusy)
            {
                MessageBox.Show("Сначала запустите место.", "Добавить штраф");
                return;
            }

            if (place.IsCalculating)
            {
                MessageBox.Show("Это место сейчас в расчёте.", "Добавить штраф");
                return;
            }

            var window = new PenaltyWindow(place)
            {
                Owner = this
            };

            bool? result = window.ShowDialog();

            if (result != true)
                return;

            AddPenalty(place, window.PenaltyMinutes);
        }

        private void AddPenalty(ClubPlace place, int minutes)
        {
            string employeeName = GetCurrentEmployeeName();
            int penaltySeconds = minutes * 60;

            if (place.IsOpenMode)
            {
                int penaltyPrice = CalculatePriceForMinutes(place.ActivePricePerMinute, minutes);
                place.AccruedAmountBeforeCurrentSegment += penaltyPrice;
                place.StartTime = DateTime.Now;

                ActionLogService.AddExtraToActiveSession(
                    placeName: place.Name,
                    type: "Штраф",
                    employeeName: employeeName,
                    description:
                        $"Штраф: {minutes} мин. " +
                        $"В открытом режиме это добавило к оплате {penaltyPrice} сом.",
                    amount: penaltyPrice
                );

                MessageBox.Show(
                    $"Штраф добавлен: {minutes} мин.\n" +
                    $"В открытом режиме это добавило к оплате: {penaltyPrice} сом",
                    "Штраф"
                );

                DrawPlaces();
                SaveActivePlacesToStorage();
                return;
            }

            place.RemainingSeconds -= penaltySeconds;

            if (place.RemainingSeconds < 0)
                place.RemainingSeconds = 0;

            int penaltyPriceForMessage = CalculatePriceForMinutes(place.ActivePricePerMinute, minutes);

            ActionLogService.AddExtraToActiveSession(
                placeName: place.Name,
                type: "Штраф",
                employeeName: employeeName,
                description:
                    $"Штраф: {minutes} мин. " +
                    $"Денежный эффект штрафа: {penaltyPriceForMessage} сом. " +
                    $"Оплачено осталось без изменений: {place.PaidAmount} сом.",
                amount: penaltyPriceForMessage
            );

            MessageBox.Show(
                $"Штраф добавлен: {minutes} мин.\n\n" +
                $"Штраф считается как уже сыгранное время: {penaltyPriceForMessage} сом\n" +
                $"Оплачено осталось без изменений: {place.PaidAmount} сом\n" +
                $"Новое оставшееся время: {TariffService.FormatTime(place.RemainingSeconds)}",
                "Штраф"
            );

            DrawPlaces();
            SaveActivePlacesToStorage();
        }

        private void MoveClient(ClubPlace fromPlace)
        {
            if (!fromPlace.IsBusy)
            {
                MessageBox.Show("На этом месте нет клиента.", "Пересадить");
                return;
            }

            if (fromPlace.IsCalculating)
            {
                MessageBox.Show("Это место сейчас в расчёте.", "Пересадить");
                return;
            }

            var freePlaces = _places.FindAll(place => !place.IsBusy);

            if (freePlaces.Count == 0)
            {
                MessageBox.Show("Нет свободных мест для пересадки.", "Пересадить");
                return;
            }

            var transferWindow = new TransferWindow(freePlaces)
            {
                Owner = this
            };

            bool? result = transferWindow.ShowDialog();

            if (result != true || transferWindow.SelectedPlace == null)
                return;

            var toPlace = transferWindow.SelectedPlace;

            double oldRate = fromPlace.ActivePricePerMinute;
            double newRate = toPlace.PricePerMinute;

            string fromPlaceName = fromPlace.Name;
            string toPlaceName = toPlace.Name;
            string employeeName = GetCurrentEmployeeName();

            bool warningWasShown = _oneMinuteWarningShownPlaceNames.Contains(fromPlace.Name);

            if (fromPlace.IsOpenMode)
                MoveOpenModeClient(fromPlace, toPlace, oldRate, newRate);
            else
                MovePrepaidClient(fromPlace, toPlace, oldRate, newRate);

            _oneMinuteWarningShownPlaceNames.Remove(fromPlaceName);

            if (warningWasShown)
                _oneMinuteWarningShownPlaceNames.Add(toPlaceName);
            else
                _oneMinuteWarningShownPlaceNames.Remove(toPlaceName);

            string description = $"Пересадка: {fromPlaceName} → {toPlaceName}.";

            if (Math.Abs(oldRate - newRate) > 0.001)
            {
                description += $" Тариф изменился: {FormatPrice(oldRate)} → {FormatPrice(newRate)}.";
            }

            ActionLogService.MoveActiveGameSession(
                oldPlaceName: fromPlaceName,
                newPlaceName: toPlaceName,
                employeeName: employeeName,
                description: description
            );

            DrawPlaces();
            SaveActivePlacesToStorage();
        }

        private void MoveOpenModeClient(ClubPlace fromPlace, ClubPlace toPlace, double oldRate, double newRate)
        {
            int oldSegmentPrice = GetCurrentSegmentPrice(fromPlace);

            toPlace.IsBusy = true;
            toPlace.IsOpenMode = true;
            toPlace.IsCalculating = false;
            toPlace.PaidAmount = 0;
            toPlace.TotalMinutes = 0;
            toPlace.RemainingSeconds = 0;

            toPlace.AccruedAmountBeforeCurrentSegment =
                fromPlace.AccruedAmountBeforeCurrentSegment + oldSegmentPrice;

            toPlace.StartTime = DateTime.Now;
            toPlace.ActivePricePerMinute = newRate;

            toPlace.StartedByEmployeeName = fromPlace.StartedByEmployeeName;
            toPlace.IncomeEmployeeName = null;

            ClearPlace(fromPlace);

            ShowTransferMessage(oldRate, newRate, toPlace.AccruedAmountBeforeCurrentSegment);
        }

        private void MovePrepaidClient(ClubPlace fromPlace, ClubPlace toPlace, double oldRate, double newRate)
        {
            int currentActualPrice = GetActualPrice(fromPlace);
            int remainingMoney = fromPlace.PaidAmount - currentActualPrice;

            if (remainingMoney < 0)
                remainingMoney = 0;

            int newRemainingSeconds = CalculateRemainingSecondsByMoney(remainingMoney, newRate);

            toPlace.IsBusy = true;
            toPlace.IsOpenMode = false;
            toPlace.IsCalculating = false;
            toPlace.PaidAmount = fromPlace.PaidAmount;
            toPlace.TotalMinutes = newRemainingSeconds / 60;
            toPlace.RemainingSeconds = newRemainingSeconds;
            toPlace.AccruedAmountBeforeCurrentSegment = currentActualPrice;
            toPlace.StartTime = DateTime.Now;
            toPlace.ActivePricePerMinute = newRate;

            toPlace.StartedByEmployeeName = fromPlace.StartedByEmployeeName;
            toPlace.IncomeEmployeeName = fromPlace.IncomeEmployeeName;

            ClearPlace(fromPlace);

            if (Math.Abs(oldRate - newRate) > 0.001)
            {
                MessageBox.Show(
                    $"Клиент пересажен с конвертацией тарифа.\n\n" +
                    $"Было: {FormatPrice(oldRate)}\n" +
                    $"Теперь: {FormatPrice(newRate)}\n\n" +
                    $"Оплачено: {toPlace.PaidAmount} сом\n" +
                    $"Уже сыграл на: {currentActualPrice} сом\n" +
                    $"Остаток денег: {remainingMoney} сом\n" +
                    $"Новое оставшееся время: {TariffService.FormatTime(newRemainingSeconds)}",
                    "Тариф изменён"
                );
            }
            else
            {
                MessageBox.Show("Клиент пересажен.", "Пересадка");
            }
        }

        private void ShowTransferMessage(double oldRate, double newRate, double accruedAmount)
        {
            if (Math.Abs(oldRate - newRate) > 0.001)
            {
                MessageBox.Show(
                    $"Клиент пересажен.\n\n" +
                    $"До пересадки считалось: {FormatPrice(oldRate)}\n" +
                    $"Теперь начинается расчёт: {FormatPrice(newRate)}\n\n" +
                    $"Сумма до пересадки: {Math.Ceiling(accruedAmount)} сом",
                    "Тариф изменён"
                );
            }
            else
            {
                MessageBox.Show("Клиент пересажен.", "Пересадка");
            }
        }

        private void StopPlace(ClubPlace place)
        {
            if (!place.IsBusy)
            {
                MessageBox.Show($"{place.Name} уже свободен.", "Остановить");
                return;
            }

            if (place.IsCalculating)
            {
                MessageBox.Show($"{place.Name} уже находится в расчёте.", "Расчёт");
                return;
            }

            bool wasOpenMode = place.IsOpenMode;
            int gameAmount = GetActualPrice(place);

            var activeSession = ActionLogService.GetActiveGameSessionByPlace(place.Name);
            Guid? sessionId = activeSession?.Id;

            int productsAmount = GetActiveSessionProductsAndServicesTotal(place.Name);
            string productsDescription = BuildActiveSessionSalesDescription(place.Name);

            string incomeEmployeeName;

            if (wasOpenMode)
            {
                incomeEmployeeName = GetCurrentEmployeeName();
                place.IncomeEmployeeName = incomeEmployeeName;
            }
            else
            {
                incomeEmployeeName = place.IncomeEmployeeName ?? place.StartedByEmployeeName ?? GetCurrentEmployeeName();
            }

            int refund = 0;
            int needToPayForGame = 0;
            int gameCashIncome = gameAmount;

            if (!wasOpenMode)
            {
                refund = place.PaidAmount - gameAmount;

                if (refund < 0)
                {
                    needToPayForGame = gameAmount - place.PaidAmount;
                    refund = 0;
                }
            }

            int totalClientMustPayNow;

            if (wasOpenMode)
            {
                totalClientMustPayNow = gameAmount + productsAmount;
            }
            else
            {
                if (refund > 0)
                    totalClientMustPayNow = productsAmount - refund;
                else
                    totalClientMustPayNow = needToPayForGame + productsAmount;
            }

            string closedByEmployeeName = GetCurrentEmployeeName();

            if (wasOpenMode && totalClientMustPayNow > 0)
            {
                // Ставим открытый режим на паузу, пока открыта ККМ.
                // Если админ нажмёт "Отмена", сеанс продолжится с этой суммы,
                // а время ожидания в окне кассы не будет добавлено клиенту.
                place.IsCalculating = true;
                place.StartTime = null;
                place.AccruedAmountBeforeCurrentSegment = gameAmount;

                DrawPlaces();
                SaveActivePlacesToStorage();

                var checkoutItems = new List<CheckoutItem>();

                if (gameAmount > 0)
                {
                    checkoutItems.Add(new CheckoutItem
                    {
                        Name = $"Открытый режим: {place.Name}",
                        Quantity = 1,
                        UnitPrice = gameAmount,
                        Category = "Игры"
                    });
                }

                if (productsAmount > 0 && activeSession != null)
                {
                    var unpaidSaleLines = activeSession.SaleLines
                        .Where(line => !line.IsPaid)
                        .ToList();

                    foreach (var line in unpaidSaleLines)
                    {
                        checkoutItems.Add(new CheckoutItem
                        {
                            Name = line.ItemName,
                            Quantity = line.Quantity,
                            UnitPrice = line.UnitPrice,
                            Category = line.ItemType == SaleItemType.Product ? "Товар" : "Услуга"
                        });
                    }
                }

                var checkoutWindow = new CashCheckoutWindow(
                    employeeName: closedByEmployeeName,
                    operationTitle: "Закрытие открытого режима",
                    items: checkoutItems,
                    placeName: place.Name,
                    gameSessionId: sessionId
                )
                {
                    Owner = this
                };

                bool? checkoutResult = checkoutWindow.ShowDialog();

                if (checkoutResult != true || checkoutWindow.PaymentRecord == null)
                {
                    place.IsCalculating = false;
                    place.IsOpenMode = true;
                    place.StartTime = DateTime.Now;
                    place.AccruedAmountBeforeCurrentSegment = gameAmount;

                    DrawPlaces();
                    SaveActivePlacesToStorage();

                    return;
                }

                if (productsAmount > 0)
                {
                    checkoutWindow.PaymentRecord.Comment =
                        $"Товар/услуга был оформлен на {place.Name}:\n" +
                        productsDescription;
                }

                PaymentService.AddPayment(checkoutWindow.PaymentRecord);
            }

            ActionLogService.CloseActiveGameSession(
                placeName: place.Name,
                closedByEmployeeName: closedByEmployeeName,
                actualPlayedAmount: gameAmount,
                refundAmount: refund,
                needToPayAmount: needToPayForGame,
                cashIncomeAmount: gameCashIncome,
                incomeEmployeeName: incomeEmployeeName
            );

            CashService.AddGameSessionIncome(
                employeeName: closedByEmployeeName,
                incomeEmployeeName: incomeEmployeeName,
                placeName: place.Name,
                title: "Игровой сеанс",
                description:
                    $"{place.Name}. Клиент играл/штраф: {gameAmount} сом. " +
                    $"Оплачено: {place.PaidAmount} сом. " +
                    $"Возврат по игре: {refund} сом. Доплата по игре: {needToPayForGame} сом.",
                amount: gameCashIncome,
                gameSessionId: sessionId
            );

            if (productsAmount > 0)
            {
                CashService.AddProductOrServiceIncome(
                    employeeName: closedByEmployeeName,
                    title: "Товары/услуги по сеансу",
                    description:
                        $"{place.Name}. Оплачено при закрытии сеанса.\n" +
                        productsDescription,
                    amount: productsAmount,
                    placeName: place.Name,
                    gameSessionId: sessionId
                );
            }

            place.IsCalculating = true;
            place.IsOpenMode = false;
            place.StartTime = null;
            place.RemainingSeconds = 0;

            DrawPlaces();

            Dispatcher.Invoke(
                () => { },
                DispatcherPriority.Render
            );

            UpdateMainCashText();

            MessageBox.Show(
                BuildStopMessage(
                    wasOpenMode,
                    gameAmount,
                    productsAmount,
                    place.PaidAmount,
                    refund,
                    needToPayForGame,
                    totalClientMustPayNow,
                    incomeEmployeeName
                ),
                wasOpenMode ? "Открытый режим" : "Расчёт"
            );

            ClearPlace(place);
            DrawPlaces();
            SaveActivePlacesToStorage();
        }

        private string BuildStopMessage(
            bool wasOpenMode,
            int gameAmount,
            int productsAmount,
            int paidAmount,
            int refund,
            int needToPayForGame,
            int totalClientMustPayNow,
            string incomeEmployeeName)
        {
            string text = "";

            if (wasOpenMode)
            {
                text += $"Время: {gameAmount} сом\n";

                if (productsAmount > 0)
                    text += $"Товары/услуги: {productsAmount} сом\n";

                text += $"\nИтого к оплате: {gameAmount + productsAmount} сом\n";
                text += $"\nВыручка за игру относится к: {incomeEmployeeName}";

                return text;
            }

            text += $"Клиент играл/штраф: {gameAmount} сом\n";
            text += $"Оплачено за игру: {paidAmount} сом\n";

            if (productsAmount > 0)
                text += $"Товары/услуги: {productsAmount} сом\n";

            if (refund > 0)
            {
                text += $"Возврат по игре: {refund} сом\n";

                if (productsAmount > 0)
                {
                    if (totalClientMustPayNow > 0)
                        text += $"\nПосле учёта товаров клиент должен доплатить: {totalClientMustPayNow} сом\n";
                    else if (totalClientMustPayNow < 0)
                        text += $"\nПосле учёта товаров вернуть клиенту: {Math.Abs(totalClientMustPayNow)} сом\n";
                    else
                        text += "\nПосле учёта товаров ничего доплачивать/возвращать не нужно.\n";
                }
            }
            else if (needToPayForGame > 0)
            {
                text += $"Доплата по игре: {needToPayForGame} сом\n";

                if (productsAmount > 0)
                    text += $"\nИтого доплатить: {needToPayForGame + productsAmount} сом\n";
            }
            else
            {
                text += "Возврат по игре не нужен.\n";

                if (productsAmount > 0)
                    text += $"\nК оплате за товары/услуги: {productsAmount} сом\n";
            }

            text += $"\nВыручка за игру относится к: {incomeEmployeeName}";

            return text;
        }

        private void ClearPlace(ClubPlace place)
        {
            CloseAlarmWindowForPlace(place.Name);
            _oneMinuteWarningShownPlaceNames.Remove(place.Name);

            place.IsBusy = false;
            place.IsOpenMode = false;
            place.IsCalculating = false;
            place.PaidAmount = 0;
            place.StartTime = null;
            place.TotalMinutes = 0;
            place.RemainingSeconds = 0;
            place.ActivePricePerMinute = place.PricePerMinute;
            place.AccruedAmountBeforeCurrentSegment = 0;
            place.StartedByEmployeeName = null;
            place.IncomeEmployeeName = null;
        }

        private void MainTimer_Tick(object? sender, EventArgs e)
        {
            bool needRedraw = false;
            bool needSave = false;

            foreach (var place in _places)
            {
                if (!place.IsBusy)
                    continue;

                if (place.IsCalculating)
                    continue;

                if (place.IsOpenMode)
                {
                    needRedraw = true;
                    continue;
                }

                if (place.RemainingSeconds > 0)
                {
                    place.RemainingSeconds--;
                    needRedraw = true;
                    needSave = true;

                    CheckOneMinuteWarning(place);
                }

                if (place.RemainingSeconds <= 0)
                {
                    string incomeEmployeeName =
                        place.IncomeEmployeeName ??
                        place.StartedByEmployeeName ??
                        GetCurrentEmployeeName();

                    var activeSession = ActionLogService.GetActiveGameSessionByPlace(place.Name);
                    Guid? sessionId = activeSession?.Id;
                    int productsAmount = GetActiveSessionProductsAndServicesTotal(place.Name);
                    string productsDescription = BuildActiveSessionSalesDescription(place.Name);

                    ActionLogService.CloseActiveGameSession(
                        placeName: place.Name,
                        closedByEmployeeName: "Автоматически",
                        actualPlayedAmount: place.PaidAmount,
                        refundAmount: 0,
                        needToPayAmount: 0,
                        cashIncomeAmount: place.PaidAmount,
                        incomeEmployeeName: incomeEmployeeName
                    );

                    CashService.AddGameSessionIncome(
                        employeeName: "Автоматически",
                        incomeEmployeeName: incomeEmployeeName,
                        placeName: place.Name,
                        title: "Игровой сеанс",
                        description:
                            $"{place.Name}. Время закончилось автоматически. " +
                            $"Оплачено: {place.PaidAmount} сом.",
                        amount: place.PaidAmount,
                        gameSessionId: sessionId
                    );

                    if (productsAmount > 0)
                    {
                        CashService.AddProductOrServiceIncome(
                            employeeName: "Автоматически",
                            title: "Товары/услуги по сеансу",
                            description:
                                $"{place.Name}. Оплачено при автоматическом закрытии.\n" +
                                productsDescription,
                            amount: productsAmount,
                            placeName: place.Name,
                            gameSessionId: sessionId
                        );
                    }

                    string message =
                        $"{place.Name}\n" +
                        $"Время закончилось.\n" +
                        $"Оплачено за игру: {place.PaidAmount} сом\n";

                    if (productsAmount > 0)
                        message += $"Товары/услуги: {productsAmount} сом\n";

                    message += $"\nВыручка за игру относится к сотруднику: {incomeEmployeeName}";

                    AlarmSoundService.PlayOnce("Hand");
                    MessageBox.Show(message, "Время закончилось");

                    ClearPlace(place);
                    UpdateMainCashText();
                    needRedraw = true;
                    needSave = true;
                }
            }

            if (needRedraw)
                DrawPlaces();

            if (needSave)
                SaveActivePlacesToStorage();
        }

        private void CheckOneMinuteWarning(ClubPlace place)
        {
            if (!place.IsBusy)
                return;

            if (place.IsOpenMode)
                return;

            if (place.IsCalculating)
                return;

            var settings = AlarmSettingsService.Current;

            if (!settings.IsEnabled)
                return;

            if (place.RemainingSeconds > settings.TriggerBeforeEndSeconds)
                return;

            if (_oneMinuteWarningShownPlaceNames.Contains(place.Name))
                return;

            _oneMinuteWarningShownPlaceNames.Add(place.Name);

            ShowAlarmWindow(place, settings);
        }

        private void ShowAlarmWindow(ClubPlace place, AlarmSettings settings)
        {
            CloseAlarmWindowForPlace(place.Name);

            var window = new WarningAlarmWindow(
                placeName: place.Name,
                remainingSeconds: place.RemainingSeconds,
                soundName: settings.SoundName,
                durationSeconds: settings.SoundDurationSeconds
            )
            {
                Owner = this
            };

            _activeAlarmWindows[place.Name] = window;

            window.Closed += (_, _) =>
            {
                if (_activeAlarmWindows.TryGetValue(place.Name, out var activeWindow) &&
                    ReferenceEquals(activeWindow, window))
                {
                    _activeAlarmWindows.Remove(place.Name);
                }
            };

            window.Show();
        }

        private void CloseAlarmWindowForPlace(string placeName)
        {
            if (!_activeAlarmWindows.TryGetValue(placeName, out var window))
                return;

            _activeAlarmWindows.Remove(placeName);

            try
            {
                window.StopAlarm();
            }
            catch
            {
                // Если окно уже закрыто, ничего не делаем.
            }
        }

        private void CloseAllAlarmWindows()
        {
            var windows = _activeAlarmWindows.Values.ToList();
            _activeAlarmWindows.Clear();

            foreach (var window in windows)
            {
                try
                {
                    window.StopAlarm();
                }
                catch
                {
                    // Если окно уже закрыто, ничего не делаем.
                }
            }
        }

        private void StockAuditBlinkTimer_Tick(object? sender, EventArgs e)
        {
            UpdateStockAuditButtonState();
        }

        private void UpdateStockAuditButtonState()
        {
            if (StockAuditButton == null)
                return;

            bool isRequired = ShiftAcceptanceService.IsAcceptanceRequired();

            if (!isRequired)
            {
                _stockAuditBlinkState = false;

                StockAuditButton.Content = "Приёмка";
                StockAuditButton.Background = new SolidColorBrush(Color.FromRgb(51, 65, 85));
                StockAuditButton.Foreground = Brushes.White;
                StockAuditButton.FontWeight = FontWeights.SemiBold;

                return;
            }

            _stockAuditBlinkState = !_stockAuditBlinkState;

            StockAuditButton.Content = "Приёмка ⚠";
            StockAuditButton.FontWeight = FontWeights.Bold;

            if (_stockAuditBlinkState)
            {
                StockAuditButton.Background = new SolidColorBrush(Color.FromRgb(251, 191, 36));
                StockAuditButton.Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42));
            }
            else
            {
                StockAuditButton.Background = new SolidColorBrush(Color.FromRgb(120, 53, 15));
                StockAuditButton.Foreground = Brushes.White;
            }
        }

        private void OpenAlarmSettingsWindow()
        {
            var window = new AlarmSettingsWindow
            {
                Owner = this
            };

            window.ShowDialog();
        }


        private void AddSaleButton_Click(object sender, RoutedEventArgs e)
        {
            OpenSaleWindowFromMain();
        }

        private void ProductServiceButton_Click(object sender, RoutedEventArgs e)
        {
            OpenSaleWindowFromMain();
        }

        private void AddProductServiceButton_Click(object sender, RoutedEventArgs e)
        {
            OpenSaleWindowFromMain();
        }

        private void OpenSaleWindowButton_Click(object sender, RoutedEventArgs e)
        {
            OpenSaleWindowFromMain();
        }

        private void OpenSaleWindowFromMain()
        {
            var window = new SaleWindow
            {
                Owner = this
            };

            bool? result = window.ShowDialog();

            if (result != true)
                return;

            if (window.SelectedSaleItem == null)
                return;

            if (window.ResultType == SaleWindowResultType.SoldNow)
            {
                ProcessSoldNow(window.SelectedSaleItem, window.Quantity, window.TotalAmount);
                return;
            }

            if (window.ResultType == SaleWindowResultType.AttachToPlace)
            {
                var targetPlace = SelectActivePlaceForSale();

                if (targetPlace == null)
                    return;

                AttachSaleToPlace(
                    targetPlace,
                    window.SelectedSaleItem,
                    window.Quantity,
                    window.TotalAmount
                );

                return;
            }
        }

        private ClubPlace? SelectActivePlaceForSale()
        {
            var activePlaces = _places
                .Where(place => place.IsBusy && !place.IsCalculating)
                .ToList();

            if (activePlaces.Count == 0)
            {
                MessageBox.Show(
                    "Нет активных ТВ или рулей для оформления товара/услуги.",
                    "Оформить на ТВ"
                );

                return null;
            }

            var transferWindow = new TransferWindow(activePlaces)
            {
                Owner = this
            };

            bool? result = transferWindow.ShowDialog();

            if (result != true || transferWindow.SelectedPlace == null)
                return null;

            return transferWindow.SelectedPlace;
        }


        private void OpenSaleWindow(ClubPlace place)
        {
            var window = new SaleWindow
            {
                Owner = this
            };

            bool? result = window.ShowDialog();

            if (result != true)
                return;

            if (window.SelectedSaleItem == null)
                return;

            if (window.ResultType == SaleWindowResultType.SoldNow)
            {
                ProcessSoldNow(window.SelectedSaleItem, window.Quantity, window.TotalAmount);
                return;
            }

            if (window.ResultType == SaleWindowResultType.AttachToPlace)
            {
                AttachSaleToPlace(place, window.SelectedSaleItem, window.Quantity, window.TotalAmount);
                return;
            }
        }

        private void ProcessSoldNow(SaleItem item, int quantity, int totalAmount)
        {
            string employeeName = GetCurrentEmployeeName();

            var checkoutItems = new List<CheckoutItem>
            {
                new CheckoutItem
                {
                    Name = item.Name,
                    Quantity = quantity,
                    UnitPrice = item.SalePrice,
                    Category = item.Type == SaleItemType.Product ? "Товар" : "Услуга"
                }
            };

            var checkoutWindow = new CashCheckoutWindow(
                employeeName: employeeName,
                operationTitle: "Продажа товара / услуги",
                items: checkoutItems
            )
            {
                Owner = this
            };

            bool? checkoutResult = checkoutWindow.ShowDialog();

            if (checkoutResult != true || checkoutWindow.PaymentRecord == null)
                return;

            if (item.Type == SaleItemType.Product)
            {
                bool decreased = ProductStockService.Decrease(item.Name, quantity);

                if (!decreased)
                {
                    MessageBox.Show(
                        $"{item.Name}\n\n" +
                        "Не удалось списать товар со склада.\n" +
                        "Возможно, товара уже не хватает.",
                        "Склад"
                    );

                    return;
                }
            }

            PaymentService.AddPayment(checkoutWindow.PaymentRecord);

            string paymentMethod = GetPaymentMethodFromPaymentRecord(checkoutWindow.PaymentRecord);

            CashService.AddProductOrServiceIncome(
                employeeName: employeeName,
                title: item.Type == SaleItemType.Product ? "Продажа товара" : "Продажа услуги",
                description:
                    $"{item.Name} × {quantity} = {totalAmount} сом.\n" +
                    $"Наличные: {checkoutWindow.PaymentRecord.CashAmount} сом.\n" +
                    $"М Банк: {checkoutWindow.PaymentRecord.MBankAmount} сом.",
                amount: totalAmount,
                placeName: ""
            );

            UpdateMainCashText();
            DrawPlaces();
            SaveActivePlacesToStorage();

            MessageBox.Show(
                "Оплата подтверждена.\n\n" +
                $"{item.Name} × {quantity} = {totalAmount} сом\n" +
                $"Наличные: {checkoutWindow.PaymentRecord.CashAmount} сом\n" +
                $"М Банк: {checkoutWindow.PaymentRecord.MBankAmount} сом",
                "Касса"
            );
        }

        private void AttachSaleToPlace(ClubPlace place, SaleItem item, int quantity, int totalAmount)
        {
            if (!place.IsBusy)
            {
                MessageBox.Show(
                    "Чтобы оформить товар или услугу на ТВ, место должно быть занято.",
                    "Оформить на ТВ"
                );

                return;
            }

            if (place.IsCalculating)
            {
                MessageBox.Show(
                    "Это место сейчас в расчёте.",
                    "Оформить на ТВ"
                );

                return;
            }

            if (item.Type == SaleItemType.Product)
            {
                bool decreased = ProductStockService.Decrease(item.Name, quantity);

                if (!decreased)
                {
                    MessageBox.Show(
                        $"{item.Name}\n\n" +
                        "Не удалось списать товар со склада.\n" +
                        "Возможно, товара уже не хватает.",
                        "Склад"
                    );

                    return;
                }
            }

            string employeeName = GetCurrentEmployeeName();

            ActionLogService.AddSaleToActiveSession(
                placeName: place.Name,
                employeeName: employeeName,
                item: item,
                quantity: quantity
            );

            DrawPlaces();
            SaveActivePlacesToStorage();

            MessageBox.Show(
                $"{item.Name} × {quantity} = {totalAmount} сом\n\n" +
                $"Оформлено на {place.Name}.\n" +
                "Оплата будет при закрытии сеанса.",
                "Оформить на ТВ"
            );
        }

        private string GetPaymentMethodFromPaymentRecord(PaymentRecord record)
        {
            if (record.CashAmount > 0 && record.MBankAmount <= 0)
                return "Наличные";

            if (record.MBankAmount > 0 && record.CashAmount <= 0)
                return "Безнал";

            return "Смешанная";
        }

        private int GetCurrentSegmentPlayedMinutes(ClubPlace place)
        {
            if (place.StartTime == null)
                return 0;

            var totalSeconds = (DateTime.Now - place.StartTime.Value).TotalSeconds;
            int minutes = (int)Math.Ceiling(totalSeconds / 60.0);

            if (minutes < 1)
                minutes = 1;

            return minutes;
        }

        private int GetCurrentSegmentPrice(ClubPlace place)
        {
            int minutes = GetCurrentSegmentPlayedMinutes(place);
            return CalculatePriceForMinutes(place.ActivePricePerMinute, minutes);
        }

        private int GetActualPrice(ClubPlace place)
        {
            if (place.IsOpenMode)
            {
                double openModeTotal =
                    place.AccruedAmountBeforeCurrentSegment + GetCurrentSegmentPrice(place);

                return (int)Math.Ceiling(openModeTotal);
            }

            int remainingValue = CalculatePriceBySeconds(place.ActivePricePerMinute, place.RemainingSeconds);
            int actualPrice = place.PaidAmount - remainingValue;

            if (actualPrice < 0)
                actualPrice = 0;

            if (actualPrice > place.PaidAmount)
                actualPrice = place.PaidAmount;

            return actualPrice;
        }

        private int CalculatePriceForMinutes(double pricePerMinute, int minutes)
        {
            double price = minutes * pricePerMinute;
            return (int)Math.Ceiling(price);
        }

        private int CalculatePriceBySeconds(double pricePerMinute, int seconds)
        {
            if (seconds <= 0)
                return 0;

            double pricePerSecond = pricePerMinute / 60.0;
            double price = seconds * pricePerSecond;

            return (int)Math.Ceiling(price);
        }

        private int CalculateRemainingSecondsByMoney(int money, double pricePerMinute)
        {
            if (money <= 0 || pricePerMinute <= 0)
                return 0;

            double minutes = money / pricePerMinute;
            return (int)Math.Floor(minutes * 60);
        }

        private void OpenCashReportFromMainText_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // Важно: ловим клик раньше старых обработчиков и не даём ему всплывать дальше.
            e.Handled = true;

            var window = new CashReportWindow
            {
                Owner = this
            };

            window.ShowDialog();

            // Сразу обновляем главную кассу по новой ККМ.
            UpdateMainCashText();
        }

        private void UpdateMainCashText()
        {
            // Главный экран должен показывать ту же новую кассу,
            // которую считает окно "Касса": игры + товары/услуги за сегодняшний день.
            // Старый CashService здесь больше не используем.
            var gamesFilter = new CashReportFilter
            {
                Section = CashReportSection.Games,
                PeriodMode = CashReportPeriodMode.Day,
                ViewMode = CashReportViewMode.Records,
                SelectedDay = DateTime.Today
            };

            var productsFilter = new CashReportFilter
            {
                Section = CashReportSection.ProductsAndServices,
                PeriodMode = CashReportPeriodMode.Day,
                ViewMode = CashReportViewMode.Records,
                SelectedDay = DateTime.Today
            };

            var gamesReport = CashReportService.BuildReport(gamesFilter);
            var productsReport = CashReportService.BuildReport(productsFilter);

            int total =
                gamesReport.Summary.TotalAmount +
                productsReport.Summary.TotalAmount;

            MainCashText.Text = $"Касса: {total} сом";
        }

        private string GetStatusText(ClubPlace place)
        {
            if (!place.IsBusy)
                return "Свободно";

            if (place.IsCalculating)
                return "Расчёт";

            if (place.IsOpenMode)
                return "Открытый режим";

            return "Занято";
        }

        private string GetTimeText(ClubPlace place)
        {
            if (!place.IsBusy)
                return "00:00";

            if (place.IsCalculating)
                return "СТОП";

            if (place.IsOpenMode)
                return $"{GetCurrentSegmentPlayedMinutes(place)} мин.";

            return TariffService.FormatTime(place.RemainingSeconds);
        }

        private string GetMoneyText(ClubPlace place)
        {
            if (!place.IsBusy)
                return "ПКМ — выбрать тариф";

            int productsAmount = GetActiveSessionProductsAndServicesTotal(place.Name);

            if (place.IsCalculating)
            {
                if (productsAmount > 0)
                    return $"Ожидает расчёта • товары/услуги: {productsAmount} сом";

                return "Ожидает расчёта";
            }

            if (place.IsOpenMode)
            {
                int gamePrice = GetActualPrice(place);

                if (productsAmount > 0)
                    return $"Время: {gamePrice} сом • товары: {productsAmount} сом • итого: {gamePrice + productsAmount} сом";

                return $"По факту сейчас: {gamePrice} сом";
            }

            int actualPrice = GetActualPrice(place);

            string text = actualPrice > 0
                ? $"Оплачено: {place.PaidAmount} сом • сыграно: {actualPrice} сом"
                : $"Оплачено: {place.PaidAmount} сом";

            if (productsAmount > 0)
                text += $" • товары: {productsAmount} сом";

            return text;
        }

        private string GetEmployeeText(ClubPlace place)
        {
            if (!place.IsBusy)
                return "";

            if (place.IsOpenMode)
            {
                string startedBy = place.StartedByEmployeeName ?? "неизвестно";
                return $"Открыл: {startedBy} • оплату примет тот, кто остановит";
            }

            string incomeEmployee = place.IncomeEmployeeName ?? place.StartedByEmployeeName ?? "неизвестно";
            return $"Выручка: {incomeEmployee}";
        }

        private string GetActiveSalesText(ClubPlace place)
        {
            if (!place.IsBusy)
                return "";

            var session = ActionLogService.GetActiveGameSessionByPlace(place.Name);

            if (session == null || session.SaleLines.Count == 0)
                return "";

            var unpaidLines = session.SaleLines
                .Where(line => !line.IsPaid)
                .ToList();

            if (unpaidLines.Count == 0)
                return "";

            int total = unpaidLines.Sum(line => line.TotalAmount);

            string text = $"Оформлено: {total} сом";

            foreach (var line in unpaidLines.Take(2))
            {
                text += $"\n• {line.ItemName} × {line.Quantity} = {line.TotalAmount} сом";
            }

            if (unpaidLines.Count > 2)
                text += $"\n• ещё {unpaidLines.Count - 2} поз.";

            return text;
        }

        private int GetActiveSessionProductsAndServicesTotal(string placeName)
        {
            return ActionLogService.GetActiveSessionProductsAndServicesTotal(placeName);
        }

        private string BuildActiveSessionSalesDescription(string placeName)
        {
            var session = ActionLogService.GetActiveGameSessionByPlace(placeName);

            if (session == null || session.SaleLines.Count == 0)
                return "";

            var lines = session.SaleLines
                .Where(line => !line.IsPaid)
                .ToList();

            if (lines.Count == 0)
                return "";

            string text = "";

            foreach (var line in lines)
            {
                string itemType = line.ItemType == SaleItemType.Product ? "Товар" : "Услуга";

                text +=
                    $"{itemType}: {line.ItemName}, " +
                    $"кол-во: {line.Quantity}, " +
                    $"цена: {line.UnitPrice} сом, " +
                    $"сумма: {line.TotalAmount} сом, " +
                    $"оформил: {line.EmployeeName}\n";
            }

            return text.Trim();
        }

        private Brush GetStatusBrush(ClubPlace place)
        {
            if (!place.IsBusy)
                return new SolidColorBrush(Color.FromRgb(74, 222, 128));

            if (place.IsCalculating)
                return new SolidColorBrush(Color.FromRgb(251, 191, 36));

            if (place.IsOpenMode)
                return new SolidColorBrush(Color.FromRgb(96, 165, 250));

            return new SolidColorBrush(Color.FromRgb(248, 113, 113));
        }

        private Brush GetCardBackground(ClubPlace place)
        {
            if (!place.IsBusy)
            {
                if (place.Type == PlaceType.Wheel)
                    return new SolidColorBrush(Color.FromRgb(30, 41, 59));

                return new SolidColorBrush(Color.FromRgb(24, 32, 43));
            }

            if (place.IsCalculating)
                return new SolidColorBrush(Color.FromRgb(70, 55, 20));

            if (place.IsOpenMode)
                return new SolidColorBrush(Color.FromRgb(20, 45, 75));

            return new SolidColorBrush(Color.FromRgb(65, 35, 40));
        }

        private string FormatPrice(double pricePerMinute)
        {
            if (pricePerMinute % 1 == 0)
                return $"{pricePerMinute:0} сом/мин";

            return $"{pricePerMinute:0.##} сом/мин";
        }
    }
}