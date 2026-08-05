using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public static class ExpiredSessionViolationService
    {
        private static readonly string FolderPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ClubTimerXbox");
        private static readonly string FilePath = Path.Combine(
            FolderPath,
            "expired_session_violations.json");

        private static List<ExpiredSessionViolationRecord> Records { get; } = Load();

        public static IReadOnlyList<ExpiredSessionViolationRecord> GetAll()
        {
            return Records.OrderByDescending(item => item.ExpiredAt).ToList();
        }

        public static void RecordOrUpdate(
            ClubPlace place,
            Guid penaltyLossId,
            DateTime now)
        {
            if (place.ExpiredGameSessionId == null ||
                place.TimeExpiredAt == null ||
                place.ExpiredPenaltyChargedMinutes <= 0)
            {
                return;
            }

            var item = Records.FirstOrDefault(record =>
                record.GameSessionId == place.ExpiredGameSessionId.Value);
            if (item == null)
            {
                item = new ExpiredSessionViolationRecord
                {
                    GameSessionId = place.ExpiredGameSessionId.Value,
                    PlaceName = place.Name,
                    EmployeeName = place.ExpiredPenaltyEmployeeName?.Trim() ?? "",
                    ExpiredAt = place.TimeExpiredAt.Value,
                    ViolationStartedAt = place.TimeExpiredAt.Value.AddMinutes(
                        ExpiredSessionPenaltyService.GraceMinutes + 1),
                    CreatedAt = now,
                    GraceMinutes = ExpiredSessionPenaltyService.GraceMinutes
                };
                Records.Add(item);
            }

            item.PlaceName = place.Name;
            item.EmployeeName = place.ExpiredPenaltyEmployeeName?.Trim() ?? item.EmployeeName;
            item.LastUpdatedAt = now;
            item.ElapsedSeconds = ExpiredSessionPenaltyService.GetElapsedSeconds(place, now);
            item.ChargedMinutes = place.ExpiredPenaltyChargedMinutes;
            item.PenaltyAmount =
                place.ExpiredPenaltyChargedMinutes * ExpiredSessionPenaltyService.SomPerMinute;
            item.Status = "Active";
            if (!item.PenaltyLossIds.Contains(penaltyLossId))
                item.PenaltyLossIds.Add(penaltyLossId);
            Save();
        }

        public static void Complete(ClubPlace place, DateTime acknowledgedAt)
        {
            if (place.ExpiredGameSessionId == null)
                return;

            var item = Records.FirstOrDefault(record =>
                record.GameSessionId == place.ExpiredGameSessionId.Value);
            if (item == null)
                return;

            item.ElapsedSeconds = ExpiredSessionPenaltyService.GetElapsedSeconds(
                place,
                acknowledgedAt);
            item.ChargedMinutes = place.ExpiredPenaltyChargedMinutes;
            item.PenaltyAmount =
                place.ExpiredPenaltyChargedMinutes * ExpiredSessionPenaltyService.SomPerMinute;
            item.AcknowledgedAt = acknowledgedAt;
            item.LastUpdatedAt = acknowledgedAt;
            item.Status = "Acknowledged";
            Save();
        }

        public static void MarkPenaltyCancelled(Guid penaltyLossId, DateTime cancelledAt)
        {
            var item = Records.FirstOrDefault(record =>
                record.PenaltyLossIds.Contains(penaltyLossId));
            if (item == null)
                return;

            if (!item.CancelledPenaltyLossIds.Contains(penaltyLossId))
                item.CancelledPenaltyLossIds.Add(penaltyLossId);
            item.LastUpdatedAt = cancelledAt;
            Save();
        }

        public static int RenameEmployeeReferences(string oldName, string newName)
        {
            int changed = 0;
            foreach (var item in Records.Where(item =>
                         EmployeeReferenceRenameService.Matches(item.EmployeeName, oldName)))
            {
                item.EmployeeName = newName.Trim();
                changed++;
            }

            if (changed > 0)
                Save();
            return changed;
        }

        public static void Clear()
        {
            Records.Clear();
            try
            {
                if (File.Exists(FilePath))
                    File.Delete(FilePath);
            }
            catch
            {
                Save();
            }
        }

        private static List<ExpiredSessionViolationRecord> Load()
        {
            try
            {
                if (!File.Exists(FilePath))
                    return new List<ExpiredSessionViolationRecord>();
                return JsonSerializer.Deserialize<List<ExpiredSessionViolationRecord>>(
                           File.ReadAllText(FilePath))
                       ?? new List<ExpiredSessionViolationRecord>();
            }
            catch
            {
                return new List<ExpiredSessionViolationRecord>();
            }
        }

        private static void Save()
        {
            Directory.CreateDirectory(FolderPath);
            AtomicFileStorageService.WriteAllText(
                FilePath,
                JsonSerializer.Serialize(
                    Records,
                    new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
