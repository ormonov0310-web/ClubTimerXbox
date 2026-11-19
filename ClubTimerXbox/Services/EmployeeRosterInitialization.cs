using System.Collections.Generic;
using System.Linq;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    internal static class EmployeeRosterInitialization
    {
        internal static List<Employee> Initialize(
            List<Employee> savedEmployees,
            IEnumerable<Employee> defaults)
        {
            if (savedEmployees.Count != 0)
                return savedEmployees;

            return defaults.Select(employee => new Employee
            {
                Name = employee.Name,
                PinCode = employee.PinCode,
                IsActive = employee.IsActive
            }).ToList();
        }
    }
}
