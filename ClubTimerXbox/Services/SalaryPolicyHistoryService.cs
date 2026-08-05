using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public static class SalaryPolicyHistoryService
    {
        private static readonly object Gate = new();
        private static readonly string FolderPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ClubTimerXbox");
        private static readonly string FilePath = Path.Combine(
            FolderPath,
            "salary_policy_history.json");
        private static readonly SalaryPolicyHistoryState State = Load();

        public static void EnsureInitialized(AutoSalarySettings legacySettings)
        {
            lock (Gate)
            {
                if (State.Versions.Count > 0)
                    return;

                State.Versions.Add(new SalaryPolicyVersion
                {
                    CreatedAt = ClubClock.Current.LocalNow,
                    EffectiveFrom = DateTime.MinValue,
                    CreatedBy = "LegacyMigration",
                    Settings = Clone(legacySettings)
                });
                Save();
            }
        }

        public static SalaryPolicyVersion Schedule(
            AutoSalarySettings settings,
            string createdBy = "Владелец")
        {
            lock (Gate)
            {
                DateTime now = ClubClock.Current.LocalNow;
                DateTime effectiveFrom = BusinessCalendarService
                    .GetBusinessDay(now)
                    .EndExclusive;
                var version = new SalaryPolicyVersion
                {
                    CreatedAt = now,
                    EffectiveFrom = effectiveFrom,
                    CreatedBy = createdBy.Trim(),
                    Settings = Clone(settings)
                };

                State.Versions.RemoveAll(item =>
                    item.EffectiveFrom == effectiveFrom &&
                    item.EffectiveFrom > now);
                State.Versions.Add(version);
                Normalize();
                Save();
                return Clone(version);
            }
        }

        public static AutoSalarySettings GetSettingsAt(DateTime at)
        {
            return GetVersionAt(at).Settings;
        }

        public static SalaryPolicyVersion GetVersionAt(DateTime at)
        {
            lock (Gate)
            {
                SalaryPolicyVersion? version = State.Versions
                    .Where(item => item.EffectiveFrom <= at)
                    .OrderByDescending(item => item.EffectiveFrom)
                    .ThenByDescending(item => item.CreatedAt)
                    .FirstOrDefault();

                version ??= State.Versions
                    .OrderBy(item => item.EffectiveFrom)
                    .FirstOrDefault();
                return version == null
                    ? new SalaryPolicyVersion
                    {
                        EffectiveFrom = DateTime.MinValue,
                        Settings = new AutoSalarySettings()
                    }
                    : Clone(version);
            }
        }

        public static SalaryPolicyVersion GetLatestVersion()
        {
            lock (Gate)
            {
                var version = State.Versions
                    .OrderByDescending(item => item.EffectiveFrom)
                    .ThenByDescending(item => item.CreatedAt)
                    .First();
                return Clone(version);
            }
        }

        public static List<SalaryPolicyVersion> GetVersions(
            DateTime fromInclusive,
            DateTime toExclusive)
        {
            lock (Gate)
            {
                var first = State.Versions
                    .Where(item => item.EffectiveFrom <= fromInclusive)
                    .OrderByDescending(item => item.EffectiveFrom)
                    .FirstOrDefault();
                var result = State.Versions
                    .Where(item => item.EffectiveFrom > fromInclusive &&
                                   item.EffectiveFrom < toExclusive)
                    .OrderBy(item => item.EffectiveFrom)
                    .Select(Clone)
                    .ToList();
                if (first != null)
                    result.Insert(0, Clone(first));
                return result;
            }
        }

        public static int RenameEmployeeReferences(string oldName, string newName)
        {
            lock (Gate)
            {
                int changed = 0;
                foreach (var version in State.Versions)
                {
                    if (!EmployeeReferenceRenameService.Matches(
                            version.Settings.OpeningResponsibleEmployeeName,
                            oldName))
                    {
                        continue;
                    }

                    version.Settings.OpeningResponsibleEmployeeName = newName.Trim();
                    changed++;
                }

                if (changed > 0)
                    Save();
                return changed;
            }
        }

        internal static AutoSalarySettings Clone(AutoSalarySettings source)
        {
            return new AutoSalarySettings
            {
                ExpenseReservePercent = source.ExpenseReservePercent,
                SalaryFundPercent = source.SalaryFundPercent,
                TimeSharePercent = source.TimeSharePercent,
                GameRevenueSharePercent = source.GameRevenueSharePercent,
                TimeMonthlyFundAmount = source.TimeMonthlyFundAmount,
                TimeMonthlyPlannedHours = source.TimeMonthlyPlannedHours,
                ProductRevenueSharePercent = source.ProductRevenueSharePercent,
                ProductBonusPercent = source.ProductBonusPercent,
                WorkDayStartHour = source.WorkDayStartHour,
                WorkDayEndHour = source.WorkDayEndHour,
                DailyGameRevenueNorm = source.DailyGameRevenueNorm,
                OverNormBonusPercent = source.OverNormBonusPercent,
                PunctualityBonusAmount = source.PunctualityBonusAmount,
                LateActiveSessionBonusAmount = source.LateActiveSessionBonusAmount,
                OpeningResponsibleEmployeeName = source.OpeningResponsibleEmployeeName,
                LateOpeningGraceMinutes = source.LateOpeningGraceMinutes,
                LateOpeningPenaltyStepMinutes = source.LateOpeningPenaltyStepMinutes,
                LateOpeningPenaltyStepAmount = source.LateOpeningPenaltyStepAmount,
                LateOpeningMaxAutoMinutes = source.LateOpeningMaxAutoMinutes
            };
        }

        private static SalaryPolicyVersion Clone(SalaryPolicyVersion source)
        {
            return new SalaryPolicyVersion
            {
                Id = source.Id,
                CreatedAt = source.CreatedAt,
                EffectiveFrom = source.EffectiveFrom,
                CreatedBy = source.CreatedBy,
                Settings = Clone(source.Settings)
            };
        }

        private static SalaryPolicyHistoryState Load()
        {
            try
            {
                if (!File.Exists(FilePath))
                    return new SalaryPolicyHistoryState();
                return JsonSerializer.Deserialize<SalaryPolicyHistoryState>(
                           File.ReadAllText(FilePath))
                       ?? new SalaryPolicyHistoryState();
            }
            catch
            {
                return new SalaryPolicyHistoryState();
            }
        }

        private static void Normalize()
        {
            State.Versions ??= new List<SalaryPolicyVersion>();
            State.Versions = State.Versions
                .Where(item => item.Settings != null)
                .OrderBy(item => item.EffectiveFrom)
                .ThenBy(item => item.CreatedAt)
                .ToList();
        }

        private static void Save()
        {
            Directory.CreateDirectory(FolderPath);
            Normalize();
            AtomicFileStorageService.WriteAllText(
                FilePath,
                JsonSerializer.Serialize(
                    State,
                    new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
