using System;
using System.IO;
using System.Text.Json;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public static class FinancialPaceStorageService
    {
        private static readonly string FolderPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ClubTimerXbox");
        private static readonly string FilePath = Path.Combine(
            FolderPath,
            "financial_pace.json");

        public static FinancialPaceState Load()
        {
            try
            {
                if (!File.Exists(FilePath))
                    return new FinancialPaceState();

                return JsonSerializer.Deserialize<FinancialPaceState>(
                    File.ReadAllText(FilePath)) ?? new FinancialPaceState();
            }
            catch
            {
                return new FinancialPaceState();
            }
        }

        public static void Save(FinancialPaceState state)
        {
            Directory.CreateDirectory(FolderPath);
            string temporaryPath = FilePath + ".tmp";
            File.WriteAllText(
                temporaryPath,
                JsonSerializer.Serialize(state, new JsonSerializerOptions
                {
                    WriteIndented = true
                }));
            File.Move(temporaryPath, FilePath, true);
        }
    }
}
