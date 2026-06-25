using System;
using System.Collections.Generic;
using System.Linq;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public static class EmployeeService
    {
        private static readonly List<Employee> _defaultEmployees = new List<Employee>
        {
            new Employee
            {
                Name = "Сталбек",
                PinCode = "1111",
                IsActive = true
            },
            new Employee
            {
                Name = "Арген",
                PinCode = "2222",
                IsActive = true
            },
            new Employee
            {
                Name = "Ислам",
                PinCode = "3333",
                IsActive = true
            },
            new Employee
            {
                Name = "Адилет",
                PinCode = "4444",
                IsActive = true
            }
        };

        public static List<Employee> Employees { get; private set; }

        public static Employee? CurrentEmployee { get; private set; }

        static EmployeeService()
        {
            Employees = EmployeeStorageService.Load();

            if (Employees.Count == 0)
            {
                Employees = _defaultEmployees
                    .Select(employee => new Employee
                    {
                        Name = employee.Name,
                        PinCode = employee.PinCode,
                        IsActive = employee.IsActive
                    })
                    .ToList();

                Save();
            }
            else
            {
                EnsureDefaultEmployeesExist();
                Save();
            }
        }

        public static List<Employee> GetActiveEmployees()
        {
            return Employees
                .Where(employee => employee.IsActive)
                .OrderBy(employee => employee.Name)
                .ToList();
        }

        public static List<Employee> GetAllEmployees()
        {
            return Employees
                .OrderBy(employee => employee.Name)
                .ToList();
        }

        public static Employee? FindByName(string employeeName)
        {
            employeeName = employeeName.Trim();

            return Employees.FirstOrDefault(employee =>
                employee.Name.Equals(employeeName, StringComparison.OrdinalIgnoreCase)
            );
        }

        public static Employee? FindById(string employeeId)
        {
            employeeId = employeeId.Trim();

            if (string.IsNullOrWhiteSpace(employeeId))
                return null;

            return Employees.FirstOrDefault(employee =>
                employee.EmployeeId.Equals(employeeId, StringComparison.OrdinalIgnoreCase)
            );
        }

        public static bool ExistsByName(string employeeName)
        {
            return FindByName(employeeName) != null;
        }

        public static bool Login(string pinCode)
        {
            pinCode = pinCode.Trim();

            var employee = Employees.FirstOrDefault(item =>
                item.IsActive &&
                item.PinCode == pinCode
            );

            if (employee == null)
                return false;

            CurrentEmployee = employee;
            return true;
        }

        public static bool ValidateEmployeePin(string employeeName, string pinCode)
        {
            employeeName = employeeName.Trim();
            pinCode = pinCode.Trim();

            var employee = Employees.FirstOrDefault(item =>
                item.IsActive &&
                item.Name.Equals(employeeName, StringComparison.OrdinalIgnoreCase) &&
                item.PinCode == pinCode
            );

            return employee != null;
        }

        public static void Logout()
        {
            CurrentEmployee = null;
        }

        public static void AddEmployee(string employeeName, string pinCode, string employeeId = "")
        {
            employeeName = employeeName.Trim();
            pinCode = pinCode.Trim();
            employeeId = employeeId.Trim();

            if (string.IsNullOrWhiteSpace(employeeName))
                return;

            if (string.IsNullOrWhiteSpace(pinCode))
                return;

            if (ExistsByName(employeeName))
                return;

            if (!string.IsNullOrWhiteSpace(employeeId) && FindById(employeeId) != null)
                return;

            Employees.Add(new Employee
            {
                EmployeeId = employeeId,
                Name = employeeName,
                PinCode = pinCode,
                IsActive = true
            });

            Save();
        }

        public static void ChangePinCode(string employeeName, string newPinCode)
        {
            employeeName = employeeName.Trim();
            newPinCode = newPinCode.Trim();

            if (string.IsNullOrWhiteSpace(employeeName))
                return;

            if (string.IsNullOrWhiteSpace(newPinCode))
                return;

            var employee = FindByName(employeeName);

            if (employee == null)
                return;

            employee.PinCode = newPinCode;

            if (CurrentEmployee != null &&
                CurrentEmployee.Name.Equals(employee.Name, StringComparison.OrdinalIgnoreCase))
            {
                CurrentEmployee.PinCode = newPinCode;
            }

            Save();
        }

        public static void ChangeName(string employeeName, string newEmployeeName)
        {
            employeeName = employeeName.Trim();
            newEmployeeName = newEmployeeName.Trim();

            if (string.IsNullOrWhiteSpace(employeeName))
                return;

            if (string.IsNullOrWhiteSpace(newEmployeeName))
                return;

            var employee = FindByName(employeeName);

            if (employee == null)
                return;

            bool duplicate = Employees.Any(item =>
                !item.Name.Equals(employee.Name, StringComparison.OrdinalIgnoreCase) &&
                item.Name.Equals(newEmployeeName, StringComparison.OrdinalIgnoreCase)
            );

            if (duplicate)
                return;

            employee.Name = newEmployeeName;

            if (CurrentEmployee != null &&
                CurrentEmployee.Name.Equals(employeeName, StringComparison.OrdinalIgnoreCase))
            {
                CurrentEmployee.Name = newEmployeeName;
            }

            Save();
        }

        public static void SetEmployeeActive(string employeeName, bool isActive)
        {
            employeeName = employeeName.Trim();

            var employee = FindByName(employeeName);

            if (employee == null)
                return;

            employee.IsActive = isActive;

            if (!isActive &&
                CurrentEmployee != null &&
                CurrentEmployee.Name.Equals(employee.Name, StringComparison.OrdinalIgnoreCase))
            {
                CurrentEmployee = null;
            }

            Save();
        }

        public static void DeleteEmployeeSoft(string employeeName)
        {
            SetEmployeeActive(employeeName, false);
        }

        public static void ReplaceAll(IEnumerable<Employee> employees)
        {
            Employees = employees
                .Where(employee => !string.IsNullOrWhiteSpace(employee.Name))
                .Select(employee => new Employee
                {
                    EmployeeId = employee.EmployeeId,
                    Name = employee.Name,
                    PinCode = employee.PinCode,
                    IsActive = employee.IsActive
                })
                .ToList();

            CurrentEmployee = null;

            Save();
        }

        private static void EnsureDefaultEmployeesExist()
        {
            foreach (var defaultEmployee in _defaultEmployees)
            {
                bool exists = Employees.Any(employee =>
                    employee.Name.Equals(defaultEmployee.Name, StringComparison.OrdinalIgnoreCase)
                );

                if (exists)
                    continue;

                Employees.Add(new Employee
                {
                    Name = defaultEmployee.Name,
                    PinCode = defaultEmployee.PinCode,
                    IsActive = defaultEmployee.IsActive
                });
            }
        }

        private static void Save()
        {
            EmployeeStorageService.Save(Employees);
        }
    }
}
