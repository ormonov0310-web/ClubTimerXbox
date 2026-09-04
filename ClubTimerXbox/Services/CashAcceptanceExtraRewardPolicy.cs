using System;
using System.Collections.Generic;
using System.Linq;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public sealed record ConfirmedCashExtraReward(
        Guid InvestigationId,
        string EmployeeName,
        int Amount,
        IReadOnlyList<string> LegacySourceIds);

    public static class CashAcceptanceExtraRewardPolicy
    {
        public static IReadOnlyList<ConfirmedCashExtraReward> FindCashRewards(
            IEnumerable<CashReconciliationItem> items,
            DateTime mixedPoolPolicyStartedAt)
        {
            var contributions = items
                .Where(item => !item.IsTechnicalEvent &&
                    (item.Kind == CashReconciliationKind.CashExtra ||
                     item.Kind == CashReconciliationKind.CashlessExtra))
                .SelectMany(pool => (pool.ExtraContributions ?? new List<CashExtraContribution>())
                    .Select(contribution => new
                    {
                        Contribution = contribution,
                        WasPreviouslyEligible = pool.Kind == CashReconciliationKind.CashExtra
                    }))
                .Where(item =>
                    item.Contribution.Kind == CashReconciliationKind.CashExtra &&
                    item.Contribution.Origin == CashReconciliationOrigin.CashAcceptance &&
                    !string.IsNullOrWhiteSpace(item.Contribution.EmployeeName));

            var result = new List<ConfirmedCashExtraReward>();
            foreach (var group in contributions.GroupBy(item => (
                InvestigationId: item.Contribution.InvestigationId == Guid.Empty
                    ? item.Contribution.Id
                    : item.Contribution.InvestigationId,
                EmployeeName: item.Contribution.EmployeeName.Trim().ToLowerInvariant())))
            {
                // The mixed-pool fix must not backfill rewards missed before this policy started.
                int amount = group.Where(item =>
                        item.Contribution.Stage == CashReconciliationStage.Ready &&
                        item.Contribution.Amount > 0 &&
                        (item.WasPreviouslyEligible ||
                         item.Contribution.CreatedAt >= mixedPoolPolicyStartedAt))
                    .Sum(item => item.Contribution.Amount);
                if (amount == 0)
                    continue;

                result.Add(new ConfirmedCashExtraReward(
                    group.Key.InvestigationId,
                    group.First().Contribution.EmployeeName.Trim(),
                    amount,
                    // Include spent parts too: an old award still consumes this acceptance's quota.
                    group.Select(item => "cash-extra:" + item.Contribution.Id.ToString("N"))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList()));
            }

            return result;
        }

        public static string BuildSourceId(string ruleCode, Guid investigationId, Employee employee)
        {
            string employeeKey = string.IsNullOrWhiteSpace(employee.EmployeeId)
                ? "name:" + employee.Name.Trim().ToLowerInvariant()
                : employee.EmployeeId.Trim().ToLowerInvariant();
            return $"acceptance-extra:{ruleCode}:{investigationId:N}:{employeeKey}";
        }

        public static bool HasReward(
            IEnumerable<EmployeeRatingEvent> events,
            string sourceId,
            Employee employee,
            IEnumerable<string> legacySourceIds)
        {
            var legacySources = new HashSet<string>(legacySourceIds, StringComparer.OrdinalIgnoreCase);
            // Cancelled and expired events also prevent a replay; existing history is never rewritten.
            return events.Any(item =>
                sourceId.Equals(item.SourceId, StringComparison.OrdinalIgnoreCase) ||
                (legacySources.Contains(item.SourceId) &&
                 (string.IsNullOrWhiteSpace(item.EmployeeId) ||
                  item.EmployeeId.Equals(employee.EmployeeId, StringComparison.OrdinalIgnoreCase))));
        }
    }
}
