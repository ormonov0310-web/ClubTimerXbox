using System;
using System.Collections.Generic;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public static class EmployeeReferenceRenameService
    {
        public static int RenameAll(
            string oldEmployeeName,
            string newEmployeeName,
            IReadOnlyList<ClubPlace>? places = null)
        {
            oldEmployeeName = oldEmployeeName.Trim();
            newEmployeeName = newEmployeeName.Trim();

            if (string.IsNullOrWhiteSpace(oldEmployeeName) ||
                string.IsNullOrWhiteSpace(newEmployeeName) ||
                oldEmployeeName.Equals(newEmployeeName, StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            int changed = 0;
            changed += ActionLogService.RenameEmployeeReferences(oldEmployeeName, newEmployeeName);
            changed += CashService.RenameEmployeeReferences(oldEmployeeName, newEmployeeName);
            changed += PaymentService.RenameEmployeeReferences(oldEmployeeName, newEmployeeName);
            changed += EmployeeLossService.RenameEmployeeReferences(oldEmployeeName, newEmployeeName);
            changed += EmployeeBonusService.RenameEmployeeReferences(oldEmployeeName, newEmployeeName);
            changed += CashAcceptanceService.RenameEmployeeReferences(oldEmployeeName, newEmployeeName);
            changed += CashReconciliationService.RenameEmployeeReferences(oldEmployeeName, newEmployeeName);
            changed += StockAuditService.RenameEmployeeReferences(oldEmployeeName, newEmployeeName);
            changed += StockPurchaseService.RenameEmployeeReferences(oldEmployeeName, newEmployeeName);
            changed += ShiftAcceptanceService.RenameEmployeeReferences(oldEmployeeName, newEmployeeName);
            changed += AutoSalaryService.RenameEmployeeReferences(oldEmployeeName, newEmployeeName);
            changed += ActiveSessionStorageService.RenameEmployeeReferences(oldEmployeeName, newEmployeeName);

            if (places != null)
            {
                foreach (var place in places)
                {
                    bool placeChanged = false;

                    if (Matches(place.StartedByEmployeeName, oldEmployeeName))
                    {
                        place.StartedByEmployeeName = newEmployeeName;
                        placeChanged = true;
                    }

                    if (Matches(place.IncomeEmployeeName, oldEmployeeName))
                    {
                        place.IncomeEmployeeName = newEmployeeName;
                        placeChanged = true;
                    }

                    if (placeChanged)
                        changed++;
                }
            }

            return changed;
        }

        internal static bool Matches(string? value, string employeeName)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   value.Trim().Equals(employeeName, StringComparison.OrdinalIgnoreCase);
        }

        internal static string RenameText(
            string? value,
            string oldEmployeeName,
            string newEmployeeName)
        {
            return string.IsNullOrEmpty(value)
                ? value ?? ""
                : value.Replace(
                    oldEmployeeName,
                    newEmployeeName,
                    StringComparison.OrdinalIgnoreCase);
        }
    }
}
