using System;
using System.Collections.Generic;
using ClubTimerXbox.Models;

namespace ClubTimerXbox
{
    internal sealed record EmployeeDebtStatsLine(string Text, bool IsEmphasized = false);

    internal static class EmployeeDebtStatsPresentation
    {
        public static IReadOnlyList<EmployeeDebtStatsLine> BuildMonthLines(
            EmployeeDebtEmployeeSnapshot debt)
        {
            var lines = new List<EmployeeDebtStatsLine>();
            AddPositive(lines, "Долг на начало месяца", debt.OpeningDebtAmount);
            AddPositive(lines, "Новый долг", debt.AddedDebtAmount);
            AddPositive(lines, "Погашено", debt.RepaidDebtAmount);
            AddPositive(lines, "Прощено", debt.ForgivenDebtAmount);
            AddPositive(lines, "Остаток долга", debt.ClosingDebtAmount, true);
            return lines;
        }

        public static IReadOnlyList<EmployeeDebtStatsLine> BuildContractLines(
            EmployeeDebtContractSnapshot contract)
        {
            var lines = new List<EmployeeDebtStatsLine>
            {
                new($"Создан: {contract.CreatedAt:dd.MM.yyyy HH:mm}")
            };
            AddPositive(lines, "Сумма долга", contract.PrincipalAmount);
            AddPositive(lines, "Погашено", contract.RepaidAmount);
            AddPositive(lines, "Прощено", contract.ForgivenAmount);
            if (contract.RemainingAmount > 0)
                AddPositive(lines, "Остаток долга", contract.RemainingAmount, true);
            else
                lines.Add(new EmployeeDebtStatsLine("Долг погашен", true));
            return lines;
        }

        private static void AddPositive(
            ICollection<EmployeeDebtStatsLine> lines,
            string label,
            int amount,
            bool emphasized = false)
        {
            if (amount > 0)
                lines.Add(new EmployeeDebtStatsLine($"{label}: {amount} сом", emphasized));
        }
    }
}
