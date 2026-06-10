using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public static class CashReportService
    {
        private class AllocatedReportLine
        {
            public DateTime CreatedAt { get; set; }

            public string OperationTitle { get; set; } = "";

            public string ItemName { get; set; } = "";

            public int Quantity { get; set; }

            public int UnitPrice { get; set; }

            public string Category { get; set; } = "";

            public string EmployeeName { get; set; } = "";

            public string PlaceName { get; set; } = "";

            public int TotalAmount { get; set; }

            public int CashAmount { get; set; }

            public int MBankAmount { get; set; }
        }

        public static CashReportResult BuildReport(CashReportFilter filter)
        {
            var range = GetDateRange(filter);

            var records = PaymentService.Records
                .Where(record =>
                    record.CreatedAt >= range.FromInclusive &&
                    record.CreatedAt < range.ToExclusive)
                .OrderByDescending(record => record.CreatedAt)
                .ToList();

            var lines = records
                .SelectMany(record => BuildAllocatedLines(record))
                .Where(line => IsLineInSection(line, filter.Section))
                .OrderByDescending(line => line.CreatedAt)
                .ToList();

            var result = new CashReportResult
            {
                Summary = BuildSummary(lines, range)
            };

            result.Rows = BuildRows(lines, filter);

            return result;
        }

        public static DateTime GetPeriodStart(CashReportFilter filter)
        {
            return GetDateRange(filter).FromInclusive;
        }

        public static DateTime GetPeriodEndExclusive(CashReportFilter filter)
        {
            return GetDateRange(filter).ToExclusive;
        }

        private static (DateTime FromInclusive, DateTime ToExclusive, string Title) GetDateRange(
            CashReportFilter filter)
        {
            if (filter.PeriodMode == CashReportPeriodMode.Month)
            {
                var from = new DateTime(filter.SelectedYear, filter.SelectedMonth, 1);
                var to = from.AddMonths(1);

                return (
                    from,
                    to,
                    GetMonthTitle(from)
                );
            }

            if (filter.PeriodMode == CashReportPeriodMode.CustomPeriod)
            {
                DateTime from = filter.PeriodStart.Date;
                DateTime end = filter.PeriodEnd.Date;

                if (end < from)
                {
                    DateTime temp = from;
                    from = end;
                    end = temp;
                }

                DateTime to = end.AddDays(1);

                return (
                    from,
                    to,
                    $"{from:dd.MM.yyyy}–{end:dd.MM.yyyy}"
                );
            }

            DateTime day = filter.SelectedDay.Date;

            return (
                day,
                day.AddDays(1),
                day.ToString("dd.MM.yyyy")
            );
        }

        private static string GetMonthTitle(DateTime month)
        {
            var culture = new CultureInfo("ru-RU");
            string monthName = culture.DateTimeFormat.GetMonthName(month.Month);

            if (string.IsNullOrWhiteSpace(monthName))
                monthName = month.Month.ToString("00");

            monthName = char.ToUpper(monthName[0]) + monthName.Substring(1);

            return $"{monthName} {month.Year}";
        }

        private static CashReportSummary BuildSummary(
            List<AllocatedReportLine> lines,
            (DateTime FromInclusive, DateTime ToExclusive, string Title) range)
        {
            return new CashReportSummary
            {
                Title = $"Итог за {range.Title}",
                TotalAmount = lines.Sum(line => line.TotalAmount),
                CashAmount = lines.Sum(line => line.CashAmount),
                MBankAmount = lines.Sum(line => line.MBankAmount),
                RecordsCount = lines.Count
            };
        }

        private static List<CashReportRow> BuildRows(
            List<AllocatedReportLine> lines,
            CashReportFilter filter)
        {
            if (filter.ViewMode == CashReportViewMode.Days)
                return BuildRowsByDays(lines);

            if (filter.ViewMode == CashReportViewMode.Places)
                return BuildRowsByPlaces(lines);

            if (filter.ViewMode == CashReportViewMode.Items)
                return BuildRowsByItems(lines);

            if (filter.ViewMode == CashReportViewMode.Employees)
                return BuildRowsByEmployees(lines);

            if (filter.ViewMode == CashReportViewMode.Categories)
                return BuildRowsByCategories(lines);

            return BuildRecordRows(lines);
        }

        private static List<AllocatedReportLine> BuildAllocatedLines(PaymentRecord record)
        {
            var result = new List<AllocatedReportLine>();

            if (record.Items == null || record.Items.Count == 0)
                return result;

            int cashLeft = record.CashAmount;
            int mBankLeft = record.MBankAmount;

            var items = record.Items
                .Where(item => item.TotalAmount > 0)
                .ToList();

            // Сначала товары, потом услуги, потом всё остальное кроме игр.
            // Цель: не дробить товар/услугу, если один способ оплаты может покрыть позицию целиком.
            var fixedItems = items
                .Where(item => !IsGameItem(item))
                .OrderBy(item => GetFixedItemPriority(item))
                .ThenByDescending(item => item.TotalAmount)
                .ToList();

            foreach (var item in fixedItems)
            {
                var allocation = AllocateFixedItem(item.TotalAmount, ref cashLeft, ref mBankLeft);

                result.Add(CreateLine(
                    record,
                    item,
                    allocation.CashAmount,
                    allocation.MBankAmount
                ));
            }

            // Игры можно дробить: остатки наличных и М Банка распределяем по игровым строкам.
            var gameItems = items
                .Where(IsGameItem)
                .ToList();

            foreach (var item in gameItems)
            {
                var allocation = AllocateFlexibleItem(item.TotalAmount, ref cashLeft, ref mBankLeft);

                result.Add(CreateLine(
                    record,
                    item,
                    allocation.CashAmount,
                    allocation.MBankAmount
                ));
            }

            // На всякий случай, если из-за старых данных остались копейки/суммы,
            // добавляем их к последней строке, чтобы итог строк совпадал с чеком.
            FixRoundingDifference(record, result);

            return result
                .OrderBy(line => IsGameCategory(line.Category) ? 2 : 1)
                .ThenBy(line => line.ItemName)
                .ToList();
        }

        private static (int CashAmount, int MBankAmount) AllocateFixedItem(
            int amount,
            ref int cashLeft,
            ref int mBankLeft)
        {
            if (amount <= 0)
                return (0, 0);

            // Если оба способа могут закрыть позицию целиком,
            // отдаём её туда, где остаток больше. Так товары/услуги остаются цельными.
            if (cashLeft >= amount && mBankLeft >= amount)
            {
                if (cashLeft >= mBankLeft)
                {
                    cashLeft -= amount;
                    return (amount, 0);
                }

                mBankLeft -= amount;
                return (0, amount);
            }

            if (cashLeft >= amount)
            {
                cashLeft -= amount;
                return (amount, 0);
            }

            if (mBankLeft >= amount)
            {
                mBankLeft -= amount;
                return (0, amount);
            }

            // Если ни один способ не покрывает позицию целиком,
            // только тогда дробим.
            int cashPart = Math.Min(cashLeft, amount);
            cashLeft -= cashPart;

            int rest = amount - cashPart;

            int mBankPart = Math.Min(mBankLeft, rest);
            mBankLeft -= mBankPart;

            return (cashPart, mBankPart);
        }

        private static (int CashAmount, int MBankAmount) AllocateFlexibleItem(
            int amount,
            ref int cashLeft,
            ref int mBankLeft)
        {
            if (amount <= 0)
                return (0, 0);

            int cashPart = Math.Min(cashLeft, amount);
            cashLeft -= cashPart;

            int rest = amount - cashPart;

            int mBankPart = Math.Min(mBankLeft, rest);
            mBankLeft -= mBankPart;

            return (cashPart, mBankPart);
        }

        private static void FixRoundingDifference(
            PaymentRecord record,
            List<AllocatedReportLine> lines)
        {
            if (lines.Count == 0)
                return;

            int total = lines.Sum(line => line.TotalAmount);
            int cash = lines.Sum(line => line.CashAmount);
            int mBank = lines.Sum(line => line.MBankAmount);

            int totalDiff = record.TotalAmount - total;
            int cashDiff = record.CashAmount - cash;
            int mBankDiff = record.MBankAmount - mBank;

            var last = lines[lines.Count - 1];

            if (totalDiff != 0)
                last.TotalAmount += totalDiff;

            if (cashDiff != 0)
                last.CashAmount += cashDiff;

            if (mBankDiff != 0)
                last.MBankAmount += mBankDiff;
        }

        private static AllocatedReportLine CreateLine(
            PaymentRecord record,
            CheckoutItem item,
            int cashAmount,
            int mBankAmount)
        {
            return new AllocatedReportLine
            {
                CreatedAt = record.CreatedAt,
                OperationTitle = record.OperationTitle,
                ItemName = item.Name,
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice,
                Category = NormalizeCategory(item),
                EmployeeName = record.EmployeeName,
                PlaceName = record.PlaceName,
                TotalAmount = item.TotalAmount,
                CashAmount = cashAmount,
                MBankAmount = mBankAmount
            };
        }

        private static int GetFixedItemPriority(CheckoutItem item)
        {
            string category = NormalizeCategory(item);

            if (category == "Товар")
                return 1;

            if (category == "Услуга")
                return 2;

            if (category == "Товары/услуги")
                return 3;

            return 4;
        }

        private static bool IsGameItem(CheckoutItem item)
        {
            string category = NormalizeCategory(item);

            if (IsGameCategory(category))
                return true;

            return item.Name.Contains("Игровое время") ||
                   item.Name.Contains("Открытый режим") ||
                   item.Name.Contains("Добавить время");
        }

        private static bool IsGameCategory(string category)
        {
            return category == "Игры";
        }

        private static string NormalizeCategory(CheckoutItem item)
        {
            if (item == null)
                return "Другое";

            if (item.Category == "Игры")
                return "Игры";

            if (item.Category == "Товар")
                return "Товар";

            if (item.Category == "Услуга")
                return "Услуга";

            if (item.Category == "Товары и услуги")
                return "Товары/услуги";

            if (item.Category == "Расходы")
                return "Расходы";

            if (item.Name.Contains("Игровое время") ||
                item.Name.Contains("Открытый режим") ||
                item.Name.Contains("Добавить время"))
                return "Игры";

            return string.IsNullOrWhiteSpace(item.Category)
                ? "Другое"
                : item.Category;
        }

        private static List<CashReportRow> BuildRecordRows(List<AllocatedReportLine> lines)
        {
            return lines
                .OrderByDescending(line => line.CreatedAt)
                .Select(line =>
                {
                    string subtitle =
                        $"{line.ItemName} × {line.Quantity} = {line.TotalAmount} сом";

                    if (!string.IsNullOrWhiteSpace(line.PlaceName))
                        subtitle += $"\nОформлено на {line.PlaceName}";

                    return new CashReportRow
                    {
                        Title = line.OperationTitle,
                        Subtitle = subtitle,
                        TimeText = line.CreatedAt.ToString("dd.MM.yyyy HH:mm"),
                        TotalAmount = line.TotalAmount,
                        CashAmount = line.CashAmount,
                        MBankAmount = line.MBankAmount,
                        EmployeeName = line.EmployeeName,
                        PlaceName = line.PlaceName,
                        Category = line.Category
                    };
                })
                .ToList();
        }

        private static List<CashReportRow> BuildRowsByDays(List<AllocatedReportLine> lines)
        {
            return lines
                .GroupBy(line => line.CreatedAt.Date)
                .OrderByDescending(group => group.Key)
                .Select(group => new CashReportRow
                {
                    Title = group.Key.ToString("dd.MM.yyyy"),
                    Subtitle = $"Записей: {group.Count()}",
                    TimeText = "",
                    TotalAmount = group.Sum(line => line.TotalAmount),
                    CashAmount = group.Sum(line => line.CashAmount),
                    MBankAmount = group.Sum(line => line.MBankAmount)
                })
                .ToList();
        }

        private static List<CashReportRow> BuildRowsByPlaces(List<AllocatedReportLine> lines)
        {
            return lines
                .Where(line => !string.IsNullOrWhiteSpace(line.PlaceName))
                .GroupBy(line => line.PlaceName)
                .OrderBy(group => GetPlaceSortNumber(group.Key))
                .ThenBy(group => group.Key)
                .Select(group => new CashReportRow
                {
                    Title = group.Key,
                    Subtitle = $"Записей: {group.Count()}",
                    TotalAmount = group.Sum(line => line.TotalAmount),
                    CashAmount = group.Sum(line => line.CashAmount),
                    MBankAmount = group.Sum(line => line.MBankAmount),
                    PlaceName = group.Key
                })
                .ToList();
        }

        private static int GetPlaceSortNumber(string placeName)
        {
            if (string.IsNullOrWhiteSpace(placeName))
                return 9999;

            string digits = new string(placeName.Where(char.IsDigit).ToArray());

            if (int.TryParse(digits, out int number))
                return number;

            return 9999;
        }

        private static List<CashReportRow> BuildRowsByItems(List<AllocatedReportLine> lines)
        {
            return lines
                .GroupBy(line => line.ItemName)
                .OrderByDescending(group => group.Sum(line => line.TotalAmount))
                .Select(group => new CashReportRow
                {
                    Title = group.Key,
                    Subtitle = $"Операций: {group.Count()}",
                    TotalAmount = group.Sum(line => line.TotalAmount),
                    CashAmount = group.Sum(line => line.CashAmount),
                    MBankAmount = group.Sum(line => line.MBankAmount),
                    Category = group.First().Category
                })
                .ToList();
        }

        private static List<CashReportRow> BuildRowsByEmployees(List<AllocatedReportLine> lines)
        {
            return lines
                .GroupBy(line =>
                    string.IsNullOrWhiteSpace(line.EmployeeName)
                        ? "Неизвестно"
                        : line.EmployeeName)
                .OrderByDescending(group => group.Sum(line => line.TotalAmount))
                .Select(group => new CashReportRow
                {
                    Title = group.Key,
                    Subtitle = $"Записей: {group.Count()}",
                    TotalAmount = group.Sum(line => line.TotalAmount),
                    CashAmount = group.Sum(line => line.CashAmount),
                    MBankAmount = group.Sum(line => line.MBankAmount),
                    EmployeeName = group.Key
                })
                .ToList();
        }

        private static List<CashReportRow> BuildRowsByCategories(List<AllocatedReportLine> lines)
        {
            return lines
                .GroupBy(line => line.Category)
                .OrderByDescending(group => group.Sum(line => line.TotalAmount))
                .Select(group => new CashReportRow
                {
                    Title = group.Key,
                    Subtitle = $"Записей: {group.Count()}",
                    TotalAmount = group.Sum(line => line.TotalAmount),
                    CashAmount = group.Sum(line => line.CashAmount),
                    MBankAmount = group.Sum(line => line.MBankAmount),
                    Category = group.Key
                })
                .ToList();
        }

        private static bool IsLineInSection(AllocatedReportLine line, CashReportSection section)
        {
            if (section == CashReportSection.Games)
                return line.Category == "Игры";

            if (section == CashReportSection.ProductsAndServices)
            {
                return line.Category == "Товар" ||
                       line.Category == "Услуга" ||
                       line.Category == "Товары/услуги";
            }

            if (section == CashReportSection.Employees)
                return !string.IsNullOrWhiteSpace(line.EmployeeName);

            if (section == CashReportSection.Expenses)
                return line.Category == "Расходы";

            return true;
        }
    }
}
