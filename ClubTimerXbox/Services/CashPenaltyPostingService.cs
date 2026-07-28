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
                if (!CashReconciliationService.TryGetFormalizedPosting(
                        assignment.ReconciliationId,
                        out string employeeName,
                        out int amount,
                        out int targetFormalizedAmount))
                {
                    continue;
                }

                string marker =
                    $"[cash-reconciliation:{assignment.ReconciliationId:N}:{targetFormalizedAmount}]";
                string description =
                    $"{source}\n" +
                    $"Сотрудник: {employeeName}\n" +
                    $"Сумма: {amount} сом\n" +
                    $"Основание: {assignment.Reason}\n" +
                    marker;

                bool cashRecordExists = CashService.Records.Any(record =>
                    record.Description.Contains(marker, StringComparison.Ordinal));
                if (!cashRecordExists)
                {
                    CashService.AddShortage(
                        checkedByEmployeeName: "Система",
                        responsibleEmployeeName: employeeName,
                        title: "Недостача кассы",
                        description: description,
                        amount: amount
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
                        isFixed: true
                    );
                }

                CashReconciliationService.MarkFormalizedPosted(
                    assignment.ReconciliationId,
                    targetFormalizedAmount
                );
            }
        }
    }
}
