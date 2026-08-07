using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public static class BusinessAccountingService
    {
        private static readonly string FolderPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ClubTimerXbox");
        private static readonly string FilePath = Path.Combine(
            FolderPath,
            "business_accounting_ledger.json");
        private static readonly object Gate = new();
        private static readonly BusinessLedgerState State = Load();

        public static int RetainedOwnerIncome
        {
            get
            {
                lock (Gate)
                    return State.RetainedOwnerIncome;
            }
        }

        public static void EnsureActivated()
        {
            lock (Gate)
            {
                if (!string.IsNullOrWhiteSpace(State.ActivatedMonthKey))
                    return;

                var period = BusinessCalendarService.GetBusinessMonth(
                    ClubClock.Current.LocalNow);
                var balances = CashBalanceSummaryService.Build(
                    period.StartInclusive,
                    period.EndExclusive);
                BusinessPeriodRange previousPeriod =
                    BusinessCalendarService.GetBusinessMonthByAnchor(
                        period.StartInclusive.AddMonths(-1));
                List<EmployeePayrollObligation> openingPayroll;
                if (State.Months.TryGetValue(previousPeriod.Key, out var closedPrevious) &&
                    closedPrevious.IsClosed)
                {
                    openingPayroll = closedPrevious.Payroll;
                }
                else
                {
                    AutoSalaryReport previousSalary =
                        AutoSalaryService.BuildReport(previousPeriod.StartInclusive);
                    openingPayroll = previousSalary.Employees
                        .Where(item => item.RemainingAmount != 0)
                        .Select(item => new EmployeePayrollObligation
                        {
                            EmployeeName = item.EmployeeName,
                            MonthKey = previousPeriod.Key,
                            AccruedAmount = item.GrossAmount,
                            PenaltyAmount = item.LossesAmount,
                            PaidAmount = item.PaidAmount
                        })
                        .ToList();
                    State.Months[previousPeriod.Key] = new BusinessMonthLedger
                    {
                        MonthKey = previousPeriod.Key,
                        IsClosed = true,
                        Payroll = openingPayroll
                    };
                }

                AutoSalaryReport currentSalary =
                    AutoSalaryService.BuildReport(period.StartInclusive);
                int currentCostOfGoodsSold = CalculateProductCostOfGoodsSold(period);
                int currentStockPurchases = StockPurchaseService.GetTotalByPeriod(
                    period.StartInclusive,
                    period.EndExclusive);
                int currentPayrollExpense = currentSalary.Employees.Sum(item =>
                    Math.Max(0, item.GrossAmount - item.LossesAmount));
                int currentProfit =
                    currentSalary.GameRevenue +
                    currentSalary.ProductRevenue -
                    currentCostOfGoodsSold -
                    CashService.GetClubExpenseTotalByPeriod(
                        period.StartInclusive,
                        period.EndExclusive) -
                    currentPayrollExpense;
                int currentSalaryDebt = currentSalary.Employees.Sum(item =>
                    Math.Max(0, item.CurrentPeriodRemainingAmount));

                State.SchemaVersion = 3;
                State.ActivatedMonthKey = period.Key;
                State.ActivatedAt = ClubClock.Current.LocalNow;
                State.CashBalance = balances.ActualCashBalance ?? balances.ExpectedCashBalance;
                State.CashlessBalance =
                    balances.ActualCashlessBalance ?? balances.ExpectedCashlessBalance;
                int openingSalaryDebt = openingPayroll.Sum(item =>
                    Math.Max(0, item.RemainingAmount));
                State.RetainedOwnerIncome =
                    State.CashBalance +
                    State.CashlessBalance +
                    currentStockPurchases -
                    currentCostOfGoodsSold -
                    openingSalaryDebt -
                    currentSalaryDebt;
                if (!State.Months.TryGetValue(period.Key, out var currentLedger))
                {
                    currentLedger = new BusinessMonthLedger { MonthKey = period.Key };
                    State.Months[period.Key] = currentLedger;
                }
                currentLedger.ProfitIncludedAtActivation = currentProfit;
                Save();
            }
        }

        public static void CloseMonth(BusinessPeriodRange period)
        {
            lock (Gate)
            {
                if (State.Months.TryGetValue(period.Key, out var existing) && existing.IsClosed)
                    return;

                string operationId = $"month-close:{period.Key}";
                if (existing != null &&
                    State.CloseJournal.TryGetValue(operationId, out var pending) &&
                    pending.LastCompletedStep >= BusinessMonthCloseStep.Prepared)
                {
                    BusinessMonthTransitionEngine.CloseMonth(
                        State,
                        period.Key,
                        ClubClock.Current.LocalNow,
                        _ => Save());
                    if (existing.IsClosed)
                        BusinessArchiveService.Seal(existing, ClubClock.Current.LocalNow);
                    Save();
                    return;
                }

                AutoSalaryReport salary = AutoSalaryService.BuildReport(period.StartInclusive);
                var ledger = existing ?? new BusinessMonthLedger();
                ledger.MonthKey = period.Key;
                ledger.GameRevenue = salary.GameRevenue;
                ledger.ProductRevenue = salary.ProductRevenue;
                ledger.ProductCostOfGoodsSold = CalculateProductCostOfGoodsSold(period);
                ledger.ClubExpenses = CashService.GetClubExpenseTotalByPeriod(
                    period.StartInclusive,
                    period.EndExclusive);
                ledger.WorkedHours = salary.Employees.ToDictionary(
                    item => item.EmployeeName,
                    item => Math.Max(0, item.WorkHours),
                    StringComparer.OrdinalIgnoreCase);
                ledger.Payroll = salary.Employees.Select(item =>
                    new EmployeePayrollObligation
                    {
                        EmployeeId = item.EmployeeId,
                        EmployeeName = item.EmployeeName,
                        MonthKey = period.Key,
                        AccruedAmount = item.GrossAmount,
                        PenaltyAmount = item.LossesAmount,
                        PaidAmount = item.PaidAmount,
                        TimeAmount = item.TimeAmount,
                        GameRevenueAmount = item.GameRevenueAmount,
                        ProductBonusAmount = item.ProductBonusAmount,
                        TimeRatingPercent = item.TimeRatingPercent,
                        RevenueRatingPercent = item.RevenueRatingPercent,
                        OverallRatingPercent = item.OverallRatingPercent
                    }).ToList();
                ledger.SalaryPolicyVersions = SalaryPolicyHistoryService.GetVersions(
                    period.StartInclusive,
                    period.EndExclusive);
                ledger.EmployeeRatings = salary.Employees.Select(item =>
                    new EmployeeRatingArchiveItem
                    {
                        EmployeeId = item.EmployeeId,
                        EmployeeName = item.EmployeeName,
                        TimePercent = item.TimeRatingPercent,
                        RevenuePercent = item.RevenueRatingPercent,
                        OverallPercent = item.OverallRatingPercent,
                        Events = item.RatingEvents
                    }).ToList();
                State.Months[period.Key] = ledger;
                Save();

                BusinessMonthTransitionEngine.CloseMonth(
                    State,
                    period.Key,
                    ClubClock.Current.LocalNow,
                    _ => Save());
                if (ledger.IsClosed)
                    BusinessArchiveService.Seal(ledger, ClubClock.Current.LocalNow);
                Save();
            }
        }

        public static int GetCarriedSalary(string employeeName, string beforeMonthKey)
        {
            lock (Gate)
            {
                return State.Months.Values
                    .Where(month => month.IsClosed &&
                                    string.CompareOrdinal(month.MonthKey, beforeMonthKey) < 0)
                    .SelectMany(month => month.Payroll)
                    .Where(item => item.EmployeeName.Equals(
                        employeeName,
                        StringComparison.OrdinalIgnoreCase))
                    .Sum(item => item.RemainingAmount);
            }
        }

        public static bool TryGetClosedPayroll(
            string monthKey,
            string employeeName,
            out EmployeePayrollObligation obligation)
        {
            lock (Gate)
            {
                obligation = new EmployeePayrollObligation();
                if (!State.Months.TryGetValue(monthKey, out var month) || !month.IsClosed)
                    return false;

                var stored = month.Payroll.FirstOrDefault(item =>
                    item.EmployeeName.Equals(employeeName, StringComparison.OrdinalIgnoreCase));
                if (stored == null)
                    return false;

                obligation = stored;
                return true;
            }
        }

        public static IReadOnlyList<PayrollPaymentAllocation> PaySalaryFifo(
            string ownerName,
            string employeeName,
            int amount,
            string paymentMethod,
            string throughMonthKey,
            string description,
            string operationId = "")
        {
            lock (Gate)
            {
                operationId = string.IsNullOrWhiteSpace(operationId)
                    ? Guid.NewGuid().ToString("N")
                    : operationId.Trim();
                if (State.PayrollPayments.TryGetValue(operationId, out var existingTransaction))
                {
                    ResumeSalaryPayment(existingTransaction, ownerName, employeeName);
                    return existingTransaction.Allocations;
                }

                var obligations = State.Months.Values
                    .Where(month => month.IsClosed &&
                                    string.CompareOrdinal(month.MonthKey, throughMonthKey) <= 0)
                    .OrderBy(month => month.MonthKey, StringComparer.Ordinal)
                    .SelectMany(month => month.Payroll)
                    .Where(item => item.EmployeeName.Equals(
                        employeeName,
                        StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (!State.Months.TryGetValue(throughMonthKey, out var throughMonth) ||
                    !throughMonth.IsClosed)
                {
                    var reportItem = AutoSalaryService
                        .BuildReport(BusinessCalendarService
                            .GetBusinessMonthByKey(throughMonthKey)
                            .StartInclusive)
                        .Employees
                        .FirstOrDefault(item => item.EmployeeName.Equals(
                            employeeName,
                            StringComparison.OrdinalIgnoreCase));
                    if (reportItem != null)
                    {
                        obligations.Add(new EmployeePayrollObligation
                        {
                            EmployeeName = employeeName,
                            MonthKey = throughMonthKey,
                            AccruedAmount = reportItem.GrossAmount,
                            PenaltyAmount = reportItem.LossesAmount,
                            PaidAmount = reportItem.PaidAmount
                        });
                    }
                }

                var allocations = PayrollPaymentAllocator
                    .AllocateFifo(obligations, employeeName, amount)
                    .ToList();
                var transaction = new PayrollPaymentTransaction
                {
                    OperationId = operationId,
                    CreatedAt = ClubClock.Current.LocalNow,
                    PaymentMethod = paymentMethod,
                    Description = description,
                    Allocations = allocations
                };
                State.PayrollPayments[operationId] = transaction;
                Save();
                ResumeSalaryPayment(transaction, ownerName, employeeName);
                return allocations;
            }
        }

        private static void ResumeSalaryPayment(
            PayrollPaymentTransaction transaction,
            string ownerName,
            string employeeName)
        {
            for (int index = transaction.PostedAllocationCount;
                 index < transaction.Allocations.Count;
                 index++)
            {
                var allocation = transaction.Allocations[index];
                string marker = $"[salary-payment:{transaction.OperationId}:{index}]";
                bool exists = CashService.Records.Any(record =>
                    record.Description?.Contains(marker, StringComparison.Ordinal) == true);
                if (!exists)
                {
                    CashService.AddSalaryPayment(
                        ownerName,
                        employeeName,
                        allocation.Amount,
                        transaction.PaymentMethod,
                        $"{transaction.Description}\n" +
                        $"Источник зарплаты: {allocation.SourceMonthKey}\n{marker}",
                        allocation.SourceMonthKey);
                }

                transaction.PostedAllocationCount = index + 1;
                Save();
            }

            transaction.IsCompleted = true;
            Save();
        }

        public static void WithdrawOwnerIncome(
            int amount,
            string paymentMethod,
            string accountingMonthKey,
            string title,
            string description,
            string operationId = "")
        {
            lock (Gate)
            {
                operationId = string.IsNullOrWhiteSpace(operationId)
                    ? Guid.NewGuid().ToString("N")
                    : operationId.Trim();
                if (State.OwnerWithdrawals.TryGetValue(operationId, out var existing))
                {
                    ResumeOwnerWithdrawal(existing);
                    return;
                }

                if (amount <= 0)
                    throw new ArgumentOutOfRangeException(nameof(amount));
                var transaction = new OwnerIncomeWithdrawalTransaction
                {
                    OperationId = operationId,
                    CreatedAt = ClubClock.Current.LocalNow,
                    Amount = amount,
                    PaymentMethod = paymentMethod,
                    AccountingMonthKey = accountingMonthKey,
                    Title = title,
                    Description = description
                };
                State.OwnerWithdrawals[operationId] = transaction;
                Save();
                ResumeOwnerWithdrawal(transaction);
            }
        }

        private static void ResumeOwnerWithdrawal(OwnerIncomeWithdrawalTransaction transaction)
        {
            string marker = $"[owner-withdrawal:{transaction.OperationId}]";
            if (!transaction.IsCashRecordPosted)
            {
                bool exists = CashService.Records.Any(record =>
                    record.Description?.Contains(marker, StringComparison.Ordinal) == true);
                if (!exists)
                {
                    CashService.AddExpense(
                        employeeName: "Владелец",
                        title: transaction.Title,
                        description: $"{transaction.Description}\n{marker}",
                        amount: transaction.Amount,
                        paymentMethod: transaction.PaymentMethod,
                        expenseCategory: "Владелец",
                        accountingMonthKey: transaction.AccountingMonthKey);
                }

                transaction.IsCashRecordPosted = true;
                Save();
            }

            if (!transaction.IsIncomeDeducted)
            {
                State.RetainedOwnerIncome -= transaction.Amount;
                transaction.IsIncomeDeducted = true;
                Save();
            }

            transaction.IsCompleted = true;
            Save();
        }

        public static BusinessLedgerState CreateDetachedSnapshotForTesting()
        {
            lock (Gate)
            {
                string json = JsonSerializer.Serialize(State);
                return JsonSerializer.Deserialize<BusinessLedgerState>(json)
                       ?? new BusinessLedgerState();
            }
        }

        private static int CalculateProductCostOfGoodsSold(BusinessPeriodRange period)
        {
            int standaloneCost = PaymentService
                .GetRecordsByPeriod(period.StartInclusive, period.EndExclusive)
                .Where(payment => payment.GameSessionId == null)
                .SelectMany(payment => payment.Items ?? new List<CheckoutItem>())
                .Where(IsProduct)
                .Sum(item => ResolvePurchasePrice(item.Name, item.PurchasePrice) * item.Quantity);

            int sessionCost = ActionLogService.GetAllGameSessions()
                .SelectMany(session => session.SaleLines)
                .Where(line => line.ItemType == SaleItemType.Product &&
                               line.CreatedAt >= period.StartInclusive &&
                               line.CreatedAt < period.EndExclusive)
                .Sum(line => ResolvePurchasePrice(line.ItemName, line.PurchasePrice) * line.Quantity);
            return standaloneCost + sessionCost;
        }

        private static bool IsProduct(CheckoutItem item)
        {
            return item.ItemType == SaleItemType.Product.ToString() ||
                   item.Category == "Товар" ||
                   ProductStockService.IsProductTracked(item.Name);
        }

        private static int ResolvePurchasePrice(string productName, int savedPrice)
        {
            return savedPrice > 0
                ? savedPrice
                : ProductStockService.GetPurchasePrice(productName);
        }

        private static BusinessLedgerState Load()
        {
            try
            {
                if (!File.Exists(FilePath))
                    return new BusinessLedgerState();

                var state = JsonSerializer.Deserialize<BusinessLedgerState>(
                    File.ReadAllText(FilePath)) ?? new BusinessLedgerState();
                state.Months = new Dictionary<string, BusinessMonthLedger>(
                    state.Months ?? new Dictionary<string, BusinessMonthLedger>(),
                    StringComparer.OrdinalIgnoreCase);
                state.CloseJournal = new Dictionary<string, BusinessMonthCloseJournal>(
                    state.CloseJournal ?? new Dictionary<string, BusinessMonthCloseJournal>(),
                    StringComparer.OrdinalIgnoreCase);
                state.PayrollPayments = new Dictionary<string, PayrollPaymentTransaction>(
                    state.PayrollPayments ?? new Dictionary<string, PayrollPaymentTransaction>(),
                    StringComparer.OrdinalIgnoreCase);
                state.OwnerWithdrawals =
                    new Dictionary<string, OwnerIncomeWithdrawalTransaction>(
                        state.OwnerWithdrawals ??
                        new Dictionary<string, OwnerIncomeWithdrawalTransaction>(),
                        StringComparer.OrdinalIgnoreCase);
                return state;
            }
            catch
            {
                return new BusinessLedgerState();
            }
        }

        private static void Save()
        {
            Directory.CreateDirectory(FolderPath);
            AtomicFileStorageService.WriteAllText(
                FilePath,
                JsonSerializer.Serialize(
                    State,
                    new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
