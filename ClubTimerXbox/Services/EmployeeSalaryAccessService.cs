using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public sealed class EmployeeSalaryAccessSession
    {
        public string Id { get; init; } = Guid.NewGuid().ToString("N");
        public DateTime OpenedAt { get; init; }
        public string EmployeeId { get; init; } = "";
        public string EmployeeName { get; init; } = "";
        public string DutyEmployeeId { get; init; } = "";
        public string DutyEmployeeName { get; init; } = "";
        public Guid ShiftId { get; init; }
        public string PayoutOperationId => $"employee-salary:{Id}";
    }

    internal static class EmployeeSalaryAccessService
    {
        internal static Employee? Identify(IEnumerable<Employee> employees, string pin)
        {
            pin = pin.Trim();
            if (pin.Length == 0)
                return null;
            var matches = employees.Where(employee => employee.IsActive &&
                !string.IsNullOrWhiteSpace(employee.EmployeeId) && employee.PinCode == pin)
                .Take(2).ToList();
            // Duplicate codes must not open another employee's private salary.
            return matches.Count == 1 ? matches[0] : null;
        }

        public static EmployeeSalaryAccessSession Open(Employee employee)
        {
            Employee duty = EmployeeService.CurrentEmployee
                ?? throw new InvalidOperationException("Смена не выбрана.");
            ShiftLogItem shift = ActionLogService.CurrentShift
                ?? throw new InvalidOperationException("Смена не открыта.");
            var session = new EmployeeSalaryAccessSession
            {
                OpenedAt = ClubClock.Current.LocalNow,
                EmployeeId = employee.EmployeeId,
                EmployeeName = employee.Name,
                DutyEmployeeId = duty.EmployeeId,
                DutyEmployeeName = duty.Name,
                ShiftId = shift.Id
            };
            Validate(session, employee, duty, shift);
            string path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ClubTimerXbox", "salary-access", session.OpenedAt.ToString("yyyy-MM"),
                session.Id + ".json");
            AtomicFileStorageService.WriteAllText(path,
                JsonSerializer.Serialize(session, new JsonSerializerOptions { WriteIndented = true }));
            return session;
        }

        internal static void Validate(EmployeeSalaryAccessSession session,
            Employee? employee, Employee? duty, ShiftLogItem? shift)
        {
            if (employee?.IsActive != true || duty?.IsActive != true || shift == null ||
                shift.IsClosed || shift.Id != session.ShiftId ||
                employee.EmployeeId != session.EmployeeId || duty.EmployeeId != session.DutyEmployeeId ||
                !shift.EmployeeName.Equals(duty.Name, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "Сотрудник или смена изменились. Закройте окно и снова введите свой код.");
        }

        public static string Describe(EmployeeSalaryAccessSession session) =>
            $"Самостоятельная выплата сотруднику: {session.EmployeeName}. " +
            $"При администраторе: {session.DutyEmployeeName}. " +
            $"[salary-access:{session.Id}]";
    }
}
