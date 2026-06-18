using System;
using System.Collections.Generic;
using System.Linq;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public static class AutoSalaryService
    {
        private const string GamesCategory = "\u0418\u0433\u0440\u044b";
        private const string ProductsAndServicesCategory =
            "\u0422\u043e\u0432\u0430\u0440\u044b \u0438 \u0443\u0441\u043b\u0443\u0433\u0438";

        public static AutoSalarySettings Settings { get; private set; } =
            NormalizeSettings(AutoSalarySettingsStorageService.Load());

        public static void UpdateSettings(AutoSalarySettings settings)
        {
            Settings = NormalizeSettings(settings);
            AutoSalarySettingsStorageService.Save(Settings);
        }

        public static AutoSalaryReport BuildReport(DateTime monthStart)
        {
            monthStart = new DateTime(monthStart.Year, monthStart.Month, 1);
            DateTime nextMonthStart = monthStart.AddMonths(1);

            int gameRevenue = CashService.GetTotalByPeriodAndCategory(
                monthStart,
                nextMonthStart,
                GamesCategory
            );
            int productRevenue = CashService.GetTotalByPeriodAndCategory(
                monthStart,
                nextMonthStart,
                ProductsAndServicesCategory
            );
            int expenseReserve = Percent(gameRevenue, Settings.ExpenseReservePercent);
            int salaryBase = Math.Max(0, gameRevenue - expenseReserve);
            int salaryFund = Percent(salaryBase, Settings.SalaryFundPercent);
            int plannedTimeFund = Math.Max(0, Settings.TimeMonthlyFundAmount);

            var report = new AutoSalaryReport
            {
                MonthKey = monthStart.ToString("yyyy-MM"),
                Settings = Settings,
                GameRevenue = gameRevenue,
                ProductRevenue = productRevenue,
                ExpenseReserveAmount = expenseReserve,
                SalaryBaseAmount = salaryBase,
                SalaryFundAmount = salaryFund,
                TimeFundAmount = plannedTimeFund,
                GameRevenueFundAmount = salaryFund,
                ProductShareFundAmount = 0
            };

            var bonusInputs = BuildEmployeeBonusInputs(monthStart, nextMonthStart);
            var employeeInputs = EmployeeService
                .GetAllEmployees()
                .Where(employee => employee.IsActive)
                .Select(employee =>
                {
                    var summary = EmployeeStatsService.GetSummary(employee.Name, monthStart);
                    int paidSalary = CashService.GetSalaryTotalByPeriodForEmployee(
                        monthStart,
                        nextMonthStart,
                        employee.Name
                    );

                    if (!bonusInputs.TryGetValue(employee.Name, out var bonusInput))
                    {
                        bonusInput = new EmployeeBonusInput
                        {
                            EmployeeName = employee.Name
                        };
                    }

                    return new EmployeeSalaryInput
                    {
                        EmployeeName = employee.Name,
                        Summary = summary,
                        PaidSalary = paidSalary,
                        WorkHours = bonusInput.WorkHours,
                        Bonuses = bonusInput.Bonuses
                    };
                })
                .ToList();

            for (int index = 0; index < employeeInputs.Count; index++)
            {
                var input = employeeInputs[index];

                int timeAmount = CalculateTimeAmount(
                    fund: report.TimeFundAmount,
                    plannedHours: Settings.TimeMonthlyPlannedHours,
                    employeeHours: input.WorkHours
                );
                int gameAmount = CalculateGameRevenueAmount(input.Summary.MonthGameIncome);
                int productBonus = Percent(
                    input.Summary.MonthProductsIncome,
                    Settings.ProductBonusPercent
                );
                int bonusAmount = input.Bonuses.Sum(bonus => bonus.Amount);
                int gross = timeAmount + gameAmount + productBonus + bonusAmount;
                int remaining = gross - input.Summary.MonthUnpaidLosses - input.PaidSalary;

                report.ProductBonusTotalAmount += productBonus;
                report.BonusTotalAmount += bonusAmount;
                report.Employees.Add(new AutoSalaryEmployeeResult
                {
                    EmployeeName = input.EmployeeName,
                    WorkHours = Math.Round(input.WorkHours, 2),
                    GameRevenue = input.Summary.MonthGameIncome,
                    ProductRevenue = input.Summary.MonthProductsIncome,
                    TimeAmount = timeAmount,
                    GameRevenueAmount = gameAmount,
                    ProductShareAmount = 0,
                    ProductBonusAmount = productBonus,
                    BonusAmount = bonusAmount,
                    Bonuses = input.Bonuses
                        .OrderByDescending(bonus => bonus.CreatedAt)
                        .ToList(),
                    GrossAmount = gross,
                    LossesAmount = input.Summary.MonthUnpaidLosses,
                    PaidAmount = input.PaidSalary,
                    RemainingAmount = remaining
                });
            }

            return report;
        }

        private static Dictionary<string, EmployeeBonusInput> BuildEmployeeBonusInputs(
            DateTime monthStart,
            DateTime nextMonthStart)
        {
            var result = EmployeeService
                .GetAllEmployees()
                .Where(employee => employee.IsActive)
                .ToDictionary(
                    employee => employee.Name,
                    employee => new EmployeeBonusInput
                    {
                        EmployeeName = employee.Name
                    },
                    StringComparer.OrdinalIgnoreCase
                );

            DateTime day = monthStart.Date;
            while (day < nextMonthStart)
            {
                DateTime scheduleStart = GetScheduleStart(day);
                DateTime scheduleEnd = GetScheduleEnd(day);

                ApplyPaidTimeForDay(result, scheduleStart, scheduleEnd);
                ApplyPunctualityBonusForDay(result, scheduleStart);
                ApplyLateActiveBonusForDay(result, scheduleStart, scheduleEnd);
                ApplyOverNormBonusForDay(result, scheduleStart, scheduleEnd);

                day = day.AddDays(1);
            }

            return result;
        }

        private static void ApplyPaidTimeForDay(
            Dictionary<string, EmployeeBonusInput> result,
            DateTime scheduleStart,
            DateTime scheduleEnd)
        {
            foreach (var input in result.Values)
            {
                var shifts = EmployeeStatsService.GetShifts(
                    input.EmployeeName,
                    scheduleStart.Date,
                    scheduleEnd.AddDays(1)
                );

                foreach (var shift in shifts)
                {
                    DateTime shiftEnd = shift.ClosedAt ?? DateTime.Now;
                    TimeSpan paidTime = GetOverlap(
                        shift.StartedAt,
                        shiftEnd,
                        scheduleStart,
                        scheduleEnd
                    );

                    paidTime += GetLateActiveTime(
                        input.EmployeeName,
                        shift.StartedAt,
                        shiftEnd,
                        scheduleEnd
                    );

                    if (paidTime <= TimeSpan.Zero)
                        continue;

                    double hours = paidTime.TotalHours;
                    input.WorkHours += hours;
                    input.AddDailyHours(scheduleStart.Date, hours);
                }
            }
        }

        private static void ApplyPunctualityBonusForDay(
            Dictionary<string, EmployeeBonusInput> result,
            DateTime scheduleStart)
        {
            if (Settings.PunctualityBonusAmount <= 0)
                return;

            DateTime earlyOpenStart = scheduleStart.Date.AddHours(6);
            if (scheduleStart <= earlyOpenStart)
                return;

            var firstShift = result.Values
                .SelectMany(input =>
                    EmployeeStatsService
                        .GetShifts(input.EmployeeName, earlyOpenStart, scheduleStart)
                        .Where(shift =>
                            shift.StartedAt.Date == scheduleStart.Date &&
                            shift.StartedAt >= earlyOpenStart &&
                            shift.StartedAt < scheduleStart)
                        .Select(shift => new
                        {
                            Input = input,
                            shift.StartedAt
                        }))
                .OrderBy(item => item.StartedAt)
                .FirstOrDefault();

            if (firstShift == null)
                return;

            firstShift.Input.Bonuses.Add(new AutoSalaryBonusItem
            {
                CreatedAt = scheduleStart,
                Type = "Punctuality",
                Title = "Пунктуальность",
                Description = $"Открыл клуб до {FormatHour(Settings.WorkDayStartHour)}.",
                Amount = Settings.PunctualityBonusAmount
            });
        }

        private static void ApplyLateActiveBonusForDay(
            Dictionary<string, EmployeeBonusInput> result,
            DateTime scheduleStart,
            DateTime scheduleEnd)
        {
            if (Settings.LateActiveSessionBonusAmount <= 0)
                return;

            foreach (var input in result.Values)
            {
                var shifts = EmployeeStatsService.GetShifts(
                    input.EmployeeName,
                    scheduleEnd,
                    scheduleEnd.AddHours(8)
                );

                bool hasLateActiveSession = shifts.Any(shift =>
                    GetLateActiveTime(
                        input.EmployeeName,
                        shift.StartedAt,
                        shift.ClosedAt ?? DateTime.Now,
                        scheduleEnd
                    ) > TimeSpan.Zero);

                if (!hasLateActiveSession)
                    continue;

                input.Bonuses.Add(new AutoSalaryBonusItem
                {
                    CreatedAt = scheduleEnd,
                    Type = "LateActiveSession",
                    Title = "Поздняя активная смена",
                    Description = $"После {FormatHour(Settings.WorkDayEndHour)} были активные игровые сеансы.",
                    Amount = Settings.LateActiveSessionBonusAmount
                });
            }
        }

        private static void ApplyOverNormBonusForDay(
            Dictionary<string, EmployeeBonusInput> result,
            DateTime scheduleStart,
            DateTime scheduleEnd)
        {
            if (Settings.DailyGameRevenueNorm <= 0 || Settings.OverNormBonusPercent <= 0)
                return;

            int dayGameRevenue = CashService.GetTotalByPeriodAndCategory(
                scheduleStart,
                scheduleEnd,
                GamesCategory
            );
            int overNormRevenue = dayGameRevenue - Settings.DailyGameRevenueNorm;

            if (overNormRevenue <= 0)
                return;

            int bonusFund = Percent(overNormRevenue, Settings.OverNormBonusPercent);
            if (bonusFund <= 0)
                return;

            var participants = result.Values
                .Select(input => new
                {
                    Input = input,
                    Hours = input.GetDailyHours(scheduleStart.Date)
                })
                .Where(item => item.Hours > 0)
                .ToList();

            int distributed = 0;

            for (int index = 0; index < participants.Count; index++)
            {
                var participant = participants[index];
                int amount = Allocate(
                    bonusFund,
                    1,
                    participants.Count,
                    ref distributed,
                    index == participants.Count - 1
                );

                if (amount <= 0)
                    continue;

                participant.Input.Bonuses.Add(new AutoSalaryBonusItem
                {
                    CreatedAt = scheduleEnd,
                    Type = "OverNormGameRevenue",
                    Title = "Бонус за план",
                    Description = $"Игры за день: {dayGameRevenue} сом, выше нормы на {overNormRevenue} сом.",
                    Amount = amount
                });
            }
        }

        private static TimeSpan GetLateActiveTime(
            string employeeName,
            DateTime shiftStart,
            DateTime shiftEnd,
            DateTime scheduleEnd)
        {
            if (shiftEnd <= scheduleEnd)
                return TimeSpan.Zero;

            var intervals = EmployeeStatsService
                .GetGameSessionsForMonth(employeeName, scheduleEnd)
                .Select(session => new
                {
                    Start = Max(session.StartedAt, Max(shiftStart, scheduleEnd)),
                    End = Min(session.ClosedAt ?? DateTime.Now, shiftEnd)
                })
                .Where(interval => interval.End > interval.Start)
                .OrderBy(interval => interval.Start)
                .ToList();

            if (intervals.Count == 0)
                return TimeSpan.Zero;

            DateTime currentStart = intervals[0].Start;
            DateTime currentEnd = intervals[0].End;
            TimeSpan total = TimeSpan.Zero;

            for (int index = 1; index < intervals.Count; index++)
            {
                var interval = intervals[index];
                if (interval.Start <= currentEnd)
                {
                    if (interval.End > currentEnd)
                        currentEnd = interval.End;

                    continue;
                }

                total += currentEnd - currentStart;
                currentStart = interval.Start;
                currentEnd = interval.End;
            }

            total += currentEnd - currentStart;
            return total;
        }

        private static TimeSpan GetOverlap(
            DateTime firstStart,
            DateTime firstEnd,
            DateTime secondStart,
            DateTime secondEnd)
        {
            DateTime start = Max(firstStart, secondStart);
            DateTime end = Min(firstEnd, secondEnd);
            return end > start ? end - start : TimeSpan.Zero;
        }

        private static DateTime GetScheduleStart(DateTime day)
        {
            return day.Date.AddHours(Settings.WorkDayStartHour);
        }

        private static DateTime GetScheduleEnd(DateTime day)
        {
            DateTime start = GetScheduleStart(day);
            DateTime end = day.Date.AddHours(Settings.WorkDayEndHour);

            if (end <= start)
                end = end.AddDays(1);

            return end;
        }

        private static DateTime Max(DateTime first, DateTime second)
        {
            return first >= second ? first : second;
        }

        private static DateTime Min(DateTime first, DateTime second)
        {
            return first <= second ? first : second;
        }

        private static string FormatHour(int hour)
        {
            return $"{NormalizeHour(hour):00}:00";
        }

        private static int CalculateTimeAmount(
            int fund,
            int plannedHours,
            double employeeHours)
        {
            if (fund <= 0 || plannedHours <= 0 || employeeHours <= 0)
                return 0;

            return (int)Math.Round(fund * (employeeHours / plannedHours));
        }

        private static int CalculateGameRevenueAmount(int employeeGameRevenue)
        {
            if (employeeGameRevenue <= 0)
                return 0;

            int expenseReserve = Percent(employeeGameRevenue, Settings.ExpenseReservePercent);
            int salaryBase = Math.Max(0, employeeGameRevenue - expenseReserve);
            return Percent(salaryBase, Settings.SalaryFundPercent);
        }

        private static int Percent(int amount, int percent)
        {
            return (int)Math.Round(amount * (percent / 100.0));
        }

        private static int Allocate(
            int fund,
            double value,
            double total,
            ref int distributed,
            bool isLast)
        {
            if (fund <= 0 || total <= 0 || value <= 0)
                return 0;

            int amount = isLast
                ? fund - distributed
                : (int)Math.Round(fund * (value / total));

            if (amount < 0)
                amount = 0;

            distributed += amount;
            return amount;
        }

        private static AutoSalarySettings NormalizeSettings(AutoSalarySettings settings)
        {
            if (settings == null)
                settings = new AutoSalarySettings();

            settings.ExpenseReservePercent = ClampPercent(settings.ExpenseReservePercent);
            settings.SalaryFundPercent = ClampPercent(settings.SalaryFundPercent);
            settings.TimeSharePercent = ClampPercent(settings.TimeSharePercent);
            settings.GameRevenueSharePercent = ClampPercent(settings.GameRevenueSharePercent);
            settings.TimeMonthlyFundAmount = Math.Max(0, settings.TimeMonthlyFundAmount);
            settings.TimeMonthlyPlannedHours = Math.Max(1, settings.TimeMonthlyPlannedHours);
            settings.ProductRevenueSharePercent = 0;
            settings.ProductBonusPercent = ClampPercent(settings.ProductBonusPercent);
            settings.WorkDayStartHour = NormalizeHour(settings.WorkDayStartHour);
            settings.WorkDayEndHour = NormalizeHour(settings.WorkDayEndHour);
            settings.DailyGameRevenueNorm = Math.Max(0, settings.DailyGameRevenueNorm);
            settings.OverNormBonusPercent = ClampPercent(settings.OverNormBonusPercent);
            settings.PunctualityBonusAmount = Math.Max(0, settings.PunctualityBonusAmount);
            settings.LateActiveSessionBonusAmount = Math.Max(0, settings.LateActiveSessionBonusAmount);

            int shareTotal =
                settings.TimeSharePercent +
                settings.GameRevenueSharePercent;

            if (shareTotal <= 0)
            {
                settings.TimeSharePercent = 45;
                settings.GameRevenueSharePercent = 55;
                return settings;
            }

            if (shareTotal != 100)
            {
                settings.TimeSharePercent = (int)Math.Round(settings.TimeSharePercent * 100.0 / shareTotal);
                settings.GameRevenueSharePercent = 100 - settings.TimeSharePercent;
            }

            return settings;
        }

        private static int ClampPercent(int value)
        {
            if (value < 0)
                return 0;

            if (value > 100)
                return 100;

            return value;
        }

        private static int NormalizeHour(int hour)
        {
            if (hour < 0)
                return 0;

            if (hour > 23)
                return 23;

            return hour;
        }

        private class EmployeeSalaryInput
        {
            public string EmployeeName { get; set; } = "";

            public EmployeeStatsSummary Summary { get; set; } = new EmployeeStatsSummary();

            public int PaidSalary { get; set; }

            public double WorkHours { get; set; }

            public List<AutoSalaryBonusItem> Bonuses { get; set; } =
                new List<AutoSalaryBonusItem>();
        }

        private class EmployeeBonusInput
        {
            private readonly Dictionary<DateTime, double> _dailyHours =
                new Dictionary<DateTime, double>();

            public string EmployeeName { get; set; } = "";

            public double WorkHours { get; set; }

            public List<AutoSalaryBonusItem> Bonuses { get; set; } =
                new List<AutoSalaryBonusItem>();

            public void AddDailyHours(DateTime day, double hours)
            {
                day = day.Date;

                if (!_dailyHours.ContainsKey(day))
                    _dailyHours[day] = 0;

                _dailyHours[day] += hours;
            }

            public double GetDailyHours(DateTime day)
            {
                day = day.Date;
                return _dailyHours.TryGetValue(day, out double hours) ? hours : 0;
            }
        }
    }
}
