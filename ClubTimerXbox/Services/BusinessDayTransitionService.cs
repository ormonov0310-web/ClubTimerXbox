using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Threading;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public static class BusinessDayTransitionService
    {
        private static readonly string FolderPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ClubTimerXbox");
        private static readonly string FilePath = Path.Combine(
            FolderPath,
            "business_day_snapshots.json");
        private static readonly BusinessDayTransitionState State = Load();
        private static DispatcherTimer? _timer;

        public static IReadOnlyDictionary<string, BusinessDaySnapshot> ClosedDays =>
            State.ClosedDays;

        public static void Start()
        {
            RunCatchUp(ClubClock.Current.LocalNow);

            _timer ??= new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
            _timer.Tick -= TimerOnTick;
            _timer.Tick += TimerOnTick;
            _timer.Start();
        }

        public static void RunCatchUp(DateTime now)
        {
            string currentDayKey = BusinessCalendarService.GetBusinessDay(now).Key;
            if (string.IsNullOrWhiteSpace(State.ActivatedDayKey) ||
                !DateTime.TryParse(State.ActivatedDayKey, out DateTime cursor))
            {
                State.ActivatedDayKey = currentDayKey;
                Save();
                return;
            }

            DateTime currentDay = DateTime.Parse(currentDayKey).Date;
            cursor = cursor.Date;
            while (cursor < currentDay)
            {
                CloseDay(cursor);
                cursor = cursor.AddDays(1);
                State.ActivatedDayKey = cursor.ToString("yyyy-MM-dd");
                Save();
            }
        }

        private static void CloseDay(DateTime businessDate)
        {
            string key = businessDate.ToString("yyyy-MM-dd");
            if (State.ClosedDays.ContainsKey(key))
                return;

            DateTime from = businessDate.AddHours(
                BusinessCalendarService.BusinessDayStartHour);
            DateTime to = from.AddDays(1);
            int cashIncome = PaymentService.Records
                .Where(record => record.CreatedAt >= from && record.CreatedAt < to)
                .Sum(record => record.CashAmount);
            int cashlessIncome = PaymentService.Records
                .Where(record => record.CreatedAt >= from && record.CreatedAt < to)
                .Sum(record => record.MBankAmount);
            int cashExpenses = CashService.Records
                .Where(record => record.CreatedAt >= from && record.CreatedAt < to &&
                                 record.Category == "Расходы" &&
                                 record.PaymentMethod == "Наличные")
                .Sum(record => record.Amount);
            int cashlessExpenses = CashService.Records
                .Where(record => record.CreatedAt >= from && record.CreatedAt < to &&
                                 record.Category == "Расходы" &&
                                 record.PaymentMethod == "Безнал")
                .Sum(record => record.Amount);

            State.ClosedDays[key] = new BusinessDaySnapshot
            {
                DayKey = key,
                ClosedAt = ClubClock.Current.LocalNow,
                GameRevenue = CashService.GetTotalByPeriodAndCategory(from, to, "Игры"),
                ProductsAndServicesRevenue = ProductServiceRevenueService.GetTotal(from, to),
                Expenses = CashService.GetExpenseTotalByPeriod(from, to),
                CashMovement = cashIncome - cashExpenses,
                CashlessMovement = cashlessIncome - cashlessExpenses
            };
            Save();
        }

        private static void TimerOnTick(object? sender, EventArgs e)
        {
            try
            {
                RunCatchUp(ClubClock.Current.LocalNow);
            }
            catch
            {
                // The same day key is retried on the next tick.
            }
        }

        private static BusinessDayTransitionState Load()
        {
            try
            {
                if (!File.Exists(FilePath))
                    return new BusinessDayTransitionState();

                var state = JsonSerializer.Deserialize<BusinessDayTransitionState>(
                    File.ReadAllText(FilePath)) ?? new BusinessDayTransitionState();
                state.ClosedDays = new Dictionary<string, BusinessDaySnapshot>(
                    state.ClosedDays ?? new Dictionary<string, BusinessDaySnapshot>(),
                    StringComparer.OrdinalIgnoreCase);
                return state;
            }
            catch
            {
                return new BusinessDayTransitionState();
            }
        }

        private static void Save()
        {
            Directory.CreateDirectory(FolderPath);
            AtomicFileStorageService.WriteAllText(
                FilePath,
                JsonSerializer.Serialize(
                    State,
                    new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
