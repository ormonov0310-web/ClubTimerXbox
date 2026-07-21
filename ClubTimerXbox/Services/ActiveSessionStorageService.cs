using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public static class ActiveSessionStorageService
    {
        private static readonly string SessionsFolderPath =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ClubTimerXbox"
            );

        private static readonly string SessionsFilePath =
            Path.Combine(SessionsFolderPath, "active_sessions.json");

        public static int RenameEmployeeReferences(
            string oldEmployeeName,
            string newEmployeeName)
        {
            var places = Load();
            int changed = 0;

            foreach (var place in places)
            {
                bool placeChanged = false;

                if (EmployeeReferenceRenameService.Matches(
                        place.StartedByEmployeeName,
                        oldEmployeeName))
                {
                    place.StartedByEmployeeName = newEmployeeName;
                    placeChanged = true;
                }

                if (EmployeeReferenceRenameService.Matches(
                        place.IncomeEmployeeName,
                        oldEmployeeName))
                {
                    place.IncomeEmployeeName = newEmployeeName;
                    placeChanged = true;
                }

                if (placeChanged)
                    changed++;
            }

            if (changed > 0)
                Save(places);

            return changed;
        }

        public static void Save(List<SavedActivePlace> activePlaces)
        {
            Directory.CreateDirectory(SessionsFolderPath);

            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            string json = JsonSerializer.Serialize(activePlaces, options);

            File.WriteAllText(SessionsFilePath, json);
        }

        public static List<SavedActivePlace> Load()
        {
            try
            {
                if (!File.Exists(SessionsFilePath))
                    return new List<SavedActivePlace>();

                string json = File.ReadAllText(SessionsFilePath);

                var places = JsonSerializer.Deserialize<List<SavedActivePlace>>(json);

                if (places == null)
                    return new List<SavedActivePlace>();

                return places;
            }
            catch
            {
                return new List<SavedActivePlace>();
            }
        }

        public static void Clear()
        {
            try
            {
                if (File.Exists(SessionsFilePath))
                    File.Delete(SessionsFilePath);
            }
            catch
            {
                // Пока ничего не делаем.
                // Позже можно добавить сообщение или запись в журнал.
            }
        }
    }
}
