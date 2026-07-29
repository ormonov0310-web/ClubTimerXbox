using System;
using System.Collections.Generic;
using System.Linq;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public static class CashlessService
    {
        private static readonly List<CashlessDayRecord> _records =
            CashlessStorageService.Load();

        public static IReadOnlyList<CashlessDayRecord> Records => _records;

        public static int GetAmountForDate(DateTime date)
        {
            DateTime targetDate = date.Date;

            var record = _records.FirstOrDefault(item => item.Date.Date == targetDate);

            if (record == null)
                return 0;

            return record.Amount;
        }

        public static int GetAmountForToday()
        {
            return GetAmountForDate(DateTime.Today);
        }

        public static void SetAmountForDate(
            DateTime date,
            int amount,
            string note = "",
            int? expectedAmount = null)
        {
            if (amount < 0)
                amount = 0;

            if (expectedAmount.HasValue && expectedAmount.Value < 0)
                expectedAmount = 0;

            DateTime targetDate = date.Date;

            var record = _records.FirstOrDefault(item => item.Date.Date == targetDate);

            if (record == null)
            {
                record = new CashlessDayRecord
                {
                    Date = targetDate
                };

                _records.Add(record);
            }

            record.Amount = amount;
            record.ExpectedAmount = expectedAmount;
            record.Note = note;
            record.UpdatedAt = DateTime.Now;

            Save();
        }

        public static void SetAmountForToday(
            int amount,
            string note = "",
            int? expectedAmount = null)
        {
            SetAmountForDate(DateTime.Today, amount, note, expectedAmount);
        }

        public static void SetAmountForTodayIfNotNewerThan(
            DateTime committedAt,
            int amount,
            string note,
            int? expectedAmount)
        {
            var current = _records.FirstOrDefault(item =>
                item.Date.Date == DateTime.Today);
            if (current != null &&
                (current.UpdatedAt > committedAt ||
                 (current.Amount == Math.Max(0, amount) &&
                  current.ExpectedAmount == expectedAmount &&
                  string.Equals(current.Note, note, StringComparison.Ordinal))))
            {
                return;
            }

            SetAmountForToday(amount, note, expectedAmount);
        }

        public static int GetExpectedCashForToday()
        {
            int totalCash = CashService.GetCashIncomeTotalByPeriod(
                DateTime.Today,
                DateTime.Today.AddDays(1)
            );

            int cashless = GetAmountForToday();

            int expectedCash = totalCash - cashless;

            if (expectedCash < 0)
                expectedCash = 0;

            return expectedCash;
        }

        public static int GetAmountByPeriod(DateTime fromInclusive, DateTime toExclusive)
        {
            return _records
                .Where(record =>
                    record.Date >= fromInclusive.Date &&
                    record.Date < toExclusive.Date)
                .Sum(record => record.Amount);
        }

        public static int? GetLatestAmountByPeriod(DateTime fromInclusive, DateTime toExclusive)
        {
            var record = _records
                .Where(item =>
                    item.Date >= fromInclusive.Date &&
                    item.Date < toExclusive.Date)
                .OrderByDescending(item => item.Date)
                .ThenByDescending(item => item.UpdatedAt)
                .FirstOrDefault();

            return record?.Amount;
        }

        public static void Clear()
        {
            _records.Clear();
            CashlessStorageService.Clear();
        }

        private static void Save()
        {
            CashlessStorageService.Save(_records);
        }
    }
}
