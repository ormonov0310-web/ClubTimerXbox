using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services;

internal sealed record SalaryMonthCalculation(AutoSalaryReport Report,
    IReadOnlyDictionary<string, List<AutoSalaryDayEarning>> DailyEarnings);

// All inputs are owned by one calculation. No live financial service is called here.
internal sealed class CapturedSalaryMonth
{
    internal sealed class EmployeeInput
    {
        public string EmployeeId { get; init; } = "";
        public string EmployeeName { get; init; } = "";
        public EmployeeStatsSummary Summary { get; init; } = new();
        public int PaidSalary { get; init; }
        public double WorkHours { get; init; }
        public List<PaidInterval> PaidIntervals { get; init; } = new();
        public List<AutoSalaryBonusItem> Bonuses { get; init; } = new();
        public EmployeePayrollObligation? ClosedPayroll { get; init; }
        public int DebtTransferred { get; init; }
        public int CarryIn { get; init; }
        public EmployeeRatingSnapshot Rating { get; init; } = new();
    }

    private readonly AutoSalaryReport _template;
    private readonly EmployeeInput[] _employees;
    private readonly SalaryAccrualSource _accrual;
    internal DateTime MonthStart { get; }

    internal CapturedSalaryMonth(DateTime monthStart, AutoSalaryReport template,
        IEnumerable<EmployeeInput> employees, SalaryAccrualSource accrual)
    {
        MonthStart = BusinessCalendarService.GetBusinessMonthByAnchor(monthStart).StartInclusive;
        _template = Copy(template);
        _employees = employees.Select(Copy).ToArray();
        _accrual = accrual;
    }

