using System;
using System.Collections.Generic;
using System.Linq;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public static class PaymentService
    {
        private static readonly List<PaymentRecord> _records = PaymentStorageService.Load();

        public static IReadOnlyList<PaymentRecord> Records => _records;

        public static int RenameEmployeeReferences(
            string oldEmployeeName,
            string newEmployeeName)
        {
            int changed = 0;

            foreach (var record in _records)
            {
                if (!EmployeeReferenceRenameService.Matches(record.EmployeeName, oldEmployeeName))
                    continue;

                record.EmployeeName = newEmployeeName;
                record.OperationTitle = EmployeeReferenceRenameService.RenameText(
                    record.OperationTitle,
                    oldEmployeeName,
                    newEmployeeName);
                record.Comment = EmployeeReferenceRenameService.RenameText(
                    record.Comment,
                    oldEmployeeName,
                    newEmployeeName);
                changed++;
            }

            if (changed > 0)
                Save();

            return changed;
        }

        public static void AddPayment(PaymentRecord record)
        {
            if (record == null)
                return;

            if (record.TotalAmount == 0)
                return;

            if (record.TotalAmount > 0)
            {
                if (record.CashAmount < 0)
                    record.CashAmount = 0;

                if (record.MBankAmount < 0)
                    record.MBankAmount = 0;
            }
            else
            {
                if (record.CashAmount > 0)
                    record.CashAmount = -record.CashAmount;

                if (record.MBankAmount > 0)
                    record.MBankAmount = -record.MBankAmount;
            }

            int paymentTotal = record.CashAmount + record.MBankAmount;

            if (paymentTotal != record.TotalAmount)
                return;

            record.CreatedAt = DateTime.Now;

            _records.Add(record);

            Save();
        }

        private static void Save()
        {
            PaymentStorageService.Save(_records);
        }

        public static int GetTotalByPeriod(DateTime fromInclusive, DateTime toExclusive)
        {
            return _records
                .Where(record =>
                    record.CreatedAt >= fromInclusive &&
                    record.CreatedAt < toExclusive)
                .Sum(record => record.TotalAmount);
        }

        public static int GetCashTotalByPeriod(DateTime fromInclusive, DateTime toExclusive)
        {
            return _records
                .Where(record =>
                    record.CreatedAt >= fromInclusive &&
                    record.CreatedAt < toExclusive)
                .Sum(record => record.CashAmount);
        }

        public static int GetMBankTotalByPeriod(DateTime fromInclusive, DateTime toExclusive)
        {
            return _records
                .Where(record =>
                    record.CreatedAt >= fromInclusive &&
                    record.CreatedAt < toExclusive)
                .Sum(record => record.MBankAmount);
        }

        public static List<PaymentRecord> GetRecordsByPeriod(DateTime fromInclusive, DateTime toExclusive)
        {
            return _records
                .Where(record =>
                    record.CreatedAt >= fromInclusive &&
                    record.CreatedAt < toExclusive)
                .OrderByDescending(record => record.CreatedAt)
                .ToList();
        }

        public static void Clear()
        {
            _records.Clear();
            PaymentStorageService.Clear();
        }
    }
}
