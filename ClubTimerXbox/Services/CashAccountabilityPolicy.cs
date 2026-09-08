using System;
using System.Collections.Generic;
using System.Linq;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public sealed record CashEmployeeRecommendation(string EmployeeName, string Type, int Amount);

    public static class CashAccountabilityPolicy
    {
        public static IReadOnlyList<CashEmployeeRecommendation> Recommendations(
            IEnumerable<CashReconciliationItem> items, DateTime from, DateTime to,
            CashAcceptanceItem? provisional = null)
        {
            var rows = items.Where(item => !item.IsTechnicalEvent &&
                    item.Status == CashReconciliationStatus.Open && item.Amount > 0 &&
                    item.CreatedAt >= from && item.CreatedAt < to &&
                    (item.Kind == CashReconciliationKind.CashShortage ||
                     item.Kind == CashReconciliationKind.CashlessShortage))
                .Select(item => new CashEmployeeRecommendation(
                    !string.IsNullOrWhiteSpace(item.ResponsibleEmployeeName)
                        ? item.ResponsibleEmployeeName.Trim() : item.SuspectedEmployeeName.Trim(),
                    !string.IsNullOrWhiteSpace(item.ResponsibleEmployeeName) ? "responsible" : "suspect",
                    item.Amount)).Where(row => row.EmployeeName.Length > 0).ToList();
            if (provisional?.IsProvisional == true)
            {
                string key = CashAcceptanceOwnerCorrectionPolicy.OperationKey(provisional);
                if (!CashConstitutionEngine.HasAppliedOperation(items, $"{key}:ledger") &&
                    provisional.ShortageAmount > 0 && !string.IsNullOrWhiteSpace(provisional.ResponsibleEmployeeName))
                    rows.Add(new(provisional.ResponsibleEmployeeName.Trim(), "responsible", provisional.ShortageAmount));
                var pending = provisional.PendingCashlessVerification;
                if (pending != null && !CashConstitutionEngine.HasAppliedOperation(items, $"{key}:cashless-ledger") &&
                    pending.ExpectedAmount > pending.ActualAmount && !string.IsNullOrWhiteSpace(pending.SuspectedEmployeeName))
                    rows.Add(new(pending.SuspectedEmployeeName.Trim(), "suspect", pending.ExpectedAmount - pending.ActualAmount));
            }
            return rows.GroupBy(row => (row.EmployeeName.ToUpperInvariant(), row.Type))
                .Select(group => new CashEmployeeRecommendation(group.First().EmployeeName, group.First().Type, group.Sum(row => row.Amount)))
                .OrderByDescending(row => row.Amount).ThenBy(row => row.EmployeeName).ToArray();
        }

        public static int UnpostedDifference(IEnumerable<CashReconciliationItem> items, CashAcceptanceItem? provisional)
        {
            if (provisional?.IsProvisional != true) return 0;
            string key = CashAcceptanceOwnerCorrectionPolicy.OperationKey(provisional);
            int difference = CashConstitutionEngine.HasAppliedOperation(items, $"{key}:ledger") ? 0 : provisional.Difference;
            var pending = provisional.PendingCashlessVerification;
            if (pending != null && !CashConstitutionEngine.HasAppliedOperation(items, $"{key}:cashless-ledger"))
                difference += pending.ActualAmount - pending.ExpectedAmount;
            return difference;
        }
    }
}
