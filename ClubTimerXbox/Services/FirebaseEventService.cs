using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public static class FirebaseEventService
    {
        private static readonly string FolderPath =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ClubTimerXbox"
            );

        private static readonly string OutboxPath =
            Path.Combine(FolderPath, "firebase_event_outbox.json");

        private static readonly object FileLock = new object();
        private static readonly SemaphoreSlim FlushLock = new SemaphoreSlim(1, 1);
        private static readonly HttpClient HttpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(5)
        };

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        public static Task PublishClubOpenedAsync(string sessionId, string employeeName)
        {
            var identity = PcIdentityService.Current;
            if (!CanPublish(identity))
                return Task.CompletedTask;

            string employee = CleanEmployeeName(employeeName);
            var record = CreateBaseRecord(
                id: $"opened_{CleanId(sessionId)}",
                type: "club_opened",
                title: $"{identity.ClubName}: клуб открыт",
                body: $"Программа клуба запущена в {DateTime.Now:HH:mm}. Сотрудник: {employee}.",
                severity: "info"
            );
            record.EmployeeName = employee;

            return QueueAndFlushAsync(record);
        }

        public static void PublishClubClosedAndWait(
            string sessionId,
            string employeeName,
            TimeSpan timeout)
        {
            var identity = PcIdentityService.Current;
            if (!CanPublish(identity))
                return;

            string employee = CleanEmployeeName(employeeName);
            var record = CreateBaseRecord(
                id: $"closed_{CleanId(sessionId)}",
                type: "club_closed",
                title: $"{identity.ClubName}: клуб закрыт",
                body: $"Программа клуба закрыта в {DateTime.Now:HH:mm}. Сотрудник: {employee}.",
                severity: "info"
            );
            record.EmployeeName = employee;

            try
            {
                QueueAndFlushAsync(record).Wait(timeout);
            }
            catch
            {
                // Запись уже лежит в локальной очереди и уйдёт при следующем запуске.
            }
        }

        public static Task PublishEmployeeChangedAsync(
            string previousEmployeeName,
            string currentEmployeeName)
        {
            var identity = PcIdentityService.Current;
            if (!CanPublish(identity))
                return Task.CompletedTask;

            string previous = CleanEmployeeName(previousEmployeeName);
            string current = CleanEmployeeName(currentEmployeeName);
            if (previous.Equals(current, StringComparison.OrdinalIgnoreCase))
                return Task.CompletedTask;

            var record = CreateBaseRecord(
                id: $"employee_{Guid.NewGuid():N}",
                type: "employee_changed",
                title: $"{identity.ClubName}: смена сотрудника",
                body: $"{previous} → {current}. Время смены: {DateTime.Now:HH:mm}.",
                severity: "info"
            );
            record.PreviousEmployeeName = previous;
            record.CurrentEmployeeName = current;
            record.EmployeeName = current;

            return QueueAndFlushAsync(record);
        }

        public static Task PublishSalaryTakenCashAsync(
            string operationId,
            string employeeName,
            int amount)
        {
            var identity = PcIdentityService.Current;
            if (!CanPublish(identity) || amount <= 0 || string.IsNullOrWhiteSpace(operationId))
                return Task.CompletedTask;

            string employee = CleanEmployeeName(employeeName);
            var record = CreateBaseRecord(
                id: BuildStableId("salary_cash", operationId.Trim()),
                type: "salary_taken_cash",
                title: $"{identity.ClubName}: зарплата",
                body: $"{employee} взял зарплату наличными: {amount} сом",
                severity: "info"
            );
            record.EmployeeName = employee;
            record.Amount = amount;
            record.PaymentMethod = "Наличные";
            record.OperationId = operationId.Trim();

            return QueueAndFlushAsync(record);
        }

        public static Task PublishUpdateResultAsync(
            UpdateSessionTicket ticket,
            string result)
        {
            var identity = PcIdentityService.Current;
            if (!CanPublish(identity))
                return Task.CompletedTask;

            string cleanResult = string.IsNullOrWhiteSpace(result)
                ? "failed"
                : result.Trim().ToLowerInvariant();
            string employee = CleanEmployeeName(ticket.EmployeeName);
            string type;
            string body;
            string severity;

            if (cleanResult == "done")
            {
                type = "update_completed";
                severity = "info";
                body = ticket.Mode switch
                {
                    AppUpdateInstallMode.ExitAndClose =>
                        $"Версия: {ticket.TargetVersion}. Сотрудник: {employee}. Работа завершена.",
                    AppUpdateInstallMode.StartupBeforeLogin =>
                        $"Версия: {ticket.TargetVersion}. Программа запущена. Ожидается вход сотрудника.",
                    _ =>
                        $"Версия: {ticket.TargetVersion}. Сотрудник: {employee}. Работа продолжена."
                };
            }
            else if (cleanResult == "rolled_back")
            {
                type = "update_rolled_back";
                severity = "urgent";
                body = $"Версия {ticket.TargetVersion} не установлена. Предыдущая версия восстановлена.";
            }
            else
            {
                type = "update_failed";
                severity = "urgent";
                body = $"Версия {ticket.TargetVersion} не установлена. Требуется проверка владельца.";
            }

            var record = CreateBaseRecord(
                id: $"update_{CleanId(ticket.SessionId)}_{cleanResult}",
                type: type,
                title: $"{identity.ClubName}: обновление",
                body: body,
                severity: severity);
            record.EmployeeName = employee;
            record.UpdateVersion = ticket.TargetVersion;
            record.UpdateResult = cleanResult;
            record.InstallMode = ticket.Mode.ToString();
            return QueueAndFlushAsync(record);
        }

        public static async Task PublishAcceptanceCompletedAsync(ShiftAcceptanceStatus status)
        {
            var identity = PcIdentityService.Current;
            if (!CanPublish(identity) || string.IsNullOrWhiteSpace(status.AcceptanceKey))
                return;

            string acceptanceKey = status.AcceptanceKey.Trim();
            string originalKey = !string.IsNullOrWhiteSpace(
                    status.CashCorrectionAcceptanceKey)
                ? status.CashCorrectionAcceptanceKey.Trim()
                : !string.IsNullOrWhiteSpace(
                    status.ManualSelfAcceptanceRecheckRootKey)
                    ? status.ManualSelfAcceptanceRecheckRootKey.Trim()
                    : acceptanceKey;

            var cash = CashAcceptanceService.FindByAnyAcceptanceKey(acceptanceKey)
                ?? CashAcceptanceService.FindByAnyAcceptanceKey(originalKey);

            var stockItems = StockAuditService.Items
                .Where(item => item.AcceptanceKey.Equals(
                    acceptanceKey,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (stockItems.Count == 0 && !originalKey.Equals(
                    acceptanceKey,
                    StringComparison.OrdinalIgnoreCase))
            {
                stockItems = StockAuditService.Items
                    .Where(item => item.AcceptanceKey.Equals(
                        originalKey,
                        StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            int cashDifference = cash?.Difference ?? 0;
            int productShortageAmount = stockItems
                .Where(item => item.Difference < 0)
                .Sum(item => item.DifferenceAmount);
            int productExtraAmount = stockItems
                .Where(item => item.Difference > 0)
                .Sum(item => item.DifferenceAmount);

            string responsible = CleanEmployeeName(
                !string.IsNullOrWhiteSpace(cash?.ResponsibleEmployeeName)
                    ? cash.ResponsibleEmployeeName
                    : string.IsNullOrWhiteSpace(status.DisplayResponsibleEmployeeName)
                        ? status.ResponsibleEmployeeName
                        : status.DisplayResponsibleEmployeeName
            );
            string current = CleanEmployeeName(
                !string.IsNullOrWhiteSpace(cash?.CheckedByEmployeeName)
                    ? cash.CheckedByEmployeeName
                    : string.IsNullOrWhiteSpace(status.DisplayNewEmployeeName)
                        ? status.NewEmployeeName
                        : status.DisplayNewEmployeeName
            );

            string cashText = DescribeDifference("наличка", cashDifference);
            if (cash != null)
            {
                cashText +=
                    $"; по программе {cash.ExpectedCashAmount} сом, " +
                    $"факт {cash.ActualCashAmount} сом";
            }

            string productText = DescribeProducts(productShortageAmount, productExtraAmount);
            string severity = cashDifference < 0 || productShortageAmount > 0
                ? "urgent"
                : cashDifference > 0 || productExtraAmount > 0
                    ? "warning"
                    : "info";

            var record = CreateBaseRecord(
                id: BuildStableId("acceptance", originalKey),
                type: "acceptance_completed",
                title: $"{identity.ClubName}: приёмка смены",
                body: $"{(status.IsManualSelfAcceptance && cash != null ? "Повторная проверка. " : "")}{responsible} → {current}. {cashText}. {productText}.",
                severity: severity
            );
            record.AcceptanceKey = originalKey;
            record.PreviousEmployeeName = responsible;
            record.CurrentEmployeeName = current;
            record.EmployeeName = current;
            record.CashDifference = cashDifference;
            record.ProductShortageAmount = productShortageAmount;
            record.ProductExtraAmount = productExtraAmount;

            // The compact owner state must be available before its notification.
            await FirebaseSyncService.PushOverviewStateAsync().ConfigureAwait(false);
            await FirebaseSyncService.PushCurrentStateAsync().ConfigureAwait(false);
            await QueueAndFlushAsync(record).ConfigureAwait(false);
        }

        public static async Task FlushPendingAsync()
        {
            if (!FirebaseConnectionService.CanSync)
                return;

            if (!await FlushLock.WaitAsync(0).ConfigureAwait(false))
                return;

            try
            {
                foreach (var record in LoadOutbox().OrderBy(item => item.CreatedAt).Take(50))
                {
                    try
                    {
                        await PutEventAsync(record).ConfigureAwait(false);
                        RemoveFromOutbox(record.Id);
                    }
                    catch
                    {
                        break;
                    }
                }
            }
            finally
            {
                FlushLock.Release();
            }
        }

        private static async Task QueueAndFlushAsync(ClubEventRecord record)
        {
            try
            {
                AddToOutbox(record);
                await FlushPendingAsync().ConfigureAwait(false);
            }
            catch
            {
                // Событие не должно мешать основной работе клуба.
            }
        }

        private static ClubEventRecord CreateBaseRecord(
            string id,
            string type,
            string title,
            string body,
            string severity)
        {
            var identity = PcIdentityService.Current;
            DateTime now = DateTime.Now;

            return new ClubEventRecord
            {
                Id = id,
                Type = type,
                ClubId = identity.ClubId,
                ClubName = identity.ClubName,
                Title = title,
                Body = body,
                Severity = severity,
                CreatedAt = DateTime.UtcNow.ToString("O"),
                CreatedAtLocal = now.ToString("yyyy-MM-dd HH:mm:ss"),
                Source = "pc",
                SourceInstallationId = identity.InstallationId
            };
        }

        private static async Task PutEventAsync(ClubEventRecord record)
        {
            string path = $"clubs/{record.ClubId}/events/{record.Id}";
            string url = await FirebaseAuthService
                .BuildDatabaseUrlAsync(path)
                .ConfigureAwait(false);
            string json = JsonSerializer.Serialize(record, JsonOptions);

            using var request = new HttpRequestMessage(HttpMethod.Put, url)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            using var response = await HttpClient
                .SendAsync(request)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
        }

        private static void AddToOutbox(ClubEventRecord record)
        {
            lock (FileLock)
            {
                var items = LoadOutboxUnsafe();
                int existingIndex = items.FindIndex(item => item.Id.Equals(
                    record.Id,
                    StringComparison.OrdinalIgnoreCase));

                if (existingIndex >= 0)
                    items[existingIndex] = record;
                else
                    items.Add(record);

                SaveOutboxUnsafe(items);
            }
        }

        private static List<ClubEventRecord> LoadOutbox()
        {
            lock (FileLock)
                return LoadOutboxUnsafe();
        }

        private static List<ClubEventRecord> LoadOutboxUnsafe()
        {
            try
            {
                if (!File.Exists(OutboxPath))
                    return new List<ClubEventRecord>();

                string json = File.ReadAllText(OutboxPath);
                return JsonSerializer.Deserialize<List<ClubEventRecord>>(json, JsonOptions)
                    ?? new List<ClubEventRecord>();
            }
            catch
            {
                return new List<ClubEventRecord>();
            }
        }

        private static void RemoveFromOutbox(string eventId)
        {
            lock (FileLock)
            {
                var items = LoadOutboxUnsafe();
                items.RemoveAll(item => item.Id.Equals(
                    eventId,
                    StringComparison.OrdinalIgnoreCase));
                SaveOutboxUnsafe(items);
            }
        }

        private static void SaveOutboxUnsafe(List<ClubEventRecord> items)
        {
            Directory.CreateDirectory(FolderPath);
            string json = JsonSerializer.Serialize(items, JsonOptions);
            string temporaryPath = OutboxPath + ".tmp";
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, OutboxPath, true);
        }

        private static bool CanPublish(PcIdentity identity)
        {
            return PcIdentityService.HasAssignedClub &&
                !string.IsNullOrWhiteSpace(identity.ClubName);
        }

        private static string CleanEmployeeName(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "Не выбран" : value.Trim();
        }

        private static string DescribeDifference(string label, int difference)
        {
            if (difference < 0)
                return $"{label}: недостача {Math.Abs(difference)} сом";

            if (difference > 0)
                return $"{label}: излишек {difference} сом";

            return $"{label}: без расхождений";
        }

        private static string DescribeProducts(int shortageAmount, int extraAmount)
        {
            if (shortageAmount > 0 && extraAmount > 0)
            {
                return $"товары: недостача {shortageAmount} сом, излишек {extraAmount} сом";
            }

            if (shortageAmount > 0)
                return $"товары: недостача {shortageAmount} сом";

            if (extraAmount > 0)
                return $"товары: излишек {extraAmount} сом";

            return "товары: без расхождений";
        }

        private static string BuildStableId(string prefix, string value)
        {
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
            return $"{prefix}_{Convert.ToHexString(hash).ToLowerInvariant()[..24]}";
        }

        private static string CleanId(string value)
        {
            string clean = new string(value
                .Where(character => char.IsLetterOrDigit(character) || character == '_' || character == '-')
                .ToArray());
            return string.IsNullOrWhiteSpace(clean) ? Guid.NewGuid().ToString("N") : clean;
        }

        private sealed class ClubEventRecord
        {
            public string Id { get; set; } = "";
            public string Type { get; set; } = "";
            public string ClubId { get; set; } = "";
            public string ClubName { get; set; } = "";
            public string Title { get; set; } = "";
            public string Body { get; set; } = "";
            public string Severity { get; set; } = "info";
            public string CreatedAt { get; set; } = "";
            public string CreatedAtLocal { get; set; } = "";
            public string Source { get; set; } = "pc";
            public string SourceInstallationId { get; set; } = "";
            public string EmployeeName { get; set; } = "";
            public string PreviousEmployeeName { get; set; } = "";
            public string CurrentEmployeeName { get; set; } = "";
            public string AcceptanceKey { get; set; } = "";
            public int CashDifference { get; set; }
            public int ProductShortageAmount { get; set; }
            public int ProductExtraAmount { get; set; }
            public int Amount { get; set; }
            public string PaymentMethod { get; set; } = "";
            public string OperationId { get; set; } = "";
            public string UpdateVersion { get; set; } = "";
            public string UpdateResult { get; set; } = "";
            public string InstallMode { get; set; } = "";
        }
    }
}
