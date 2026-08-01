using System;
using System.Collections.Generic;
using System.Linq;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public sealed class BusinessMonthCloseDeferredException : Exception
    {
        public BusinessMonthCloseDeferredException(string message)
            : base(message)
        {
        }
    }

    public static class BusinessMonthTransitionEngine
    {
        public static BusinessMonthCloseJournal CloseMonth(
            BusinessLedgerState state,
            string monthKey,
            DateTime now,
            Action<BusinessMonthCloseStep>? afterStep = null)
        {
            ArgumentNullException.ThrowIfNull(state);
            BusinessCalendarService.GetBusinessMonthByKey(monthKey);

            string operationId = $"month-close:{monthKey}";
            if (!state.Months.TryGetValue(monthKey, out BusinessMonthLedger? month))
            {
                month = new BusinessMonthLedger { MonthKey = monthKey };
                state.Months[monthKey] = month;
            }

            if (!state.CloseJournal.TryGetValue(operationId, out BusinessMonthCloseJournal? journal))
            {
                journal = new BusinessMonthCloseJournal
                {
                    OperationId = operationId,
                    MonthKey = monthKey,
                    StartedAt = now,
                    LastCompletedStep = BusinessMonthCloseStep.None
                };
                state.CloseJournal[operationId] = journal;
            }

            if (journal.LastCompletedStep >= BusinessMonthCloseStep.Completed)
                return journal;

            CompleteStep(journal, BusinessMonthCloseStep.Prepared, afterStep);

            if (journal.LastCompletedStep < BusinessMonthCloseStep.CashInvestigationsFinalized)
            {
                try
                {
                    FinalizeCashInvestigations(month);
                }
                catch (BusinessMonthCloseDeferredException exception)
                {
                    journal.IsDeferred = true;
                    journal.DeferredReason = exception.Message;
                    return journal;
                }

                journal.IsDeferred = false;
                journal.DeferredReason = "";
                CompleteStep(
                    journal,
                    BusinessMonthCloseStep.CashInvestigationsFinalized,
                    afterStep);
            }

            CompleteStep(journal, BusinessMonthCloseStep.SalarySnapshotCreated, afterStep);

            if (journal.LastCompletedStep < BusinessMonthCloseStep.ProfitSnapshotCreated)
            {
                int payrollExpense = month.Payroll.Sum(item =>
                    Math.Max(0, item.AccruedAmount + item.BonusAmount - item.PenaltyAmount));
                month.ClosedNetProfit =
                    month.GameRevenue +
                    month.ProductRevenue -
                    month.ProductCostOfGoodsSold +
                    month.ServiceRevenue +
                    month.OtherRevenue -
                    month.ClubExpenses -
                    payrollExpense;
                CompleteStep(
                    journal,
                    BusinessMonthCloseStep.ProfitSnapshotCreated,
                    afterStep);
            }

            if (journal.LastCompletedStep < BusinessMonthCloseStep.CarryForwardCreated)
            {
                state.RetainedOwnerIncome +=
                    month.ClosedNetProfit - month.ProfitIncludedAtActivation;
                CompleteStep(
                    journal,
                    BusinessMonthCloseStep.CarryForwardCreated,
                    afterStep);
            }

            if (journal.LastCompletedStep < BusinessMonthCloseStep.Completed)
            {
                month.IsClosed = true;
                journal.CompletedAt = now;
                CompleteStep(journal, BusinessMonthCloseStep.Completed, afterStep);
            }

            return journal;
        }

        private static void FinalizeCashInvestigations(BusinessMonthLedger month)
        {
            int net = month.ExtraReserve - month.UnknownCashShortage;
            if (net >= 0)
            {
                month.ArchivedExtra += net;
                month.ExtraReserve = 0;
                month.UnknownCashShortage = 0;
                return;
            }

            int shortage = Math.Abs(net);
            double totalHours = month.WorkedHours
                .Where(item => item.Value > 0)
                .Sum(item => item.Value);
            if (totalHours <= 0)
                throw new BusinessMonthCloseDeferredException(
                    "Unknown shortage cannot be allocated without worked hours.");

            var employees = month.WorkedHours
                .Where(item => item.Value > 0)
                .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();
            int assigned = 0;
            for (int index = 0; index < employees.Count; index++)
            {
                var employee = employees[index];
                int amount = index == employees.Count - 1
                    ? shortage - assigned
                    : (int)Math.Floor(shortage * employee.Value / totalHours);
                assigned += amount;
                if (amount <= 0)
                    continue;

                EmployeePayrollObligation? obligation = month.Payroll.FirstOrDefault(item =>
                    item.EmployeeName.Equals(employee.Key, StringComparison.OrdinalIgnoreCase));
                if (obligation == null)
                {
                    obligation = new EmployeePayrollObligation
                    {
                        EmployeeName = employee.Key,
                        MonthKey = month.MonthKey
                    };
                    month.Payroll.Add(obligation);
                }
                obligation.PenaltyAmount += amount;
            }

            month.ExtraReserve = 0;
            month.UnknownCashShortage = 0;
        }

        private static void CompleteStep(
            BusinessMonthCloseJournal journal,
            BusinessMonthCloseStep step,
            Action<BusinessMonthCloseStep>? afterStep)
        {
            if (journal.LastCompletedStep >= step)
                return;

            journal.LastCompletedStep = step;
            afterStep?.Invoke(step);
        }
    }
}
