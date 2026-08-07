#if DEBUG
using System;
using System.Collections.Generic;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public sealed class BusinessScenarioHarness : IDisposable
    {
        private readonly IDisposable _clockScope;

        public BusinessScenarioHarness(DateTime localNow)
        {
            Clock = new ManualClubClock(localNow);
            _clockScope = ClubClock.UseForTesting(Clock);
        }

        public ManualClubClock Clock { get; }

        public BusinessLedgerState State { get; } = new();

        public bool UsesFirebase => false;

        public bool UsesApplicationData => false;

        public BusinessScenarioHarness SetMoney(int cash, int cashless)
        {
            State.CashBalance = Math.Max(0, cash);
            State.CashlessBalance = Math.Max(0, cashless);
            return this;
        }

        public BusinessMonthLedger Month(string monthKey)
        {
            BusinessCalendarService.GetBusinessMonthByKey(monthKey);
            if (!State.Months.TryGetValue(monthKey, out var month))
            {
                month = new BusinessMonthLedger { MonthKey = monthKey };
                State.Months[monthKey] = month;
            }
            return month;
        }

        public BusinessScenarioHarness AddSalary(
            string monthKey,
            string employeeName,
            int accrued,
            int penalties = 0,
            int paid = 0)
        {
            Month(monthKey).Payroll.Add(new EmployeePayrollObligation
            {
                EmployeeName = employeeName,
                MonthKey = monthKey,
                AccruedAmount = accrued,
                PenaltyAmount = penalties,
                PaidAmount = paid
            });
            return this;
        }

        public BusinessMonthCloseJournal CloseMonth(
            string monthKey,
            BusinessMonthCloseStep? interruptAfter = null)
        {
            return BusinessMonthTransitionEngine.CloseMonth(
                State,
                monthKey,
                Clock.LocalNow,
                step =>
                {
                    if (interruptAfter == step)
                        throw new InvalidOperationException($"Interrupted after {step}.");
                });
        }

        public void Advance(TimeSpan value)
        {
            Clock.Advance(value);
        }

        public void SetLocalTime(DateTime value)
        {
            Clock.SetLocal(value);
        }

        public IReadOnlyList<PayrollPaymentAllocation> PaySalary(
            string employeeName,
            int amount,
            string paymentMethod = "Наличные")
        {
            var obligations = new List<EmployeePayrollObligation>();
            foreach (var month in State.Months.Values)
                obligations.AddRange(month.Payroll);
            var allocations = PayrollPaymentAllocator.AllocateFifo(
                obligations,
                employeeName,
                amount);
            if (paymentMethod == "Безнал")
            {
                if (amount > State.CashlessBalance)
                    throw new InvalidOperationException("Insufficient cashless balance.");
                State.CashlessBalance -= amount;
            }
            else
            {
                if (amount > State.CashBalance)
                    throw new InvalidOperationException("Insufficient cash balance.");
                State.CashBalance -= amount;
            }
            return allocations;
        }

        public void WithdrawOwnerIncome(int amount, string paymentMethod = "Наличные")
        {
            if (amount <= 0)
                throw new InvalidOperationException("Owner withdrawal must be positive.");

            if (paymentMethod == "Безнал")
            {
                if (amount > State.CashlessBalance)
                    throw new InvalidOperationException("Insufficient cashless balance.");
                State.CashlessBalance -= amount;
            }
            else
            {
                if (amount > State.CashBalance)
                    throw new InvalidOperationException("Insufficient cash balance.");
                State.CashBalance -= amount;
            }
            State.RetainedOwnerIncome -= amount;
        }

        public void Dispose()
        {
            _clockScope.Dispose();
        }
    }
}
#endif