    internal SalaryMonthCalculation Calculate(bool includeDaily = true)
    {
        DateTime monthStart = MonthStart;
        DateTime nextMonthStart = monthStart.AddMonths(1);
        var report = Copy(_template);
        var accrual = _accrual;
        var employeeInputs = _employees;
        var fund = SalaryAccrualCalculator.GameFund(monthStart, nextMonthStart, accrual);
        report.ExpenseReserveAmount = fund.ExpenseReserve;
        report.SalaryBaseAmount = Math.Max(0, report.GameRevenue - fund.ExpenseReserve);
        report.SalaryFundAmount = fund.SalaryFund;
        report.GameRevenueFundAmount = fund.SalaryFund;
        for (int index = 0; index < employeeInputs.Length; index++)
        {
            var input = employeeInputs[index];

            RatingAccrualBreakdown timeAccrual = SalaryAccrualCalculator.Time(
                input.EmployeeName, input.WorkHours, input.PaidIntervals, monthStart, accrual);
            RatingAccrualBreakdown gameAccrual = SalaryAccrualCalculator.Games(
                input.EmployeeName,
                monthStart,
                nextMonthStart, accrual);
            int timeAmount = timeAccrual.Amount;
            int gameAmount = gameAccrual.Amount;
            int timeRatingEarnedAmount = timeAccrual.EarnedAmount;
            int timeRatingLostAmount = timeAccrual.LostAmount;
            int gameRatingEarnedAmount = gameAccrual.EarnedAmount;
            int gameRatingLostAmount = gameAccrual.LostAmount;
            int productBonus = SalaryAccrualCalculator.Products(
                input.EmployeeName,
                monthStart,
                nextMonthStart, accrual);
            int bonusAmount = input.Bonuses.Sum(bonus => bonus.Amount);
            int gross = timeAmount + gameAmount + productBonus + bonusAmount;
            int losses = input.Summary.MonthUnpaidLosses;
            int paid = input.PaidSalary;

            if (input.ClosedPayroll is { } closedPayroll)
            {
                gross = closedPayroll.AccruedAmount + closedPayroll.BonusAmount;
                losses = closedPayroll.PenaltyAmount;
                paid = closedPayroll.PaidAmount;
                timeAmount = closedPayroll.TimeAmount;
                gameAmount = closedPayroll.GameRevenueAmount;
                productBonus = closedPayroll.ProductBonusAmount;
                bonusAmount = Math.Max(
                    0,
                    gross - timeAmount - gameAmount - productBonus);
                if (closedPayroll.RatingFinancialEffectCaptured)
                {
                    timeRatingEarnedAmount = closedPayroll.TimeRatingEarnedAmount;
                    timeRatingLostAmount = closedPayroll.TimeRatingLostAmount;
                    gameRatingEarnedAmount = closedPayroll.GameRatingEarnedAmount;
                    gameRatingLostAmount = closedPayroll.GameRatingLostAmount;
                }
            }

            int debtTransferred = input.DebtTransferred;
            int currentRemaining = gross - losses - paid - debtTransferred;
            int carryIn = input.CarryIn;
            int remaining = currentRemaining + carryIn;

            report.ProductBonusTotalAmount += productBonus;
            report.BonusTotalAmount += bonusAmount;
            var rating = input.Rating;
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
                DebtTransferredAmount = debtTransferred,
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


        var daily = new Dictionary<string, List<AutoSalaryDayEarning>>(StringComparer.OrdinalIgnoreCase);
        if (!includeDaily) return new(report, daily);
        foreach (var input in employeeInputs)
        {
            var monthly = report.Employees.First(item => item.EmployeeName.Equals(input.EmployeeName, StringComparison.OrdinalIgnoreCase));
            daily.Add(input.EmployeeName, input.ClosedPayroll?.DailyEarnings.Count > 0
                ? input.ClosedPayroll.DailyEarnings.Select(Copy).OrderByDescending(item => item.Date).ToList()
                : CalculateDaily(input.EmployeeName, monthStart, monthly, input.WorkHours, input.PaidIntervals, accrual));
        }
        return new(report, daily);
    }

    internal static List<AutoSalaryDayEarning> CalculateDaily(string employeeName, DateTime monthStart,
        AutoSalaryEmployeeResult monthly, double workHours, IReadOnlyList<PaidInterval> intervals, SalaryAccrualSource accrual)
    {
        DateTime nextMonthStart = monthStart.AddMonths(1);
        var result = new Dictionary<DateTime, AutoSalaryDayEarning>();
        double intervalHours = intervals.Sum(interval =>
            Math.Max(0, (interval.End - interval.Start).TotalHours));
        double protectedExtraHours = Math.Max(0, workHours - intervalHours);

        for (DateTime dayStart = monthStart;
             dayStart < nextMonthStart;
             dayStart = dayStart.AddDays(1))
        {
            DateTime dayEnd = dayStart.AddDays(1);
            var dailyIntervals = intervals
                .Select(interval => new PaidInterval(
                    Max(interval.Start, dayStart),
                    Min(interval.End, dayEnd)))
                .Where(interval => interval.End > interval.Start)
                .ToList();
            double dailyHours = dailyIntervals.Sum(interval =>
                (interval.End - interval.Start).TotalHours);
            double recoveredHours = dayStart == monthStart
                ? protectedExtraHours
                : 0;
            RatingAccrualBreakdown timeAccrual = SalaryAccrualCalculator.Time(
                employeeName, dailyHours + recoveredHours, dailyIntervals, monthStart, accrual);
            RatingAccrualBreakdown gameAccrual = SalaryAccrualCalculator.Games(
                employeeName,
                dayStart,
                dayEnd, accrual);
            int productBonus = SalaryAccrualCalculator.Products(
                employeeName,
                dayStart,
                dayEnd, accrual);

            if (dailyHours <= 0 && recoveredHours <= 0 &&
                timeAccrual.Amount == 0 && gameAccrual.Amount == 0 &&
                productBonus == 0)
            {
                continue;
            }

            DateTime businessDate = BusinessCalendarService.GetBusinessDate(dayStart);
            result[businessDate] = new AutoSalaryDayEarning
            {
                Date = businessDate,
                WorkHours = Math.Round(dailyHours + recoveredHours, 2),
                TimeAmount = timeAccrual.Amount,
                TimeRatingEarnedAmount = timeAccrual.EarnedAmount,
                TimeRatingLostAmount = timeAccrual.LostAmount,
                TimeRatingPercents = timeAccrual.RatingPercents.ToList(),
                GameAmount = gameAccrual.Amount,
                GameRatingEarnedAmount = gameAccrual.EarnedAmount,
                GameRatingLostAmount = gameAccrual.LostAmount,
                GameRatingPercents = gameAccrual.RatingPercents.ToList(),
                ProductServiceBonusAmount = productBonus
            };
        }

        DateTime firstBusinessDate = BusinessCalendarService.GetBusinessDate(monthStart);
        DateTime lastBusinessDate = BusinessCalendarService.GetBusinessDate(
            nextMonthStart.AddTicks(-1));
        // Use the same salary snapshot as the monthly total, even if a new portion just arrived.
        foreach (var bonus in monthly.Bonuses.Where(item => item.Amount > 0))
        {
            DateTime businessDate = bonus.GetBusinessDate();
            if (businessDate < firstBusinessDate)
                businessDate = firstBusinessDate;
            else if (businessDate > lastBusinessDate)
                businessDate = lastBusinessDate;

            if (!result.TryGetValue(businessDate, out var day))
            {
                day = new AutoSalaryDayEarning { Date = businessDate };
                result[businessDate] = day;
            }

            day.OtherBonusAmount += bonus.Amount;
        }

        var days = result.Values.ToList();
        EmployeeDailyEarningReconciler.Reconcile(
            days,
            firstBusinessDate,
            monthly.TimeAmount,
            monthly.GameRevenueAmount,
            monthly.ProductBonusAmount,
            monthly.BonusAmount,
            monthly.TimeRatingEarnedAmount,
            monthly.TimeRatingLostAmount,
            monthly.GameRatingEarnedAmount,
            monthly.GameRatingLostAmount);

        return days
            .Where(item => item.WorkHours > 0 || item.TotalAmount != 0)
            .OrderByDescending(item => item.Date)
            .ToList();
    }

    private static DateTime Max(DateTime first, DateTime second) => first >= second ? first : second;
    private static DateTime Min(DateTime first, DateTime second) => first <= second ? first : second;
    private static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.SerializeToUtf8Bytes(value))!;
}
