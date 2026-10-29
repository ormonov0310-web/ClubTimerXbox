using System;
using System.Collections.Generic;
using System.Linq;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public static class AutoSalaryService
    {
        private const string GamesCategory = "\u0418\u0433\u0440\u044b";
        private static readonly DateTime FullDayOverNormBonusEffectiveFrom =
            new DateTime(2026, 9, 1, BusinessCalendarService.BusinessDayStartHour, 0, 0);
        private static readonly AutoSalarySettings LegacySettings =
            NormalizeSettings(AutoSalarySettingsStorageService.Load());

        static AutoSalaryService()
        {
            SalaryPolicyHistoryService.EnsureInitialized(LegacySettings);
        }

        public static AutoSalarySettings Settings => NormalizeSettings(
            SalaryPolicyHistoryService.GetSettingsAt(ClubClock.Current.LocalNow));

        public static SalaryPolicyVersion LatestPolicyVersion =>
            SalaryPolicyHistoryService.GetLatestVersion();

        public static int RenameEmployeeReferences(
            string oldEmployeeName,
            string newEmployeeName)
        {
            int changed = SalaryWorkTimeProtectionService.RenameEmployeeReferences(
                oldEmployeeName,
                newEmployeeName
            );

            changed += SalaryPolicyHistoryService.RenameEmployeeReferences(
                oldEmployeeName,
                newEmployeeName);
            changed += EmployeeRatingService.RenameEmployeeReferences(
                oldEmployeeName,
                newEmployeeName);

            return changed;
        }

        public static SalaryPolicyVersion UpdateSettings(AutoSalarySettings settings)
        {
            settings = NormalizeSettings(settings);
            AutoSalarySettingsStorageService.Save(settings);
            return SalaryPolicyHistoryService.Schedule(settings);
        }

        public static AutoSalaryReport BuildReport(DateTime monthStart)
        {
            using var portions = OverNormPortionService.BeginCalculation();
            var result = CaptureMonthCalculation(monthStart).Calculate(
                includeDaily: EmployeeDebtService.HasOutstandingContracts);
            ApplyMonthCalculation(result);
            return result.Report;
        }

        internal static SalaryMonthCalculation BuildMonthCalculation(DateTime monthStart)
        {
            using var portions = OverNormPortionService.BeginCalculation();
            var result = CaptureMonthCalculation(monthStart).Calculate();
            ApplyMonthCalculation(result);
            return result;
        }

        internal static void ApplyMonthCalculation(SalaryMonthCalculation result)
        {
            var report = result.Report;
            string activeMonth = BusinessCalendarService.GetBusinessMonth(ClubClock.Current.LocalNow).Key;
            if (report.MonthKey != activeMonth || !EmployeeDebtService.HasOutstandingContracts ||
                !EmployeeDebtService.ReconcileAutomatic(report, result.DailyEarnings))
                return;
            foreach (var employee in report.Employees)
            {
                employee.DebtTransferredAmount = EmployeeDebtService.GetSalaryTransferTotal(employee.EmployeeId, report.MonthKey);
                employee.CurrentPeriodRemainingAmount = employee.GrossAmount - employee.LossesAmount -
                    employee.PaidAmount - employee.DebtTransferredAmount;
                employee.RemainingAmount = employee.CurrentPeriodRemainingAmount + employee.CarryInAmount;
            }
        }

        private sealed record MonthInputs(Lazy<Dictionary<string, EmployeeBonusInput>> Bonuses,
            Lazy<SalaryAccrualSource> Accrual)
        {
            internal IReadOnlyDictionary<string, List<AutoSalaryDayEarning>>? DailyEarnings { get; set; }
        }

        private static MonthInputs CreateMonthInputs(DateTime monthStart, DateTime nextMonthStart,
            IEnumerable<string> employeeNames)
        {
            var names = employeeNames.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            return new(new(() => BuildEmployeeBonusInputs(monthStart, nextMonthStart)), new(() =>
            {
                using var measurement = SnapshotBuildMeasurements.Measure("salary.capture");
                var rules = new SalaryAccrualRules(SalaryPolicyHistoryService.CaptureVersions(),
                    names.Select(name => new KeyValuePair<string, EmployeeRatingCalculation>(name,
                        EmployeeRatingService.CaptureCalculation(name))));
                var games = CashService.GetRecordsByPeriod(monthStart, nextMonthStart)
                    .Where(record => record.Category == GamesCategory)
                    .Select(record => new SalaryGameRevenue(CashService.GetBusinessTime(record),
                        record.IncomeEmployeeName, record.Amount));
                var products = ProductServiceRevenueService.GetEntries(monthStart, nextMonthStart)
                    .Select(entry => new SalaryProductRevenue(entry.OccurredAt, entry.EmployeeName, entry.Amount));
                return new SalaryAccrualSource(games, products, rules);
            }));
        }

        internal static CapturedSalaryMonth CaptureMonthCalculation(DateTime monthStart)
        {
            using var measurement = SnapshotBuildMeasurements.Measure("salary.month");
            var period = BusinessCalendarService.GetBusinessMonthByAnchor(monthStart);
            monthStart = period.StartInclusive;
            DateTime nextMonthStart = period.EndExclusive;

            EmployeeRatingService.SynchronizeConfirmedLosses();
            EmployeeRatingService.SynchronizeConfirmedCashExtras();
            var sharedInputs = CreateMonthInputs(monthStart, nextMonthStart,
                EmployeeService.GetAllEmployees().Select(employee => employee.Name));
            var accrual = sharedInputs.Accrual.Value;
            SalaryPolicyVersion latestPolicy = LatestPolicyVersion;
            AutoSalarySettings displaySettings = NormalizeSettings(
                SalaryPolicyHistoryService.Clone(latestPolicy.Settings));

            int gameRevenue = CashService.GetTotalByPeriodAndCategory(monthStart, nextMonthStart, GamesCategory);
            int productRevenue = accrual.Products(monthStart, nextMonthStart).Sum(item => item.Amount);
            int plannedTimeFund = Math.Max(0, displaySettings.TimeMonthlyFundAmount);

            var report = new AutoSalaryReport
            {
                MonthKey = period.Key,
                Settings = displaySettings,
                SettingsEffectiveFrom = latestPolicy.EffectiveFrom,
                HasPendingSettings = latestPolicy.EffectiveFrom > ClubClock.Current.LocalNow,
                GameRevenue = gameRevenue,
                ProductRevenue = productRevenue,
                TimeFundAmount = plannedTimeFund,
                ProductShareFundAmount = 0
            };

            var bonusInputs = sharedInputs.Bonuses.Value;
            report.OverNormBonusReviewMessages = OverNormPortionService.GetReviewMessages(monthStart, nextMonthStart);
            var employeeInputs = EmployeeService
                .GetAllEmployees()
                .Where(employee => employee.IsActive ||
                    (bonusInputs.TryGetValue(employee.Name, out var earned) &&
                     earned.Bonuses.Any(b => b.Type == "OverNormGameRevenuePortion")))
                .Select(employee =>
                {
                    var summary = EmployeeStatsService.GetSummary(employee.Name, monthStart);
                    int paidSalary = CashService.GetSalaryTotalByPeriodForEmployee(
                        monthStart,
                        nextMonthStart,
                        employee.Name
                    );

                    if (!bonusInputs.TryGetValue(employee.Name, out var bonusInput))
                    {
                        bonusInput = new EmployeeBonusInput
                        {
                            EmployeeName = employee.Name
                        };
                    }

                    return new CapturedSalaryMonth.EmployeeInput
                    {
                        EmployeeId = employee.EmployeeId,
                        EmployeeName = employee.Name,
                        Summary = summary,
                        PaidSalary = paidSalary,
                        WorkHours = bonusInput.WorkHours,
                        PaidIntervals = bonusInput.PaidIntervals,
                        Bonuses = bonusInput.Bonuses,
                        ClosedPayroll = BusinessAccountingService.TryGetClosedPayroll(period.Key, employee.Name, out var closed) ? closed : null,
                        DebtTransferred = EmployeeDebtService.GetSalaryTransferTotal(employee.EmployeeId, period.Key),
                        CarryIn = period.Key == BusinessCalendarService.GetBusinessMonth(ClubClock.Current.LocalNow).Key
                            ? BusinessAccountingService.GetCarriedSalary(employee.Name, period.Key) : 0,
                        Rating = EmployeeRatingService.GetSnapshot(employee.Name,
                            nextMonthStart <= ClubClock.Current.LocalNow ? nextMonthStart.AddTicks(-1) : ClubClock.Current.LocalNow)
                    };
                })
                .ToList();

            return new CapturedSalaryMonth(monthStart, report, employeeInputs, accrual);
        }

        public static List<AutoSalaryDayEarning> BuildDailyEarnings(
            string employeeName,
            DateTime monthStart,
            AutoSalaryReport? preparedReport = null)
        {
            using var portions = OverNormPortionService.BeginCalculation();
            return BuildDailyEarnings(employeeName, monthStart, preparedReport, null);
        }

        private static List<AutoSalaryDayEarning> BuildDailyEarnings(
            string employeeName,
            DateTime monthStart,
            AutoSalaryReport? preparedReport,
            MonthInputs? sharedInputs)
        {
            employeeName = employeeName.Trim();
            var period = BusinessCalendarService.GetBusinessMonthByAnchor(monthStart);
            monthStart = period.StartInclusive;
            DateTime nextMonthStart = period.EndExclusive;
            if (BusinessAccountingService.TryGetClosedPayroll(
                    period.Key,
                    employeeName,
                    out var closedPayroll) &&
                closedPayroll.DailyEarnings.Count > 0)
            {
                return closedPayroll.DailyEarnings
                    .Select(CloneDailyEarning)
                    .OrderByDescending(item => item.Date)
                    .ToList();
            }

            if (preparedReport == null)
            {
                var result = BuildMonthCalculation(monthStart);
                return result.DailyEarnings.TryGetValue(employeeName, out var days)
                    ? days.Select(CloneDailyEarning).ToList() : new();
            }
            var monthly = preparedReport.Employees.FirstOrDefault(item =>
                item.EmployeeName.Equals(employeeName, StringComparison.OrdinalIgnoreCase));
            if (monthly == null)
                return new List<AutoSalaryDayEarning>();

            sharedInputs ??= CreateMonthInputs(monthStart, nextMonthStart,
                preparedReport.Employees.Select(employee => employee.EmployeeName));
            var bonusInputs = sharedInputs.Bonuses.Value;
            var accrual = sharedInputs.Accrual.Value;
            if (!bonusInputs.TryGetValue(employeeName, out var source))
            {
                source = new EmployeeBonusInput
                {
                    EmployeeName = employeeName
                };
            }

            return CapturedSalaryMonth.CalculateDaily(employeeName, monthStart, monthly,
                source.WorkHours, source.PaidIntervals, accrual);
        }

        public static IReadOnlyDictionary<string, List<AutoSalaryDayEarning>>
            BuildDailyEarningsByEmployee(
                DateTime monthStart,
                AutoSalaryReport preparedReport)
        {
            using var portions = OverNormPortionService.BeginCalculation();
            var period = BusinessCalendarService.GetBusinessMonthByAnchor(monthStart);
            // One input snapshot for the entire batch; closed payrolls need none.
            var inputs = CreateMonthInputs(period.StartInclusive, period.EndExclusive,
                preparedReport.Employees.Select(employee => employee.EmployeeName));
            return BuildDailyEarningsByEmployee(monthStart, preparedReport, inputs);
        }

        private static IReadOnlyDictionary<string, List<AutoSalaryDayEarning>> BuildDailyEarningsByEmployee(
            DateTime monthStart, AutoSalaryReport preparedReport,
            MonthInputs inputs)
        {
            using var measurement = SnapshotBuildMeasurements.Measure("salary.daily");
            return inputs.DailyEarnings ??= preparedReport.Employees.ToDictionary(
                employee => employee.EmployeeName,
                employee => BuildDailyEarnings(
                    employee.EmployeeName,
                    monthStart,
                    preparedReport,
                    inputs),
                StringComparer.OrdinalIgnoreCase);
        }

        private static AutoSalaryDayEarning CloneDailyEarning(
            AutoSalaryDayEarning source)
        {
            return new AutoSalaryDayEarning
            {
                Date = source.Date,
                WorkHours = source.WorkHours,
                TimeAmount = source.TimeAmount,
                TimeRatingEarnedAmount = source.TimeRatingEarnedAmount,
                TimeRatingLostAmount = source.TimeRatingLostAmount,
                TimeRatingPercents = source.TimeRatingPercents.ToList(),
                GameAmount = source.GameAmount,
                GameRatingEarnedAmount = source.GameRatingEarnedAmount,
                GameRatingLostAmount = source.GameRatingLostAmount,
                GameRatingPercents = source.GameRatingPercents.ToList(),
                ProductServiceBonusAmount = source.ProductServiceBonusAmount,
                OtherBonusAmount = source.OtherBonusAmount
            };
        }

        private static Dictionary<string, EmployeeBonusInput> BuildEmployeeBonusInputs(
            DateTime monthStart,
            DateTime nextMonthStart)
        {
            using var measurement = SnapshotBuildMeasurements.Measure("salary.inputs");
            var result = EmployeeService
                .GetAllEmployees()
                .Where(employee => employee.IsActive)
                .ToDictionary(
                    employee => employee.Name,
                    employee => new EmployeeBonusInput
                    {
                        EmployeeName = employee.Name
                    },
                    StringComparer.OrdinalIgnoreCase
                );

            DateTime day = monthStart.Date;
            while (day < nextMonthStart.Date)
            {
                AutoSalarySettings settings = SalaryPolicyHistoryService.GetSettingsAt(
                    day.Date.AddHours(BusinessCalendarService.BusinessDayStartHour));
                DateTime scheduleStart = GetScheduleStart(day, settings);
                DateTime scheduleEnd = GetScheduleEnd(day, settings);

                ApplyPaidTimeForDay(result, scheduleStart, scheduleEnd);
                ApplyPunctualityBonusForDay(result, scheduleStart, settings);
                ApplyLateActiveBonusForDay(result, scheduleStart, settings);
                ApplyOverNormBonusForDay(result,
                    day.Date.AddHours(BusinessCalendarService.BusinessDayStartHour),
                    scheduleStart, scheduleEnd, settings);

                day = day.AddDays(1);
            }

            ApplyManualOwnerBonuses(result, monthStart, nextMonthStart);
            ApplyRatingCompensations(result, monthStart, nextMonthStart);

            foreach (var input in result.Values)
            {
                input.WorkHours = SalaryWorkTimeProtectionService.Protect(
                    monthStart,
                    input.EmployeeName,
                    input.WorkHours
                );
            }

            return result;
        }

        private static void ApplyRatingCompensations(
            Dictionary<string, EmployeeBonusInput> result,
            DateTime monthStart,
            DateTime nextMonthStart)
        {
            foreach (var input in result.Values)
            {
                var snapshot = EmployeeRatingService.GetSnapshot(
                    input.EmployeeName,
                    nextMonthStart.AddTicks(-1));
                foreach (var item in snapshot.History.Where(item =>
                             item.Status == EmployeeRatingEventStatus.CancelledAsError &&
                             item.CompensationAmount > 0 &&
                             item.EndedAt >= monthStart &&
                             item.EndedAt < nextMonthStart))
                {
                    input.Bonuses.Add(new AutoSalaryBonusItem
                    {
                        CreatedAt = item.EndedAt ?? item.CreatedAt,
                        Type = "RatingCompensation",
                        Title = "Компенсация рейтинга",
                        Description = string.IsNullOrWhiteSpace(item.ResolutionNote)
                            ? $"Отмена ошибочного снижения: {item.Title}."
                            : item.ResolutionNote,
                        Amount = item.CompensationAmount
                    });
                }
            }
        }

        public static void SetRecoveredWorkHours(
            DateTime monthStart,
            string employeeName,
            double recoveredHours)
        {
            SalaryWorkTimeProtectionService.SetRecoveredHours(
                monthStart,
                employeeName,
                recoveredHours
            );
        }

        private static void ApplyManualOwnerBonuses(
            Dictionary<string, EmployeeBonusInput> result,
            DateTime monthStart,
            DateTime nextMonthStart)
        {
            foreach (var bonus in EmployeeBonusService.GetSalaryMonthBonuses(monthStart, nextMonthStart))
            {
                if (bonus.Amount <= 0)
                    continue;

                string employeeName = bonus.EmployeeName.Trim();
                if (string.IsNullOrWhiteSpace(employeeName))
                    continue;

                if (!result.TryGetValue(employeeName, out var input))
                {
                    input = new EmployeeBonusInput
                    {
                        EmployeeName = employeeName
                    };
                    result[employeeName] = input;
                }

                input.Bonuses.Add(new AutoSalaryBonusItem
                {
                    CreatedAt = bonus.CreatedAt,
                    Type = bonus.BonusType,
                    Title = string.IsNullOrWhiteSpace(bonus.Title)
                        ? "Премия от владельца"
                        : bonus.Title,
                    Description = bonus.Description,
                    Amount = bonus.Amount
                });
            }
        }

        private static void ApplyPaidTimeForDay(
            Dictionary<string, EmployeeBonusInput> result,
            DateTime scheduleStart,
            DateTime scheduleEnd)
        {
            foreach (var input in result.Values)
            {
                var shifts = EmployeeStatsService.GetShifts(
                    input.EmployeeName,
                    scheduleStart.Date,
                    scheduleEnd.AddDays(1)
                );

                foreach (var shift in shifts)
                {
                    DateTime shiftEnd = shift.ClosedAt ?? ClubClock.Current.LocalNow;
                    if (shift.StartedAt >= scheduleEnd || shiftEnd <= scheduleStart)
                        continue;

                    DateTime paidStart = Max(shift.StartedAt, scheduleStart);
                    DateTime paidEnd = Min(shiftEnd, scheduleEnd);
                    TimeSpan paidTime = TimeSpan.Zero;
                    if (paidEnd > paidStart)
                    {
                        input.AddPaidInterval(paidStart, paidEnd);
                        paidTime += paidEnd - paidStart;
                    }

                    TimeSpan latePaidTime = GetLateActiveTime(
                        input.EmployeeName,
                        shift.StartedAt,
                        shiftEnd,
                        scheduleEnd
                    );
                    if (latePaidTime > TimeSpan.Zero)
                    {
                        input.AddPaidInterval(scheduleEnd, scheduleEnd + latePaidTime);
                        paidTime += latePaidTime;
                    }

                    if (paidTime <= TimeSpan.Zero)
                        continue;

                    double hours = paidTime.TotalHours;
                    input.WorkHours += hours;
                    input.AddDailyHours(scheduleStart.Date, hours);
                }
            }
        }

        private static void ApplyPunctualityBonusForDay(
            Dictionary<string, EmployeeBonusInput> result,
            DateTime scheduleStart,
            AutoSalarySettings settings)
        {
            if (settings.PunctualityBonusAmount <= 0)
                return;

            DateTime earlyOpenStart = scheduleStart.Date.AddHours(6);
            if (scheduleStart <= earlyOpenStart)
                return;

            var firstShift = result.Values
                .SelectMany(input =>
                    EmployeeStatsService
                        .GetShifts(input.EmployeeName, earlyOpenStart, scheduleStart)
                        .Where(shift =>
                            shift.StartedAt.Date == scheduleStart.Date &&
                            shift.StartedAt >= earlyOpenStart &&
                            shift.StartedAt < scheduleStart)
                        .Select(shift => new
                        {
                            Input = input,
                            shift.StartedAt
                        }))
                .OrderBy(item => item.StartedAt)
                .FirstOrDefault();

            if (firstShift == null)
                return;

            firstShift.Input.Bonuses.Add(new AutoSalaryBonusItem
            {
                CreatedAt = scheduleStart,
                Type = "Punctuality",
                Title = "Пунктуальность",
                Description = $"Открыл клуб до {FormatHour(settings.WorkDayStartHour)}.",
                Amount = settings.PunctualityBonusAmount
            });
        }

        private static void ApplyLateActiveBonusForDay(
            Dictionary<string, EmployeeBonusInput> result,
            DateTime scheduleStart,
            AutoSalarySettings settings)
        {
            if (settings.LateActiveSessionBonusAmount <= 0)
                return;

            BusinessPeriodRange businessDay = BusinessCalendarService.GetBusinessDay(scheduleStart);
            LateActiveSessionReward? reward = LateActiveSessionRewardPolicy.FindFirstReward(
                ActionLogService.GetAllGameSessions(),
                ActionLogService.GetAllShifts(),
                businessDay.StartInclusive,
                businessDay.EndExclusive,
                ClubClock.Current.LocalNow);
            if (reward == null ||
                !result.TryGetValue(reward.EmployeeName, out EmployeeBonusInput? input))
            {
                return;
            }

            input.Bonuses.Add(new AutoSalaryBonusItem
            {
                CreatedAt = reward.QualifiedAt,
                Type = "LateActiveSession",
                Title = "Поздняя активная смена",
                Description = LateActiveSessionRewardPolicy.BuildDescription(reward),
                Amount = settings.LateActiveSessionBonusAmount
            });
        }

        private static void ApplyOverNormBonusForDay(
            Dictionary<string, EmployeeBonusInput> result,
            DateTime businessDayStart,
            DateTime scheduleStart,
            DateTime scheduleEnd,
            AutoSalarySettings settings)
        {
            BusinessPeriodRange businessDay = BusinessCalendarService.GetBusinessDay(businessDayStart);
            if (OverNormPortionService.AppliesTo(businessDay.StartInclusive))
            {
                foreach (var item in OverNormPortionService.GetBonuses(businessDay.StartInclusive))
                {
                    if (!result.TryGetValue(item.EmployeeName, out var input))
                    {
                        input = new EmployeeBonusInput { EmployeeName = item.EmployeeName };
                        result[item.EmployeeName] = input;
                    }
                    input.Bonuses.Add(item.Bonus);
                }
                return;
            }
            if (settings.DailyGameRevenueNorm <= 0 || settings.OverNormBonusPercent <= 0)
                return;
            if (businessDay.StartInclusive < FullDayOverNormBonusEffectiveFrom)
            {
                ApplyLegacyOverNormBonusForDay(
                    result,
                    scheduleStart,
                    scheduleEnd,
                    settings);
                return;
            }

            if (ClubClock.Current.LocalNow < businessDay.EndExclusive)
                return;

            int dayGameRevenue = CashService.GetTotalByPeriodAndCategory(
                businessDay.StartInclusive,
                businessDay.EndExclusive,
                GamesCategory
            );
            int overNormRevenue = dayGameRevenue - settings.DailyGameRevenueNorm;

            if (overNormRevenue <= 0)
                return;

            int bonusFund = Percent(overNormRevenue, settings.OverNormBonusPercent);
            if (bonusFund <= 0)
                return;

            var participants = result.Values
                .Select(input => new OverNormBonusParticipant(
                    input.EmployeeName,
                    GetWorkedHoursInPeriod(
                        input.EmployeeName,
                        businessDay.StartInclusive,
                        businessDay.EndExclusive)))
                .ToList();
            IReadOnlyDictionary<string, int> allocations =
                OverNormBonusAllocator.Allocate(bonusFund, participants);

            foreach (var participant in participants)
            {
                if (!allocations.TryGetValue(participant.EmployeeName, out int amount) ||
                    amount <= 0 ||
                    !result.TryGetValue(participant.EmployeeName, out EmployeeBonusInput? input))
                {
                    continue;
                }

                input.Bonuses.Add(new AutoSalaryBonusItem
                {
                    CreatedAt = businessDay.EndExclusive,
                    Type = "OverNormGameRevenue",
                    Title = "Бонус за план",
                    Description =
                        $"Игры за рабочий день: {dayGameRevenue} сом, выше нормы на {overNormRevenue} сом. " +
                        $"Участие: {participant.Hours:0.##} ч.",
                    Amount = amount
                });
            }
        }

        private static void ApplyLegacyOverNormBonusForDay(
            Dictionary<string, EmployeeBonusInput> result,
            DateTime scheduleStart,
            DateTime scheduleEnd,
            AutoSalarySettings settings)
        {
            int dayGameRevenue = CashService.GetTotalByPeriodAndCategory(
                scheduleStart,
                scheduleEnd,
                GamesCategory);
            int overNormRevenue = dayGameRevenue - settings.DailyGameRevenueNorm;
            int bonusFund = overNormRevenue > 0
                ? Percent(overNormRevenue, settings.OverNormBonusPercent)
                : 0;
            if (bonusFund <= 0)
                return;

            var participants = result.Values
                .Where(input => input.GetDailyHours(scheduleStart.Date) > 0)
                .ToList();
            int distributed = 0;
            for (int index = 0; index < participants.Count; index++)
            {
                EmployeeBonusInput participant = participants[index];
                int amount = Allocate(
                    bonusFund,
                    1,
                    participants.Count,
                    ref distributed,
                    index == participants.Count - 1);
                if (amount <= 0)
                    continue;

                participant.Bonuses.Add(new AutoSalaryBonusItem
                {
                    CreatedAt = scheduleEnd,
                    Type = "OverNormGameRevenue",
                    Title = "Бонус за план",
                    Description = $"Игры за день: {dayGameRevenue} сом, выше нормы на {overNormRevenue} сом.",
                    Amount = amount
                });
            }
        }

        private static double GetWorkedHoursInPeriod(
            string employeeName,
            DateTime fromInclusive,
            DateTime toExclusive)
        {
            var intervals = EmployeeStatsService
                .GetShifts(employeeName, fromInclusive.Date, toExclusive.AddDays(1))
                .Select(shift => new PaidInterval(
                    Max(shift.StartedAt, fromInclusive),
                    Min(shift.ClosedAt ?? ClubClock.Current.LocalNow, toExclusive)))
                .Where(interval => interval.End > interval.Start)
                .OrderBy(interval => interval.Start)
                .ToList();
            if (intervals.Count == 0)
                return 0;

            DateTime start = intervals[0].Start;
            DateTime end = intervals[0].End;
            TimeSpan total = TimeSpan.Zero;
            for (int index = 1; index < intervals.Count; index++)
            {
                PaidInterval interval = intervals[index];
                if (interval.Start <= end)
                {
                    end = Max(end, interval.End);
                    continue;
                }

                total += end - start;
                start = interval.Start;
                end = interval.End;
            }

            total += end - start;
            return total.TotalHours;
        }

        private static TimeSpan GetLateActiveTime(
            string employeeName,
            DateTime shiftStart,
            DateTime shiftEnd,
            DateTime scheduleEnd)
        {
            if (shiftEnd <= scheduleEnd)
                return TimeSpan.Zero;

            var intervals = EmployeeStatsService
                .GetGameSessionsForMonth(employeeName, scheduleEnd)
                .Select(session => new
                {
                    Start = Max(session.StartedAt, Max(shiftStart, scheduleEnd)),
                    End = Min(session.ClosedAt ?? ClubClock.Current.LocalNow, shiftEnd)
                })
                .Where(interval => interval.End > interval.Start)
                .OrderBy(interval => interval.Start)
                .ToList();

            if (intervals.Count == 0)
                return TimeSpan.Zero;

            DateTime currentStart = intervals[0].Start;
            DateTime currentEnd = intervals[0].End;
            TimeSpan total = TimeSpan.Zero;

            for (int index = 1; index < intervals.Count; index++)
            {
                var interval = intervals[index];
                if (interval.Start <= currentEnd)
                {
                    if (interval.End > currentEnd)
                        currentEnd = interval.End;

                    continue;
                }

                total += currentEnd - currentStart;
                currentStart = interval.Start;
                currentEnd = interval.End;
            }

            total += currentEnd - currentStart;
            return total;
        }

        private static TimeSpan GetOverlap(
            DateTime firstStart,
            DateTime firstEnd,
            DateTime secondStart,
            DateTime secondEnd)
        {
            DateTime start = Max(firstStart, secondStart);
            DateTime end = Min(firstEnd, secondEnd);
            return end > start ? end - start : TimeSpan.Zero;
        }

        private static DateTime GetScheduleStart(
            DateTime day,
            AutoSalarySettings settings)
        {
            return day.Date.AddHours(settings.WorkDayStartHour);
        }

        private static DateTime GetScheduleEnd(
            DateTime day,
            AutoSalarySettings settings)
        {
            DateTime start = GetScheduleStart(day, settings);
            DateTime end = day.Date.AddHours(settings.WorkDayEndHour);

            if (end <= start)
                end = end.AddDays(1);

            return end;
        }

        private static DateTime Max(DateTime first, DateTime second)
        {
            return first >= second ? first : second;
        }

        private static DateTime Min(DateTime first, DateTime second)
        {
            return first <= second ? first : second;
        }

        private static string FormatHour(int hour)
        {
            return $"{NormalizeHour(hour):00}:00";
        }


        private static int Percent(int amount, int percent)
        {
            return (int)Math.Round(amount * (percent / 100.0));
        }

        private static int Allocate(
            int fund,
            double value,
            double total,
            ref int distributed,
            bool isLast)
        {
            if (fund <= 0 || total <= 0 || value <= 0)
                return 0;

            int amount = isLast
                ? fund - distributed
                : (int)Math.Round(fund * (value / total));

            if (amount < 0)
                amount = 0;

            distributed += amount;
            return amount;
        }

        private static AutoSalarySettings NormalizeSettings(AutoSalarySettings settings)
        {
            if (settings == null)
                settings = new AutoSalarySettings();

            settings.ExpenseReservePercent = ClampPercent(settings.ExpenseReservePercent);
            settings.SalaryFundPercent = ClampPercent(settings.SalaryFundPercent);
            settings.TimeSharePercent = ClampPercent(settings.TimeSharePercent);
            settings.GameRevenueSharePercent = ClampPercent(settings.GameRevenueSharePercent);
            settings.TimeMonthlyFundAmount = Math.Max(0, settings.TimeMonthlyFundAmount);
            settings.TimeMonthlyPlannedHours = Math.Max(1, settings.TimeMonthlyPlannedHours);
            settings.ProductRevenueSharePercent = 0;
            settings.ProductBonusPercent = ClampPercent(settings.ProductBonusPercent);
            settings.WorkDayStartHour = NormalizeHour(settings.WorkDayStartHour);
            settings.WorkDayEndHour = NormalizeHour(settings.WorkDayEndHour);
            settings.DailyGameRevenueNorm = Math.Max(0, settings.DailyGameRevenueNorm);
            settings.OverNormBonusPercent = ClampPercent(settings.OverNormBonusPercent);
            settings.PunctualityBonusAmount = Math.Max(0, settings.PunctualityBonusAmount);
            settings.LateActiveSessionBonusAmount = Math.Max(0, settings.LateActiveSessionBonusAmount);
            settings.OpeningResponsibleEmployeeName =
                settings.OpeningResponsibleEmployeeName?.Trim() ?? "";
            settings.LateOpeningGraceMinutes = settings.LateOpeningGraceMinutes <= 0
                ? 30
                : settings.LateOpeningGraceMinutes;
            settings.LateOpeningPenaltyStepMinutes = settings.LateOpeningPenaltyStepMinutes <= 0
                ? 30
                : settings.LateOpeningPenaltyStepMinutes;
            settings.LateOpeningPenaltyStepAmount = settings.LateOpeningPenaltyStepAmount <= 0
                ? 50
                : settings.LateOpeningPenaltyStepAmount;
            settings.LateOpeningMaxAutoMinutes = settings.LateOpeningMaxAutoMinutes <= 0
                ? 150
                : settings.LateOpeningMaxAutoMinutes;

            int shareTotal =
                settings.TimeSharePercent +
                settings.GameRevenueSharePercent;

            if (shareTotal <= 0)
            {
                settings.TimeSharePercent = 45;
                settings.GameRevenueSharePercent = 55;
                return settings;
            }

            if (shareTotal != 100)
            {
                settings.TimeSharePercent = (int)Math.Round(settings.TimeSharePercent * 100.0 / shareTotal);
                settings.GameRevenueSharePercent = 100 - settings.TimeSharePercent;
            }

            return settings;
        }

        private static int ClampPercent(int value)
        {
            if (value < 0)
                return 0;

            if (value > 100)
                return 100;

            return value;
        }

        private static int NormalizeHour(int hour)
        {
            if (hour < 0)
                return 0;

            if (hour > 23)
                return 23;

            return hour;
        }

        private class EmployeeBonusInput
        {
            private readonly Dictionary<DateTime, double> _dailyHours =
                new Dictionary<DateTime, double>();

            public string EmployeeName { get; set; } = "";

            public double WorkHours { get; set; }

            public List<PaidInterval> PaidIntervals { get; set; } = new();

            public List<AutoSalaryBonusItem> Bonuses { get; set; } =
                new List<AutoSalaryBonusItem>();

            public void AddDailyHours(DateTime day, double hours)
            {
                day = day.Date;

                if (!_dailyHours.ContainsKey(day))
                    _dailyHours[day] = 0;

                _dailyHours[day] += hours;
            }

            public double GetDailyHours(DateTime day)
            {
                day = day.Date;
                return _dailyHours.TryGetValue(day, out double hours) ? hours : 0;
            }

            public void AddPaidInterval(DateTime start, DateTime end)
            {
                if (end > start)
                    PaidIntervals.Add(new PaidInterval(start, end));
            }
        }

    }
}
