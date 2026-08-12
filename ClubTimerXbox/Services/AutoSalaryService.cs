using System;
using System.Collections.Generic;
using System.Linq;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public static class AutoSalaryService
    {
        private const string GamesCategory = "\u0418\u0433\u0440\u044b";
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
            var period = BusinessCalendarService.GetBusinessMonthByAnchor(monthStart);
            monthStart = period.StartInclusive;
            DateTime nextMonthStart = period.EndExclusive;

            EmployeeRatingService.SynchronizeConfirmedLosses();
            EmployeeRatingService.SynchronizeConfirmedCashExtras();
            SalaryPolicyVersion latestPolicy = LatestPolicyVersion;
            AutoSalarySettings displaySettings = NormalizeSettings(
                SalaryPolicyHistoryService.Clone(latestPolicy.Settings));

            int gameRevenue = CashService.GetTotalByPeriodAndCategory(
                monthStart,
                nextMonthStart,
                GamesCategory
            );
            int productRevenue = ProductServiceRevenueService.GetTotal(
                monthStart,
                nextMonthStart
            );
            var gameFund = CalculateGameFund(monthStart, nextMonthStart);
            int expenseReserve = gameFund.ExpenseReserve;
            int salaryBase = Math.Max(0, gameRevenue - expenseReserve);
            int salaryFund = gameFund.SalaryFund;
            int plannedTimeFund = Math.Max(0, displaySettings.TimeMonthlyFundAmount);

            var report = new AutoSalaryReport
            {
                MonthKey = period.Key,
                Settings = displaySettings,
                SettingsEffectiveFrom = latestPolicy.EffectiveFrom,
                HasPendingSettings = latestPolicy.EffectiveFrom > ClubClock.Current.LocalNow,
                GameRevenue = gameRevenue,
                ProductRevenue = productRevenue,
                ExpenseReserveAmount = expenseReserve,
                SalaryBaseAmount = salaryBase,
                SalaryFundAmount = salaryFund,
                TimeFundAmount = plannedTimeFund,
                GameRevenueFundAmount = salaryFund,
                ProductShareFundAmount = 0
            };

            var bonusInputs = BuildEmployeeBonusInputs(monthStart, nextMonthStart);
            var employeeInputs = EmployeeService
                .GetAllEmployees()
                .Where(employee => employee.IsActive)
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

                    return new EmployeeSalaryInput
                    {
                        EmployeeId = employee.EmployeeId,
                        EmployeeName = employee.Name,
                        Summary = summary,
                        PaidSalary = paidSalary,
                        WorkHours = bonusInput.WorkHours,
                        PaidIntervals = bonusInput.PaidIntervals,
                        Bonuses = bonusInput.Bonuses
                    };
                })
                .ToList();

            for (int index = 0; index < employeeInputs.Count; index++)
            {
                var input = employeeInputs[index];

                RatingAccrualBreakdown timeAccrual = CalculateTimeAccrual(
                    input,
                    monthStart,
                    nextMonthStart);
                RatingAccrualBreakdown gameAccrual = CalculateGameRevenueAccrual(
                    input.EmployeeName,
                    monthStart,
                    nextMonthStart);
                int timeAmount = timeAccrual.Amount;
                int gameAmount = gameAccrual.Amount;
                int timeRatingEarnedAmount = timeAccrual.EarnedAmount;
                int timeRatingLostAmount = timeAccrual.LostAmount;
                int gameRatingEarnedAmount = gameAccrual.EarnedAmount;
                int gameRatingLostAmount = gameAccrual.LostAmount;
                int productBonus = CalculateProductBonusAmount(
                    input.EmployeeName,
                    monthStart,
                    nextMonthStart);
                int bonusAmount = input.Bonuses.Sum(bonus => bonus.Amount);
                int gross = timeAmount + gameAmount + productBonus + bonusAmount;
                int losses = input.Summary.MonthUnpaidLosses;
                int paid = input.PaidSalary;

                if (BusinessAccountingService.TryGetClosedPayroll(
                        report.MonthKey,
                        input.EmployeeName,
                        out var closedPayroll))
                {
                    gross = closedPayroll.AccruedAmount + closedPayroll.BonusAmount;
                    losses = closedPayroll.PenaltyAmount;
                    paid = closedPayroll.PaidAmount;
                    if (closedPayroll.RatingFinancialEffectCaptured)
                    {
                        timeRatingEarnedAmount = closedPayroll.TimeRatingEarnedAmount;
                        timeRatingLostAmount = closedPayroll.TimeRatingLostAmount;
                        gameRatingEarnedAmount = closedPayroll.GameRatingEarnedAmount;
                        gameRatingLostAmount = closedPayroll.GameRatingLostAmount;
                    }
                }

                int currentRemaining = gross - losses - paid;
                string currentMonthKey = BusinessCalendarService
                    .GetBusinessMonth(ClubClock.Current.LocalNow)
                    .Key;
                int carryIn = report.MonthKey.Equals(
                        currentMonthKey,
                        StringComparison.OrdinalIgnoreCase)
                    ? BusinessAccountingService.GetCarriedSalary(
                        input.EmployeeName,
                        report.MonthKey)
                    : 0;
                int remaining = currentRemaining + carryIn;

                report.ProductBonusTotalAmount += productBonus;
                report.BonusTotalAmount += bonusAmount;
                DateTime ratingAt = nextMonthStart <= ClubClock.Current.LocalNow
                    ? nextMonthStart.AddTicks(-1)
                    : ClubClock.Current.LocalNow;
                var rating = EmployeeRatingService.GetSnapshot(
                    input.EmployeeName,
                    ratingAt);
                report.Employees.Add(new AutoSalaryEmployeeResult
                {
                    EmployeeId = input.EmployeeId,
                    EmployeeName = input.EmployeeName,
                    WorkHours = Math.Round(input.WorkHours, 2),
                    GameRevenue = input.Summary.MonthGameIncome,
                    ProductRevenue = input.Summary.MonthProductsIncome,
                    TimeAmount = timeAmount,
                    GameRevenueAmount = gameAmount,
                    ProductShareAmount = 0,
                    ProductBonusAmount = productBonus,
                    BonusAmount = bonusAmount,
                    Bonuses = input.Bonuses
                        .OrderByDescending(bonus => bonus.CreatedAt)
                        .ToList(),
                    GrossAmount = gross,
                    LossesAmount = losses,
                    MoneyLossesAmount = input.Summary.MonthUnpaidMoneyLosses,
                    RawMoneyLossesAmount = input.Summary.MonthRawUnpaidMoneyLosses,
                    ProductLossesAmount = input.Summary.MonthUnpaidProductLosses,
                    ViolationLossesAmount = input.Summary.MonthUnpaidViolationLosses,
                    PaidAmount = paid,
                    CarryInAmount = carryIn,
                    CurrentPeriodRemainingAmount = currentRemaining,
                    RemainingAmount = remaining,
                    TimeRatingPercent = rating.TimePercent,
                    RevenueRatingPercent = rating.RevenuePercent,
                    OverallRatingPercent = rating.OverallPercent,
                    RatingHasWarning = rating.HasWarning,
                    TimeRatingEarnedAmount = timeRatingEarnedAmount,
                    TimeRatingLostAmount = timeRatingLostAmount,
                    GameRatingEarnedAmount = gameRatingEarnedAmount,
                    GameRatingLostAmount = gameRatingLostAmount,
                    RatingEvents = rating.History
                        .Where(item =>
                            item.EffectiveFrom < nextMonthStart &&
                            item.EffectiveUntil > monthStart)
                        .ToList()
                });
            }

            return report;
        }

        private static Dictionary<string, EmployeeBonusInput> BuildEmployeeBonusInputs(
            DateTime monthStart,
            DateTime nextMonthStart)
        {
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
                ApplyLateActiveBonusForDay(result, scheduleStart, scheduleEnd, settings);
                ApplyOverNormBonusForDay(result, scheduleStart, scheduleEnd, settings);

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
            DateTime scheduleEnd,
            AutoSalarySettings settings)
        {
            if (settings.LateActiveSessionBonusAmount <= 0)
                return;

            foreach (var input in result.Values)
            {
                var shifts = EmployeeStatsService.GetShifts(
                    input.EmployeeName,
                    scheduleEnd,
                    scheduleEnd.AddHours(8)
                );

                var sessions = EmployeeStatsService
                    .GetGameSessionsForMonth(input.EmployeeName, scheduleEnd);
                bool hasLateActiveSession = shifts.Any(shift =>
                    shift.StartedAt <= scheduleEnd &&
                    (shift.ClosedAt ?? ClubClock.Current.LocalNow) > scheduleEnd &&
                    sessions.Any(session =>
                        session.StartedAt <= scheduleEnd.AddMinutes(-20) &&
                        (session.ClosedAt ?? ClubClock.Current.LocalNow) > scheduleEnd));

                if (!hasLateActiveSession)
                    continue;

                input.Bonuses.Add(new AutoSalaryBonusItem
                {
                    CreatedAt = scheduleEnd,
                    Type = "LateActiveSession",
                    Title = "Поздняя активная смена",
                    Description = $"После {FormatHour(settings.WorkDayEndHour)} были активные игровые сеансы.",
                    Amount = settings.LateActiveSessionBonusAmount
                });
            }
        }

        private static void ApplyOverNormBonusForDay(
            Dictionary<string, EmployeeBonusInput> result,
            DateTime scheduleStart,
            DateTime scheduleEnd,
            AutoSalarySettings settings)
        {
            if (settings.DailyGameRevenueNorm <= 0 || settings.OverNormBonusPercent <= 0)
                return;

            int dayGameRevenue = CashService.GetTotalByPeriodAndCategory(
                scheduleStart,
                scheduleEnd,
                GamesCategory
            );
            int overNormRevenue = dayGameRevenue - settings.DailyGameRevenueNorm;

            if (overNormRevenue <= 0)
                return;

            int bonusFund = Percent(overNormRevenue, settings.OverNormBonusPercent);
            if (bonusFund <= 0)
                return;

            var participants = result.Values
                .Select(input => new
                {
                    Input = input,
                    Hours = input.GetDailyHours(scheduleStart.Date)
                })
                .Where(item => item.Hours > 0)
                .ToList();

            int distributed = 0;

            for (int index = 0; index < participants.Count; index++)
            {
                var participant = participants[index];
                int amount = Allocate(
                    bonusFund,
                    1,
                    participants.Count,
                    ref distributed,
                    index == participants.Count - 1
                );

                if (amount <= 0)
                    continue;

                participant.Input.Bonuses.Add(new AutoSalaryBonusItem
                {
                    CreatedAt = scheduleEnd,
                    Type = "OverNormGameRevenue",
                    Title = "Бонус за план",
                    Description = $"Игры за день: {dayGameRevenue} сом, выше нормы на {overNormRevenue} сом.",
                    Amount = amount
                });
            }
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

        private static RatingAccrualBreakdown CalculateTimeAccrual(
            EmployeeSalaryInput input,
            DateTime monthStart,
            DateTime nextMonthStart)
        {
            var groups = new Dictionary<TimeAccrualGroupKey, double>();
            double intervalHours = 0;
            foreach (var interval in input.PaidIntervals)
            {
                foreach (var segment in SplitBySalaryRules(
                             input.EmployeeName,
                             interval.Start,
                             interval.End))
                {
                    SalaryPolicyVersion policy =
                        SalaryPolicyHistoryService.GetVersionAt(segment.Start);
                    AutoSalarySettings settings = policy.Settings;
                    int rating = EmployeeRatingService.GetPercent(
                        input.EmployeeName,
                        EmployeeRatingBranch.Time,
                        segment.Start);
                    double hours = (segment.End - segment.Start).TotalHours;
                    intervalHours += hours;
                    var key = new TimeAccrualGroupKey(
                        policy.Id,
                        EmployeeRatingService.GetAccrualSignature(
                            input.EmployeeName,
                            EmployeeRatingBranch.Time,
                            segment.Start),
                        settings.TimeMonthlyFundAmount,
                        settings.TimeMonthlyPlannedHours,
                        rating);
                    groups[key] = groups.TryGetValue(key, out double total)
                        ? total + hours
                        : hours;
                }
            }

            double protectedExtraHours = Math.Max(0, input.WorkHours - intervalHours);
            if (protectedExtraHours > 0)
            {
                // Recovered hours represent an older protected balance. Pinning them to
                // the month opening prevents a later rating change from repricing them.
                DateTime at = monthStart;
                SalaryPolicyVersion policy = SalaryPolicyHistoryService.GetVersionAt(at);
                AutoSalarySettings settings = policy.Settings;
                int rating = EmployeeRatingService.GetPercent(
                    input.EmployeeName,
                    EmployeeRatingBranch.Time,
                    at);
                var key = new TimeAccrualGroupKey(
                    policy.Id,
                    EmployeeRatingService.GetAccrualSignature(
                        input.EmployeeName,
                        EmployeeRatingBranch.Time,
                        at),
                    settings.TimeMonthlyFundAmount,
                    settings.TimeMonthlyPlannedHours,
                    rating);
                groups[key] = groups.TryGetValue(key, out double total)
                    ? total + protectedExtraHours
                    : protectedExtraHours;
            }

            int amount = 0;
            int earnedAmount = 0;
            int lostAmount = 0;
            foreach (var group in groups)
            {
                var settings = new AutoSalarySettings
                {
                    TimeMonthlyFundAmount = group.Key.MonthlyFund,
                    TimeMonthlyPlannedHours = group.Key.PlannedHours
                };
                double baseline = EmployeeSalaryRuleEngine.CalculateTimeAccrual(
                    group.Value,
                    settings,
                    100);
                RatingFinancialEffect effect =
                    EmployeeSalaryRuleEngine.CalculateRatingFinancialEffect(
                        baseline,
                        group.Key.Rating);
                amount += effect.ActualAmount;
                earnedAmount += effect.EarnedAmount;
                lostAmount += effect.LostAmount;
            }

            return new RatingAccrualBreakdown(
                Math.Max(0, amount),
                earnedAmount,
                lostAmount);
        }

        private static RatingAccrualBreakdown CalculateGameRevenueAccrual(
            string employeeName,
            DateTime fromInclusive,
            DateTime toExclusive)
        {
            var groups = CashService.GetRecordsByPeriod(fromInclusive, toExclusive)
                .Where(record =>
                    record.Category == GamesCategory &&
                    record.IncomeEmployeeName.Equals(
                        employeeName,
                        StringComparison.OrdinalIgnoreCase))
                .Select(record =>
                {
                    DateTime at = CashService.GetBusinessTime(record);
                    SalaryPolicyVersion policy =
                        SalaryPolicyHistoryService.GetVersionAt(at);
                    AutoSalarySettings settings = policy.Settings;
                    int rating = EmployeeRatingService.GetPercent(
                        employeeName,
                        EmployeeRatingBranch.Revenue,
                        at);
                    return new
                    {
                        record.Amount,
                        PolicyId = policy.Id,
                        RatingSignature = EmployeeRatingService.GetAccrualSignature(
                            employeeName,
                            EmployeeRatingBranch.Revenue,
                            at),
                        settings.ExpenseReservePercent,
                        settings.SalaryFundPercent,
                        Rating = rating
                    };
                })
                .GroupBy(item => new
                {
                    item.PolicyId,
                    item.RatingSignature,
                    item.ExpenseReservePercent,
                    item.SalaryFundPercent,
                    item.Rating
                });

            double amount = 0;
            double baselineAmount = 0;
            double earnedAmount = 0;
            double lostAmount = 0;
            foreach (var group in groups)
            {
                int revenue = group.Sum(item => item.Amount);
                int reserve = Percent(revenue, group.Key.ExpenseReservePercent);
                int salaryBeforeRating = Percent(
                    Math.Max(0, revenue - reserve),
                    group.Key.SalaryFundPercent);
                double ratedAmount = salaryBeforeRating * group.Key.Rating / 100.0;
                amount += ratedAmount;
                baselineAmount += salaryBeforeRating;
                double difference = ratedAmount - salaryBeforeRating;
                if (difference >= 0)
                    earnedAmount += difference;
                else
                    lostAmount += -difference;
            }

            int actual = Math.Max(0, (int)Math.Round(amount));
            int baseline = Math.Max(0, (int)Math.Round(baselineAmount));
            int earned = Math.Max(0, (int)Math.Round(earnedAmount));
            int lost = Math.Max(0, (int)Math.Round(lostAmount));

            // Separate rounding of gains and losses may differ by one som from
            // the already fixed salary amount. Keep the displayed net exact.
            int reconciliation = (actual - baseline) - (earned - lost);
            if (reconciliation > 0)
                earned += reconciliation;
            else if (reconciliation < 0)
                lost += -reconciliation;

            return new RatingAccrualBreakdown(actual, earned, lost);
        }

        private static int CalculateProductBonusAmount(
            string employeeName,
            DateTime fromInclusive,
            DateTime toExclusive)
        {
            var groups = ProductServiceRevenueService
                .GetEntries(fromInclusive, toExclusive, employeeName)
                .Select(entry =>
                {
                    SalaryPolicyVersion policy =
                        SalaryPolicyHistoryService.GetVersionAt(entry.OccurredAt);
                    return new
                    {
                        entry.Amount,
                        PolicyId = policy.Id,
                        policy.Settings.ProductBonusPercent
                    };
                })
                .GroupBy(item => new { item.PolicyId, item.ProductBonusPercent });
            int amount = groups.Sum(group => Percent(
                group.Sum(item => item.Amount),
                group.Key.ProductBonusPercent));
            return Math.Max(0, amount);
        }

        private static (int ExpenseReserve, int SalaryFund) CalculateGameFund(
            DateTime fromInclusive,
            DateTime toExclusive)
        {
            var groups = CashService.GetRecordsByPeriod(fromInclusive, toExclusive)
                .Where(record => record.Category == GamesCategory)
                .Select(record =>
                {
                    SalaryPolicyVersion policy = SalaryPolicyHistoryService.GetVersionAt(
                        CashService.GetBusinessTime(record));
                    AutoSalarySettings settings = policy.Settings;
                    return new
                    {
                        record.Amount,
                        PolicyId = policy.Id,
                        settings.ExpenseReservePercent,
                        settings.SalaryFundPercent
                    };
                })
                .GroupBy(item => new
                {
                    item.PolicyId,
                    item.ExpenseReservePercent,
                    item.SalaryFundPercent
                });
            int reserve = 0;
            int salary = 0;
            foreach (var group in groups)
            {
                int revenue = group.Sum(item => item.Amount);
                int groupReserve = Percent(revenue, group.Key.ExpenseReservePercent);
                reserve += groupReserve;
                salary += Percent(
                    Math.Max(0, revenue - groupReserve),
                    group.Key.SalaryFundPercent);
            }

            return (
                Math.Max(0, reserve),
                Math.Max(0, salary));
        }

        private static IEnumerable<PaidInterval> SplitBySalaryRules(
            string employeeName,
            DateTime start,
            DateTime end)
        {
            var boundaries = SalaryPolicyHistoryService
                .GetVersions(start, end)
                .Select(item => item.EffectiveFrom)
                .Concat(EmployeeRatingService.GetBoundaries(employeeName, start, end))
                .Where(value => value > start && value < end)
                .Distinct()
                .OrderBy(value => value)
                .ToList();
            DateTime cursor = start;
            foreach (DateTime boundary in boundaries)
            {
                yield return new PaidInterval(cursor, boundary);
                cursor = boundary;
            }

            if (end > cursor)
                yield return new PaidInterval(cursor, end);
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

        private class EmployeeSalaryInput
        {
            public string EmployeeId { get; set; } = "";

            public string EmployeeName { get; set; } = "";

            public EmployeeStatsSummary Summary { get; set; } = new EmployeeStatsSummary();

            public int PaidSalary { get; set; }

            public double WorkHours { get; set; }

            public List<PaidInterval> PaidIntervals { get; set; } = new();

            public List<AutoSalaryBonusItem> Bonuses { get; set; } =
                new List<AutoSalaryBonusItem>();
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

        private readonly record struct PaidInterval(DateTime Start, DateTime End);

        private readonly record struct TimeAccrualGroupKey(
            Guid PolicyId,
            string RatingSignature,
            int MonthlyFund,
            int PlannedHours,
            int Rating);

        private readonly record struct RatingAccrualBreakdown(
            int Amount,
            int EarnedAmount,
            int LostAmount);
    }
}
