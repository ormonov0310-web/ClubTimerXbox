using System;
using System.Collections.Generic;
using System.Linq;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public class EmployeeStatsSummary
    {
        public string EmployeeName { get; set; } = "";

        public TimeSpan TodayWorkTime { get; set; }
        public TimeSpan MonthWorkTime { get; set; }

        public int TodayGameIncome { get; set; }
        public int TodayProductsIncome { get; set; }
        public int TodayTotalIncome { get; set; }

        public int MonthGameIncome { get; set; }
        public int MonthProductsIncome { get; set; }
        public int MonthTotalIncome { get; set; }

        public int TodayShortages { get; set; }
        public int MonthShortages { get; set; }
        public int MonthUnpaidLosses { get; set; }
        public int MonthPaidLosses { get; set; }

        public int ClosedGameSessionsCount { get; set; }
        public int ProductServiceOperationsCount { get; set; }
        public int ShortageCount { get; set; }
    }

    public class EmployeeDayIncome
    {
        public DateTime Date { get; set; }
        public int GameIncome { get; set; }
        public int ProductsIncome { get; set; }
        public int TotalIncome => GameIncome + ProductsIncome;
    }

    public class EmployeeShiftInfo
    {
        public DateTime StartedAt { get; set; }
        public DateTime? ClosedAt { get; set; }
        public TimeSpan Duration { get; set; }
        public bool IsClosed { get; set; }
    }

    public class EmployeeShortageInfo
    {
        public DateTime CreatedAt { get; set; }
        public string Title { get; set; } = "";
        public string LossType { get; set; } = "";
        public string Description { get; set; } = "";
        public int Amount { get; set; }
        public string CheckedByEmployeeName { get; set; } = "";
        public bool IsPaid { get; set; }
        public DateTime? PaidAt { get; set; }
    }

    public class EmployeeProductServiceInfo
    {
        public DateTime CreatedAt { get; set; }
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public int Amount { get; set; }
        public string PlaceName { get; set; } = "";
    }

    public class EmployeeGameSessionInfo
    {
        public DateTime StartedAt { get; set; }
        public DateTime? ClosedAt { get; set; }
        public string PlaceName { get; set; } = "";
        public string TariffText { get; set; } = "";
        public int GameAmount { get; set; }
        public int ProductsAmount { get; set; }
        public int TotalAmount => GameAmount + ProductsAmount;
        public string ClosedByEmployeeName { get; set; } = "";
    }

    public class EmployeeJournalInfo
    {
        public DateTime CreatedAt { get; set; }
        public string Type { get; set; } = "";
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public int Amount { get; set; }
    }

    public static class EmployeeStatsService
    {
        public static EmployeeStatsSummary GetSummary(string employeeName)
        {
            return GetSummary(employeeName, new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1));
        }

        public static EmployeeStatsSummary GetSummary(string employeeName, DateTime monthStart)
        {
            DateTime todayStart = DateTime.Today;
            DateTime tomorrowStart = todayStart.AddDays(1);

            monthStart = new DateTime(monthStart.Year, monthStart.Month, 1);
            DateTime nextMonthStart = monthStart.AddMonths(1);

            var todayRecords = GetCashRecordsForEmployee(employeeName, todayStart, tomorrowStart);
            var monthRecords = GetCashRecordsForEmployee(employeeName, monthStart, nextMonthStart);

            var todayLosses = GetLossRecordsForEmployee(employeeName, todayStart, tomorrowStart);
            var monthLosses = GetLossRecordsForEmployee(employeeName, monthStart, nextMonthStart);

            var monthSessions = GetGameSessions(employeeName, monthStart, nextMonthStart);

            return new EmployeeStatsSummary
            {
                EmployeeName = employeeName,

                TodayWorkTime = GetWorkTime(employeeName, todayStart, tomorrowStart),
                MonthWorkTime = GetWorkTime(employeeName, monthStart, nextMonthStart),

                TodayGameIncome = todayRecords
                    .Where(record => record.Category == "Игры")
                    .Sum(record => record.Amount),

                TodayProductsIncome = todayRecords
                    .Where(record => record.Category == "Товары и услуги")
                    .Sum(record => record.Amount),

                TodayTotalIncome = todayRecords
                    .Where(record => record.Category == "Игры" || record.Category == "Товары и услуги")
                    .Sum(record => record.Amount),

                MonthGameIncome = monthRecords
                    .Where(record => record.Category == "Игры")
                    .Sum(record => record.Amount),

                MonthProductsIncome = monthRecords
                    .Where(record => record.Category == "Товары и услуги")
                    .Sum(record => record.Amount),

                MonthTotalIncome = monthRecords
                    .Where(record => record.Category == "Игры" || record.Category == "Товары и услуги")
                    .Sum(record => record.Amount),

                TodayShortages = todayLosses.Sum(record => record.Amount),
                MonthShortages = monthLosses.Sum(record => record.Amount),
                MonthUnpaidLosses = monthLosses.Where(record => !record.IsPaid).Sum(record => record.Amount),
                MonthPaidLosses = monthLosses.Where(record => record.IsPaid).Sum(record => record.Amount),

                ClosedGameSessionsCount = monthSessions.Count,
                ProductServiceOperationsCount = monthRecords.Count(record => record.Category == "Товары и услуги"),
                ShortageCount = monthLosses.Count
            };
        }

        public static List<EmployeeJournalInfo> GetJournalForCurrentMonth(string employeeName)
        {
            DateTime monthStart = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            return GetJournalForMonth(employeeName, monthStart);
        }

        public static List<EmployeeJournalInfo> GetJournalForMonth(string employeeName, DateTime monthStart)
        {
            monthStart = new DateTime(monthStart.Year, monthStart.Month, 1);
            DateTime nextMonthStart = monthStart.AddMonths(1);

            var result = new List<EmployeeJournalInfo>();

            AddShiftJournal(employeeName, monthStart, nextMonthStart, result);
            AddGameSessionJournal(employeeName, monthStart, nextMonthStart, result);
            AddCashJournal(employeeName, monthStart, nextMonthStart, result);
            AddShortageJournal(employeeName, monthStart, nextMonthStart, result);

            return result
                .OrderByDescending(item => item.CreatedAt)
                .ToList();
        }

        private static void AddShiftJournal(
            string employeeName,
            DateTime fromInclusive,
            DateTime toExclusive,
            List<EmployeeJournalInfo> result)
        {
            var shifts = ActionLogService.GetAllShifts()
                .Where(shift =>
                    shift.EmployeeName == employeeName &&
                    shift.StartedAt >= fromInclusive &&
                    shift.StartedAt < toExclusive)
                .ToList();

            foreach (var shift in shifts)
            {
                result.Add(new EmployeeJournalInfo
                {
                    CreatedAt = shift.StartedAt,
                    Type = "Смена",
                    Title = "Смена открыта",
                    Description = $"Сотрудник {employeeName} открыл смену."
                });

                if (shift.ClosedAt != null)
                {
                    result.Add(new EmployeeJournalInfo
                    {
                        CreatedAt = shift.ClosedAt.Value,
                        Type = "Смена",
                        Title = "Смена закрыта",
                        Description =
                            $"Сотрудник {employeeName} закрыл смену.\n" +
                            $"Длительность: {FormatTime(shift.ClosedAt.Value - shift.StartedAt)}"
                    });
                }
            }
        }

        private static void AddGameSessionJournal(
            string employeeName,
            DateTime fromInclusive,
            DateTime toExclusive,
            List<EmployeeJournalInfo> result)
        {
            var sessions = ActionLogService.GetAllGameSessions();

            foreach (var session in sessions)
            {
                if (session.StartedByEmployeeName == employeeName &&
                    session.StartedAt >= fromInclusive &&
                    session.StartedAt < toExclusive)
                {
                    result.Add(new EmployeeJournalInfo
                    {
                        CreatedAt = session.StartedAt,
                        Type = "Игры",
                        Title = $"Открыл {session.PlaceName}",
                        Description =
                            $"Тариф: {session.TariffText}\n" +
                            $"Оплачено: {session.PaidAmount} сом"
                    });
                }

                if (session.IsClosed &&
                    session.ClosedAt != null &&
                    session.ClosedAt.Value >= fromInclusive &&
                    session.ClosedAt.Value < toExclusive &&
                    session.ClosedByEmployeeName == employeeName)
                {
                    result.Add(new EmployeeJournalInfo
                    {
                        CreatedAt = session.ClosedAt.Value,
                        Type = "Игры",
                        Title = $"Закрыл {session.PlaceName}",
                        Description =
                            $"Игра: {session.CashIncomeAmount} сом\n" +
                            $"Товары/услуги: {session.ProductsAndServicesAmount} сом\n" +
                            $"Итого: {session.TotalToPayAmount} сом",
                        Amount = session.TotalToPayAmount
                    });
                }

                if (session.IncomeEmployeeName == employeeName &&
                    session.IsClosed &&
                    session.ClosedAt != null &&
                    session.ClosedAt.Value >= fromInclusive &&
                    session.ClosedAt.Value < toExclusive)
                {
                    result.Add(new EmployeeJournalInfo
                    {
                        CreatedAt = session.ClosedAt.Value,
                        Type = "Выручка",
                        Title = $"Выручка за {session.PlaceName}",
                        Description =
                            $"Выручка относится к сотруднику: {employeeName}\n" +
                            $"Игра: {session.CashIncomeAmount} сом\n" +
                            $"Товары/услуги: {session.ProductsAndServicesAmount} сом",
                        Amount = session.TotalToPayAmount
                    });
                }

                foreach (var extra in session.ExtraLines)
                {
                    if (extra.EmployeeName != employeeName)
                        continue;

                    if (extra.CreatedAt < fromInclusive || extra.CreatedAt >= toExclusive)
                        continue;

                    result.Add(new EmployeeJournalInfo
                    {
                        CreatedAt = extra.CreatedAt,
                        Type = extra.Type,
                        Title = $"{extra.Type} • {session.PlaceName}",
                        Description = extra.Description,
                        Amount = extra.Amount
                    });
                }

                foreach (var sale in session.SaleLines)
                {
                    if (sale.EmployeeName != employeeName)
                        continue;

                    if (sale.CreatedAt < fromInclusive || sale.CreatedAt >= toExclusive)
                        continue;

                    result.Add(new EmployeeJournalInfo
                    {
                        CreatedAt = sale.CreatedAt,
                        Type = "Товар/услуга",
                        Title = $"Оформил {sale.ItemName}",
                        Description =
                            $"Место: {session.PlaceName}\n" +
                            $"Количество: {sale.Quantity}\n" +
                            $"Цена: {sale.UnitPrice} сом\n" +
                            $"Сумма: {sale.TotalAmount} сом",
                        Amount = sale.TotalAmount
                    });
                }
            }
        }

        private static void AddCashJournal(
            string employeeName,
            DateTime fromInclusive,
            DateTime toExclusive,
            List<EmployeeJournalInfo> result)
        {
            var records = CashService.Records
                .Where(record =>
                    record.CreatedAt >= fromInclusive &&
                    record.CreatedAt < toExclusive &&
                    record.EmployeeName == employeeName &&
                    record.Category != "Недостачи")
                .ToList();

            foreach (var record in records)
            {
                result.Add(new EmployeeJournalInfo
                {
                    CreatedAt = record.CreatedAt,
                    Type = record.Category,
                    Title = record.Title,
                    Description = record.Description,
                    Amount = record.Amount
                });
            }
        }

        private static void AddShortageJournal(
            string employeeName,
            DateTime fromInclusive,
            DateTime toExclusive,
            List<EmployeeJournalInfo> result)
        {
            var records = GetLossRecordsForEmployee(employeeName, fromInclusive, toExclusive);

            foreach (var record in records)
            {
                result.Add(new EmployeeJournalInfo
                {
                    CreatedAt = record.CreatedAt,
                    Type = "Потеря",
                    Title = record.Title,
                    Description = record.Description,
                    Amount = record.Amount
                });
            }
        }

        public static List<EmployeeShiftInfo> GetShifts(string employeeName)
        {
            return GetShifts(employeeName, DateTime.MinValue, DateTime.MaxValue);
        }

        public static List<EmployeeShiftInfo> GetShifts(
            string employeeName,
            DateTime fromInclusive,
            DateTime toExclusive)
        {
            return ActionLogService.GetAllShifts()
                .Where(shift =>
                    shift.EmployeeName == employeeName &&
                    shift.StartedAt < toExclusive &&
                    (shift.ClosedAt ?? DateTime.Now) >= fromInclusive)
                .OrderByDescending(shift => shift.StartedAt)
                .Select(shift =>
                {
                    DateTime startTime = shift.StartedAt;
                    DateTime endTime = shift.ClosedAt ?? DateTime.Now;

                    if (startTime < fromInclusive)
                        startTime = fromInclusive;

                    if (endTime > toExclusive)
                        endTime = toExclusive;

                    return new EmployeeShiftInfo
                    {
                        StartedAt = shift.StartedAt,
                        ClosedAt = shift.ClosedAt,
                        IsClosed = shift.IsClosed,
                        Duration = endTime > startTime
                            ? endTime - startTime
                            : TimeSpan.Zero
                    };
                })
                .ToList();
        }

        public static List<EmployeeDayIncome> GetDailyIncomeForCurrentMonth(string employeeName)
        {
            DateTime monthStart = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            return GetDailyIncomeForMonth(employeeName, monthStart);
        }

        public static List<EmployeeDayIncome> GetDailyIncomeForMonth(string employeeName, DateTime monthStart)
        {
            monthStart = new DateTime(monthStart.Year, monthStart.Month, 1);
            DateTime nextMonthStart = monthStart.AddMonths(1);

            var records = GetCashRecordsForEmployee(employeeName, monthStart, nextMonthStart)
                .Where(record => record.Category == "Игры" || record.Category == "Товары и услуги")
                .ToList();

            return records
                .GroupBy(record => record.CreatedAt.Date)
                .OrderByDescending(group => group.Key)
                .Select(group => new EmployeeDayIncome
                {
                    Date = group.Key,
                    GameIncome = group
                        .Where(record => record.Category == "Игры")
                        .Sum(record => record.Amount),
                    ProductsIncome = group
                        .Where(record => record.Category == "Товары и услуги")
                        .Sum(record => record.Amount)
                })
                .ToList();
        }

        public static List<EmployeeShortageInfo> GetShortagesForCurrentMonth(string employeeName)
        {
            DateTime monthStart = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            return GetShortagesForMonth(employeeName, monthStart);
        }

        public static List<EmployeeShortageInfo> GetShortagesForMonth(string employeeName, DateTime monthStart)
        {
            monthStart = new DateTime(monthStart.Year, monthStart.Month, 1);
            DateTime nextMonthStart = monthStart.AddMonths(1);

            var losses = GetLossRecordsForEmployee(employeeName, monthStart, nextMonthStart)
                .Select(record => new EmployeeShortageInfo
                {
                    CreatedAt = record.CreatedAt,
                    Title = record.Title,
                    LossType = record.LossType,
                    Description = record.Description,
                    Amount = record.Amount,
                    CheckedByEmployeeName = record.CheckedByEmployeeName,
                    IsPaid = record.IsPaid,
                    PaidAt = record.PaidAt
                })
                .ToList();

            var legacyCashShortages = GetShortageRecordsForEmployee(employeeName, monthStart, nextMonthStart)
                .Where(record => !losses.Any(loss =>
                    loss.Amount == record.Amount &&
                    loss.Title.Equals(record.Title, StringComparison.OrdinalIgnoreCase) &&
                    Math.Abs((loss.CreatedAt - record.CreatedAt).TotalSeconds) < 5))
                .Select(record => new EmployeeShortageInfo
                {
                    CreatedAt = record.CreatedAt,
                    Title = record.Title,
                    LossType = record.Title.Contains("налич", StringComparison.OrdinalIgnoreCase)
                        ? "Недостача наличных"
                        : "Недостача",
                    Description = record.Description,
                    Amount = record.Amount,
                    CheckedByEmployeeName = record.EmployeeName,
                    IsPaid = false,
                    PaidAt = null
                });

            return losses
                .Concat(legacyCashShortages)
                .OrderByDescending(item => item.CreatedAt)
                .ToList();
        }

        public static List<EmployeeGameSessionInfo> GetGameSessionsForCurrentMonth(string employeeName)
        {
            DateTime monthStart = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            return GetGameSessionsForMonth(employeeName, monthStart);
        }

        public static List<EmployeeGameSessionInfo> GetGameSessionsForMonth(string employeeName, DateTime monthStart)
        {
            monthStart = new DateTime(monthStart.Year, monthStart.Month, 1);
            DateTime nextMonthStart = monthStart.AddMonths(1);

            return GetGameSessions(employeeName, monthStart, nextMonthStart);
        }

        public static List<EmployeeProductServiceInfo> GetProductServicesForMonth(string employeeName, DateTime monthStart)
        {
            monthStart = new DateTime(monthStart.Year, monthStart.Month, 1);
            DateTime nextMonthStart = monthStart.AddMonths(1);

            return CashService.Records
                .Where(record =>
                    record.CreatedAt >= monthStart &&
                    record.CreatedAt < nextMonthStart &&
                    record.EmployeeName == employeeName &&
                    record.Category == "Товары и услуги")
                .OrderByDescending(record => record.CreatedAt)
                .Select(record => new EmployeeProductServiceInfo
                {
                    CreatedAt = record.CreatedAt,
                    Title = record.Title,
                    Description = record.Description,
                    Amount = record.Amount,
                    PlaceName = record.PlaceName
                })
                .ToList();
        }

        public static TimeSpan GetWorkTime(
            string employeeName,
            DateTime fromInclusive,
            DateTime toExclusive)
        {
            TimeSpan total = TimeSpan.Zero;

            var shifts = ActionLogService.GetAllShifts()
                .Where(shift =>
                    shift.EmployeeName == employeeName &&
                    shift.StartedAt < toExclusive &&
                    (shift.ClosedAt ?? DateTime.Now) >= fromInclusive)
                .ToList();

            foreach (var shift in shifts)
            {
                DateTime start = shift.StartedAt;
                DateTime end = shift.ClosedAt ?? DateTime.Now;

                if (start < fromInclusive)
                    start = fromInclusive;

                if (end > toExclusive)
                    end = toExclusive;

                if (end > start)
                    total += end - start;
            }

            return total;
        }

        private static List<CashRecord> GetCashRecordsForEmployee(
            string employeeName,
            DateTime fromInclusive,
            DateTime toExclusive)
        {
            return CashService.Records
                .Where(record =>
                    record.CreatedAt >= fromInclusive &&
                    record.CreatedAt < toExclusive &&
                    record.IncomeEmployeeName == employeeName &&
                    record.Category != "Недостачи")
                .ToList();
        }

        private static List<CashRecord> GetShortageRecordsForEmployee(
            string employeeName,
            DateTime fromInclusive,
            DateTime toExclusive)
        {
            return CashService.Records
                .Where(record =>
                    record.CreatedAt >= fromInclusive &&
                    record.CreatedAt < toExclusive &&
                    record.Category == "Недостачи" &&
                    record.IncomeEmployeeName == employeeName)
                .ToList();
        }

        private static List<EmployeeLossItem> GetLossRecordsForEmployee(
            string employeeName,
            DateTime fromInclusive,
            DateTime toExclusive)
        {
            return EmployeeLossService.GetByPeriod(fromInclusive, toExclusive)
                .Where(record =>
                    record.ResponsibleEmployeeName.Equals(employeeName, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        private static List<EmployeeGameSessionInfo> GetGameSessions(
            string employeeName,
            DateTime fromInclusive,
            DateTime toExclusive)
        {
            return ActionLogService.GetClosedGameSessions()
                .Where(session =>
                    session.ClosedAt != null &&
                    session.ClosedAt.Value >= fromInclusive &&
                    session.ClosedAt.Value < toExclusive &&
                    session.IncomeEmployeeName == employeeName)
                .OrderByDescending(session => session.ClosedAt)
                .Select(session => new EmployeeGameSessionInfo
                {
                    StartedAt = session.StartedAt,
                    ClosedAt = session.ClosedAt,
                    PlaceName = session.PlaceName,
                    TariffText = session.TariffText,
                    GameAmount = session.CashIncomeAmount,
                    ProductsAmount = session.ProductsAndServicesAmount,
                    ClosedByEmployeeName = session.ClosedByEmployeeName
                })
                .ToList();
        }

        public static string FormatTime(TimeSpan time)
        {
            int totalHours = (int)time.TotalHours;
            int minutes = time.Minutes;

            return $"{totalHours} ч {minutes} мин";
        }
    }
}
