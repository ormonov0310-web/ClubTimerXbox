using System;
using System.Linq;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public sealed class CashBalanceSummary
    {
        public int? ActualCashBalance { get; set; }

        public int ExpectedCashBalance { get; set; }

        public int? ProgramCashBalance { get; set; }

        public int? ActualCashlessBalance { get; set; }

        public int ExpectedCashlessBalance { get; set; }

        public int? ProgramCashlessBalance { get; set; }

        public bool HasFullActualBalance => ActualCashBalance.HasValue && ActualCashlessBalance.HasValue;

        public int ProgramTotal => ExpectedCashBalance + ExpectedCashlessBalance;

        public int? ActualTotal => HasFullActualBalance
            ? ActualCashBalance!.Value + ActualCashlessBalance!.Value
            : null;

        public int? Difference => ActualTotal.HasValue
            ? ActualTotal.Value - ProgramTotal
            : null;

        public int MoneyShortage => Difference.HasValue && Difference.Value < 0
            ? Math.Abs(Difference.Value)
            : 0;

        public int MoneyExtra => Difference.HasValue && Difference.Value > 0
            ? Difference.Value
            : 0;
    }

    public static class CashBalanceSummaryService
    {
        public static CashBalanceSummary Build(DateTime fromInclusive, DateTime toExclusive)
        {
            return new CashBalanceSummary
            {
                ActualCashBalance = CalculateActualCashBalanceByPeriod(fromInclusive, toExclusive),
                ExpectedCashBalance = CalculateExpectedCashBalanceByPeriod(fromInclusive, toExclusive),
                ProgramCashBalance = CalculateProgramCashBalanceByPeriod(fromInclusive, toExclusive),
                ActualCashlessBalance = CalculateActualCashlessBalanceByPeriod(fromInclusive, toExclusive),
                ExpectedCashlessBalance = CalculateExpectedCashlessBalanceByPeriod(fromInclusive, toExclusive),
                ProgramCashlessBalance = CalculateProgramCashlessBalanceByPeriod(fromInclusive, toExclusive)
            };
        }

        public static int? GetMoneyShortageCap(DateTime fromInclusive, DateTime toExclusive)
        {
            var summary = Build(fromInclusive, toExclusive);

            if (!summary.HasFullActualBalance)
                return null;

            return summary.MoneyShortage;
        }

        public static int? CalculateActualCashBalanceByPeriod(
            DateTime fromInclusive,
            DateTime toExclusive)
        {
            var latestAcceptance = CashAcceptanceService
                .GetByPeriod(fromInclusive, toExclusive)
                .FirstOrDefault();

            if (latestAcceptance == null)
                return null;

            DateTime checkpoint = latestAcceptance.CreatedAt;

            int cashIncomeAfterCheckpoint = PaymentService.Records
                .Where(record =>
                    record.CreatedAt > checkpoint &&
                    record.CreatedAt < toExclusive)
                .Sum(record => record.CashAmount);

            int cashExpensesAfterCheckpoint = CashService.Records
                .Where(record =>
                    record.CreatedAt > checkpoint &&
                    record.CreatedAt < toExclusive &&
                    record.Category == "Расходы" &&
                    record.PaymentMethod == "Наличные")
                .Sum(record => record.Amount);

            return latestAcceptance.ActualCashAmount +
                   cashIncomeAfterCheckpoint -
                   cashExpensesAfterCheckpoint;
        }

        public static int CalculateExpectedCashBalanceByPeriod(
            DateTime fromInclusive,
            DateTime toExclusive)
        {
            return CalculateCashBalanceFromMonthStart(fromInclusive, toExclusive);
        }

        public static int? CalculateProgramCashBalanceByPeriod(
            DateTime fromInclusive,
            DateTime toExclusive)
        {
            var checkpoint = CashBalanceCheckpointService.GetLatestByPeriod(
                fromInclusive,
                toExclusive
            );

            if (checkpoint == null)
                return CalculateExpectedCashBalanceByPeriod(fromInclusive, toExclusive);

            return CalculateCashBalanceAfterCheckpoint(
                checkpoint.CashAmount,
                checkpoint.CreatedAt,
                toExclusive
            );
        }

        public static int? CalculateActualCashlessBalanceByPeriod(
            DateTime fromInclusive,
            DateTime toExclusive)
        {
            var latestVerification = CashlessService.Records
                .Where(record =>
                    record.Date >= fromInclusive.Date &&
                    record.Date < toExclusive.Date)
                .OrderByDescending(record => record.Date)
                .ThenByDescending(record => record.UpdatedAt)
                .FirstOrDefault();

            if (latestVerification == null)
                return null;

            DateTime checkpoint = latestVerification.UpdatedAt;

            int cashlessIncomeAfterCheckpoint = PaymentService.Records
                .Where(record =>
                    record.CreatedAt > checkpoint &&
                    record.CreatedAt < toExclusive)
                .Sum(record => record.MBankAmount);

            int cashlessExpensesAfterCheckpoint = CashService.Records
                .Where(record =>
                    record.CreatedAt > checkpoint &&
                    record.CreatedAt < toExclusive &&
                    record.Category == "Расходы" &&
                    record.PaymentMethod == "Безнал")
                .Sum(record => record.Amount);

            return latestVerification.Amount +
                   cashlessIncomeAfterCheckpoint -
                   cashlessExpensesAfterCheckpoint;
        }

        public static int CalculateExpectedCashlessBalanceByPeriod(
            DateTime fromInclusive,
            DateTime toExclusive)
        {
            return CalculateCashlessBalanceFromMonthStart(fromInclusive, toExclusive);
        }

        public static int? CalculateProgramCashlessBalanceByPeriod(
            DateTime fromInclusive,
            DateTime toExclusive)
        {
            var latestVerification = CashlessService.Records
                .Where(record =>
                    record.Date >= fromInclusive.Date &&
                    record.Date < toExclusive.Date)
                .OrderByDescending(record => record.Date)
                .ThenByDescending(record => record.UpdatedAt)
                .FirstOrDefault(record => record.ExpectedAmount.HasValue);

            if (latestVerification == null)
                return CalculateExpectedCashlessBalanceByPeriod(fromInclusive, toExclusive);

            return CalculateCashlessBalanceAfterCheckpoint(
                latestVerification.ExpectedAmount!.Value,
                latestVerification.UpdatedAt,
                fromInclusive,
                toExclusive
            );
        }

        private static int CalculateCashBalanceAfterCheckpoint(
            int checkpointAmount,
            DateTime checkpointTime,
            DateTime toExclusive)
        {
            int incomeAfterCheckpoint = PaymentService.Records
                .Where(record =>
                    record.CreatedAt > checkpointTime &&
                    record.CreatedAt < toExclusive)
                .Sum(record => record.CashAmount);

            int expensesAfterCheckpoint = CashService.Records
                .Where(record =>
                    record.CreatedAt > checkpointTime &&
                    record.CreatedAt < toExclusive &&
                    record.Category == "Расходы" &&
                    record.PaymentMethod == "Наличные")
                .Sum(record => record.Amount);

            return Math.Max(0, checkpointAmount + incomeAfterCheckpoint - expensesAfterCheckpoint);
        }

        private static int CalculateCashBalanceFromMonthStart(
            DateTime fromInclusive,
            DateTime toExclusive)
        {
            int income = PaymentService.Records
                .Where(record =>
                    record.CreatedAt >= fromInclusive &&
                    record.CreatedAt < toExclusive)
                .Sum(record => record.CashAmount);

            int expenses = CashService.Records
                .Where(record =>
                    record.CreatedAt >= fromInclusive &&
                    record.CreatedAt < toExclusive &&
                    record.Category == "Расходы" &&
                    record.PaymentMethod == "Наличные" &&
                    !CashService.IsPriorMonthExpense(record, fromInclusive))
                .Sum(record => record.Amount);

            return Math.Max(0, income - expenses);
        }

        private static int CalculateCashlessBalanceAfterCheckpoint(
            int checkpointAmount,
            DateTime checkpointTime,
            DateTime fromInclusive,
            DateTime toExclusive)
        {
            int incomeAfterCheckpoint = PaymentService.Records
                .Where(record =>
                    record.CreatedAt > checkpointTime &&
                    record.CreatedAt < toExclusive)
                .Sum(record => record.MBankAmount);

            int expensesAfterCheckpoint = CashService.Records
                .Where(record =>
                    record.CreatedAt > checkpointTime &&
                    record.CreatedAt < toExclusive &&
                    record.Category == "Расходы" &&
                    record.PaymentMethod == "Безнал")
                .Sum(record => record.Amount);

            return Math.Max(0, checkpointAmount + incomeAfterCheckpoint - expensesAfterCheckpoint);
        }

        private static int CalculateCashlessBalanceFromMonthStart(
            DateTime fromInclusive,
            DateTime toExclusive)
        {
            int income = PaymentService.Records
                .Where(record =>
                    record.CreatedAt >= fromInclusive &&
                    record.CreatedAt < toExclusive)
                .Sum(record => record.MBankAmount);

            int expenses = CashService.Records
                .Where(record =>
                    record.CreatedAt >= fromInclusive &&
                    record.CreatedAt < toExclusive &&
                    record.Category == "Расходы" &&
                    record.PaymentMethod == "Безнал" &&
                    !CashService.IsPriorMonthExpense(record, fromInclusive))
                .Sum(record => record.Amount);

            return Math.Max(0, income - expenses);
        }
    }
}
