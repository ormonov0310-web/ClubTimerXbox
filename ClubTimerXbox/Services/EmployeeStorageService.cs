using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public static class EmployeeStorageService
    {
        private static readonly string FolderPath =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ClubTimerXbox"
            );

        private static readonly string FilePath =
            Path.Combine(FolderPath, "employees.json");

        public static List<Employee> Load()
        {
            try
            {
                if (!File.Exists(FilePath))
                    return new List<Employee>();

                string json = File.ReadAllText(FilePath);

                var employees = JsonSerializer.Deserialize<List<Employee>>(json);

                if (employees == null)
                    return new List<Employee>();

                return employees;
            }
            catch
            {
                return new List<Employee>();
            }
        }

        public static void Save(List<Employee> employees)
        {
            Directory.CreateDirectory(FolderPath);

            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            string json = JsonSerializer.Serialize(employees, options);

            File.WriteAllText(FilePath, json);
        }

        public static void Clear()
        {
            try
            {
                if (File.Exists(FilePath))
                    File.Delete(FilePath);
            }
            catch
            {
                // Пока ничего не делаем.
            }
        }
    }
}