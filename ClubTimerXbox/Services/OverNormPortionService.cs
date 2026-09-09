using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Threading;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services;

internal static class OverNormPortionService
{
    private static readonly Dictionary<string, OverNormPortionStore> Stores = new();
    private static DispatcherTimer? _timer;
    private static bool _waiting;
    private static string _lastDay = "";

    private static OverNormPortionStore Store
    {
        get
        {
            string club = PcIdentityService.Current.ClubId;
            if (!Regex.IsMatch(club, @"\A[a-z0-9][a-z0-9_-]{0,63}\z"))
                throw new InvalidDataException("Club identity is required for bonus accounting.");
            lock (Stores)
            {
                if (!Stores.TryGetValue(club, out var store))
                {
                    store = new OverNormPortionStore(Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "ClubTimerXbox", "OverNormPortions", club + ".json"),
                        club, ClubClock.Current.LocalNow);
                    Stores.Add(club, store);
                }
                return store;
            }
        }
    }

    public static void Start()
    {
        _ = Store;
        _timer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick -= OnTick;
        _timer.Tick += OnTick;
        _timer.Start();
        RefreshCurrent();
    }

    private static void OnTick(object? sender, EventArgs args)
    {
        string key = BusinessCalendarService.GetBusinessDay(ClubClock.Current.LocalNow).Key;
        if (_waiting || key != _lastDay) RefreshCurrent();
    }

    private static void RefreshCurrent()
    {
        try
        {
            DateTime start = BusinessCalendarService.GetBusinessDay(ClubClock.Current.LocalNow).StartInclusive;
            var day = GetDay(start);
            _waiting = day?.WaitingPortions > 0;
            _lastDay = OverNormPortionEngine.DayKey(start);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine("Portion bonus requires retry: " + ex.GetType().Name);
        }
    }

    public static void OnGameIncomeRecorded(DateTime occurredAt)
    {
        if (_timer == null) return;
        try
        {
            DateTime start = BusinessCalendarService.GetBusinessDay(occurredAt).StartInclusive;
            _ = GetDay(start);
            RefreshCurrent();
        }
        catch (Exception ex)
        {
            // The cash row is already durable. Report generation retries before salary can be paid.
            System.Diagnostics.Trace.WriteLine("Portion bonus pending: " + ex.GetType().Name);
        }
    }

    public static bool AppliesTo(DateTime start) => start >= Store.EffectiveFrom;

    public static List<string> GetReviewMessages(DateTime from, DateTime to) => Store.ReadDays(from, to)
        .Where(day => day.RevenueBelowAwardedThreshold > 0)
        .Select(day => $"{day.Start:dd.MM.yyyy}: игровая выручка после исправления ниже уже " +
            $"оплаченного порога на {day.RevenueBelowAwardedThreshold} сом. " +
            "Проверьте бонусы за план: прошлые начисления автоматически не списаны.").ToList();

    public static OverNormPortionDay? GetDay(DateTime start)
    {
        DateTime now = ClubClock.Current.LocalNow;
        if (!AppliesTo(start)) return null;
        DateTime end = start.AddDays(1);
        var employees = EmployeeService.GetAllEmployees();
        var work = new List<PortionWork>();
        foreach (var shift in ActionLogService.GetAllShifts())
        {
            if (shift.StartedAt >= end || (shift.ClosedAt ?? now) <= start) continue;
            var employee = employees.FirstOrDefault(e => e.Name.Equals(shift.EmployeeName,
                StringComparison.OrdinalIgnoreCase));
            if (employee == null || string.IsNullOrWhiteSpace(employee.EmployeeId)) continue;
            work.Add(new PortionWork(employee.EmployeeId, employee.Name, shift.StartedAt,
                shift.ClosedAt ?? now));
        }
        var revenue = CashService.Records.Where(r => r.Type == CashRecordType.GameSession &&
                r.Category == "Игры" && CashService.GetBusinessTime(r) >= start &&
                CashService.GetBusinessTime(r) < end)
            .Select(r => new PortionRevenue(r.Id, r.CreatedAt, r.Amount)).ToList();
        var settings = SalaryPolicyHistoryService.GetSettingsAt(start);
        string monthKey = BusinessCalendarService.GetBusinessMonth(start).Key;
        return Store.GetDay(start, now, settings.DailyGameRevenueNorm, settings.OverNormBonusPercent,
            revenue, work, BusinessAccountingService.IsMonthClosed(monthKey));
    }

    public static List<(string EmployeeName, AutoSalaryBonusItem Bonus)> GetBonuses(DateTime start)
    {
        var result = new List<(string, AutoSalaryBonusItem)>();
        var day = GetDay(start);
        if (day == null) return result;
        string club = PcIdentityService.Current.ClubId;
        foreach (var portion in day.Portions)
            foreach (var share in portion.Shares.Where(s => s.Amount > 0))
            {
                string name = EmployeeService.FindById(share.EmployeeId)?.Name ?? share.EmployeeName;
                result.Add((name, new AutoSalaryBonusItem
                {
                    Id = $"over-plan:{club}:{OverNormPortionEngine.DayKey(start)}:{portion.Number}:{share.EmployeeId}",
                    CreatedAt = portion.RecordedAt,
                    EarnedBusinessDate = start.Date,
                    Type = "OverNormGameRevenuePortion",
                    Title = "Бонус за план",
                    Description = $"Порция {portion.Number}: 500 сом сверх плана. " +
                        $"Распределено {portion.AllocatedAt:dd.MM HH:mm}. " +
                        $"Участие: {TimeSpan.FromTicks(share.WorkedTicks).TotalHours:0.##} ч.",
                    Amount = share.Amount
                }));
            }
        return result;
    }
}
