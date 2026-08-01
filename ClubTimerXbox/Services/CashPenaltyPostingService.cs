using System;
using System.Collections.Generic;
using System.Linq;

namespace ClubTimerXbox.Services
{
    public static class CashPenaltyPostingService
    {
        public static void Recover()
        {
            Post(
                CashReconciliationService.GetUnpostedFormalizedAssignments(),
                "Восстановлено после незавершённой проводки кассы."
            );
        }

        public static void Post(
            IEnumerable<CashAccountingAssignment> assignments,
            string source)
        {
            foreach (var assignment in assignments.Where(item =>
                item.ReconciliationId != Guid.Empty))
            {
                bool hasAllocation = assignment.AllocationId != Guid.Empty;
                string employeeName;
                int amount;
                int targetFormalizedAmount;
                bool found = hasAllocation
                    ? CashReconciliationService.TryGetFormalizedPosting(
                        assignment.ReconciliationId,
                        assignment.AllocationId,
                        out employeeName,
                        out amount,
                        out targetFormalizedAmount)
                    : CashReconciliationService.TryGetFormalizedPosting(
                        assignment.ReconciliationId,
                        out employeeName,
                        out amount,
                        out targetFormalizedAmount);
                if (!found)
                {
                    continue;
                }

                string marker = hasAllocation
                    ? $"[cash-allocation:{assignment.AllocationId:N}]"
                    : $"[cash-reconciliation:{assignment.ReconciliationId:N}:{targetFormalizedAmount}]";
                string description =
                    $"{source}\n" +
                    $"Сотрудник: {employeeName}\n" +
                    $"Сумма: {amount} сом\n" +
                    $"Основание: {assignment.Reason}\n" +
                    marker;
                DateTime sourceTime = CashReconciliationService.Items
                    .FirstOrDefault(item => item.Id == assignment.ReconciliationId)
                    ?.CreatedAt ?? ClubClock.Current.LocalNow;
                string salaryMonthKey = BusinessCalendarService
                    .GetBusinessMonth(sourceTime)
                    .Key;

                bool cashRecordExists = CashService.Records.Any(record =>
                    record.Description.Contains(marker, StringComparison.Ordinal));
                if (!cashRecordExists)
                {
                    CashService.AddShortage(
                        checkedByEmployeeName: "Система",
                        responsibleEmployeeName: employeeName,
                        title: "Недостача кассы",
                        description: description,
                        amount: amount,
                        businessOccurredAt: sourceTime
                    );
                }

                bool employeeLossExists = EmployeeLossService.Items.Any(item =>
                    item.Description.Contains(marker, StringComparison.Ordinal));
                if (!employeeLossExists)
                {
                    EmployeeLossService.AddLoss(
                        responsibleEmployeeName: employeeName,
                        checkedByEmployeeName: "Система",
                        lossType: "Недостача кассы",
                        title: "Недостача кассы",
                        description: description,
                        amount: amount,
                        note: "Оформлено Конституционным движком кассы",
                        lossKind: "money",
                        isFixed: true,
                        salaryMonthKey: salaryMonthKey
                    );
                }

                if (hasAllocation)
                {
                    CashReconciliationService.MarkFormalizedPosted(
                        assignment.ReconciliationId,
                        assignment.AllocationId,
                        targetFormalizedAmount
                    );
                }
                else
                {
                    CashReconciliationService.MarkFormalizedPosted(
                        assignment.ReconciliationId,
                        targetFormalizedAmount
                    );
                }
            }
        }
    }
}
