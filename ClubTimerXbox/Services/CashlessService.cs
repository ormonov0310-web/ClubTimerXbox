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
            return GetAmountForDate(BusinessCalendarService.GetBusinessDate(
                ClubClock.Current.LocalNow));
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
            record.UpdatedAt = ClubClock.Current.LocalNow;

            Save();
        }

        public static void SetAmountForToday(
            int amount,
            string note = "",
            int? expectedAmount = null)
        {
            SetAmountForDate(
                BusinessCalendarService.GetBusinessDate(ClubClock.Current.LocalNow),
                amount,
                note,
                expectedAmount);
        }

        public static void ApplyCommittedCorrection(
            DateTime committedAt,
            int amount,
            string note,
            int? expectedAmount)
        {
            var current = _records.FirstOrDefault(item =>
                item.Date.Date == BusinessCalendarService.GetBusinessDate(committedAt));
            var replacement = CashlessCheckpointPolicy.Build(current, committedAt, amount, note, expectedAmount);
            if (replacement == null)
                return;
            int index = current == null ? -1 : _records.IndexOf(current);
            if (index < 0) _records.Add(replacement);
            else _records[index] = replacement;
            try { Save(); }
            catch
            {
                if (index < 0) _records.Remove(replacement);
                else _records[index] = current!;
                throw;
            }
        }

        public static int GetExpectedCashForToday()
        {
            int totalCash = CashService.GetCashIncomeTotalByPeriod(
                BusinessCalendarService.GetBusinessDay(ClubClock.Current.LocalNow).StartInclusive,
                BusinessCalendarService.GetBusinessDay(ClubClock.Current.LocalNow).EndExclusive
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
