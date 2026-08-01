using System;
using System.Collections.Generic;
using System.Linq;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public static class PayrollPaymentAllocator
    {
        public static IReadOnlyList<PayrollPaymentAllocation> AllocateFifo(
            IEnumerable<EmployeePayrollObligation> obligations,
            string employeeName,
            int amount)
        {
            employeeName = employeeName.Trim();
            if (string.IsNullOrWhiteSpace(employeeName))
                throw new ArgumentException("Employee is required.", nameof(employeeName));
            if (amount <= 0)
                throw new ArgumentOutOfRangeException(nameof(amount));

            var available = obligations
                .Where(item => item.EmployeeName.Equals(
                    employeeName,
                    StringComparison.OrdinalIgnoreCase))
                .Where(item => item.RemainingAmount > 0)
                .OrderBy(item => item.MonthKey, StringComparer.Ordinal)
                .ToList();
            int total = available.Sum(item => item.RemainingAmount);
            if (amount > total)
                throw new InvalidOperationException(
                    $"Payment {amount} exceeds outstanding salary {total}.");

            int remaining = amount;
            var result = new List<PayrollPaymentAllocation>();
            foreach (var obligation in available)
            {
                if (remaining == 0)
                    break;

                int allocated = Math.Min(remaining, obligation.RemainingAmount);
                obligation.PaidAmount += allocated;
                remaining -= allocated;
                result.Add(new PayrollPaymentAllocation
                {
                    EmployeeName = obligation.EmployeeName,
                    SourceMonthKey = obligation.MonthKey,
                    Amount = allocated
                });
            }

            return result;
        }
    }
}
