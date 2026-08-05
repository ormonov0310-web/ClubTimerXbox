using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public static class EmployeeRatingService
    {
        private static readonly object Gate = new();
        private static readonly string FolderPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ClubTimerXbox");
        private static readonly string FilePath = Path.Combine(
            FolderPath,
            "employee_rating.json");
        private static readonly EmployeeRatingState State = Load();

        public static void EnsureActivated()
        {
            lock (Gate)
            {
                Normalize();
                bool changed = false;
                if (State.ActivatedAt == default)
                {
                    State.ActivatedAt = ClubClock.Current.LocalNow;
                    changed = true;
                }

                foreach (var employee in EmployeeService.GetAllEmployees())
                {
                    if (EnsureProfile(employee))
                        changed = true;
                }

                if (changed)
                    Save();
            }
        }

        public static void SynchronizeConfirmedLosses()
        {
            EnsureActivated();
            lock (Gate)
            {
                bool changed = false;
                foreach (var loss in EmployeeLossService.Items
                             .Where(item => item.CreatedAt >= State.ActivatedAt &&
                                            item.Amount > 0 &&
                                            !item.SuppressAutomaticRating &&
                                            !string.IsNullOrWhiteSpace(item.ResponsibleEmployeeName)))
                {
                    bool confirmed = loss.IsFixed || EmployeeLossService.IsProductLoss(loss);
                    if (!confirmed)
                        continue;

                    string sourceId = "loss:" + loss.Id.ToString("N");
                    if (State.Events.Any(item => item.SourceId.Equals(
                            sourceId,
                            StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    var employee = EmployeeService.FindByName(loss.ResponsibleEmployeeName);
                    if (employee == null)
                        continue;

                    bool violation = EmployeeLossService.IsViolationLoss(loss);
                    string ruleCode = violation
                        ? "TIME_OTHER_VIOLATION"
                        : loss.Amount <= 100
                            ? "REVENUE_CONFIRMED_LOSS_SMALL"
                            : "REVENUE_CONFIRMED_LOSS_LARGE";
                    AddRuleEventUnsafe(
                        employee,
                        EmployeeRatingRuleCatalog.Get(ruleCode),
                        sourceId,
                        violation ? "ViolationLoss" : "ConfirmedLoss",
                        loss.Title,
                        loss.Description,
                        loss.CreatedAt);
                    changed = true;
                }

                if (changed)
                    Save();
            }
        }

        public static void SynchronizeConfirmedCashExtras()
        {
            EnsureActivated();
            lock (Gate)
            {
                bool changed = false;
                foreach (var contribution in CashReconciliationService.Items
                             .Where(item => item.Kind == CashReconciliationKind.CashExtra)
                             .SelectMany(item => item.ExtraContributions ??
                                 new List<CashExtraContribution>())
                             .Where(item =>
                                 item.Origin == CashReconciliationOrigin.CashAcceptance &&
                                 item.Stage == CashReconciliationStage.Ready &&
                                 item.Amount > 0 &&
                                 !string.IsNullOrWhiteSpace(item.EmployeeName)))
                {
                    string sourceId = "cash-extra:" + contribution.Id.ToString("N");
                    if (State.Events.Any(item => item.SourceId.Equals(
                            sourceId,
                            StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    var employee = EmployeeService.FindByName(contribution.EmployeeName);
                    if (employee == null)
                        continue;

                    AddRuleEventUnsafe(
                        employee,
                        EmployeeRatingRuleCatalog.Get("REVENUE_CONFIRMED_EXTRA"),
                        sourceId,
                        "ConfirmedCashExtra",
                        "Подтверждённый излишек",
                        $"После сверки безнала подтверждён излишек: {contribution.Amount} сом.",
                        ClubClock.Current.LocalNow);
                    changed = true;
                }

                if (changed)
                    Save();
            }
        }

        public static EmployeeRatingSnapshot GetSnapshot(
            string employeeName,
            DateTime at)
        {
            EnsureActivated();
            lock (Gate)
            {
                var employee = EmployeeService.FindByName(employeeName);
                string employeeId = employee?.EmployeeId ?? "name:" + employeeName.Trim().ToLowerInvariant();
                var profile = GetOrCreateProfileUnsafe(employeeId, employeeName);
                int time = GetPercentUnsafe(profile, EmployeeRatingBranch.Time, at);
                int revenue = GetPercentUnsafe(profile, EmployeeRatingBranch.Revenue, at);
                var history = State.Events
                    .Where(item => MatchesEmployee(item, employeeId, employeeName))
                    .OrderByDescending(item => item.EffectiveFrom)
                    .ToList();
                var active = history
                    .Where(item => item.EffectiveFrom <= at && at < item.EffectiveUntil)
                    .ToList();

                return new EmployeeRatingSnapshot
                {
                    EmployeeId = employeeId,
                    EmployeeName = profile.EmployeeName,
                    TimePercent = time,
                    RevenuePercent = revenue,
                    OverallPercent = EmployeeSalaryRuleEngine.CalculateOverallRating(time, revenue),
                    HasWarning = time <= 91 || revenue <= 91,
                    ActiveEvents = active,
                    History = history
                };
            }
        }

        public static int GetPercent(
            string employeeName,
            EmployeeRatingBranch branch,
            DateTime at)
        {
            EnsureActivated();
            lock (Gate)
            {
                var employee = EmployeeService.FindByName(employeeName);
                string employeeId = employee?.EmployeeId ?? "name:" + employeeName.Trim().ToLowerInvariant();
                var profile = GetOrCreateProfileUnsafe(employeeId, employeeName);
                return GetPercentUnsafe(profile, branch, at);
            }
        }

        public static IReadOnlyList<DateTime> GetBoundaries(
            string employeeName,
            DateTime fromInclusive,
            DateTime toExclusive)
        {
            EnsureActivated();
            lock (Gate)
            {
                var employee = EmployeeService.FindByName(employeeName);
                string employeeId = employee?.EmployeeId ?? "name:" + employeeName.Trim().ToLowerInvariant();
                var profile = GetOrCreateProfileUnsafe(employeeId, employeeName);
                return profile.BaseVersions
                    .Select(item => item.EffectiveFrom)
                    .Concat(State.Events
                        .Where(item => MatchesEmployee(item, employeeId, employeeName))
                        .SelectMany(item => new[] { item.EffectiveFrom, item.EffectiveUntil }))
                    .Where(value => value > fromInclusive && value < toExclusive)
                    .Distinct()
                    .OrderBy(value => value)
                    .ToList();
            }
        }

        public static string GetAccrualSignature(
            string employeeName,
            EmployeeRatingBranch branch,
            DateTime at)
        {
            EnsureActivated();
            lock (Gate)
            {
                var employee = EmployeeService.FindByName(employeeName);
                string employeeId = employee?.EmployeeId ?? "name:" + employeeName.Trim().ToLowerInvariant();
                var profile = GetOrCreateProfileUnsafe(employeeId, employeeName);
                Guid baseId = profile.BaseVersions
                    .Where(item => item.EffectiveFrom <= at)
                    .OrderByDescending(item => item.EffectiveFrom)
                    .ThenByDescending(item => item.CreatedAt)
                    .Select(item => item.Id)
                    .FirstOrDefault();
                string eventBoundary = string.Join(",", State.Events
                    .Where(item =>
                        MatchesEmployee(item, employeeId, employeeName) &&
                        item.Branch == branch &&
                        item.EffectiveFrom <= at)
                    .OrderBy(item => item.EffectiveFrom)
                    .Select(item => $"{item.Id:N}:{item.EffectiveUntil:O}"));
                return $"{baseId:N}|{eventBoundary}";
            }
        }

        public static EmployeeRatingBaseVersion SetBaseRatings(
            string employeeName,
            int timePercent,
            int revenuePercent,
            string reason)
        {
            EnsureActivated();
            lock (Gate)
            {
                var employee = EmployeeService.FindByName(employeeName)
                    ?? throw new InvalidOperationException("Сотрудник не найден.");
                var profile = GetOrCreateProfileUnsafe(employee.EmployeeId, employee.Name);
                var version = new EmployeeRatingBaseVersion
                {
                    CreatedAt = ClubClock.Current.LocalNow,
                    EffectiveFrom = ClubClock.Current.LocalNow,
                    TimePercent = Clamp(timePercent),
                    RevenuePercent = Clamp(revenuePercent),
                    Reason = reason.Trim()
                };
                profile.BaseVersions.Add(version);
                Save();
                return version;
            }
        }

        public static EmployeeRatingBaseVersion ResetTo100(string employeeName, string reason)
        {
            EnsureActivated();
            lock (Gate)
            {
                var employee = EmployeeService.FindByName(employeeName)
                    ?? throw new InvalidOperationException("Сотрудник не найден.");
                DateTime now = ClubClock.Current.LocalNow;
                var profile = GetOrCreateProfileUnsafe(employee.EmployeeId, employee.Name);
                var version = new EmployeeRatingBaseVersion
                {
                    CreatedAt = now,
                    EffectiveFrom = now,
                    TimePercent = 100,
                    RevenuePercent = 100,
                    Reason = reason.Trim()
                };
                profile.BaseVersions.Add(version);

                foreach (var item in State.Events.Where(item =>
                             MatchesEmployee(item, employee.EmployeeId, employee.Name) &&
                             item.EffectiveFrom <= now &&
                             now < item.EffectiveUntil))
                {
                    item.EndedAt = now;
                    item.Status = EmployeeRatingEventStatus.Forgiven;
                    item.ResolutionNote = "Рейтинг возвращён владельцем к 100%.";
                }

                Save();
                return version;
            }
        }

        public static EmployeeRatingEvent AddManualEvent(
            string employeeName,
            EmployeeRatingBranch branch,
            int targetPercent,
            int durationDays,
            string title,
            string description)
        {
            EnsureActivated();
            lock (Gate)
            {
                var employee = EmployeeService.FindByName(employeeName)
                    ?? throw new InvalidOperationException("Сотрудник не найден.");
                DateTime now = ClubClock.Current.LocalNow;
                var profile = GetOrCreateProfileUnsafe(employee.EmployeeId, employee.Name);
                int basePercent = GetBasePercentUnsafe(profile, branch, now);
                var direction = targetPercent >= basePercent
                    ? EmployeeRatingEffectDirection.Reward
                    : EmployeeRatingEffectDirection.Penalty;
                DateTime businessStart = BusinessCalendarService
                    .GetBusinessDay(now)
                    .StartInclusive;
                var item = AddEventUnsafe(
                    employee,
                    branch,
                    targetPercent,
                    businessStart.AddDays(Math.Clamp(durationDays, 1, 14)),
                    "manual:" + Guid.NewGuid().ToString("N"),
                    "OwnerManual",
                    title,
                    description,
                    now,
                    "OWNER_MANUAL",
                    1,
                    direction,
                    Math.Abs(Clamp(targetPercent) - basePercent),
                    basePercent);
                Save();
                return item;
            }
        }

        public static EmployeeRatingEvent AddRuleEvent(
            string employeeName,
            string ruleCode,
            string sourceId,
            string sourceType,
            string description,
            DateTime effectiveFrom)
        {
            EnsureActivated();
            lock (Gate)
            {
                var existing = State.Events.FirstOrDefault(item =>
                    item.SourceId.Equals(sourceId, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                    return existing;

                var employee = EmployeeService.FindByName(employeeName)
                    ?? throw new InvalidOperationException("Сотрудник не найден.");
                var item = AddRuleEventUnsafe(
                    employee,
                    EmployeeRatingRuleCatalog.Get(ruleCode),
                    sourceId,
                    sourceType,
                    EmployeeRatingRuleCatalog.Get(ruleCode).Title,
                    description,
                    effectiveFrom);
                Save();
                return item;
            }
        }

        public static EmployeeRatingEvent EndEvent(
            Guid eventId,
            bool cancelAsError,
            int compensationAmount,
            string note)
        {
            EnsureActivated();
            lock (Gate)
            {
                var item = State.Events.FirstOrDefault(value => value.Id == eventId)
                    ?? throw new InvalidOperationException("Запись рейтинга не найдена.");
                DateTime now = ClubClock.Current.LocalNow;
                if (now < item.EffectiveFrom)
                    now = item.EffectiveFrom;
                item.EndedAt = now;
                item.Status = cancelAsError
                    ? EmployeeRatingEventStatus.CancelledAsError
                    : EmployeeRatingEventStatus.Forgiven;
                item.CompensationAmount = cancelAsError
                    ? Math.Max(0, compensationAmount)
                    : 0;
                item.ResolutionNote = note.Trim();
                Save();
                return item;
            }
        }

        public static bool EndBySource(
            string sourceId,
            bool cancelAsError,
            int compensationAmount,
            string note)
        {
            EnsureActivated();
            lock (Gate)
            {
                var item = State.Events.FirstOrDefault(value =>
                    value.SourceId.Equals(sourceId, StringComparison.OrdinalIgnoreCase));
                if (item == null)
                    return false;
                DateTime now = ClubClock.Current.LocalNow;
                item.EndedAt = now < item.EffectiveFrom ? item.EffectiveFrom : now;
                item.Status = cancelAsError
                    ? EmployeeRatingEventStatus.CancelledAsError
                    : EmployeeRatingEventStatus.Forgiven;
                item.CompensationAmount = cancelAsError
                    ? Math.Max(0, compensationAmount)
                    : 0;
                item.ResolutionNote = note.Trim();
                Save();
                return true;
            }
        }

        public static int RenameEmployeeReferences(string oldName, string newName)
        {
            lock (Gate)
            {
                int changed = 0;
                foreach (var profile in State.Profiles.Where(item =>
                             EmployeeReferenceRenameService.Matches(item.EmployeeName, oldName)))
                {
                    profile.EmployeeName = newName.Trim();
                    changed++;
                }

                foreach (var item in State.Events.Where(item =>
                             EmployeeReferenceRenameService.Matches(item.EmployeeName, oldName)))
                {
                    item.EmployeeName = newName.Trim();
                    item.Title = EmployeeReferenceRenameService.RenameText(item.Title, oldName, newName);
                    item.Description = EmployeeReferenceRenameService.RenameText(item.Description, oldName, newName);
                    changed++;
                }

                if (changed > 0)
                    Save();
                return changed;
            }
        }

        private static EmployeeRatingEvent AddEventUnsafe(
            Employee employee,
            EmployeeRatingBranch branch,
            int targetPercent,
            DateTime scheduledUntil,
            string sourceId,
            string sourceType,
            string title,
            string description,
            DateTime effectiveFrom,
            string ruleCode,
            int ruleVersion,
            EmployeeRatingEffectDirection direction,
            int changePercent,
            int basePercentAtCreation)
        {
            GetOrCreateProfileUnsafe(employee.EmployeeId, employee.Name);
            var item = new EmployeeRatingEvent
            {
                EmployeeId = employee.EmployeeId,
                EmployeeName = employee.Name,
                Branch = branch,
                RuleCode = ruleCode.Trim(),
                RuleVersion = Math.Max(1, ruleVersion),
                Direction = direction,
                ChangePercent = Math.Max(0, changePercent),
                BasePercentAtCreation = Clamp(basePercentAtCreation),
                SourceId = sourceId,
                SourceType = sourceType,
                Title = title.Trim(),
                Description = description.Trim(),
                CreatedAt = ClubClock.Current.LocalNow,
                EffectiveFrom = effectiveFrom,
                ScheduledUntil = scheduledUntil,
                TargetPercent = Clamp(targetPercent),
                Status = EmployeeRatingEventStatus.Active
            };
            State.Events.Add(item);
            return item;
        }

        private static EmployeeRatingEvent AddRuleEventUnsafe(
            Employee employee,
            EmployeeRatingRuleDefinition rule,
            string sourceId,
            string sourceType,
            string title,
            string description,
            DateTime effectiveFrom)
        {
            var profile = GetOrCreateProfileUnsafe(employee.EmployeeId, employee.Name);
            int basePercent = GetBasePercentUnsafe(profile, rule.Branch, effectiveFrom);
            int targetPercent = rule.Direction == EmployeeRatingEffectDirection.Penalty
                ? basePercent - rule.ChangePercent
                : basePercent + rule.ChangePercent;
            return AddEventUnsafe(
                employee,
                rule.Branch,
                targetPercent,
                effectiveFrom.Add(rule.Duration),
                sourceId,
                sourceType,
                title,
                description,
                effectiveFrom,
                rule.Code,
                rule.Version,
                rule.Direction,
                rule.ChangePercent,
                basePercent);
        }

        private static int GetBasePercentUnsafe(
            EmployeeRatingProfile profile,
            EmployeeRatingBranch branch,
            DateTime at)
        {
            var version = profile.BaseVersions
                .Where(item => item.EffectiveFrom <= at)
                .OrderByDescending(item => item.EffectiveFrom)
                .ThenByDescending(item => item.CreatedAt)
                .FirstOrDefault()
                ?? profile.BaseVersions.OrderBy(item => item.EffectiveFrom).FirstOrDefault()
                ?? NewInitialBase();
            return Clamp(branch == EmployeeRatingBranch.Time
                ? version.TimePercent
                : version.RevenuePercent);
        }

        private static int GetPercentUnsafe(
            EmployeeRatingProfile profile,
            EmployeeRatingBranch branch,
            DateTime at)
        {
            return EmployeeSalaryRuleEngine.ResolveRating(
                profile,
                State.Events.Where(item => MatchesEmployee(
                    item,
                    profile.EmployeeId,
                    profile.EmployeeName)),
                branch,
                at);
        }

        private static bool EnsureProfile(Employee employee)
        {
            var existing = State.Profiles.FirstOrDefault(item =>
                item.EmployeeId.Equals(employee.EmployeeId, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                GetOrCreateProfileUnsafe(employee.EmployeeId, employee.Name);
                return true;
            }

            if (existing.EmployeeName.Equals(employee.Name, StringComparison.Ordinal))
                return false;
            existing.EmployeeName = employee.Name;
            return true;
        }

        private static EmployeeRatingProfile GetOrCreateProfileUnsafe(
            string employeeId,
            string employeeName)
        {
            var profile = State.Profiles.FirstOrDefault(item =>
                item.EmployeeId.Equals(employeeId, StringComparison.OrdinalIgnoreCase));
            if (profile != null)
            {
                profile.EmployeeName = employeeName.Trim();
                if (profile.BaseVersions.Count == 0)
                    profile.BaseVersions.Add(NewInitialBase());
                return profile;
            }

            profile = new EmployeeRatingProfile
            {
                EmployeeId = employeeId.Trim(),
                EmployeeName = employeeName.Trim(),
                BaseVersions = new List<EmployeeRatingBaseVersion> { NewInitialBase() }
            };
            State.Profiles.Add(profile);
            return profile;
        }

        private static EmployeeRatingBaseVersion NewInitialBase()
        {
            return new EmployeeRatingBaseVersion
            {
                CreatedAt = ClubClock.Current.LocalNow,
                EffectiveFrom = DateTime.MinValue,
                TimePercent = 100,
                RevenuePercent = 100,
                Reason = "Начальный рейтинг"
            };
        }

        private static bool MatchesEmployee(
            EmployeeRatingEvent item,
            string employeeId,
            string employeeName)
        {
            return (!string.IsNullOrWhiteSpace(employeeId) &&
                    item.EmployeeId.Equals(employeeId, StringComparison.OrdinalIgnoreCase)) ||
                   item.EmployeeName.Equals(employeeName, StringComparison.OrdinalIgnoreCase);
        }

        private static int Clamp(int value) => Math.Clamp(value, 0, 120);

        private static EmployeeRatingState Load()
        {
            try
            {
                if (!File.Exists(FilePath))
                    return new EmployeeRatingState();
                return JsonSerializer.Deserialize<EmployeeRatingState>(
                           File.ReadAllText(FilePath))
                       ?? new EmployeeRatingState();
            }
            catch
            {
                return new EmployeeRatingState();
            }
        }

        private static void Normalize()
        {
            State.Profiles ??= new List<EmployeeRatingProfile>();
            State.Events ??= new List<EmployeeRatingEvent>();
            foreach (var profile in State.Profiles)
            {
                profile.BaseVersions ??= new List<EmployeeRatingBaseVersion>();
                profile.EmployeeId = profile.EmployeeId.Trim();
                profile.EmployeeName = profile.EmployeeName.Trim();
            }
            foreach (var item in State.Events)
            {
                item.RuleCode = item.RuleCode.Trim();
                item.RuleVersion = Math.Max(1, item.RuleVersion);
                item.BasePercentAtCreation = Clamp(item.BasePercentAtCreation);
                item.ChangePercent = item.ChangePercent > 0
                    ? item.ChangePercent
                    : Math.Abs(Clamp(item.TargetPercent) - item.BasePercentAtCreation);
                if (string.IsNullOrWhiteSpace(item.RuleCode))
                {
                    item.RuleCode = "LEGACY";
                    item.Direction = item.TargetPercent > item.BasePercentAtCreation
                        ? EmployeeRatingEffectDirection.Reward
                        : EmployeeRatingEffectDirection.Penalty;
                }
            }
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
