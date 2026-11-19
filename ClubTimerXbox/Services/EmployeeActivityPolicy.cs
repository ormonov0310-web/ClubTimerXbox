using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services;

internal static class EmployeeActivityPolicy
{
    internal static bool CanParticipate([NotNullWhen(true)] Employee? employee) => employee?.IsActive == true;

    internal static Employee RequireActive(Employee? employee)
    {
        if (!CanParticipate(employee))
            throw new InvalidOperationException("Сотрудник не найден или отключён. Выберите активного сотрудника.");
        return employee;
    }

    internal static PortionWork? NewPortionWork(Employee? employee, ShiftLogItem shift, DateTime now)
    {
        DateTime end = shift.ClosedAt ?? now;
        if (!CanParticipate(employee) || string.IsNullOrWhiteSpace(employee.EmployeeId) ||
            end <= shift.StartedAt)
            return null;
        return new PortionWork(employee.EmployeeId, employee.Name, shift.StartedAt, end);
    }

    internal static List<ShiftLogItem> CloseOpenShifts(
        IEnumerable<ShiftLogItem> shifts, string employeeName, DateTime at)
    {
        var closed = new List<ShiftLogItem>();
        foreach (var shift in shifts)
        {
            if (shift.IsClosed || !shift.EmployeeName.Equals(employeeName, StringComparison.OrdinalIgnoreCase))
                continue;
            DateTime end = shift.ClosedAt.HasValue && shift.ClosedAt.Value < at ? shift.ClosedAt.Value : at;
            shift.ClosedAt = end < shift.StartedAt ? shift.StartedAt : end;
            shift.IsClosed = true;
            closed.Add(shift);
        }
        return closed;
    }
}
