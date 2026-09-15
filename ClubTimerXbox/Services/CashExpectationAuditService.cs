using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public static class CashExpectationAuditService
    {
        private const int MaxItems = 1000;
        private static readonly object Gate = new object();
        private static readonly string FolderPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ClubTimerXbox");
        private static readonly string FilePath = Path.Combine(
            FolderPath,
            "cash_expectation_audit.json");
        private static readonly List<CashExpectationAuditItem> Items = Load();

        public static bool Record(
            string rootAcceptanceKey,
            string attemptKey,
            string checkedByEmployeeName,
            string responsibleEmployeeName,
            CashPhysicalBalanceCalculation calculation,
            int actualCashAmount)
        {
            string idempotencyKey = BuildIdempotencyKey(
                rootAcceptanceKey,
                attemptKey,
                calculation,
                actualCashAmount);

            lock (Gate)
            {
                if (Items.Any(item => item.IdempotencyKey.Equals(
                        idempotencyKey,
                        StringComparison.Ordinal)))
                {
                    return true;
                }

                var before = Items.ToList();
                Items.Add(new CashExpectationAuditItem
                {
                    Id = Guid.NewGuid(),
                    IdempotencyKey = idempotencyKey,
                    CreatedAt = ClubClock.Current.LocalNow,
                    RootAcceptanceKey = rootAcceptanceKey.Trim(),
                    AttemptKey = attemptKey.Trim(),
                    CheckedByEmployeeName = checkedByEmployeeName.Trim(),
                    ResponsibleEmployeeName = responsibleEmployeeName.Trim(),
                    Source = calculation.Source.ToString(),
                    SourceId = calculation.SourceId,
                    SourceObservedAt = calculation.SourceObservedAt,
                    SourceCommittedAt = calculation.SourceCommittedAt,
                    BaseAmount = calculation.BaseAmount,
                    CashIncome = calculation.CashIncome,
                    CashExpenses = calculation.CashExpenses,
                    ExpectedCashAmount = calculation.Amount,
                    ActualCashAmount = Math.Max(0, actualCashAmount),
                    Difference = Math.Max(0, actualCashAmount) - calculation.Amount
                });

                if (Items.Count > MaxItems)
                {
                    int removeCount = Items.Count - MaxItems;
                    var idsToRemove = Items
                        .OrderBy(candidate => candidate.CreatedAt)
                        .Take(removeCount)
                        .Select(candidate => candidate.Id)
                        .ToHashSet();
                    Items.RemoveAll(item => idsToRemove.Contains(item.Id));
                }

                try
                {
                    Save();
                    return true;
                }
                catch
                {
                    Items.Clear();
                    Items.AddRange(before);
                    return false;
                }
            }
        }

        private static string BuildIdempotencyKey(
            string rootAcceptanceKey,
            string attemptKey,
            CashPhysicalBalanceCalculation calculation,
            int actualCashAmount)
        {
            string sourceId = calculation.SourceId?.ToString("N") ?? "none";
            string raw = string.Join(
                ":",
                rootAcceptanceKey.Trim(),
                attemptKey.Trim(),
                calculation.Source,
                sourceId,
                calculation.SourceObservedAt.Ticks,
                calculation.SourceCommittedAt.Ticks,
                calculation.BaseAmount,
                calculation.CashIncome,
                calculation.CashExpenses,
                calculation.Amount,
                Math.Max(0, actualCashAmount));
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
        }

        private static List<CashExpectationAuditItem> Load()
        {
            try
            {
                if (!File.Exists(FilePath))
                    return new List<CashExpectationAuditItem>();

                string json = File.ReadAllText(FilePath);
                return JsonSerializer.Deserialize<List<CashExpectationAuditItem>>(json) ??
                       new List<CashExpectationAuditItem>();
            }
            catch
            {
                return new List<CashExpectationAuditItem>();
            }
        }

        private static void Save()
        {
            Directory.CreateDirectory(FolderPath);
            string json = JsonSerializer.Serialize(
                Items,
                new JsonSerializerOptions { WriteIndented = true });
            AtomicFileStorageService.WriteAllText(FilePath, json);
        }
    }
}
