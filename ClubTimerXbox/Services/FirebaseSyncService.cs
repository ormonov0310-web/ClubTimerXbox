using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public static class FirebaseSyncService
    {
        private static readonly HttpClient _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(8)
        };
        private static DateTime _lastOwnerEmployeesPush = DateTime.MinValue;

        private static string ClubRootPath => $"clubs/{PcIdentityService.Current.ClubId}";

        private static string ClubCurrentPath => $"{ClubRootPath}/current";

        private static string ClubCommandsPath => $"{ClubRootPath}/commands";

        private static string ClubMetaPath => $"{ClubRootPath}/meta";

        private static string OwnerClubMetaPath => $"owner/clubs/{PcIdentityService.Current.ClubId}";

        public static async Task PushCurrentStateAsync(List<ClubPlace> places)
        {
            if (!FirebaseConnectionService.CanSync)
                return;

            try
            {
                var pcIdentity = PcIdentityService.Current;
                DateTime todayStart = DateTime.Today;
                DateTime tomorrowStart = todayStart.AddDays(1);

                DateTime monthStart = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
                DateTime nextMonthStart = monthStart.AddMonths(1);

                int gamesToday = GetPaymentTotal(
                    CashReportSection.Games,
                    CashReportPeriodMode.Day,
                    todayStart
                );

                int productsToday = GetPaymentTotal(
                    CashReportSection.ProductsAndServices,
                    CashReportPeriodMode.Day,
                    todayStart
                );

                int cashToday = gamesToday + productsToday;
                var incomePaymentToday = GetCombinedPaymentSummary(
                    CashReportPeriodMode.Day,
                    todayStart
                );

                int shortagesToday = CashService.GetShortageTotalByPeriod(
                    todayStart,
                    tomorrowStart
                );

                int expensesToday = CashService.GetExpenseTotalByPeriod(
                    todayStart,
                    tomorrowStart
                );

                int cashExpenseToday = CashService.GetCashExpenseTotalByPeriod(
                    todayStart,
                    tomorrowStart
                );

                int cashlessExpenseToday = CashService.GetCashlessExpenseTotalByPeriod(
                    todayStart,
                    tomorrowStart
                );

                int cashlessToday = CashlessService.GetAmountForToday();
                int expectedCashToday = CashlessService.GetExpectedCashForToday();

                int monthGames = GetPaymentTotal(
                    CashReportSection.Games,
                    CashReportPeriodMode.Month,
                    monthStart
                );

                int monthProducts = GetPaymentTotal(
                    CashReportSection.ProductsAndServices,
                    CashReportPeriodMode.Month,
                    monthStart
                );

                int cashMonth = monthGames + monthProducts;
                var incomePaymentMonth = GetCombinedPaymentSummary(
                    CashReportPeriodMode.Month,
                    monthStart
                );

                int shortagesMonth = CashService.GetShortageTotalByPeriod(
                    monthStart,
                    nextMonthStart
                );

                int cashlessMonth = CashlessService.GetAmountByPeriod(
                    monthStart,
                    nextMonthStart
                );
                var cashBalanceSummary = CashBalanceSummaryService.Build(monthStart, nextMonthStart);
                int? actualCashlessBalanceMonth = cashBalanceSummary.ActualCashlessBalance;
                int? programCashlessBalanceMonth = cashBalanceSummary.ProgramCashlessBalance;
                int? actualCashBalanceMonth = cashBalanceSummary.ActualCashBalance;
                int? programCashBalanceMonth = cashBalanceSummary.ProgramCashBalance;
                bool cashlessVerifiedMonth = CashlessService.Records.Any(record =>
                    record.Date >= monthStart.Date &&
                    record.Date < nextMonthStart.Date);

                int expensesMonth = CashService.GetClubExpenseTotalByPeriod(
                    monthStart,
                    nextMonthStart
                );

                int cashExpenseMonth = CashService.GetClubCashExpenseTotalByPeriod(
                    monthStart,
                    nextMonthStart
                );

                int cashlessExpenseMonth = CashService.GetClubCashlessExpenseTotalByPeriod(
                    monthStart,
                    nextMonthStart
                );

                int salaryToday = CashService.GetSalaryTotalByPeriod(
                    todayStart,
                    tomorrowStart
                );

                int salaryMonth = CashService.GetSalaryTotalByPeriod(
                    monthStart,
                    nextMonthStart
                );
                var salaryRecordsMonth = CashService.GetSalaryRecordsByPeriod(
                    monthStart,
                    nextMonthStart
                );
                int salaryCashMonth = salaryRecordsMonth
                    .Where(record => record.PaymentMethod == "Наличные")
                    .Sum(record => record.Amount);
                int salaryCashlessMonth = salaryRecordsMonth
                    .Where(record => record.PaymentMethod == "Безнал")
                    .Sum(record => record.Amount);

                int stockPurchaseToday = StockPurchaseService.GetTotalByPeriod(
                    todayStart,
                    tomorrowStart
                );

                int stockPurchaseMonth = StockPurchaseService.GetTotalByPeriod(
                    monthStart,
                    nextMonthStart
                );
                var stockPurchaseExpenseRecordsMonth = CashService.GetExpenseRecordsByExpenseCategory(
                    monthStart,
                    nextMonthStart,
                    "Закупка"
                );
                int stockPurchaseCashMonth = stockPurchaseExpenseRecordsMonth
                    .Where(record => record.PaymentMethod == "Наличные")
                    .Sum(record => record.Amount);
                int stockPurchaseCashlessMonth = stockPurchaseExpenseRecordsMonth
                    .Where(record => record.PaymentMethod == "Безнал")
                    .Sum(record => record.Amount);

                var ownerWithdrawRecordsMonth = CashService.GetOwnerWithdrawRecordsByPeriod(
                    monthStart,
                    nextMonthStart
                );
                int ownerWithdrawMonth = ownerWithdrawRecordsMonth.Sum(record => record.Amount);
                int ownerWithdrawCashMonth = ownerWithdrawRecordsMonth
                    .Where(record => record.PaymentMethod == "Наличные")
                    .Sum(record => record.Amount);
                int ownerWithdrawCashlessMonth = ownerWithdrawRecordsMonth
                    .Where(record => record.PaymentMethod == "Безнал")
                    .Sum(record => record.Amount);

                int cashExpenseMovementMonth = GetMonthMovementExpenseTotal(
                    monthStart,
                    nextMonthStart,
                    "Наличные"
                );
                int cashlessExpenseMovementMonth = GetMonthMovementExpenseTotal(
                    monthStart,
                    nextMonthStart,
                    "Безнал"
                );
                int cashMovementMonth = incomePaymentMonth.CashAmount - cashExpenseMovementMonth;
                int cashlessMovementMonth = incomePaymentMonth.MBankAmount - cashlessExpenseMovementMonth;
                int openingCashBalanceMonth = CalculateOpeningBalance(
                    null,
                    cashBalanceSummary.ExpectedCashBalance,
                    cashMovementMonth
                );
                int openingCashlessBalanceMonth = CalculateOpeningBalance(
                    null,
                    cashBalanceSummary.ExpectedCashlessBalance,
                    cashlessMovementMonth
                );
                int expectedCashBalanceMonth = CalculateExpectedBalanceWithOpening(
                    openingCashBalanceMonth,
                    cashMovementMonth
                );
                int expectedCashlessBalanceMonth = CalculateExpectedBalanceWithOpening(
                    openingCashlessBalanceMonth,
                    cashlessMovementMonth
                );
                int effectiveProgramCashBalanceMonth =
                    programCashBalanceMonth ?? expectedCashBalanceMonth;
                int effectiveProgramCashlessBalanceMonth =
                    programCashlessBalanceMonth ?? expectedCashlessBalanceMonth;
                int moneyProgramBalanceMonth =
                    effectiveProgramCashBalanceMonth + effectiveProgramCashlessBalanceMonth;
                int? moneyActualBalanceMonth = CalculateActualMoneyBalance(
                    actualCashBalanceMonth,
                    actualCashlessBalanceMonth
                );
                int? moneyDifferenceMonth = moneyActualBalanceMonth.HasValue
                    ? moneyActualBalanceMonth.Value - moneyProgramBalanceMonth
                    : null;
                int moneyShortageMonth = moneyDifferenceMonth.HasValue && moneyDifferenceMonth.Value < 0
                    ? Math.Abs(moneyDifferenceMonth.Value)
                    : 0;
                int moneyExtraMonth = moneyDifferenceMonth.HasValue && moneyDifferenceMonth.Value > 0
                    ? moneyDifferenceMonth.Value
                    : 0;
                DateTime reconciliationCycleStart = CashBalanceCheckpointService
                    .GetCurrentCycleStart(monthStart, nextMonthStart);
                int openCycleShortages = CashReconciliationService.GetOpenShortageTotal(
                    reconciliationCycleStart,
                    nextMonthStart
                );
                int openCycleExtras = CashReconciliationService.GetOpenExtraTotal(
                    reconciliationCycleStart,
                    nextMonthStart
                );
                int openCycleNetShortage = Math.Max(0, openCycleShortages - openCycleExtras);
                int openCycleNetExtra = Math.Max(0, openCycleExtras - openCycleShortages);
                var cycleFormalizedMoneyLossesByEmployee = EmployeeLossService
                    .GetCappedUnpaidMoneyTotalsByEmployee(
                        reconciliationCycleStart,
                        nextMonthStart,
                        null
                    );
                int cycleFormalizedMoneyLosses = cycleFormalizedMoneyLossesByEmployee
                    .Values
                    .Sum();
                int accountabilityShortage = Math.Max(
                    moneyShortageMonth,
                    cycleFormalizedMoneyLosses + openCycleNetShortage
                );
                int accountabilityFormalized = Math.Min(
                    accountabilityShortage,
                    cycleFormalizedMoneyLosses
                );
                int accountabilityPending = Math.Max(
                    openCycleNetShortage,
                    Math.Max(0, accountabilityShortage - accountabilityFormalized)
                );
                int accountabilityExtra = Math.Max(moneyExtraMonth, openCycleNetExtra);
                string accountabilityResponsible = CashReconciliationService
                    .GetSuggestedResponsibleForOpenShortages(
                        reconciliationCycleStart,
                        nextMonthStart
                    );
                if (string.IsNullOrWhiteSpace(accountabilityResponsible))
                {
                    string historicalResponsible = CashReconciliationService
                        .GetSuggestedResponsibleForShortageHistory(
                            reconciliationCycleStart,
                            nextMonthStart
                        );
                    if (cycleFormalizedMoneyLossesByEmployee.TryGetValue(
                            historicalResponsible,
                            out int historicalFormalizedAmount) &&
                        historicalFormalizedAmount > 0)
                    {
                        accountabilityResponsible = historicalResponsible;
                    }
                }
                string accountabilitySuspect = string.IsNullOrWhiteSpace(accountabilityResponsible)
                    ? CashReconciliationService.GetSuggestedSuspectForOpenShortages(
                        reconciliationCycleStart,
                        nextMonthStart
                    )
                    : "";
                if (string.IsNullOrWhiteSpace(accountabilityResponsible) &&
                    string.IsNullOrWhiteSpace(accountabilitySuspect))
                {
                    accountabilitySuspect = CashReconciliationService
                        .GetSuggestedSuspectForShortageHistory(
                            reconciliationCycleStart,
                            nextMonthStart
                        );
                }
                int ownerAvailableCashBalanceMonth = CalculateOwnerAvailableBalance(
                    actualCashBalanceMonth,
                    effectiveProgramCashBalanceMonth,
                    ownerWithdrawRecordsMonth,
                    "Наличные",
                    nextMonthStart
                );
                int ownerAvailableCashlessBalanceMonth = CalculateOwnerAvailableBalance(
                    actualCashlessBalanceMonth,
                    effectiveProgramCashlessBalanceMonth,
                    ownerWithdrawRecordsMonth,
                    "Безнал",
                    nextMonthStart
                );

                var expenseCategories = CashService.GetDefaultExpenseCategories()
                    .Where(IsOwnerReportExpenseCategory)
                    .ToList();

                var expensesByCategory = expenseCategories
                    .Select(category => new
                    {
                        category,
                        total = CashService.GetExpenseTotalByPeriodAndExpenseCategory(
                            monthStart,
                            nextMonthStart,
                            category
                        )
                    })
                    .ToList();

                var employees = EmployeeService.GetAllEmployees();

                var salaryRecords = CashService.GetSalaryRecordsByPeriod(
                        monthStart,
                        nextMonthStart
                    )
                    .Take(100)
                    .Select(record => new
                    {
                        createdAt = record.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                        employeeName = record.RelatedEmployeeName,
                        amount = record.Amount,
                        paymentMethod = record.PaymentMethod,
                        description = record.Description,
                        addedBy = record.EmployeeName
                    })
                    .ToList();

                var stockPurchases = StockPurchaseService.Purchases
                    .OrderByDescending(purchase => purchase.CreatedAt)
                    .Take(100)
                    .Select(purchase => new
                    {
                        createdAt = purchase.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                        addedBy = purchase.AddedBy,
                        note = purchase.Note,
                        totalAmount = purchase.TotalAmount,
                        items = purchase.Items.Select(item => new
                        {
                            productName = item.ProductName,
                            quantity = item.Quantity,
                            purchasePrice = item.PurchasePrice,
                            salePrice = item.SalePrice,
                            minimumQuantity = item.MinimumQuantity,
                            totalAmount = item.TotalAmount
                        }).ToList()
                    })
                    .ToList();

                var stockItems = ProductStockService.StockItems
                    .Select(item => new
                    {
                        itemType = "Product",
                        productName = item.ProductName,
                        name = item.ProductName,
                        quantity = item.Quantity,
                        purchasePrice = item.PurchasePrice,
                        salePrice = item.SalePrice,
                        minimumQuantity = item.MinimumQuantity,
                        isLowStock = ProductStockService.IsLowStock(item.ProductName),
                        updatedAt = item.UpdatedAt.ToString("yyyy-MM-dd HH:mm:ss")
                    })
                    .ToList();

                var serviceItems = CustomServiceService.GetAllServices()
                    .Select(item => new
                    {
                        itemType = "Service",
                        productName = item.Name,
                        name = item.Name,
                        quantity = 0,
                        purchasePrice = 0,
                        salePrice = item.SalePrice,
                        minimumQuantity = 0,
                        isLowStock = false,
                        updatedAt = ""
                    })
                    .ToList();

                var saleItems = stockItems
                    .Cast<object>()
                    .Concat(serviceItems.Cast<object>())
                    .ToList();

                LateOpeningPenaltyService.EnsureTodayNoOpeningRecommendation();

                var reportsByMonth = BuildReportsByMonth();
                var autoSalaryReport = AutoSalaryService.BuildReport(monthStart);
                int salaryGrossMonth = autoSalaryReport.Employees.Sum(employee => employee.GrossAmount);
                int salaryLossesMonth = autoSalaryReport.Employees.Sum(employee => employee.LossesAmount);
                int salaryAccruedMonth = autoSalaryReport.Employees.Sum(employee =>
                    Math.Max(0, employee.GrossAmount - employee.LossesAmount));
                int possibleProfitMonth = CalculatePossibleProfit(
                    cashMonth,
                    expensesMonth,
                    stockPurchaseMonth,
                    salaryAccruedMonth,
                    shortagesMonth
                );

                if (actualCashlessBalanceMonth.HasValue)
                {
                    CashReconciliationService.ResolveStaleCashlessZeroBaselineArtifacts(
                        monthStart,
                        nextMonthStart,
                        expectedCashlessBalanceMonth,
                        actualCashlessBalanceMonth.Value
                    );
                }

                var cashReconciliation = CashReconciliationService
                    .GetRecentItems()
                    .Select(item =>
                    {
                        EnsureCashlessShortageSuspect(item, reconciliationCycleStart);
                        return item;
                    })
                    .Select(item => new
                    {
                        id = item.Id.ToString(),
                        createdAt = item.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                        kind = item.Kind.ToString(),
                        status = item.Status.ToString(),
                        amount = item.Amount,
                        originalAmount = item.OriginalAmount,
                        resolvedAmount = item.ResolvedAmount,
                        formalizedAmount = item.FormalizedAmount,
                        remainingAmount = item.Amount,
                        expectedAmount = item.ExpectedAmount,
                        actualAmount = item.ActualAmount,
                        checkedByEmployeeName = item.CheckedByEmployeeName,
                        responsibleEmployeeName = item.ResponsibleEmployeeName,
                        suspectedEmployeeName = item.SuspectedEmployeeName,
                        title = item.Title,
                        note = item.Note,
                        resolvedAt = item.ResolvedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "",
                        resolvedBy = item.ResolvedBy,
                        resolutionNote = item.ResolutionNote
                    })
                    .ToList();

                var data = new
                {
                    updatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    app = AppVersionService.BuildPayload(),
                    club = new
                    {
                        id = pcIdentity.ClubId,
                        name = pcIdentity.ClubName,
                        isActivated = pcIdentity.IsActivated,
                        installationId = pcIdentity.InstallationId,
                        activatedAt = pcIdentity.ActivatedAt,
                        pcName = Environment.MachineName,
                        appVersion = AppVersionService.Version,
                        updateChannel = AppVersionService.UpdateChannel
                    },
                    currentEmployeeName = EmployeeService.CurrentEmployee?.Name ?? "",
                    settings = BuildClubSettingsPayload(AppSettingsService.Current),
                    acceptance = new
                    {
                        isRequired = ShiftAcceptanceService.Current.IsRequired,
                        isCompleted = ShiftAcceptanceService.Current.IsCompleted,
                        productsAccepted = ShiftAcceptanceService.Current.ProductsAccepted,
                        cashAccepted = ShiftAcceptanceService.Current.CashAccepted,
                        newEmployeeName = ShiftAcceptanceService.Current.NewEmployeeName,
                        responsibleEmployeeName = ShiftAcceptanceService.Current.ResponsibleEmployeeName,
                        createdAt = ShiftAcceptanceService.Current.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                        productsAcceptedAt = ShiftAcceptanceService.Current.ProductsAcceptedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "",
                        cashAcceptedAt = ShiftAcceptanceService.Current.CashAcceptedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "",
                        completedAt = ShiftAcceptanceService.Current.CompletedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? ""
                    },

                    cash = new
                    {
                        cashToday,
                        gamesToday,
                        productsToday,
                        incomeCashToday = incomePaymentToday.CashAmount,
                        incomeMBankToday = incomePaymentToday.MBankAmount,
                        shortagesToday,
                        gamesMonth = monthGames,
                        productsMonth = monthProducts,
                        shortagesMonth,
                        expensesToday,
                        cashExpenseToday,
                        cashlessExpenseToday,
                        cashlessToday,
                        expectedCashToday,

                        cashMonth,
                        incomeCashMonth = incomePaymentMonth.CashAmount,
                        incomeMBankMonth = incomePaymentMonth.MBankAmount,
                        cashlessMonth,
                        actualCashBalanceMonth,
                        programCashBalanceMonth = effectiveProgramCashBalanceMonth,
                        actualCashlessBalanceMonth,
                        programCashlessBalanceMonth = effectiveProgramCashlessBalanceMonth,
                        expectedCashBalanceMonth,
                        expectedCashlessBalanceMonth,
                        openingCashBalanceMonth,
                        openingCashlessBalanceMonth,
                        openingMoneyBalanceMonth = openingCashBalanceMonth + openingCashlessBalanceMonth,
                        cashMovementMonth,
                        cashlessMovementMonth,
                        cashExpenseMovementMonth,
                        cashlessExpenseMovementMonth,
                        moneyProgramBalanceMonth,
                        moneyActualBalanceMonth,
                        moneyDifferenceMonth,
                        moneyShortageMonth,
                        moneyExtraMonth,
                        ownerAvailableCashBalanceMonth,
                        ownerAvailableCashlessBalanceMonth,
                        ownerAvailableMoneyBalanceMonth = ownerAvailableCashBalanceMonth + ownerAvailableCashlessBalanceMonth,
                        cashlessVerifiedMonth,
                        expensesMonth,
                        cashExpenseMonth,
                        cashlessExpenseMonth,

                        salaryToday,
                        salaryMonth,
                        salaryAccruedMonth,
                        salaryGrossMonth,
                        salaryLossesMonth,
                        salaryCashMonth,
                        salaryCashlessMonth,

                        stockPurchaseToday,
                        stockPurchaseMonth,
                        stockPurchaseCashMonth,
                        stockPurchaseCashlessMonth,

                        ownerWithdrawMonth,
                        ownerWithdrawCashMonth,
                        ownerWithdrawCashlessMonth,
                        possibleProfitMonth
                    },

                    cashRecords = new
                    {
                        today = new
                        {
                            games = BuildCashRecordItems(todayStart, tomorrowStart, "Игры"),
                            productsAndServices = BuildCashRecordItems(todayStart, tomorrowStart, "Товары и услуги"),
                            expenses = BuildCashRecordItems(todayStart, tomorrowStart, "Расходы"),
                            ownerWithdraw = BuildOwnerWithdrawRecordItems(todayStart, tomorrowStart),
                            losses = BuildCashRecordItems(todayStart, tomorrowStart, "Недостачи")
                        },
                        month = new
                        {
                            games = BuildCashRecordItems(monthStart, nextMonthStart, "Игры"),
                            productsAndServices = BuildCashRecordItems(monthStart, nextMonthStart, "Товары и услуги"),
                            expenses = BuildCashRecordItems(monthStart, nextMonthStart, "Расходы"),
                            ownerWithdraw = BuildOwnerWithdrawRecordItems(monthStart, nextMonthStart),
                            losses = BuildCashRecordItems(monthStart, nextMonthStart, "Недостачи")
                        }
                    },

                    reportsByMonth,

                    autoSalary = BuildAutoSalaryPayload(autoSalaryReport),

                    cashAccountability = new
                    {
                        cycleStartedAt = reconciliationCycleStart.ToString("yyyy-MM-dd HH:mm:ss"),
                        shortageAmount = accountabilityShortage,
                        extraAmount = accountabilityExtra,
                        formalizedAmount = accountabilityFormalized,
                        pendingAmount = accountabilityPending,
                        recommendedEmployeeName = !string.IsNullOrWhiteSpace(accountabilityResponsible)
                            ? accountabilityResponsible
                            : accountabilitySuspect,
                        recommendationType = !string.IsNullOrWhiteSpace(accountabilityResponsible)
                            ? "responsible"
                            : !string.IsNullOrWhiteSpace(accountabilitySuspect)
                                ? "suspect"
                                : "unknown"
                    },

                    cashReconciliation,

                    places = places.Select(place => new
                    {
                        name = place.Name,
                        type = place.Type.ToString(),
                        isBusy = place.IsBusy,
                        isOpenMode = place.IsOpenMode,
                        isCalculating = place.IsCalculating,
                        isTimeExpiredAwaitingAcknowledgement = place.IsTimeExpiredAwaitingAcknowledgement,
                        paidAmount = place.PaidAmount,
                        currentGameAmount = GetCurrentGameAmount(place),
                        remainingSeconds = place.RemainingSeconds,
                        totalMinutes = place.TotalMinutes,
                        startedByEmployeeName = place.StartedByEmployeeName,
                        incomeEmployeeName = place.IncomeEmployeeName,
                        hasAttachedItems = HasUnpaidSessionSales(place.Name),
                        productsAndServicesAmount = GetUnpaidSessionSalesAmount(place.Name),
                        saleLines = GetUnpaidSessionSaleLines(place.Name)
                    }).ToList(),

                    stock = stockItems,

                    services = serviceItems,

                    saleItems = saleItems,

                    stockPurchases = stockPurchases,

                    employees = employees.Select(employee =>
                    {
                        var summary = EmployeeStatsService.GetSummary(employee.Name);
                        var autoSalary = autoSalaryReport.Employees
                            .FirstOrDefault(item =>
                                item.EmployeeName.Equals(employee.Name, StringComparison.OrdinalIgnoreCase));

                        int salaryForMonth = CashService.GetSalaryTotalByPeriodForEmployee(
                            monthStart,
                            nextMonthStart,
                            employee.Name
                        );

                        var journal = EmployeeStatsService
                            .GetJournalForCurrentMonth(employee.Name)
                            .Take(150)
                            .ToList();

                        var allJournal = journal
                            .Select(item => new
                            {
                                id = item.Id?.ToString() ?? "",
                                createdAt = item.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                                type = item.Type,
                                lossKind = item.LossKind,
                                title = item.Title,
                                description = item.Description,
                                amount = item.Amount,
                                isFixed = item.IsFixed
                            })
                            .ToList();

                        var incomeJournal = journal
                            .Where(item =>
                                item.Type == "Игры" ||
                                item.Type == "Выручка" ||
                                item.Type == "Товар/услуга" ||
                                item.Type == "Товары и услуги")
                            .Select(item => new
                            {
                                id = item.Id?.ToString() ?? "",
                                createdAt = item.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                                type = item.Type,
                                lossKind = item.LossKind,
                                title = item.Title,
                                description = item.Description,
                                amount = item.Amount,
                                isFixed = item.IsFixed
                            })
                            .ToList();

                        var shortageJournal = journal
                            .Where(item =>
                                item.Type == "Недостача" ||
                                item.Type == "Потеря" ||
                                item.Type.Contains("Штраф"))
                            .Select(item => new
                            {
                                id = item.Id?.ToString() ?? "",
                                createdAt = item.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                                type = item.Type,
                                lossKind = item.LossKind,
                                title = item.Title,
                                description = item.Description,
                                amount = item.Amount,
                                isFixed = item.IsFixed
                            })
                            .ToList();

                        return new
                        {
                            employeeId = employee.EmployeeId,
                            name = employee.Name,
                            pinCode = employee.PinCode,
                            isActive = employee.IsActive,

                            todayWorkTime = EmployeeStatsService.FormatTime(summary.TodayWorkTime),
                            monthWorkTime = EmployeeStatsService.FormatTime(summary.MonthWorkTime),

                            todayIncome = summary.TodayTotalIncome,
                            monthIncome = summary.MonthTotalIncome,

                            todayGameIncome = summary.TodayGameIncome,
                            todayProductsIncome = summary.TodayProductsIncome,

                            monthGameIncome = summary.MonthGameIncome,
                            monthProductsIncome = summary.MonthProductsIncome,

                            todayShortages = summary.TodayShortages,
                            monthShortages = summary.MonthShortages,
                            monthMoneyLosses = summary.MonthUnpaidMoneyLosses,
                            monthRawMoneyLosses = summary.MonthRawUnpaidMoneyLosses,
                            monthProductLosses = summary.MonthUnpaidProductLosses,
                            monthViolationLosses = summary.MonthUnpaidViolationLosses,

                            monthSalaryPaid = salaryForMonth,
                            autoSalary = autoSalary == null
                                ? null
                                : new
                                {
                                    workHours = autoSalary.WorkHours,
                                    timeAmount = autoSalary.TimeAmount,
                                    gameRevenueAmount = autoSalary.GameRevenueAmount,
                                    productShareAmount = autoSalary.ProductShareAmount,
                                    productBonusAmount = autoSalary.ProductBonusAmount,
                                    bonusAmount = autoSalary.BonusAmount,
                                    grossAmount = autoSalary.GrossAmount,
                                    lossesAmount = autoSalary.LossesAmount,
                                    moneyLossesAmount = autoSalary.MoneyLossesAmount,
                                    rawMoneyLossesAmount = autoSalary.RawMoneyLossesAmount,
                                    productLossesAmount = autoSalary.ProductLossesAmount,
                                    violationLossesAmount = autoSalary.ViolationLossesAmount,
                                    paidAmount = autoSalary.PaidAmount,
                                    remainingAmount = autoSalary.RemainingAmount
                                },

                            closedGameSessionsCount = summary.ClosedGameSessionsCount,
                            productServiceOperationsCount = summary.ProductServiceOperationsCount,
                            shortageCount = summary.ShortageCount,

                            journal = allJournal,
                            incomeJournal = incomeJournal,
                            shortageJournal = shortageJournal
                        };
                    }).ToList(),

                    expenseCategories = expenseCategories,

                    expensesByCategory = expensesByCategory,

                    salaryRecords = salaryRecords,

                    expenses = CashService.GetRecordsByPeriodAndCategory(monthStart, nextMonthStart, "Расходы")
                        .Take(150)
                        .Select(record => new
                        {
                            createdAt = record.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                            title = record.Title,
                            description = record.Description,
                            amount = record.Amount,
                            paymentMethod = record.PaymentMethod,
                            expenseCategory = string.IsNullOrWhiteSpace(record.ExpenseCategory)
                                ? "Другое"
                                : record.ExpenseCategory,
                            relatedEmployeeName = record.RelatedEmployeeName,
                            employeeName = record.EmployeeName
                        })
                        .ToList()
                };

                await PutAsync(ClubCurrentPath, data);
                await PatchAsync(ClubMetaPath, BuildClubMeta(pcIdentity));
                await PatchAsync(OwnerClubMetaPath, BuildClubMeta(pcIdentity));
                await PushOwnerEmployeesIfNeededAsync(employees, pcIdentity);
            }
            catch
            {
                // Если интернет пропал, программа должна продолжать работать.
            }
        }

        public static async Task CheckCommandsAsync(IReadOnlyList<ClubPlace>? places = null)
        {
            if (!FirebaseConnectionService.CanSync)
                return;

            try
            {
                var currentPlaces = places ?? Array.Empty<ClubPlace>();
                await CheckCommandsAsync(ClubCommandsPath, currentPlaces);
            }
            catch
            {
                // Пока молча игнорируем ошибки связи.
            }
        }

        private static async Task CheckCommandsAsync(
            string commandsPath,
            IReadOnlyList<ClubPlace> places)
        {
            Dictionary<string, FirebaseCommand>? commands;

            try
            {
                commands = await GetAsync<Dictionary<string, FirebaseCommand>>(
                    commandsPath,
                    "orderBy=%22status%22&equalTo=%22pending%22"
                );
            }
            catch (HttpRequestException)
            {
                // The status index is deployed separately from the desktop update.
                commands = await GetAsync<Dictionary<string, FirebaseCommand>>(
                    commandsPath
                );
            }

            if (commands == null)
                return;

            foreach (var pair in commands)
            {
                string commandId = pair.Key;
                FirebaseCommand command = pair.Value;

                if (command == null)
                    continue;

                if (command.Status != "pending")
                    continue;

                command.FirebasePath = commandsPath;

                if (!IsCommandForCurrentPc(command))
                {
                    await MarkCommandError(
                        commandId,
                        command,
                        "Команда адресована другому ПК или каналу."
                    );
                    continue;
                }

                await ApplyCommandAsync(commandId, command, places);
            }
        }

        private static bool IsCommandForCurrentPc(FirebaseCommand command)
        {
            var identity = PcIdentityService.Current;

            bool clubMatches = string.IsNullOrWhiteSpace(command.TargetClubId) ||
                command.TargetClubId.Equals(
                    identity.ClubId,
                    StringComparison.OrdinalIgnoreCase
                );
            bool installationMatches =
                string.IsNullOrWhiteSpace(command.TargetInstallationId) ||
                command.TargetInstallationId.Equals(
                    identity.InstallationId,
                    StringComparison.OrdinalIgnoreCase
                );

            return clubMatches && installationMatches;
        }

        private static object BuildClubMeta(PcIdentity identity)
        {
            return new
            {
                id = identity.ClubId,
                name = identity.ClubName,
                isActivated = identity.IsActivated,
                installationId = identity.InstallationId,
                activatedAt = identity.ActivatedAt,
                pcName = Environment.MachineName,
                appVersion = AppVersionService.Version,
                updateChannel = AppVersionService.UpdateChannel,
                app = AppVersionService.BuildPayload(),
                updatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            };
        }

        private static object BuildClubSettingsPayload(ClubSettings settings)
        {
            return new
            {
                tvCount = settings.TvCount,
                wheelCount = settings.WheelCount,
                vipRoomCount = settings.VipRoomCount,
                tariffs = new
                {
                    tv = BuildTariffPayload(settings.TvTariff),
                    wheel = BuildTariffPayload(settings.WheelTariff),
                    vip = BuildTariffPayload(settings.VipTariff)
                }
            };
        }

        private static object BuildTariffPayload(TariffSettings tariff)
        {
            return new
            {
                oneHourPrice = tariff.OneHourPrice,
                halfHourPrice = tariff.HalfHourPrice,
                fiveMinutesPrice = tariff.FiveMinutesPrice
            };
        }

        private static async Task PushOwnerEmployeesIfNeededAsync(
            List<Employee> employees,
            PcIdentity identity)
        {
            if (DateTime.Now - _lastOwnerEmployeesPush < TimeSpan.FromMinutes(1))
                return;

            _lastOwnerEmployeesPush = DateTime.Now;
            string updatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            foreach (var employee in employees)
            {
                if (string.IsNullOrWhiteSpace(employee.EmployeeId))
                    continue;

                await PutAsync($"owner/employees/{employee.EmployeeId}/profile", new
                {
                    employeeId = employee.EmployeeId,
                    name = employee.Name,
                    pinCode = employee.PinCode,
                    updatedAt
                });

                await PutAsync($"owner/employees/{employee.EmployeeId}/clubs/{identity.ClubId}", new
                {
                    clubId = identity.ClubId,
                    clubName = identity.ClubName,
                    isActive = employee.IsActive,
                    updatedAt
                });
            }
        }

        private static int GetPaymentTotal(
            CashReportSection section,
            CashReportPeriodMode periodMode,
            DateTime selectedDate)
        {
            var filter = new CashReportFilter
            {
                Section = section,
                PeriodMode = periodMode,
                ViewMode = CashReportViewMode.Records,
                SelectedDay = selectedDate,
                SelectedYear = selectedDate.Year,
                SelectedMonth = selectedDate.Month
            };

            var report = CashReportService.BuildReport(filter);

            return report.Summary.TotalAmount;
        }

        private static CashReportSummary GetCombinedPaymentSummary(
            CashReportPeriodMode periodMode,
            DateTime selectedDate)
        {
            var games = GetPaymentSummary(
                CashReportSection.Games,
                periodMode,
                selectedDate
            );

            var products = GetPaymentSummary(
                CashReportSection.ProductsAndServices,
                periodMode,
                selectedDate
            );

            return new CashReportSummary
            {
                TotalAmount = games.TotalAmount + products.TotalAmount,
                CashAmount = games.CashAmount + products.CashAmount,
                MBankAmount = games.MBankAmount + products.MBankAmount,
                RecordsCount = games.RecordsCount + products.RecordsCount
            };
        }

        private static int? CalculateActualCashBalanceByPeriod(
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

            int balance =
                latestAcceptance.ActualCashAmount +
                cashIncomeAfterCheckpoint -
                cashExpensesAfterCheckpoint;

            return balance;
        }

        private static int CalculateExpectedCashBalanceByPeriod(
            DateTime fromInclusive,
            DateTime toExclusive)
        {
            return CalculateCashBalanceFromMonthStart(
                fromInclusive,
                toExclusive
            );
        }

        private static int? CalculateProgramCashBalanceByPeriod(
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

            int balance = checkpointAmount + incomeAfterCheckpoint - expensesAfterCheckpoint;

            return Math.Max(0, balance);
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

        private static int? CalculateActualCashlessBalanceByPeriod(
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

            int balance =
                latestVerification.Amount +
                cashlessIncomeAfterCheckpoint -
                cashlessExpensesAfterCheckpoint;

            return balance;
        }

        private static int CalculateExpectedCashlessBalanceByPeriod(
            DateTime fromInclusive,
            DateTime toExclusive)
        {
            return CalculateCashlessBalanceFromMonthStart(
                fromInclusive,
                toExclusive
            );
        }

        private static int? CalculateProgramCashlessBalanceByPeriod(
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

            int balance = checkpointAmount + incomeAfterCheckpoint - expensesAfterCheckpoint;

            return Math.Max(0, balance);
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

        private static CashReportSummary GetPaymentSummary(
            CashReportSection section,
            CashReportPeriodMode periodMode,
            DateTime selectedDate)
        {
            var filter = new CashReportFilter
            {
                Section = section,
                PeriodMode = periodMode,
                ViewMode = CashReportViewMode.Records,
                SelectedDay = selectedDate,
                SelectedYear = selectedDate.Year,
                SelectedMonth = selectedDate.Month
            };

            return CashReportService.BuildReport(filter).Summary;
        }

        private static Dictionary<string, object> BuildReportsByMonth()
        {
            var monthStarts = new HashSet<DateTime>();

            void AddMonth(DateTime date)
            {
                monthStarts.Add(new DateTime(date.Year, date.Month, 1));
            }

            AddMonth(DateTime.Today);

            foreach (var record in CashService.Records)
            {
                AddMonth(record.CreatedAt);

                if (DateTime.TryParse($"{record.SalaryMonthKey}-01", out DateTime salaryMonth))
                    AddMonth(salaryMonth);

                if (DateTime.TryParse($"{record.AccountingMonthKey}-01", out DateTime accountingMonth))
                    AddMonth(accountingMonth);
            }

            foreach (var record in PaymentService.Records)
                AddMonth(record.CreatedAt);

            foreach (var record in CashlessService.Records)
                AddMonth(record.Date);

            foreach (var purchase in StockPurchaseService.Purchases)
                AddMonth(purchase.CreatedAt);

            return monthStarts
                .OrderByDescending(date => date)
                .Take(18)
                .ToDictionary(
                    date => date.ToString("yyyy-MM"),
                    date => (object)BuildMonthReport(date)
                );
        }

        private static object BuildAutoSalaryPayload(AutoSalaryReport report)
        {
            return new
            {
                monthKey = report.MonthKey,
                settings = new
                {
                    expenseReservePercent = report.Settings.ExpenseReservePercent,
                    salaryFundPercent = report.Settings.SalaryFundPercent,
                    timeSharePercent = report.Settings.TimeSharePercent,
                    gameRevenueSharePercent = report.Settings.GameRevenueSharePercent,
                    timeMonthlyFundAmount = report.Settings.TimeMonthlyFundAmount,
                    timeMonthlyPlannedHours = report.Settings.TimeMonthlyPlannedHours,
                    productRevenueSharePercent = report.Settings.ProductRevenueSharePercent,
                    productBonusPercent = report.Settings.ProductBonusPercent,
                    workDayStartHour = report.Settings.WorkDayStartHour,
                    workDayEndHour = report.Settings.WorkDayEndHour,
                    dailyGameRevenueNorm = report.Settings.DailyGameRevenueNorm,
                    overNormBonusPercent = report.Settings.OverNormBonusPercent,
                    punctualityBonusAmount = report.Settings.PunctualityBonusAmount,
                    lateActiveSessionBonusAmount = report.Settings.LateActiveSessionBonusAmount,
                    openingResponsibleEmployeeName = report.Settings.OpeningResponsibleEmployeeName,
                    lateOpeningGraceMinutes = report.Settings.LateOpeningGraceMinutes,
                    lateOpeningPenaltyStepMinutes = report.Settings.LateOpeningPenaltyStepMinutes,
                    lateOpeningPenaltyStepAmount = report.Settings.LateOpeningPenaltyStepAmount,
                    lateOpeningMaxAutoMinutes = report.Settings.LateOpeningMaxAutoMinutes
                },
                gameRevenue = report.GameRevenue,
                productRevenue = report.ProductRevenue,
                expenseReserveAmount = report.ExpenseReserveAmount,
                salaryBaseAmount = report.SalaryBaseAmount,
                salaryFundAmount = report.SalaryFundAmount,
                timeFundAmount = report.TimeFundAmount,
                gameRevenueFundAmount = report.GameRevenueFundAmount,
                productShareFundAmount = report.ProductShareFundAmount,
                productBonusTotalAmount = report.ProductBonusTotalAmount,
                bonusTotalAmount = report.BonusTotalAmount,
                employees = report.Employees.Select(employee => new
                {
                    employeeName = employee.EmployeeName,
                    workHours = employee.WorkHours,
                    gameRevenue = employee.GameRevenue,
                    productRevenue = employee.ProductRevenue,
                    timeAmount = employee.TimeAmount,
                    gameRevenueAmount = employee.GameRevenueAmount,
                    productShareAmount = employee.ProductShareAmount,
                    productBonusAmount = employee.ProductBonusAmount,
                    bonusAmount = employee.BonusAmount,
                    bonuses = employee.Bonuses.Select(bonus => new
                    {
                        createdAt = bonus.CreatedAt.ToString("O"),
                        type = bonus.Type,
                        title = bonus.Title,
                        description = bonus.Description,
                        amount = bonus.Amount
                    }).ToList(),
                    grossAmount = employee.GrossAmount,
                    lossesAmount = employee.LossesAmount,
                    moneyLossesAmount = employee.MoneyLossesAmount,
                    rawMoneyLossesAmount = employee.RawMoneyLossesAmount,
                    productLossesAmount = employee.ProductLossesAmount,
                    paidAmount = employee.PaidAmount,
                    remainingAmount = employee.RemainingAmount
                }).ToList()
            };
        }

        private static ProductServiceMonthSummary BuildProductServiceMonthSummary(
            DateTime fromInclusive,
            DateTime toExclusive,
            AutoSalaryReport salaryReport)
        {
            var summary = new ProductServiceMonthSummary
            {
                EmployeeBonus = salaryReport.ProductBonusTotalAmount,
                ProductBonusPercent = salaryReport.Settings.ProductBonusPercent
            };

            foreach (var payment in PaymentService.GetRecordsByPeriod(fromInclusive, toExclusive))
            {
                if (payment.GameSessionId != null)
                    continue;

                foreach (var item in payment.Items ?? new List<CheckoutItem>())
                {
                    if (!IsProductServiceCheckoutItem(item))
                        continue;

                    AddProductServiceSale(
                        summary,
                        item,
                        payment.CreatedAt,
                        payment.EmployeeName,
                        payment.PlaceName
                    );
                }
            }

            foreach (var session in ActionLogService.GetAllGameSessions())
            {
                if (session.ClosedAt == null ||
                    session.ClosedAt.Value < fromInclusive ||
                    session.ClosedAt.Value >= toExclusive)
                {
                    continue;
                }

                foreach (var line in session.SaleLines.Where(line => line.IsPaid))
                {
                    AddProductServiceSale(summary, line);
                }
            }

            foreach (var audit in StockAuditService.GetByPeriod(fromInclusive, toExclusive)
                         .Where(item => item.Difference < 0))
            {
                AddProductStat(
                    summary,
                    audit.ProductName,
                    soldQuantity: 0,
                    lostQuantity: Math.Abs(audit.Difference),
                    revenue: 0,
                    margin: 0
                );
            }

            summary.NetProfit =
                summary.ServiceRevenue + summary.ProductProfit - summary.EmployeeBonus;

            return summary;
        }

        private static bool IsProductServiceCheckoutItem(CheckoutItem item)
        {
            if (item == null)
                return false;

            string category = item.Category?.Trim() ?? "";

            return category == "Товар" ||
                   category == "Услуга" ||
                   category == "Товары и услуги" ||
                   category == "Товары/услуги" ||
                   item.ItemType == SaleItemType.Product.ToString() ||
                   item.ItemType == SaleItemType.Service.ToString();
        }

        private static void AddProductServiceSale(
            ProductServiceMonthSummary summary,
            CheckoutItem item,
            DateTime createdAt,
            string employeeName,
            string placeName)
        {
            bool isProduct = IsProductSale(item.ItemType, item.Category, item.Name);
            int total = item.TotalAmount;
            string itemType = isProduct ? "Товар" : "Услуга";
            int purchasePrice = isProduct
                ? ResolvePurchasePrice(item.Name, item.PurchasePrice)
                : 0;
            int cost = purchasePrice * item.Quantity;
            int profit = isProduct ? total - cost : total;

            summary.Sales.Add(new ProductServiceSaleRow
            {
                CreatedAt = createdAt,
                ItemName = item.Name,
                ItemType = itemType,
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice,
                PurchasePrice = purchasePrice,
                TotalAmount = total,
                ProfitAmount = profit,
                EmployeeName = employeeName,
                PlaceName = placeName
            });

            if (isProduct)
            {
                summary.ProductRevenue += total;
                summary.ProductPurchaseCost += cost;
                summary.ProductProfit += total - cost;
                AddProductStat(summary, item.Name, item.Quantity, 0, total, total - cost);
                return;
            }

            summary.ServiceRevenue += total;
        }

        private static void AddProductServiceSale(
            ProductServiceMonthSummary summary,
            GameSessionSaleLine line)
        {
            if (line.ItemType == SaleItemType.Product)
            {
                int purchasePrice = ResolvePurchasePrice(line.ItemName, line.PurchasePrice);
                int cost = purchasePrice * line.Quantity;
                int profit = line.TotalAmount - cost;

                summary.Sales.Add(new ProductServiceSaleRow
                {
                    CreatedAt = line.CreatedAt,
                    ItemName = line.ItemName,
                    ItemType = "Товар",
                    Quantity = line.Quantity,
                    UnitPrice = line.UnitPrice,
                    PurchasePrice = purchasePrice,
                    TotalAmount = line.TotalAmount,
                    ProfitAmount = profit,
                    EmployeeName = line.EmployeeName,
                    PlaceName = ""
                });

                summary.ProductRevenue += line.TotalAmount;
                summary.ProductPurchaseCost += cost;
                summary.ProductProfit += profit;
                AddProductStat(summary, line.ItemName, line.Quantity, 0, line.TotalAmount, profit);
                return;
            }

            summary.Sales.Add(new ProductServiceSaleRow
            {
                CreatedAt = line.CreatedAt,
                ItemName = line.ItemName,
                ItemType = "Услуга",
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                PurchasePrice = 0,
                TotalAmount = line.TotalAmount,
                ProfitAmount = line.TotalAmount,
                EmployeeName = line.EmployeeName,
                PlaceName = ""
            });
            summary.ServiceRevenue += line.TotalAmount;
        }

        private static bool IsProductSale(
            string itemType,
            string category,
            string itemName)
        {
            if (itemType == SaleItemType.Product.ToString() || category == "Товар")
                return true;

            if (itemType == SaleItemType.Service.ToString() || category == "Услуга")
                return false;

            return ProductStockService.IsProductTracked(itemName);
        }

        private static int ResolvePurchasePrice(string productName, int savedPurchasePrice)
        {
            if (savedPurchasePrice > 0)
                return savedPurchasePrice;

            return ProductStockService.GetPurchasePrice(productName);
        }

        private static void AddProductStat(
            ProductServiceMonthSummary summary,
            string productName,
            int soldQuantity,
            int lostQuantity,
            int revenue,
            int margin)
        {
            if (string.IsNullOrWhiteSpace(productName))
                return;

            ProductStatRow stat;

            if (summary.ProductStats.ContainsKey(productName))
            {
                stat = summary.ProductStats[productName];
            }
            else
            {
                stat = new ProductStatRow
                {
                    ProductName = productName
                };
                summary.ProductStats[productName] = stat;
            }

            stat.SoldQuantity += soldQuantity;
            stat.LostQuantity += lostQuantity;
            stat.Revenue += revenue;
            stat.Margin += margin;
            stat.EmployeeBonus = Percent(stat.Revenue, summary.ProductBonusPercent);
            stat.NetProfit = stat.Margin - stat.EmployeeBonus;
        }

        private static int Percent(int amount, int percent)
        {
            return (int)Math.Round(amount * (percent / 100.0));
        }

        private static int GetMonthMovementExpenseTotal(
            DateTime monthStart,
            DateTime nextMonthStart,
            string paymentMethod)
        {
            return CashService
                .GetExpenseRecordsByPaymentMethod(monthStart, nextMonthStart, paymentMethod)
                .Where(record => !CashService.IsPriorMonthExpense(record, monthStart))
                .Sum(record => record.Amount);
        }

        private static int CalculateExpectedCashBalanceForReconciliation(
            DateTime monthStart,
            DateTime nextMonthStart,
            int? actualCashBalance)
        {
            _ = actualCashBalance;

            return CashBalanceSummaryService.CalculateProgramCashBalanceByPeriod(
                       monthStart,
                       nextMonthStart
                   ) ??
                   CashBalanceSummaryService.CalculateExpectedCashBalanceByPeriod(
                       monthStart,
                       nextMonthStart
                   );
        }

        private static int CalculateExpectedCashlessBalanceForReconciliation(
            DateTime monthStart,
            DateTime nextMonthStart,
            int? actualCashlessBalance)
        {
            _ = actualCashlessBalance;

            return CashBalanceSummaryService.CalculateProgramCashlessBalanceByPeriod(
                       monthStart,
                       nextMonthStart
                   ) ??
                   CashBalanceSummaryService.CalculateExpectedCashlessBalanceByPeriod(
                       monthStart,
                       nextMonthStart
                   );
        }

        private static int? CalculateMoneyShortageCapForReconciliation(
            DateTime monthStart,
            DateTime nextMonthStart)
        {
            int? actualCash = CalculateActualCashBalanceByPeriod(monthStart, nextMonthStart);
            int? actualCashless = CalculateActualCashlessBalanceByPeriod(monthStart, nextMonthStart);

            if (!actualCash.HasValue || !actualCashless.HasValue)
                return null;

            int expectedCash = CalculateExpectedCashBalanceForReconciliation(
                monthStart,
                nextMonthStart,
                actualCash
            );
            int expectedCashless = CalculateExpectedCashlessBalanceForReconciliation(
                monthStart,
                nextMonthStart,
                actualCashless
            );
            int difference = actualCash.Value + actualCashless.Value - expectedCash - expectedCashless;

            return difference < 0
                ? Math.Abs(difference)
                : 0;
        }

        private static int CalculateOpeningBalance(
            int? actualBalance,
            int expectedBalance,
            int movementAmount)
        {
            int endBalance = actualBalance ?? expectedBalance;
            int positiveMovement = Math.Max(0, movementAmount);
            return Math.Max(0, endBalance - positiveMovement);
        }

        private static int CalculateExpectedBalanceWithOpening(
            int openingBalance,
            int movementAmount)
        {
            return Math.Max(0, openingBalance + movementAmount);
        }

        private static int CalculateOwnerAvailableBalance(
            int? actualBalance,
            int expectedBalance,
            IEnumerable<CashRecord> ownerWithdrawRecords,
            string paymentMethod,
            DateTime nextMonthStart)
        {
            int baseBalance = actualBalance ?? expectedBalance;
            int postMonthWithdrawals = ownerWithdrawRecords
                .Where(record =>
                    record.PaymentMethod == paymentMethod &&
                    record.CreatedAt >= nextMonthStart)
                .Sum(record => record.Amount);

            return Math.Max(0, baseBalance - postMonthWithdrawals);
        }

        private static int CalculateOwnerAvailableBalanceForPayment(
            DateTime monthStart,
            DateTime nextMonthStart,
            string paymentMethod)
        {
            var ownerWithdrawRecords = CashService.GetOwnerWithdrawRecordsByPeriod(
                monthStart,
                nextMonthStart
            );

            if (paymentMethod.Equals("Наличные", StringComparison.OrdinalIgnoreCase))
            {
                int? actualCashBalance = CalculateActualCashBalanceByPeriod(monthStart, nextMonthStart);
                int expectedCashBalance = CalculateExpectedCashBalanceForReconciliation(
                    monthStart,
                    nextMonthStart,
                    actualCashBalance
                );

                int available = CalculateOwnerAvailableBalance(
                    actualCashBalance,
                    expectedCashBalance,
                    ownerWithdrawRecords,
                    "Наличные",
                    nextMonthStart
                );
                int opening = CalculateOwnerOpeningBalanceAvailableForPayment(
                    monthStart,
                    nextMonthStart,
                    "Наличные"
                );

                return Math.Max(0, available - opening);
            }

            if (paymentMethod.Equals("Безнал", StringComparison.OrdinalIgnoreCase))
            {
                int? actualCashlessBalance = CalculateActualCashlessBalanceByPeriod(monthStart, nextMonthStart);
                int expectedCashlessBalance = CalculateExpectedCashlessBalanceForReconciliation(
                    monthStart,
                    nextMonthStart,
                    actualCashlessBalance
                );

                int available = CalculateOwnerAvailableBalance(
                    actualCashlessBalance,
                    expectedCashlessBalance,
                    ownerWithdrawRecords,
                    "Безнал",
                    nextMonthStart
                );
                int opening = CalculateOwnerOpeningBalanceAvailableForPayment(
                    monthStart,
                    nextMonthStart,
                    "Безнал"
                );

                return Math.Max(0, available - opening);
            }

            throw new Exception("Для забора владельца выберите Наличные или Безнал.");
        }

        private static int CalculateOwnerOpeningBalanceAvailableForPayment(
            DateTime monthStart,
            DateTime nextMonthStart,
            string paymentMethod)
        {
            var cashBalanceSummary = CashBalanceSummaryService.Build(monthStart, nextMonthStart);
            var incomePayment = GetCombinedPaymentSummary(CashReportPeriodMode.Month, monthStart);

            if (paymentMethod.Equals("Наличные", StringComparison.OrdinalIgnoreCase))
            {
                int cashMovement = incomePayment.CashAmount - GetMonthMovementExpenseTotal(
                    monthStart,
                    nextMonthStart,
                    "Наличные"
                );

                return CalculateOpeningBalance(
                    null,
                    cashBalanceSummary.ExpectedCashBalance,
                    cashMovement
                );
            }

            if (paymentMethod.Equals("Безнал", StringComparison.OrdinalIgnoreCase))
            {
                int cashlessMovement = incomePayment.MBankAmount - GetMonthMovementExpenseTotal(
                    monthStart,
                    nextMonthStart,
                    "Безнал"
                );

                return CalculateOpeningBalance(
                    null,
                    cashBalanceSummary.ExpectedCashlessBalance,
                    cashlessMovement
                );
            }

            throw new Exception("Для забора стартового остатка выберите Наличные или Безнал.");
        }

        private static int CalculatePossibleProfit(
            int turnover,
            int clubExpenses,
            int stockPurchases,
            int salaryAccrued,
            int losses)
        {
            return turnover - clubExpenses - stockPurchases - salaryAccrued - losses;
        }

        private static int? CalculateActualMoneyBalance(
            int? actualCashBalance,
            int? actualCashlessBalance)
        {
            if (!actualCashBalance.HasValue || !actualCashlessBalance.HasValue)
                return null;

            return actualCashBalance.Value + actualCashlessBalance.Value;
        }

        private class ProductServiceMonthSummary
        {
            public int ProductRevenue { get; set; }

            public int ServiceRevenue { get; set; }

            public int ProductPurchaseCost { get; set; }

            public int ProductProfit { get; set; }

            public int EmployeeBonus { get; set; }

            public int NetProfit { get; set; }

            public int ProductBonusPercent { get; set; }

            public List<ProductServiceSaleRow> Sales { get; } = new List<ProductServiceSaleRow>();

            public Dictionary<string, ProductStatRow> ProductStats { get; } =
                new Dictionary<string, ProductStatRow>(StringComparer.OrdinalIgnoreCase);
        }

        private class ProductServiceSaleRow
        {
            public DateTime CreatedAt { get; set; }

            public string ItemName { get; set; } = "";

            public string ItemType { get; set; } = "";

            public int Quantity { get; set; }

            public int UnitPrice { get; set; }

            public int PurchasePrice { get; set; }

            public int TotalAmount { get; set; }

            public int ProfitAmount { get; set; }

            public string EmployeeName { get; set; } = "";

            public string PlaceName { get; set; } = "";
        }

        private class ProductStatRow
        {
            public string ProductName { get; set; } = "";

            public int SoldQuantity { get; set; }

            public int LostQuantity { get; set; }

            public int Revenue { get; set; }

            public int Margin { get; set; }

            public int EmployeeBonus { get; set; }

            public int NetProfit { get; set; }
        }

        private static object BuildMonthReport(DateTime monthStart)
        {
            DateTime nextMonthStart = monthStart.AddMonths(1);

            int games = GetPaymentTotal(CashReportSection.Games, CashReportPeriodMode.Month, monthStart);
            int products = GetPaymentTotal(CashReportSection.ProductsAndServices, CashReportPeriodMode.Month, monthStart);
            int income = games + products;
            int expenses = CashService.GetClubExpenseTotalByPeriod(monthStart, nextMonthStart);
            int stockPurchase = StockPurchaseService.GetTotalByPeriod(monthStart, nextMonthStart);
            var stockPurchaseExpenseRecords = CashService.GetExpenseRecordsByExpenseCategory(
                monthStart,
                nextMonthStart,
                "Закупка"
            );
            int stockPurchaseCash = stockPurchaseExpenseRecords
                .Where(record => record.PaymentMethod == "Наличные")
                .Sum(record => record.Amount);
            int stockPurchaseCashless = stockPurchaseExpenseRecords
                .Where(record => record.PaymentMethod == "Безнал")
                .Sum(record => record.Amount);
            var ownerWithdrawRecords = CashService.GetOwnerWithdrawRecordsByPeriod(
                monthStart,
                nextMonthStart
            );
            int ownerWithdraw = ownerWithdrawRecords.Sum(record => record.Amount);
            int ownerWithdrawCash = ownerWithdrawRecords
                .Where(record => record.PaymentMethod == "Наличные")
                .Sum(record => record.Amount);
            int ownerWithdrawCashless = ownerWithdrawRecords
                .Where(record => record.PaymentMethod == "Безнал")
                .Sum(record => record.Amount);
            int salary = CashService.GetSalaryTotalByPeriod(monthStart, nextMonthStart);
            var salaryRecords = CashService.GetSalaryRecordsByPeriod(monthStart, nextMonthStart);
            int salaryCash = salaryRecords
                .Where(record => record.PaymentMethod == "Наличные")
                .Sum(record => record.Amount);
            int salaryCashless = salaryRecords
                .Where(record => record.PaymentMethod == "Безнал")
                .Sum(record => record.Amount);
            var salaryReport = AutoSalaryService.BuildReport(monthStart);
            var productServiceSummary = BuildProductServiceMonthSummary(
                monthStart,
                nextMonthStart,
                salaryReport
            );
            int salaryGross = salaryReport.Employees.Sum(employee => employee.GrossAmount);
            int salaryLosses = salaryReport.Employees.Sum(employee => employee.LossesAmount);
            int salaryAccrued = salaryReport.Employees.Sum(employee =>
                Math.Max(0, employee.GrossAmount - employee.LossesAmount));
            int losses = CashService.GetShortageTotalByPeriod(monthStart, nextMonthStart);
            int possibleProfit = CalculatePossibleProfit(
                income,
                expenses,
                stockPurchase,
                salaryAccrued,
                losses
            );
            var incomePayment = GetCombinedPaymentSummary(CashReportPeriodMode.Month, monthStart);
            int cashExpense = CashService.GetClubCashExpenseTotalByPeriod(monthStart, nextMonthStart);
            int cashlessExpense = CashService.GetClubCashlessExpenseTotalByPeriod(monthStart, nextMonthStart);
            var cashBalanceSummary = CashBalanceSummaryService.Build(monthStart, nextMonthStart);
            int? actualCashBalance = cashBalanceSummary.ActualCashBalance;
            int? programCashBalance = cashBalanceSummary.ProgramCashBalance;
            int? actualCashlessBalance = cashBalanceSummary.ActualCashlessBalance;
            int? programCashlessBalance = cashBalanceSummary.ProgramCashlessBalance;
            int cashExpenseMovement = GetMonthMovementExpenseTotal(
                monthStart,
                nextMonthStart,
                "Наличные"
            );
            int cashlessExpenseMovement = GetMonthMovementExpenseTotal(
                monthStart,
                nextMonthStart,
                "Безнал"
            );
            int cashMovement = incomePayment.CashAmount - cashExpenseMovement;
            int cashlessMovement = incomePayment.MBankAmount - cashlessExpenseMovement;
            int openingCashBalance = CalculateOpeningBalance(
                null,
                cashBalanceSummary.ExpectedCashBalance,
                cashMovement
            );
            int openingCashlessBalance = CalculateOpeningBalance(
                null,
                cashBalanceSummary.ExpectedCashlessBalance,
                cashlessMovement
            );
            int expectedCashBalance = CalculateExpectedBalanceWithOpening(
                openingCashBalance,
                cashMovement
            );
            int expectedCashlessBalance = CalculateExpectedBalanceWithOpening(
                openingCashlessBalance,
                cashlessMovement
            );
            int effectiveProgramCashBalance =
                programCashBalance ?? expectedCashBalance;
            int effectiveProgramCashlessBalance =
                programCashlessBalance ?? expectedCashlessBalance;
            int moneyProgramBalance =
                effectiveProgramCashBalance + effectiveProgramCashlessBalance;
            int? moneyActualBalance = CalculateActualMoneyBalance(
                actualCashBalance,
                actualCashlessBalance
            );
            int? moneyDifference = moneyActualBalance.HasValue
                ? moneyActualBalance.Value - moneyProgramBalance
                : null;
            int moneyShortage = moneyDifference.HasValue && moneyDifference.Value < 0
                ? Math.Abs(moneyDifference.Value)
                : 0;
            int moneyExtra = moneyDifference.HasValue && moneyDifference.Value > 0
                ? moneyDifference.Value
                : 0;
            int ownerAvailableCashBalance = CalculateOwnerAvailableBalance(
                actualCashBalance,
                effectiveProgramCashBalance,
                ownerWithdrawRecords,
                "Наличные",
                nextMonthStart
            );
            int ownerAvailableCashlessBalance = CalculateOwnerAvailableBalance(
                actualCashlessBalance,
                effectiveProgramCashlessBalance,
                ownerWithdrawRecords,
                "Безнал",
                nextMonthStart
            );

            var expenseRecords = BuildCashRecordItems(monthStart, nextMonthStart, "Расходы");
            var expensesByCategory = CashService
                .GetRecordsByPeriodAndCategory(monthStart, nextMonthStart, "Расходы")
                .Where(record => IsOwnerReportExpenseCategory(record.ExpenseCategory))
                .GroupBy(record => string.IsNullOrWhiteSpace(record.ExpenseCategory) ? "Другое" : record.ExpenseCategory)
                .Select(group => new
                {
                    category = group.Key,
                    total = group.Sum(record => record.Amount),
                    cash = group.Where(record => record.PaymentMethod == "Наличные").Sum(record => record.Amount),
                    cashless = group.Where(record => record.PaymentMethod == "Безнал").Sum(record => record.Amount)
                })
                .OrderByDescending(item => item.total)
                .Cast<object>()
                .ToArray();

            return new
            {
                monthKey = monthStart.ToString("yyyy-MM"),
                cash = new
                {
                    gamesMonth = games,
                    productsMonth = products,
                    productSalesMonth = productServiceSummary.ProductRevenue,
                    serviceSalesMonth = productServiceSummary.ServiceRevenue,
                    productSoldPurchaseCostMonth = productServiceSummary.ProductPurchaseCost,
                    productProfitMonth = productServiceSummary.ProductProfit,
                    productServiceBonusMonth = productServiceSummary.EmployeeBonus,
                    productServiceNetProfitMonth = productServiceSummary.NetProfit,
                    cashMonth = income,
                    expensesMonth = expenses,
                    stockPurchaseMonth = stockPurchase,
                    stockPurchaseCashMonth = stockPurchaseCash,
                    stockPurchaseCashlessMonth = stockPurchaseCashless,
                    ownerWithdrawMonth = ownerWithdraw,
                    ownerWithdrawCashMonth = ownerWithdrawCash,
                    ownerWithdrawCashlessMonth = ownerWithdrawCashless,
                    salaryAccruedMonth = salaryAccrued,
                    salaryGrossMonth = salaryGross,
                    salaryLossesMonth = salaryLosses,
                    salaryMonth = salary,
                    salaryCashMonth = salaryCash,
                    salaryCashlessMonth = salaryCashless,
                    shortagesMonth = losses,
                    possibleProfitMonth = possibleProfit,
                    incomeCashMonth = incomePayment.CashAmount,
                    incomeMBankMonth = incomePayment.MBankAmount,
                    actualCashBalanceMonth = actualCashBalance,
                    programCashBalanceMonth = effectiveProgramCashBalance,
                    actualCashlessBalanceMonth = actualCashlessBalance,
                    programCashlessBalanceMonth = effectiveProgramCashlessBalance,
                    expectedCashBalanceMonth = expectedCashBalance,
                    expectedCashlessBalanceMonth = expectedCashlessBalance,
                    openingCashBalanceMonth = openingCashBalance,
                    openingCashlessBalanceMonth = openingCashlessBalance,
                    openingMoneyBalanceMonth = openingCashBalance + openingCashlessBalance,
                    cashMovementMonth = cashMovement,
                    cashlessMovementMonth = cashlessMovement,
                    cashExpenseMovementMonth = cashExpenseMovement,
                    cashlessExpenseMovementMonth = cashlessExpenseMovement,
                    moneyProgramBalanceMonth = moneyProgramBalance,
                    moneyActualBalanceMonth = moneyActualBalance,
                    moneyDifferenceMonth = moneyDifference,
                    moneyShortageMonth = moneyShortage,
                    moneyExtraMonth = moneyExtra,
                    ownerAvailableCashBalanceMonth = ownerAvailableCashBalance,
                    ownerAvailableCashlessBalanceMonth = ownerAvailableCashlessBalance,
                    ownerAvailableMoneyBalanceMonth = ownerAvailableCashBalance + ownerAvailableCashlessBalance,
                    cashExpenseMonth = cashExpense,
                    cashlessExpenseMonth = cashlessExpense
                },
                cashRecords = new
                {
                    expenses = expenseRecords,
                    ownerWithdraw = BuildOwnerWithdrawRecordItems(monthStart, nextMonthStart),
                    games = BuildCashRecordItems(monthStart, nextMonthStart, "Игры"),
                    productsAndServices = BuildCashRecordItems(monthStart, nextMonthStart, "Товары и услуги"),
                    losses = BuildCashRecordItems(monthStart, nextMonthStart, "Недостачи")
                },
                productService = new
                {
                    stockPurchases = BuildStockPurchaseItems(monthStart, nextMonthStart),
                    sales = productServiceSummary.Sales
                        .OrderByDescending(item => item.CreatedAt)
                        .Take(150)
                        .Select(item => new
                        {
                            createdAt = item.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                            itemName = item.ItemName,
                            itemType = item.ItemType,
                            quantity = item.Quantity,
                            unitPrice = item.UnitPrice,
                            purchasePrice = item.PurchasePrice,
                            totalAmount = item.TotalAmount,
                            profitAmount = item.ProfitAmount,
                            employeeName = item.EmployeeName,
                            placeName = item.PlaceName
                        })
                        .ToArray(),
                    productStats = productServiceSummary.ProductStats.Values
                        .OrderByDescending(item => item.Revenue)
                        .Select(item => new
                        {
                            productName = item.ProductName,
                            soldQuantity = item.SoldQuantity,
                            lostQuantity = item.LostQuantity,
                            revenue = item.Revenue,
                            margin = item.Margin,
                            employeeBonus = item.EmployeeBonus,
                            netProfit = item.NetProfit
                        })
                        .ToArray()
                },
                employees = BuildEmployeeMonthItems(monthStart, nextMonthStart, salaryReport),
                expensesByCategory
            };
        }

        private static bool IsOwnerReportExpenseCategory(string expenseCategory)
        {
            if (string.IsNullOrWhiteSpace(expenseCategory))
                return true;

            return !expenseCategory.Equals("Зарплата", StringComparison.OrdinalIgnoreCase) &&
                   !expenseCategory.Equals("Закупка", StringComparison.OrdinalIgnoreCase) &&
                   !expenseCategory.Equals("Владелец", StringComparison.OrdinalIgnoreCase);
        }

        private static object[] BuildEmployeeMonthItems(
            DateTime monthStart,
            DateTime nextMonthStart,
            AutoSalaryReport salaryReport)
        {
            return EmployeeService
                .GetAllEmployees()
                .Select(employee =>
                {
                    var summary = EmployeeStatsService.GetSummary(employee.Name, monthStart);
                    var autoSalary = salaryReport.Employees
                        .FirstOrDefault(item =>
                            item.EmployeeName.Equals(employee.Name, StringComparison.OrdinalIgnoreCase));

                    int salaryForMonth = CashService.GetSalaryTotalByPeriodForEmployee(
                        monthStart,
                        nextMonthStart,
                        employee.Name
                    );

                    var journal = EmployeeStatsService
                        .GetJournalForMonth(employee.Name, monthStart)
                        .Take(150)
                        .ToList();

                    var allJournal = journal
                        .Select(item => new
                        {
                            id = item.Id?.ToString() ?? "",
                            createdAt = item.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                            type = item.Type,
                            lossKind = item.LossKind,
                            title = item.Title,
                            description = item.Description,
                            amount = item.Amount,
                            isFixed = item.IsFixed
                        })
                        .ToList();

                    var incomeJournal = journal
                        .Where(item =>
                            item.Type == "Игры" ||
                            item.Type == "Выручка" ||
                            item.Type == "Товар/услуга" ||
                            item.Type == "Товары и услуги")
                        .Select(item => new
                        {
                            id = item.Id?.ToString() ?? "",
                            createdAt = item.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                            type = item.Type,
                            lossKind = item.LossKind,
                            title = item.Title,
                            description = item.Description,
                            amount = item.Amount,
                            isFixed = item.IsFixed
                        })
                        .ToList();

                    var shortageJournal = journal
                        .Where(item =>
                            item.Type == "Недостача" ||
                            item.Type == "Потеря" ||
                            item.Type.Contains("Штраф"))
                        .Select(item => new
                        {
                            id = item.Id?.ToString() ?? "",
                            createdAt = item.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                            type = item.Type,
                            lossKind = item.LossKind,
                            title = item.Title,
                            description = item.Description,
                            amount = item.Amount,
                            isFixed = item.IsFixed
                        })
                        .ToList();

                    return new
                    {
                        employeeId = employee.EmployeeId,
                        name = employee.Name,
                        pinCode = employee.PinCode,
                        isActive = employee.IsActive,
                        monthWorkTime = EmployeeStatsService.FormatTime(summary.MonthWorkTime),
                        monthIncome = summary.MonthTotalIncome,
                        monthGameIncome = summary.MonthGameIncome,
                        monthProductsIncome = summary.MonthProductsIncome,
                        monthShortages = summary.MonthShortages,
                        monthMoneyLosses = summary.MonthUnpaidMoneyLosses,
                        monthRawMoneyLosses = summary.MonthRawUnpaidMoneyLosses,
                        monthProductLosses = summary.MonthUnpaidProductLosses,
                        monthViolationLosses = summary.MonthUnpaidViolationLosses,
                        monthSalaryPaid = salaryForMonth,
                        autoSalary = autoSalary == null
                            ? null
                            : new
                            {
                                workHours = autoSalary.WorkHours,
                                gameRevenue = autoSalary.GameRevenue,
                                productRevenue = autoSalary.ProductRevenue,
                                timeAmount = autoSalary.TimeAmount,
                                gameRevenueAmount = autoSalary.GameRevenueAmount,
                                productShareAmount = autoSalary.ProductShareAmount,
                                productBonusAmount = autoSalary.ProductBonusAmount,
                                bonusAmount = autoSalary.BonusAmount,
                                grossAmount = autoSalary.GrossAmount,
                                lossesAmount = autoSalary.LossesAmount,
                                moneyLossesAmount = autoSalary.MoneyLossesAmount,
                                rawMoneyLossesAmount = autoSalary.RawMoneyLossesAmount,
                                productLossesAmount = autoSalary.ProductLossesAmount,
                                violationLossesAmount = autoSalary.ViolationLossesAmount,
                                paidAmount = autoSalary.PaidAmount,
                                remainingAmount = autoSalary.RemainingAmount
                            },
                        closedGameSessionsCount = summary.ClosedGameSessionsCount,
                        productServiceOperationsCount = summary.ProductServiceOperationsCount,
                        shortageCount = summary.ShortageCount,
                        journal = allJournal,
                        incomeJournal = incomeJournal,
                        shortageJournal = shortageJournal
                    };
                })
                .Cast<object>()
                .ToArray();
        }

        private static object[] BuildStockPurchaseItems(
            DateTime fromInclusive,
            DateTime toExclusive)
        {
            return StockPurchaseService
                .GetPurchasesByPeriod(fromInclusive, toExclusive)
                .Take(100)
                .Select(purchase => new
                {
                    createdAt = purchase.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                    addedBy = purchase.AddedBy,
                    note = purchase.Note,
                    totalAmount = purchase.TotalAmount,
                    items = purchase.Items.Select(item => new
                    {
                        productName = item.ProductName,
                        quantity = item.Quantity,
                        purchasePrice = item.PurchasePrice,
                        salePrice = item.SalePrice,
                        minimumQuantity = item.MinimumQuantity,
                        totalAmount = item.TotalAmount
                    }).ToArray()
                })
                .Cast<object>()
                .ToArray();
        }

        private static object[] BuildCashRecordItems(
            DateTime fromInclusive,
            DateTime toExclusive,
            string category)
        {
            return CashService
                .GetRecordsByPeriodAndCategory(fromInclusive, toExclusive, category)
                .Take(120)
                .Select(record => new
                {
                    id = record.Id.ToString(),
                    createdAt = record.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                    title = record.Title,
                    description = record.Description,
                    amount = record.Amount,
                    category = record.Category,
                    expenseCategory = record.ExpenseCategory,
                    paymentMethod = record.PaymentMethod,
                    employeeName = record.EmployeeName,
                    incomeEmployeeName = record.IncomeEmployeeName,
                    relatedEmployeeName = record.RelatedEmployeeName,
                    accountingMonthKey = record.AccountingMonthKey,
                    salaryMonthKey = record.SalaryMonthKey,
                    placeName = record.PlaceName
                })
                .Cast<object>()
                .ToArray();
        }

        private static object[] BuildOwnerWithdrawRecordItems(
            DateTime fromInclusive,
            DateTime toExclusive)
        {
            return CashService
                .GetOwnerWithdrawRecordsByPeriod(fromInclusive, toExclusive)
                .Take(120)
                .Select(record => new
                {
                    id = record.Id.ToString(),
                    createdAt = record.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                    title = record.Title,
                    description = record.Description,
                    amount = record.Amount,
                    category = record.Category,
                    expenseCategory = record.ExpenseCategory,
                    paymentMethod = record.PaymentMethod,
                    employeeName = record.EmployeeName,
                    incomeEmployeeName = record.IncomeEmployeeName,
                    relatedEmployeeName = record.RelatedEmployeeName,
                    accountingMonthKey = record.AccountingMonthKey,
                    salaryMonthKey = record.SalaryMonthKey,
                    placeName = record.PlaceName
                })
                .Cast<object>()
                .ToArray();
        }

        private static bool HasUnpaidSessionSales(string placeName)
        {
            return GetUnpaidSessionSalesAmount(placeName) > 0;
        }

        private static int GetCurrentGameAmount(ClubPlace place)
        {
            if (!place.IsBusy)
                return 0;

            if (place.IsOpenMode)
            {
                return TariffService.CalculateOpenModePrice(
                    place.AccruedAmountBeforeCurrentSegment,
                    place.ActivePricePerMinute,
                    place.StartTime);
            }

            return place.PaidAmount;
        }

        private static int GetUnpaidSessionSalesAmount(string placeName)
        {
            var session = ActionLogService.GetActiveGameSessionByPlace(placeName);

            if (session == null)
                return 0;

            return session.SaleLines
                .Where(line => !line.IsPaid)
                .Sum(line => line.TotalAmount);
        }

        private static object[] GetUnpaidSessionSaleLines(string placeName)
        {
            var session = ActionLogService.GetActiveGameSessionByPlace(placeName);

            if (session == null)
                return new object[0];

            return session.SaleLines
                .Where(line => !line.IsPaid)
                .Select(line => new
                {
                    itemName = line.ItemName,
                    itemType = line.ItemType.ToString(),
                    quantity = line.Quantity,
                    unitPrice = line.UnitPrice,
                    totalAmount = line.TotalAmount,
                    employeeName = line.EmployeeName,
                    createdAt = line.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss")
                })
                .Cast<object>()
                .ToArray();
        }

        private static async Task ApplyCommandAsync(
            string commandId,
            FirebaseCommand command,
            IReadOnlyList<ClubPlace> places)
        {
            try
            {
                if (command.Type == "ShowMessage")
                {
                    MessageBox.Show(
                        command.Message,
                        "Команда из Firebase"
                    );

                    await MarkCommandApplied(commandId, command, "Сообщение показано.");
                    return;
                }

                if (command.Type == "RenameClub")
                {
                    string clubName = ApplyRenameClub(command);

                    await PushCurrentStateAsync(places.ToList());

                    await MarkCommandApplied(
                        commandId,
                        command,
                        $"Клуб переименован: {clubName}."
                    );

                    return;
                }

                if (command.Type == "UpdateProductSalePrice")
                {
                    ApplyUpdateProductSalePrice(command);

                    await MarkCommandApplied(
                        commandId,
                        command,
                        $"Цена товара обновлена: {command.ProductName} → {command.SalePrice} сом."
                    );

                    return;
                }

                if (command.Type == "UpdateStockProduct")
                {
                    ApplyUpdateStockProduct(command);

                    await MarkCommandApplied(
                        commandId,
                        command,
                        $"Товар обновлён: {command.ProductName} → {command.NewProductName}."
                    );

                    return;
                }

                if (command.Type == "UpdateServiceItem")
                {
                    ApplyUpdateServiceItem(command);

                    await MarkCommandApplied(
                        commandId,
                        command,
                        $"Услуга обновлена: {command.ProductName} → {command.NewProductName}."
                    );

                    return;
                }

                if (command.Type == "AddStockProduct")
                {
                    ApplyAddStockProduct(command);

                    await MarkCommandApplied(
                        commandId,
                        command,
                        $"Товар добавлен: {command.ProductName}."
                    );

                    return;
                }

                if (command.Type == "DeleteStockProduct")
                {
                    ApplyDeleteStockProduct(command);

                    await MarkCommandApplied(
                        commandId,
                        command,
                        $"Товар удалён: {command.ProductName}."
                    );

                    return;
                }

                if (command.Type == "AddSaleItem")
                {
                    ApplyAddSaleItem(command);

                    await MarkCommandApplied(
                        commandId,
                        command,
                        $"Позиция добавлена: {command.ProductName}, тип: {NormalizeSaleItemType(command.ItemType)}."
                    );

                    return;
                }

                if (command.Type == "DeleteSaleItem")
                {
                    ApplyDeleteSaleItem(command);

                    await MarkCommandApplied(
                        commandId,
                        command,
                        $"Позиция удалена: {command.ProductName}, тип: {NormalizeSaleItemType(command.ItemType)}."
                    );

                    return;
                }

                if (command.Type == "ConfirmStockPurchase")
                {
                    var purchase = ApplyConfirmStockPurchase(command);

                    await MarkCommandApplied(
                        commandId,
                        command,
                        $"Закуп подтверждён: {purchase.TotalAmount} сом, позиций: {purchase.Items.Count}."
                    );

                    return;
                }

                if (command.Type == "AddExpense")
                {
                    ApplyAddExpense(command);

                    await MarkCommandApplied(
                        commandId,
                        command,
                        $"Расход добавлен: {command.Title}, {command.Amount} сом, тип: {NormalizePaymentMethod(command.PaymentMethod)}, категория: {CashService.NormalizeExpenseCategory(command.ExpenseCategory)}."
                    );

                    return;
                }

                if (command.Type == "DeleteExpense")
                {
                    ApplyDeleteExpense(command);

                    await MarkCommandApplied(
                        commandId,
                        command,
                        $"Расход удалён: {command.RecordId}."
                    );

                    return;
                }

                if (command.Type == "RenameExpenseCategory")
                {
                    int changed = ApplyRenameExpenseCategory(command);

                    await MarkCommandApplied(
                        commandId,
                        command,
                        $"Категория расходов переименована: {command.ExpenseCategory} → {command.NewExpenseCategory}. Записей: {changed}."
                    );

                    return;
                }

                if (command.Type == "UpdateExpenseCategoryAmount")
                {
                    ApplyUpdateExpenseCategoryAmount(command);

                    await MarkCommandApplied(
                        commandId,
                        command,
                        $"Сумма категории расходов обновлена: {command.ExpenseCategory}, новый итог {command.Amount} сом."
                    );

                    return;
                }

                if (command.Type == "DeleteExpenseCategory")
                {
                    int deleted = ApplyDeleteExpenseCategory(command);

                    await MarkCommandApplied(
                        commandId,
                        command,
                        $"Категория расходов удалена: {command.ExpenseCategory}. Записей: {deleted}."
                    );

                    return;
                }

                if (command.Type == "ResolveCashReconciliation")
                {
                    var item = ApplyResolveCashReconciliation(command);

                    await MarkCommandApplied(
                        commandId,
                        command,
                        $"Сверка закрыта: {item.Title}, {item.Amount} сом."
                    );

                    return;
                }

                if (command.Type == "AddManualEmployeeMoneyLoss")
                {
                    ApplyAddManualEmployeeMoneyLoss(command);

                    await MarkCommandApplied(
                        commandId,
                        command,
                        $"Фиксированный штраф добавлен: {command.EmployeeName}, {command.Amount} сом."
                    );

                    return;
                }

                if (command.Type == "DeleteEmployeeViolationLoss")
                {
                    ApplyDeleteEmployeeViolationLoss(command);

                    await MarkCommandApplied(
                        commandId,
                        command,
                        "Штраф за нарушение удалён."
                    );

                    return;
                }

                if (command.Type == "VerifyCashlessActual")
                {
                    string message = ApplyVerifyCashlessActual(command);

                    await MarkCommandApplied(
                        commandId,
                        command,
                        message
                    );

                    return;
                }

                if (command.Type == "BalanceCashlessActual")
                {
                    string message = ApplyBalanceCashlessActual(command);

                    await MarkCommandApplied(
                        commandId,
                        command,
                        message
                    );

                    return;
                }

                if (command.Type == "AddSalaryPayment")
                {
                    ApplyAddSalaryPayment(command);
                    var (salaryMonthStart, _) = ParseCommandMonth(command.MonthKey);

                    await MarkCommandApplied(
                        commandId,
                        command,
                        $"Зарплата выдана: {command.EmployeeName}, {command.Amount} сом, тип: {NormalizePaymentMethod(command.PaymentMethod)}, месяц: {salaryMonthStart:yyyy-MM}."
                    );

                    return;
                }

                if (command.Type == "AddEmployeeBonus")
                {
                    var bonus = ApplyAddEmployeeBonus(command);

                    await MarkCommandApplied(
                        commandId,
                        command,
                        $"Премия добавлена: {bonus.EmployeeName}, {bonus.Amount} сом, месяц: {bonus.SalaryMonthKey}."
                    );

                    return;
                }

                if (command.Type == "UpdateAutoSalarySettings")
                {
                    var settings = ApplyUpdateAutoSalarySettings(command);

                    await MarkCommandApplied(
                        commandId,
                        command,
                        $"Настройки авто ЗП сохранены: резерв {settings.ExpenseReservePercent}%, фонд выручки {settings.SalaryFundPercent}%, ставка времени {GetAutoSalaryHourlyRate(settings)} сом/ч, график {settings.WorkDayStartHour:00}:00-{settings.WorkDayEndHour:00}:00."
                    );

                    return;
                }

                if (command.Type == "SetCashlessForToday")
                {
                    ApplySetCashlessForToday(command);

                    await MarkCommandApplied(
                        commandId,
                        command,
                        $"Безнал за сегодня сохранён: {command.Amount} сом."
                    );

                    return;
                }

                if (command.Type == "ClearAllHistoryKeepEmployees")
                {
                    ApplyClearAllHistoryKeepEmployees();

                    await MarkCommandApplied(
                        commandId,
                        command,
                        "История очищена. Сотрудники, товары/услуги и настройки сохранены."
                    );

                    return;
                }

                if (command.Type == "AddEmployee")
                {
                    ApplyAddEmployee(command);

                    await MarkCommandApplied(
                        commandId,
                        command,
                        $"Работник добавлен: {command.EmployeeName}."
                    );

                    return;
                }

                if (command.Type == "UpdateEmployeePin")
                {
                    ApplyUpdateEmployeePin(command);

                    await MarkCommandApplied(
                        commandId,
                        command,
                        $"Код работника изменён: {command.EmployeeName}."
                    );

                    return;
                }

                if (command.Type == "UpdateEmployeeName")
                {
                    int renamedReferences = ApplyUpdateEmployeeName(command, places);

                    await MarkCommandApplied(
                        commandId,
                        command,
                        $"Имя работника изменено: {command.EmployeeName} -> {command.NewEmployeeName}. " +
                        $"Перенесено связанных записей: {renamedReferences}."
                    );

                    return;
                }

                if (command.Type == "DisableEmployee")
                {
                    ApplyDisableEmployee(command);

                    await MarkCommandApplied(
                        commandId,
                        command,
                        $"Работник отключён: {command.EmployeeName}."
                    );

                    return;
                }

                if (command.Type == "EnableEmployee")
                {
                    ApplyEnableEmployee(command);

                    await MarkCommandApplied(
                        commandId,
                        command,
                        $"Работник включён: {command.EmployeeName}."
                    );

                    return;
                }

                if (command.Type == "PrepareAppUpdate")
                {
                    string message = await AppUpdateService.PrepareLatestUpdateAsync();

                    await MarkCommandApplied(
                        commandId,
                        command,
                        message
                    );

                    return;
                }

                if (command.Type == "InstallAppUpdate")
                {
                    var result = await AppUpdateService.InstallLatestUpdateAsync(places);

                    await MarkCommandApplied(
                        commandId,
                        command,
                        result.Message
                    );

                    if (result.ShouldShutdown)
                    {
                        _ = Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            Application.Current.Shutdown();
                        }));
                    }

                    return;
                }

                await MarkCommandError(
                    commandId,
                    command,
                    $"Неизвестный тип команды: {command.Type}"
                );
            }
            catch (Exception ex)
            {
                await MarkCommandError(
                    commandId,
                    command,
                    ex.Message
                );
            }
        }

        private static void ApplyUpdateProductSalePrice(FirebaseCommand command)
        {
            if (string.IsNullOrWhiteSpace(command.ProductName))
                throw new Exception("Не указан productName.");

            if (command.SalePrice < 0)
                throw new Exception("salePrice не может быть меньше 0.");

            var product = ProductStockService.FindByProductName(command.ProductName);

            if (product == null)
                throw new Exception($"Товар не найден: {command.ProductName}");

            ProductStockService.UpdateProductSettings(
                productName: product.ProductName,
                purchasePrice: product.PurchasePrice,
                salePrice: command.SalePrice,
                minimumQuantity: product.MinimumQuantity
            );
        }

        private static void ApplyUpdateStockProduct(FirebaseCommand command)
        {
            string oldProductName = command.ProductName.Trim();
            string newProductName = command.NewProductName.Trim();

            if (string.IsNullOrWhiteSpace(oldProductName))
                throw new Exception("Не указано старое название товара.");

            if (string.IsNullOrWhiteSpace(newProductName))
                newProductName = oldProductName;

            var product = ProductStockService.FindByProductName(oldProductName);

            if (product == null)
                throw new Exception($"Товар не найден: {oldProductName}");

            bool nameChanged = !oldProductName.Equals(newProductName, StringComparison.OrdinalIgnoreCase);

            if (nameChanged)
            {
                if (ProductStockService.ExistsByProductName(newProductName))
                    throw new Exception($"Товар с таким названием уже есть: {newProductName}");

                if (CustomServiceService.ExistsByName(newProductName))
                    throw new Exception($"Услуга с таким названием уже есть: {newProductName}");
            }

            if (command.Quantity < 0)
                throw new Exception("quantity не может быть меньше 0.");

            if (command.PurchasePrice < 0)
                throw new Exception("purchasePrice не может быть меньше 0.");

            if (command.SalePrice < 0)
                throw new Exception("salePrice не может быть меньше 0.");

            if (command.MinimumQuantity < 0)
                throw new Exception("minimumQuantity не может быть меньше 0.");

            bool updated = ProductStockService.UpdateProductFull(
                oldProductName: oldProductName,
                newProductName: newProductName,
                quantity: command.Quantity,
                purchasePrice: command.PurchasePrice,
                salePrice: command.SalePrice,
                minimumQuantity: command.MinimumQuantity
            );

            if (!updated)
                throw new Exception("Не удалось обновить товар.");
        }

        private static void ApplyUpdateServiceItem(FirebaseCommand command)
        {
            string oldServiceName = command.ProductName.Trim();
            string newServiceName = command.NewProductName.Trim();

            if (string.IsNullOrWhiteSpace(oldServiceName))
                throw new Exception("Не указано старое название услуги.");

            if (string.IsNullOrWhiteSpace(newServiceName))
                newServiceName = oldServiceName;

            var service = CustomServiceService.FindByName(oldServiceName);

            if (service == null)
                throw new Exception($"Услуга не найдена: {oldServiceName}");

            bool nameChanged = !oldServiceName.Equals(newServiceName, StringComparison.OrdinalIgnoreCase);

            if (nameChanged)
            {
                if (CustomServiceService.ExistsByName(newServiceName))
                    throw new Exception($"Услуга с таким названием уже есть: {newServiceName}");

                if (ProductStockService.ExistsByProductName(newServiceName))
                    throw new Exception($"Товар с таким названием уже есть: {newServiceName}");
            }

            if (command.SalePrice < 0)
                throw new Exception("salePrice не может быть меньше 0.");

            service.Name = newServiceName;
            service.SalePrice = command.SalePrice;
            service.IsActive = true;

            CustomServiceService.Save();
        }

        private static void ApplyAddStockProduct(FirebaseCommand command)
        {
            command.ItemType = "Product";
            ApplyAddSaleItem(command);
        }

        private static void ApplyDeleteStockProduct(FirebaseCommand command)
        {
            command.ItemType = "Product";
            ApplyDeleteSaleItem(command);
        }

        private static void ApplyAddSaleItem(FirebaseCommand command)
        {
            string itemType = NormalizeSaleItemType(command.ItemType);
            string name = command.ProductName.Trim();

            if (string.IsNullOrWhiteSpace(name))
                throw new Exception("Не указано название товара/услуги.");

            if (itemType == "Product")
            {
                if (ProductStockService.ExistsByProductName(name))
                    throw new Exception($"Такой товар уже есть: {name}");

                if (CustomServiceService.ExistsByName(name))
                    throw new Exception($"Такая услуга уже есть: {name}");

                if (command.InitialQuantity < 0)
                    throw new Exception("initialQuantity не может быть меньше 0.");

                if (command.PurchasePrice < 0)
                    throw new Exception("purchasePrice не может быть меньше 0.");

                if (command.SalePrice < 0)
                    throw new Exception("salePrice не может быть меньше 0.");

                if (command.MinimumQuantity < 0)
                    throw new Exception("minimumQuantity не может быть меньше 0.");

                ProductStockService.AddNewProduct(
                    productName: name,
                    initialQuantity: command.InitialQuantity,
                    purchasePrice: command.PurchasePrice,
                    salePrice: command.SalePrice,
                    minimumQuantity: command.MinimumQuantity
                );

                return;
            }

            if (itemType == "Service")
            {
                if (CustomServiceService.ExistsByName(name))
                    throw new Exception($"Такая услуга уже есть: {name}");

                if (ProductStockService.ExistsByProductName(name))
                    throw new Exception($"Такой товар уже есть: {name}");

                if (command.SalePrice < 0)
                    throw new Exception("salePrice не может быть меньше 0.");

                CustomServiceService.AddService(
                    name: name,
                    salePrice: command.SalePrice
                );

                return;
            }

            throw new Exception($"Неизвестный тип позиции: {command.ItemType}");
        }

        private static void ApplyDeleteSaleItem(FirebaseCommand command)
        {
            string itemType = NormalizeSaleItemType(command.ItemType);
            string name = command.ProductName.Trim();

            if (string.IsNullOrWhiteSpace(name))
                throw new Exception("Не указано название товара/услуги.");

            if (itemType == "Product")
            {
                var product = ProductStockService.FindByProductName(name);

                if (product == null)
                    throw new Exception($"Товар не найден: {name}");

                bool deleted = ProductStockService.DeleteProduct(product.ProductName);

                if (!deleted)
                    throw new Exception($"Не удалось удалить товар: {name}");

                return;
            }

            if (itemType == "Service")
            {
                var service = CustomServiceService.FindByName(name);

                if (service == null)
                    throw new Exception($"Услуга не найдена: {name}");

                bool deleted = CustomServiceService.DeleteService(service.Name);

                if (!deleted)
                    throw new Exception($"Не удалось удалить услугу: {name}");

                return;
            }

            throw new Exception($"Неизвестный тип позиции: {command.ItemType}");
        }

        private static StockPurchase ApplyConfirmStockPurchase(FirebaseCommand command)
        {
            if (command.PurchaseItems == null || command.PurchaseItems.Count == 0)
                throw new Exception("Корзина закупа пустая.");

            var items = new List<StockPurchaseItem>();

            foreach (var item in command.PurchaseItems)
            {
                if (item == null)
                    continue;

                string productName = item.ProductName.Trim();

                if (string.IsNullOrWhiteSpace(productName))
                    continue;

                if (item.Quantity <= 0)
                    continue;

                int purchasePrice = item.PurchasePrice;
                int salePrice = item.SalePrice;

                if (purchasePrice < 0)
                    purchasePrice = 0;

                if (salePrice < 0)
                    salePrice = 0;

                items.Add(new StockPurchaseItem
                {
                    ProductName = productName,
                    Quantity = item.Quantity,
                    PurchasePrice = purchasePrice,
                    SalePrice = salePrice,
                    MinimumQuantity = item.MinimumQuantity
                });
            }

            if (items.Count == 0)
                throw new Exception("В корзине закупа нет правильных товаров.");

            var purchase = StockPurchaseService.AddPurchase(
                items: items,
                addedBy: "Владелец",
                note: command.Description
            );

            CashService.AddExpense(
                employeeName: "Владелец",
                title: "Закуп товаров",
                description: BuildPurchaseDescription(purchase),
                amount: purchase.TotalAmount,
                paymentMethod: NormalizePaymentMethod(command.PaymentMethod),
                expenseCategory: "Закупка"
            );

            return purchase;
        }

        private static string BuildPurchaseDescription(StockPurchase purchase)
        {
            var lines = new List<string>();

            if (!string.IsNullOrWhiteSpace(purchase.Note))
            {
                lines.Add(purchase.Note);
            }

            foreach (var item in purchase.Items)
            {
                lines.Add(
                    $"{item.ProductName}: {item.Quantity} шт × {item.PurchasePrice} сом = {item.TotalAmount} сом"
                );
            }

            return string.Join("\n", lines);
        }

        private static void ApplyAddExpense(FirebaseCommand command)
        {
            if (command.Amount <= 0)
                throw new Exception("amount должен быть больше 0.");

            string expenseCategory = CashService.NormalizeExpenseCategory(command.ExpenseCategory);
            string title = string.IsNullOrWhiteSpace(command.Title)
                ? expenseCategory
                : command.Title.Trim();
            string accountingMonthKey = "";
            string paymentMethod = NormalizePaymentMethod(command.PaymentMethod);

            if (expenseCategory.Equals("Владелец", StringComparison.OrdinalIgnoreCase))
            {
                var (monthStart, nextMonthStart) = ParseCommandMonth(command.MonthKey);
                bool openingBalanceMode = command.OwnerWithdrawMode.Equals(
                    "OpeningBalance",
                    StringComparison.OrdinalIgnoreCase
                );
                accountingMonthKey = openingBalanceMode
                    ? monthStart.AddMonths(-1).ToString("yyyy-MM")
                    : monthStart.ToString("yyyy-MM");
                int available = openingBalanceMode
                    ? CalculateOwnerOpeningBalanceAvailableForPayment(
                        monthStart,
                        nextMonthStart,
                        paymentMethod
                    )
                    : CalculateOwnerAvailableBalanceForPayment(
                        monthStart,
                        nextMonthStart,
                        paymentMethod
                    );

                if (command.Amount > available)
                {
                    throw new Exception(
                        $"Недостаточно остатка за {accountingMonthKey}. " +
                        $"Доступно: {available} сом, запрошено: {command.Amount} сом."
                    );
                }
            }

            CashService.AddExpense(
                employeeName: "Владелец",
                title: title,
                description: command.Description,
                amount: command.Amount,
                paymentMethod: paymentMethod,
                expenseCategory: expenseCategory,
                accountingMonthKey: accountingMonthKey
            );
        }

        private static string ApplyRenameClub(FirebaseCommand command)
        {
            string clubName = command.NewClubName?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(clubName))
                throw new InvalidOperationException("Название клуба не должно быть пустым.");

            if (clubName.Length > 80)
                throw new InvalidOperationException("Название клуба слишком длинное.");

            var identity = PcIdentityService.Current;
            identity.ClubName = clubName;
            PcIdentityService.Save(identity);

            return clubName;
        }

        private static void ApplyDeleteExpense(FirebaseCommand command)
        {
            if (!Guid.TryParse(command.RecordId, out Guid recordId))
                throw new Exception("Не указан корректный recordId.");

            bool deleted = CashService.DeleteRecord(recordId, "Расходы");

            if (!deleted)
                throw new Exception($"Расход не найден: {command.RecordId}");
        }

        private static int ApplyRenameExpenseCategory(FirebaseCommand command)
        {
            if (string.IsNullOrWhiteSpace(command.ExpenseCategory))
                throw new Exception("Не указана категория.");

            if (string.IsNullOrWhiteSpace(command.NewExpenseCategory))
                throw new Exception("Не указано новое название категории.");

            var (monthStart, nextMonthStart) = ParseCommandMonth(command.MonthKey);

            return CashService.RenameExpenseCategoryByPeriod(
                monthStart,
                nextMonthStart,
                command.ExpenseCategory,
                command.NewExpenseCategory
            );
        }

        private static int ApplyDeleteExpenseCategory(FirebaseCommand command)
        {
            if (string.IsNullOrWhiteSpace(command.ExpenseCategory))
                throw new Exception("Не указана категория.");

            var (monthStart, nextMonthStart) = ParseCommandMonth(command.MonthKey);

            return CashService.DeleteExpenseCategoryByPeriod(
                monthStart,
                nextMonthStart,
                command.ExpenseCategory
            );
        }

        private static void ApplyUpdateExpenseCategoryAmount(FirebaseCommand command)
        {
            if (string.IsNullOrWhiteSpace(command.ExpenseCategory))
                throw new Exception("Не указана категория.");

            if (command.Amount < 0)
                throw new Exception("Сумма не должна быть меньше 0.");

            var (monthStart, nextMonthStart) = ParseCommandMonth(command.MonthKey);

            int updatedAmount = CashService.UpdateExpenseCategoryTotalByPeriod(
                monthStart,
                nextMonthStart,
                command.ExpenseCategory,
                command.Amount
            );

            if (updatedAmount <= 0 && command.Amount > 0)
                throw new Exception("Категория расходов не найдена.");
        }

        private static (DateTime monthStart, DateTime nextMonthStart) ParseCommandMonth(string monthKey)
        {
            if (DateTime.TryParse($"{monthKey}-01", out DateTime parsed))
            {
                var monthStart = new DateTime(parsed.Year, parsed.Month, 1);
                return (monthStart, monthStart.AddMonths(1));
            }

            var current = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            return (current, current.AddMonths(1));
        }

        private static CashReconciliationItem ApplyResolveCashReconciliation(FirebaseCommand command)
        {
            if (!Guid.TryParse(command.ReconciliationId, out Guid reconciliationId))
                throw new Exception("Не указан корректный reconciliationId.");

            var sourceItem = CashReconciliationService.Items
                .FirstOrDefault(entry => entry.Id == reconciliationId);
            int actionAmount = sourceItem?.Amount ?? 0;

            if (sourceItem != null &&
                command.ResolutionType == "PaymentTypeMistake" &&
                (sourceItem.Kind == CashReconciliationKind.CashShortage ||
                 sourceItem.Kind == CashReconciliationKind.CashlessShortage))
            {
                throw new Exception(
                    "Недостачу нельзя закрыть вручную как ошибку оплаты. " +
                    "Если это ошибка типа оплаты, система закроет её встречным излишком после сверки. " +
                    "Если денег реально не хватает, внесите корректировку или оформите штраф."
                );
            }

            var item = CashReconciliationService.Resolve(
                reconciliationId,
                "Владелец",
                command.ResolutionType,
                command.Description
            );

            if (command.ResolutionType == "RealShortage" &&
                actionAmount > 0 &&
                item.Kind == CashReconciliationKind.CashShortage)
            {
                string description =
                    $"Сверка налички закрыта владельцем как реальная недостача.\n" +
                    $"Должно быть: {item.ExpectedAmount} сом\n" +
                    $"Фактически: {item.ActualAmount} сом\n" +
                    $"Недостача: {actionAmount} сом";

                CashService.AddShortage(
                    checkedByEmployeeName: item.CheckedByEmployeeName,
                    responsibleEmployeeName: item.ResponsibleEmployeeName,
                    title: "Недостача наличных",
                    description: description,
                    amount: actionAmount
                );

                EmployeeLossService.AddCashShortage(
                    responsibleEmployeeName: item.ResponsibleEmployeeName,
                    checkedByEmployeeName: item.CheckedByEmployeeName,
                    description: description,
                    amount: actionAmount,
                    isFixed: true
                );
            }

            if (command.ResolutionType == "RealShortage" &&
                actionAmount > 0 &&
                item.Kind == CashReconciliationKind.CashlessShortage)
            {
                var monthStart = ResolveReconciliationMonth(command, item);
                var nextMonthStart = monthStart.AddMonths(1);

                DistributeCashlessShortage(
                    actionAmount,
                    monthStart,
                    nextMonthStart,
                    item.ExpectedAmount,
                    item.ActualAmount
                );
            }

            return item;
        }

        private static DateTime ResolveReconciliationMonth(
            FirebaseCommand command,
            CashReconciliationItem item)
        {
            if (DateTime.TryParse($"{command.MonthKey}-01", out DateTime commandMonth))
                return new DateTime(commandMonth.Year, commandMonth.Month, 1);

            return new DateTime(item.CreatedAt.Year, item.CreatedAt.Month, 1);
        }

        private static void ApplyAddManualEmployeeMoneyLoss(FirebaseCommand command)
        {
            string employeeName = command.EmployeeName.Trim();

            if (string.IsNullOrWhiteSpace(employeeName))
                throw new Exception("Не указан сотрудник.");

            if (command.Amount <= 0)
                throw new Exception("Сумма штрафа должна быть больше 0.");

            var employee = EmployeeService
                .GetAllEmployees()
                .FirstOrDefault(item =>
                    item.Name.Equals(employeeName, StringComparison.OrdinalIgnoreCase));

            if (employee == null)
                throw new Exception($"Сотрудник не найден: {employeeName}.");

            string title = string.IsNullOrWhiteSpace(command.Title)
                ? "Ручной денежный штраф"
                : command.Title.Trim();
            string lossKind = command.LossKind.Trim().Equals("violation", StringComparison.OrdinalIgnoreCase)
                ? "violation"
                : "money";
            string description = string.IsNullOrWhiteSpace(command.Description)
                ? (lossKind == "violation"
                    ? "Закреплено владельцем как штраф за нарушение."
                    : "Закреплено владельцем из сырых денежных потерь.")
                : command.Description.Trim();
            string fullDescription =
                $"{description}\n" +
                (lossKind == "violation"
                    ? "Тип: фиксированный штраф за нарушение.\nЭта запись не влияет на разбор кассы."
                    : "Тип: фиксированная денежная потеря.\nЭта запись не уменьшается автоматической балансировкой кассы.");
            var (monthStart, nextMonthStart) = ParseCommandMonth(command.MonthKey);
            DateTime reconciliationFrom = monthStart;
            bool isCurrentMonth = monthStart.Year == DateTime.Today.Year &&
                                  monthStart.Month == DateTime.Today.Month;
            if (isCurrentMonth)
            {
                reconciliationFrom = CashBalanceCheckpointService
                    .GetCurrentCycleStart(monthStart, nextMonthStart);
            }

            if (lossKind == "money")
            {
                CashReconciliationService.NetOpenMoneyCorrections(
                    reconciliationFrom,
                    nextMonthStart,
                    "Система",
                    "Встречные суммы текущего цикла зачтены перед ручным оформлением потери."
                );
                int openShortageTotal = CashReconciliationService.GetOpenShortageTotal(
                    reconciliationFrom,
                    nextMonthStart
                );
                int openExtraTotal = CashReconciliationService.GetOpenExtraTotal(
                    reconciliationFrom,
                    nextMonthStart
                );
                int availableShortage = Math.Max(0, openShortageTotal - openExtraTotal);

                if (command.Amount > availableShortage && isCurrentMonth)
                {
                    int? moneyShortage = CalculateMoneyShortageCapForReconciliation(
                        monthStart,
                        nextMonthStart
                    );
                    int cycleMoneyLosses = EmployeeLossService
                        .GetCappedUnpaidMoneyTotalsByEmployee(
                            reconciliationFrom,
                            nextMonthStart,
                            null
                        )
                        .Values
                        .Sum();
                    int uncoveredBalanceShortage = moneyShortage.HasValue
                        ? Math.Max(0, moneyShortage.Value - cycleMoneyLosses)
                        : 0;
                    int missingOpenAmount = Math.Min(
                        command.Amount - availableShortage,
                        Math.Max(0, uncoveredBalanceShortage - availableShortage)
                    );

                    if (missingOpenAmount > 0)
                    {
                        var balance = CashBalanceSummaryService.Build(
                            monthStart,
                            nextMonthStart
                        );
                        CashReconciliationService.AddBalanceRawDifference(
                            expectedAmount: balance.ProgramCashlessBalance ?? balance.ExpectedCashlessBalance,
                            actualAmount: balance.ActualCashlessBalance ?? 0,
                            amount: missingOpenAmount,
                            isShortage: true,
                            note:
                                $"Восстановлен непокрытый остаток для ручного оформления: {missingOpenAmount} сом. " +
                                $"Выбран сотрудник {employee.Name}.",
                            responsibleEmployeeName: employee.Name
                        );
                        availableShortage += missingOpenAmount;
                    }
                }

                if (availableShortage <= 0)
                    throw new Exception("Нет открытой недостачи в разборе кассы. Используйте штраф за нарушение.");

                if (command.Amount > availableShortage)
                    throw new Exception($"Нельзя оформить за потери больше открытой недостачи: {availableShortage} сом.");
            }

            if (lossKind == "money")
            {
                CashService.AddShortage(
                    checkedByEmployeeName: "Владелец",
                    responsibleEmployeeName: employee.Name,
                    title: title,
                    description: fullDescription,
                    amount: command.Amount
                );
            }

            EmployeeLossService.AddLoss(
                responsibleEmployeeName: employee.Name,
                checkedByEmployeeName: "Владелец",
                lossType: title,
                title: title,
                description: fullDescription,
                amount: command.Amount,
                note: "Ручное фиксированное удержание владельцем",
                lossKind: lossKind,
                isFixed: true
            );

            if (lossKind == "violation")
            {
                EmployeeLossService.FormalizeViolationRecommendationsForEmployee(
                    employee.Name,
                    monthStart,
                    nextMonthStart,
                    command.Amount,
                    $"Оформлено ручным штрафом за нарушение на {employee.Name}: {command.Amount} сом."
                );
            }
            else
            {
                CashReconciliationService.FormalizeOpenShortagesForPeriod(
                    reconciliationFrom,
                    nextMonthStart,
                    command.Amount,
                    "Владелец",
                    $"Оформлено ручным денежным штрафом на {employee.Name}: {command.Amount} сом."
                );
            }
        }

        private static void ApplyDeleteEmployeeViolationLoss(FirebaseCommand command)
        {
            if (!Guid.TryParse(command.RecordId, out Guid lossId))
                throw new Exception("Не указан корректный id штрафа.");

            bool deleted = EmployeeLossService.DeleteFixedViolation(lossId);

            if (!deleted)
            {
                throw new Exception(
                    "Можно удалить только оформленный штраф за нарушение. " +
                    "Кассовые потери, товарные потери и рекомендации этим действием не удаляются."
                );
            }
        }

        private static string ApplyVerifyCashlessActual(FirebaseCommand command)
        {
            if (command.Amount < 0)
                throw new Exception("amount не может быть меньше 0.");

            var monthStart = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            var nextMonthStart = monthStart.AddMonths(1);
            DateTime reconciliationCycleStart = CashBalanceCheckpointService
                .GetCurrentCycleStart(monthStart, nextMonthStart);

            int cashlessExpenses = CashService.GetCashlessExpenseTotalByPeriod(monthStart, nextMonthStart);
            int expectedCashlessIncome = PaymentService.Records
                .Where(record =>
                    record.CreatedAt >= monthStart &&
                    record.CreatedAt < nextMonthStart)
                .Sum(record => record.MBankAmount);
            int calculatedExpectedCashlessBalance = CalculateExpectedCashlessBalanceByPeriod(
                monthStart,
                nextMonthStart
            );

            int actualCashless = command.Amount;
            int expectedCashlessBalance = CalculateExpectedCashlessBalanceForVerification(
                monthStart,
                nextMonthStart,
                command.ExpectedAmount >= 0
                    ? command.ExpectedAmount
                    : calculatedExpectedCashlessBalance
            );
            DateTime cashlessSuspectFrom = GetLatestCashlessVerificationTime(
                monthStart,
                nextMonthStart
            ) ?? reconciliationCycleStart;
            if (cashlessSuspectFrom < reconciliationCycleStart)
                cashlessSuspectFrom = reconciliationCycleStart;
            int difference = actualCashless - expectedCashlessBalance;

            CashlessService.SetAmountForToday(
                amount: actualCashless,
                note: string.IsNullOrWhiteSpace(command.Description)
                    ? "Сверка безнала владельцем"
                    : command.Description,
                expectedAmount: expectedCashlessBalance
            );

            if (difference == 0)
            {
                CashReconciliationService.ResolveStaleCashlessZeroBaselineArtifacts(
                    reconciliationCycleStart,
                    nextMonthStart,
                    expectedCashlessBalance,
                    actualCashless
                );

                return "Остаток безнала сошелся.";
            }

            if (difference > 0)
            {
                var cashlessExtra = CashReconciliationService.AddCashlessVerification(
                    expectedAmount: expectedCashlessBalance,
                    actualAmount: actualCashless,
                    amount: difference,
                    status: CashReconciliationStatus.Open,
                    note: $"Фактический остаток безнала больше программы на {difference} сом. Оставлено как резерв для ошибок типа оплаты."
                );
                int netted = CashReconciliationService.NetOpenMoneyCorrections(
                    reconciliationCycleStart,
                    nextMonthStart,
                    "Система",
                    "Общий зачёт после сверки безнала."
                );

                if (cashlessExtra.Status == CashReconciliationStatus.Resolved)
                    return $"Остаток безнала больше программы на {difference} сом. Общий зачёт после сверки закрыл разбор кассы на {netted} сом.";

                if (netted > 0)
                    return $"Остаток безнала больше программы на {difference} сом. Общий зачёт закрыл {netted} сом. Остаток излишка: {cashlessExtra.Amount} сом.";

                return $"Остаток безнала больше программы на {difference} сом. Излишек оставлен как резерв.";
            }

            int shortage = Math.Abs(difference);
            string suspectedEmployee = FindCashlessShortageSuspect(
                shortage,
                cashlessSuspectFrom,
                DateTime.Now
            );
            var notes = new List<string>
            {
                $"Поступило безнала по программе: {expectedCashlessIncome} сом.",
                $"Расходы безнала: {cashlessExpenses} сом.",
                $"Ожидаемый остаток безнала: {expectedCashlessBalance} сом.",
                $"Фактический остаток: {actualCashless} сом.",
                $"Недостача остатка безнала: {shortage} сом."
            };
            if (!string.IsNullOrWhiteSpace(suspectedEmployee))
                notes.Add($"Рекомендация системы: проверить безнал-операции сотрудника {suspectedEmployee}.");

            var reconciliation = CashReconciliationService.AddCashlessVerification(
                expectedAmount: expectedCashlessBalance,
                actualAmount: actualCashless,
                amount: shortage,
                status: CashReconciliationStatus.Open,
                note: string.Join("\n", notes),
                suspectedEmployeeName: suspectedEmployee
            );
            int nettedShortage = CashReconciliationService.NetOpenMoneyCorrections(
                reconciliationCycleStart,
                nextMonthStart,
                "Система",
                "Общий зачёт после сверки безнала."
            );

            if (reconciliation.Status == CashReconciliationStatus.Resolved)
            {
                notes.Add($"Общий зачёт после сверки закрыл разбор кассы на {nettedShortage} сом.");
                return string.Join(" ", notes);
            }

            if (nettedShortage > 0)
                notes.Add($"Общий зачёт закрыл встречные излишки и недостачи на {nettedShortage} сом. Активный остаток недостачи: {reconciliation.Amount} сом.");

            int? moneyShortageCap = CalculateMoneyShortageCapForReconciliation(
                monthStart,
                nextMonthStart
            );
            int rawExistingMoneyLosses = EmployeeLossService
                .GetCappedUnpaidMoneyTotalsByEmployee(
                    reconciliationCycleStart,
                    nextMonthStart,
                    null
                )
                .Values
                .Sum();
            int allowedNewMoneyShortage = moneyShortageCap.HasValue
                ? Math.Max(0, moneyShortageCap.Value - rawExistingMoneyLosses)
                : reconciliation.Amount;
            int finalShortage = Math.Min(reconciliation.Amount, allowedNewMoneyShortage);

            if (finalShortage <= 0)
            {
                CashReconciliationService.Resolve(
                    reconciliation.Id,
                    "Система",
                    "PaymentTypeMistake",
                    "Общая касса нал+безнал уже покрыта существующими денежными удержаниями. Новый штраф не создан."
                );
                notes.Add("Общая денежная недостача уже покрыта существующими удержаниями. Новый штраф не создан.");
                return string.Join(" ", notes);
            }

            CashReconciliationService.UpdateOpenAmount(
                reconciliation.Id,
                finalShortage,
                finalShortage == shortage
                    ? "Оставлено активной карточкой в разделе Разница кассы."
                    : $"После общего сопоставления нал+безнал активная сумма уменьшена до {finalShortage} сом."
            );

            if (moneyShortageCap.HasValue)
            {
                notes.Add($"Общая денежная недостача кассы: {moneyShortageCap.Value} сом.");
                notes.Add($"Уже было денежных удержаний: {rawExistingMoneyLosses} сом.");
            }

            notes.Add($"Активная карточка на {finalShortage} сом добавлена в Разница кассы. Владелец может закрыть её или оформить как потери.");
            return string.Join(" ", notes);
        }

        private static DateTime? GetLatestCashlessVerificationTime(
            DateTime monthStart,
            DateTime nextMonthStart)
        {
            return CashlessService.Records
                .Where(record =>
                    record.Date >= monthStart.Date &&
                    record.Date < nextMonthStart.Date)
                .OrderByDescending(record => record.UpdatedAt)
                .Select(record => (DateTime?)record.UpdatedAt)
                .FirstOrDefault();
        }

        private static void EnsureCashlessShortageSuspect(
            CashReconciliationItem item,
            DateTime monthStart)
        {
            if (item == null ||
                item.Kind != CashReconciliationKind.CashlessShortage ||
                item.Status != CashReconciliationStatus.Open ||
                item.Amount <= 0 ||
                !string.IsNullOrWhiteSpace(item.ResponsibleEmployeeName) ||
                !string.IsNullOrWhiteSpace(item.SuspectedEmployeeName))
            {
                return;
            }

            string suspectedEmployee = FindCashlessShortageSuspect(
                item.Amount,
                monthStart,
                item.CreatedAt
            );

            if (string.IsNullOrWhiteSpace(suspectedEmployee))
                return;

            CashReconciliationService.SetSuspectedEmployee(
                item.Id,
                suspectedEmployee,
                $"Рекомендация системы: проверить безнал-операции сотрудника {suspectedEmployee}."
            );
        }

        private static string FindCashlessShortageSuspect(
            int shortageAmount,
            DateTime fromExclusive,
            DateTime toInclusive)
        {
            if (shortageAmount <= 0)
                return "";

            return PaymentService.Records
                .Where(record =>
                    record.CreatedAt > fromExclusive &&
                    record.CreatedAt <= toInclusive &&
                    record.MBankAmount > 0)
                .GroupBy(record =>
                    string.IsNullOrWhiteSpace(record.EmployeeName)
                        ? "Неизвестно"
                        : record.EmployeeName.Trim(),
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => new
                {
                    EmployeeName = group.Key,
                    Amount = group.Sum(record => record.MBankAmount),
                    LastPaymentAt = group.Max(record => record.CreatedAt)
                })
                .Where(group => group.Amount >= shortageAmount)
                .OrderByDescending(group => group.Amount)
                .ThenByDescending(group => group.LastPaymentAt)
                .Select(group => group.EmployeeName)
                .FirstOrDefault() ?? "";
        }

        private static int CalculateExpectedCashlessBalanceForVerification(
            DateTime monthStart,
            DateTime nextMonthStart,
            int fallbackExpectedCashless)
        {
            var latestActual = CashlessService.Records
                .Where(record =>
                    record.Date >= monthStart.Date &&
                    record.Date < nextMonthStart.Date)
                .OrderByDescending(record => record.UpdatedAt)
                .FirstOrDefault();

            if (latestActual == null)
                return fallbackExpectedCashless;

            int incomeAfterLatestActual = PaymentService.Records
                .Where(record =>
                    record.CreatedAt > latestActual.UpdatedAt &&
                    record.CreatedAt < nextMonthStart)
                .Sum(record => record.MBankAmount);
            int expensesAfterLatestActual = CashService.Records
                .Where(record =>
                    record.CreatedAt > latestActual.UpdatedAt &&
                    record.CreatedAt < nextMonthStart &&
                    record.Type == CashRecordType.Expense &&
                    record.PaymentMethod.Equals("Безнал", StringComparison.OrdinalIgnoreCase))
                .Sum(record => record.Amount);

            return Math.Max(
                0,
                latestActual.Amount + incomeAfterLatestActual - expensesAfterLatestActual
            );
        }

        private static string ApplyBalanceCashlessActual(FirebaseCommand command)
        {
            if (command.Amount < 0)
                throw new Exception("amount не может быть меньше 0.");

            var monthStart = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            var nextMonthStart = monthStart.AddMonths(1);
            DateTime reconciliationCycleStart = CashBalanceCheckpointService
                .GetCurrentCycleStart(monthStart, nextMonthStart);
            int actualCashless = command.Amount;
            int? actualCash = CalculateActualCashBalanceByPeriod(
                monthStart,
                nextMonthStart
            );
            int expectedCash = CalculateExpectedCashBalanceForReconciliation(
                monthStart,
                nextMonthStart,
                actualCash
            );
            int calculatedExpectedCashless = CalculateExpectedCashlessBalanceForReconciliation(
                monthStart,
                nextMonthStart,
                actualCashless
            );
            int expectedCashless = calculatedExpectedCashless;
            int cashDifference = actualCash.HasValue
                ? actualCash.Value - expectedCash
                : 0;
            int totalDifference = actualCash.HasValue
                ? actualCash.Value + actualCashless - expectedCash - expectedCashless
                : actualCashless - expectedCashless;
            var existingMoneyLossesByEmployee = EmployeeLossService
                .GetCappedUnpaidMoneyTotalsByEmployee(
                    reconciliationCycleStart,
                    nextMonthStart,
                    null
                );
            int rawExistingMoneyLosses = existingMoneyLossesByEmployee
                .Values
                .Sum();
            int finalRawDifference = totalDifference < 0
                ? -Math.Max(0, Math.Abs(totalDifference) - rawExistingMoneyLosses)
                : totalDifference;
            string note = string.IsNullOrWhiteSpace(command.Description)
                ? "Баланс безнала владельцем"
                : command.Description;

            int netted = CashReconciliationService.NetOpenMoneyCorrections(
                reconciliationCycleStart,
                nextMonthStart,
                "Система",
                "Открытые излишки и недостачи взаимно зачтены перед итоговой корректировкой."
            );
            string suggestedResponsible = CashReconciliationService.GetSuggestedResponsibleForOpenShortages(
                reconciliationCycleStart,
                nextMonthStart
            );
            if (string.IsNullOrWhiteSpace(suggestedResponsible))
            {
                string historicalResponsible = CashReconciliationService
                    .GetSuggestedResponsibleForShortageHistory(
                        reconciliationCycleStart,
                        nextMonthStart
                    );
                if (existingMoneyLossesByEmployee.TryGetValue(
                        historicalResponsible,
                        out int historicalFormalizedAmount) &&
                    historicalFormalizedAmount > 0)
                {
                    suggestedResponsible = historicalResponsible;
                }
            }
            string suggestedSuspect = CashReconciliationService.GetSuggestedSuspectForOpenShortages(
                reconciliationCycleStart,
                nextMonthStart
            );
            if (string.IsNullOrWhiteSpace(suggestedResponsible) &&
                string.IsNullOrWhiteSpace(suggestedSuspect))
            {
                suggestedSuspect = CashReconciliationService
                    .GetSuggestedSuspectForShortageHistory(
                        reconciliationCycleStart,
                        nextMonthStart
                    );
            }
            if (string.IsNullOrWhiteSpace(suggestedResponsible) &&
                string.IsNullOrWhiteSpace(suggestedSuspect))
            {
                suggestedSuspect = string.IsNullOrWhiteSpace(
                    ShiftAcceptanceService.Current.DisplayResponsibleEmployeeName)
                    ? ShiftAcceptanceService.Current.ResponsibleEmployeeName.Trim()
                    : ShiftAcceptanceService.Current.DisplayResponsibleEmployeeName.Trim();
            }
            string rawRecommendation = !string.IsNullOrWhiteSpace(suggestedResponsible)
                ? suggestedResponsible
                : suggestedSuspect;
            int autoFormalizedShortage = 0;
            if (finalRawDifference < 0 &&
                !string.IsNullOrWhiteSpace(suggestedResponsible))
            {
                int amountToFormalize = Math.Abs(finalRawDifference);
                autoFormalizedShortage = CashReconciliationService.FormalizeOpenShortagesForEmployee(
                    reconciliationCycleStart,
                    nextMonthStart,
                    suggestedResponsible,
                    amountToFormalize,
                    "Система",
                    $"После общей сверки подтверждена реальная денежная недостача {amountToFormalize} сом. Оформлено на {suggestedResponsible}."
                );

                int missingFormalizedAmount = amountToFormalize - autoFormalizedShortage;
                if (missingFormalizedAmount > 0)
                {
                    CashReconciliationService.AddBalanceRawDifference(
                        expectedAmount: expectedCashless,
                        actualAmount: actualCashless,
                        amount: missingFormalizedAmount,
                        isShortage: true,
                        note:
                            $"Восстановлен непокрытый остаток текущего цикла: {missingFormalizedAmount} сом. " +
                            $"Ответственный сотрудник: {suggestedResponsible}.",
                        responsibleEmployeeName: suggestedResponsible
                    );

                    autoFormalizedShortage += CashReconciliationService
                        .FormalizeOpenShortagesForEmployee(
                            reconciliationCycleStart,
                            nextMonthStart,
                            suggestedResponsible,
                            missingFormalizedAmount,
                            "Система",
                            $"После корректировки подтверждён остаток недостачи {missingFormalizedAmount} сом. Оформлено на {suggestedResponsible}."
                        );
                }

                if (autoFormalizedShortage > 0)
                {
                    CashService.AddShortage(
                        checkedByEmployeeName: "Система",
                        responsibleEmployeeName: suggestedResponsible,
                        title: "Недостача после сверки кассы",
                        description:
                            $"Общая сверка подтвердила денежную недостачу {autoFormalizedShortage} сом.\n" +
                            $"Наличные факт: {(actualCash.HasValue ? actualCash.Value.ToString() : "нет приёмки")} сом.\n" +
                            $"Безнал факт: {actualCashless} сом.",
                        amount: autoFormalizedShortage
                    );

                    EmployeeLossService.AddLoss(
                        responsibleEmployeeName: suggestedResponsible,
                        checkedByEmployeeName: "Система",
                        lossType: "Недостача кассы",
                        title: "Недостача кассы",
                        description:
                            $"Автоматически оформлено после общей сверки кассы: {autoFormalizedShortage} сом.",
                        amount: autoFormalizedShortage,
                        note: "Оформлено из открытой карточки разборки после подтверждения сверкой.",
                        lossKind: "money",
                        isFixed: true
                    );

                    finalRawDifference += autoFormalizedShortage;
                }
            }

            int closed = CashReconciliationService.CloseOpenItemsForBalance(
                monthStart,
                nextMonthStart,
                "Владелец",
                $"Баланс зафиксирован: безнал {actualCashless} сом. Старый цикл закрыт перед новой итоговой корректировкой."
            );

            CashlessService.SetAmountForToday(
                amount: actualCashless,
                note: note,
                expectedAmount: actualCashless
            );

            if (actualCash.HasValue)
            {
                CashBalanceCheckpointService.AddCurrentMonthCheckpoint(
                    actualCash.Value,
                    note
                );
            }

            if (finalRawDifference != 0)
            {
                string cashFactText = actualCash.HasValue
                    ? $"{actualCash.Value} сом"
                    : "нет приёмки";
                string rawNote =
                    "Итоговая сырая корректировка после баланса.\n" +
                    $"Наличные по программе: {expectedCash} сом\n" +
                    $"Наличные факт: {cashFactText}\n" +
                    $"Коррекция программы по наличке: {cashDifference:+#;-#;0} сом\n" +
                    $"Безнал по программе: {expectedCashless} сом\n" +
                    $"Безнал факт: {actualCashless} сом\n" +
                    $"Общая денежная разница: {totalDifference:+#;-#;0} сом\n" +
                    $"Уже есть денежных удержаний: {rawExistingMoneyLosses} сом\n" +
                    $"Итог после учета удержаний: {(finalRawDifference < 0 ? "сырые потери" : "излишек")} {Math.Abs(finalRawDifference)} сом";

                if (finalRawDifference < 0 && !string.IsNullOrWhiteSpace(rawRecommendation))
                    rawNote += $"\nРекомендация системы: проверить сотрудника {rawRecommendation}.";

                CashReconciliationService.AddBalanceRawDifference(
                    expectedAmount: expectedCashless,
                    actualAmount: actualCashless,
                    amount: Math.Abs(finalRawDifference),
                    isShortage: finalRawDifference < 0,
                    note: rawNote,
                    responsibleEmployeeName: finalRawDifference < 0
                        ? suggestedResponsible
                        : "",
                    suspectedEmployeeName: finalRawDifference < 0 &&
                        string.IsNullOrWhiteSpace(suggestedResponsible)
                            ? suggestedSuspect
                            : ""
                );
            }

            string cashPart = actualCash.HasValue
                ? $" Наличные зафиксированы: {actualCash.Value} сом."
                : " Наличные не зафиксированы: в этом месяце нет приёмки.";
            string rawPart = finalRawDifference == 0
                ? (autoFormalizedShortage > 0
                    ? $" Подтверждённая недостача {autoFormalizedShortage} сом оформлена на {suggestedResponsible}."
                    : " Сырых потерь и излишков после коррекции нет.")
                : finalRawDifference < 0
                    ? $" В сырые потери перенесено {Math.Abs(finalRawDifference)} сом." +
                      (!string.IsNullOrWhiteSpace(rawRecommendation)
                          ? $" Рекомендация: {rawRecommendation}."
                          : "")
                    : $" В излишек перенесено {finalRawDifference} сом.";

            string nettedPart = netted > 0
                ? $" Взаимно зачтено старых разборов: {netted} сом."
                : "";

            return $"Баланс безнала зафиксирован: {actualCashless} сом.{cashPart}{nettedPart}{rawPart} Закрыто старых открытых сверок: {closed}.";
        }

        private static int ForgiveExistingCashShortagesWithCashlessExtra(
            int cashlessExtraAmount,
            DateTime fromInclusive,
            DateTime toExclusive)
        {
            if (cashlessExtraAmount <= 0 ||
                cashlessExtraAmount > CashReconciliationService.AutoResolveLimit)
            {
                return 0;
            }

            int reducedInCashRecords = CashService.ReduceCashShortagesByPaymentMistake(
                amount: cashlessExtraAmount,
                fromInclusive: fromInclusive,
                toExclusive: toExclusive,
                titleKeyword: "налич"
            );
            int reducedInEmployeeLosses = EmployeeLossService.ForgiveCashShortagesByPaymentMistake(
                amount: cashlessExtraAmount,
                fromInclusive: fromInclusive,
                toExclusive: toExclusive
            );
            int consumed = Math.Max(reducedInCashRecords, reducedInEmployeeLosses);

            if (consumed <= 0)
                return 0;

            return CashReconciliationService.ConsumeOpenCashlessExtra(
                consumed,
                fromInclusive,
                toExclusive
            );
        }

        private static void DistributeCashlessShortage(
            int shortageAmount,
            DateTime fromInclusive,
            DateTime toExclusive,
            int expectedCashless,
            int actualCashless)
        {
            var groups = PaymentService.Records
                .Where(record =>
                    record.CreatedAt >= fromInclusive &&
                    record.CreatedAt < toExclusive &&
                    record.MBankAmount > 0)
                .GroupBy(record =>
                    string.IsNullOrWhiteSpace(record.EmployeeName)
                        ? "Неизвестно"
                        : record.EmployeeName)
                .Select(group => new
                {
                    EmployeeName = group.Key,
                    Amount = group.Sum(record => record.MBankAmount)
                })
                .Where(group => group.Amount > 0)
                .OrderByDescending(group => group.Amount)
                .ToList();

            if (groups.Count == 0)
            {
                AddCashlessShortageForEmployee(
                    "Неизвестно",
                    shortageAmount,
                    expectedCashless,
                    actualCashless,
                    isFixed: true
                );

                return;
            }

            int total = groups.Sum(group => group.Amount);
            int distributed = 0;

            for (int index = 0; index < groups.Count; index++)
            {
                int amount = index == groups.Count - 1
                    ? shortageAmount - distributed
                    : (int)Math.Round(shortageAmount * (groups[index].Amount / (double)total));

                if (amount <= 0)
                    continue;

                distributed += amount;

                AddCashlessShortageForEmployee(
                    groups[index].EmployeeName,
                    amount,
                    expectedCashless,
                    actualCashless,
                    isFixed: true
                );
            }
        }

        private static void AddCashlessShortageForEmployee(
            string employeeName,
            int amount,
            int expectedCashlessBalance,
            int actualCashless,
            bool isFixed)
        {
            string description =
                $"Автоматическая сверка безнала владельцем.\n" +
                $"Ожидаемый остаток безнала: {expectedCashlessBalance} сом\n" +
                $"Фактический остаток: {actualCashless} сом\n" +
                $"Доля сотрудника: {amount} сом";

            CashService.AddShortage(
                checkedByEmployeeName: "Владелец",
                responsibleEmployeeName: employeeName,
                title: "Недостача безнала",
                description: description,
                amount: amount
            );

            EmployeeLossService.AddLoss(
                responsibleEmployeeName: employeeName,
                checkedByEmployeeName: "Владелец",
                lossType: "Недостача безнала",
                title: "Недостача безнала",
                description: description,
                amount: amount,
                note: "Оформлено владельцем из активной корректировки",
                lossKind: "money",
                isFixed: isFixed
            );
        }

        private static void ApplyAddSalaryPayment(FirebaseCommand command)
        {
            string employeeName = command.EmployeeName.Trim();

            if (string.IsNullOrWhiteSpace(employeeName))
                throw new Exception("Не указан работник для зарплаты.");

            if (command.Amount <= 0)
                throw new Exception("amount должен быть больше 0.");

            var employee = EmployeeService.FindByName(employeeName);

            if (employee == null)
                throw new Exception($"Работник не найден: {employeeName}");

            var (monthStart, _) = ParseCommandMonth(command.MonthKey);
            var salary = AutoSalaryService
                .BuildReport(monthStart)
                .Employees
                .FirstOrDefault(item =>
                    item.EmployeeName.Equals(employee.Name, StringComparison.OrdinalIgnoreCase));
            int remaining = salary?.RemainingAmount ?? 0;

            if (remaining <= 0)
                throw new Exception("У работника нет остатка зарплаты после штрафов.");

            if (command.Amount > remaining)
                throw new Exception($"Нельзя выдать больше остатка после штрафов: {remaining} сом.");

            CashService.AddSalaryPayment(
                ownerName: "Владелец",
                employeeName: employee.Name,
                amount: command.Amount,
                paymentMethod: NormalizePaymentMethod(command.PaymentMethod),
                description: string.IsNullOrWhiteSpace(command.Description)
                    ? $"Выплата зарплаты за {monthStart:yyyy-MM}"
                    : $"{command.Description}\nМесяц зарплаты: {monthStart:yyyy-MM}",
                salaryMonthKey: monthStart.ToString("yyyy-MM")
            );
        }

        private static EmployeeBonusItem ApplyAddEmployeeBonus(FirebaseCommand command)
        {
            string employeeName = command.EmployeeName.Trim();

            if (string.IsNullOrWhiteSpace(employeeName))
                throw new Exception("Не указан работник для премии.");

            if (command.Amount <= 0)
                throw new Exception("Сумма премии должна быть больше 0.");

            var employee = EmployeeService.FindByName(employeeName);

            if (employee == null)
                throw new Exception($"Работник не найден: {employeeName}");

            var (monthStart, _) = ParseCommandMonth(command.MonthKey);
            string description = string.IsNullOrWhiteSpace(command.Description)
                ? "Премия от владельца"
                : $"{command.Description.Trim()}\nМесяц зарплаты: {monthStart:yyyy-MM}";

            return EmployeeBonusService.AddOwnerBonus(
                employee.Name,
                command.Amount,
                monthStart,
                description
            );
        }

        private static AutoSalarySettings ApplyUpdateAutoSalarySettings(FirebaseCommand command)
        {
            var settings = new AutoSalarySettings
            {
                ExpenseReservePercent = command.ExpenseReservePercent,
                SalaryFundPercent = command.SalaryFundPercent,
                TimeSharePercent = command.TimeSharePercent,
                GameRevenueSharePercent = command.GameRevenueSharePercent,
                TimeMonthlyFundAmount = command.TimeMonthlyFundAmount,
                TimeMonthlyPlannedHours = command.TimeMonthlyPlannedHours,
                ProductRevenueSharePercent = command.ProductRevenueSharePercent,
                ProductBonusPercent = command.ProductBonusPercent,
                WorkDayStartHour = command.WorkDayStartHour,
                WorkDayEndHour = command.WorkDayEndHour,
                DailyGameRevenueNorm = command.DailyGameRevenueNorm,
                OverNormBonusPercent = command.OverNormBonusPercent,
                PunctualityBonusAmount = command.PunctualityBonusAmount,
                LateActiveSessionBonusAmount = command.LateActiveSessionBonusAmount,
                OpeningResponsibleEmployeeName = command.OpeningResponsibleEmployeeName,
                LateOpeningGraceMinutes = command.LateOpeningGraceMinutes,
                LateOpeningPenaltyStepMinutes = command.LateOpeningPenaltyStepMinutes,
                LateOpeningPenaltyStepAmount = command.LateOpeningPenaltyStepAmount,
                LateOpeningMaxAutoMinutes = command.LateOpeningMaxAutoMinutes
            };

            AutoSalaryService.UpdateSettings(settings);

            return AutoSalaryService.Settings;
        }

        private static int GetAutoSalaryHourlyRate(AutoSalarySettings settings)
        {
            if (settings.TimeMonthlyFundAmount <= 0 || settings.TimeMonthlyPlannedHours <= 0)
                return 0;

            return (int)Math.Round(
                settings.TimeMonthlyFundAmount / (double)settings.TimeMonthlyPlannedHours
            );
        }

        private static void ApplySetCashlessForToday(FirebaseCommand command)
        {
            if (command.Amount < 0)
                throw new Exception("amount не может быть меньше 0.");

            CashlessService.SetAmountForToday(
                amount: command.Amount,
                note: command.Description
            );
        }

        private static void ApplyClearAllHistoryKeepEmployees()
        {
            // Keep system setup intact: employees, product catalog/stock, salary formula,
            // alarm settings, Tuya credentials, device preferences, work modes, and cloud schedules.
            ActiveSessionStorageService.Clear();
            ActionLogService.Clear();
            CashService.Clear();
            CashBalanceCheckpointService.Clear();
            CashlessService.Clear();
            CashlessBalanceCheckpointService.Clear();
            CashAcceptanceService.Clear();
            CashReconciliationService.Clear();
            EmployeeLossService.Clear();
            EmployeeBonusService.Clear();
            PaymentService.Clear();
            ProductIncomingService.Clear();
            StockAuditService.Clear();
            StockPurchaseService.Clear();
            ShiftAcceptanceService.Reset();
        }

        private static void ApplyAddEmployee(FirebaseCommand command)
        {
            string employeeName = command.EmployeeName.Trim();
            string pinCode = command.PinCode.Trim();

            if (string.IsNullOrWhiteSpace(employeeName))
                throw new Exception("Не указано имя работника.");

            if (string.IsNullOrWhiteSpace(pinCode))
                throw new Exception("Не указан код работника.");

            if (EmployeeService.ExistsByName(employeeName))
                throw new Exception($"Работник уже существует: {employeeName}");

            EmployeeService.AddEmployee(employeeName, pinCode, command.EmployeeId);
        }

        private static void ApplyUpdateEmployeePin(FirebaseCommand command)
        {
            string employeeName = command.EmployeeName.Trim();
            string pinCode = command.PinCode.Trim();

            if (string.IsNullOrWhiteSpace(employeeName))
                throw new Exception("Не указано имя работника.");

            if (string.IsNullOrWhiteSpace(pinCode))
                throw new Exception("Не указан новый код работника.");

            var employee = EmployeeService.FindByName(employeeName);

            if (employee == null)
                throw new Exception($"Работник не найден: {employeeName}");

            EmployeeService.ChangePinCode(employeeName, pinCode);
        }

        private static int ApplyUpdateEmployeeName(
            FirebaseCommand command,
            IReadOnlyList<ClubPlace> places)
        {
            string employeeName = command.EmployeeName.Trim();
            string newEmployeeName = command.NewEmployeeName.Trim();

            if (string.IsNullOrWhiteSpace(employeeName))
                throw new Exception("Не указано текущее имя работника.");

            if (string.IsNullOrWhiteSpace(newEmployeeName))
                throw new Exception("Не указано новое имя работника.");

            var employee = EmployeeService.FindByName(employeeName);

            if (employee == null)
                throw new Exception($"Работник не найден: {employeeName}");

            var duplicate = EmployeeService.FindByName(newEmployeeName);

            if (duplicate != null &&
                !duplicate.Name.Equals(employeeName, StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception($"Работник уже существует: {newEmployeeName}");
            }

            int renamedReferences = EmployeeReferenceRenameService.RenameAll(
                employeeName,
                newEmployeeName,
                places);

            EmployeeService.ChangeName(employeeName, newEmployeeName);
            return renamedReferences;
        }

        private static void ApplyDisableEmployee(FirebaseCommand command)
        {
            string employeeName = command.EmployeeName.Trim();

            if (string.IsNullOrWhiteSpace(employeeName))
                throw new Exception("Не указано имя работника.");

            var employee = EmployeeService.FindByName(employeeName);

            if (employee == null)
                throw new Exception($"Работник не найден: {employeeName}");

            EmployeeService.SetEmployeeActive(employeeName, false);
        }

        private static void ApplyEnableEmployee(FirebaseCommand command)
        {
            string employeeName = command.EmployeeName.Trim();

            if (string.IsNullOrWhiteSpace(employeeName))
                throw new Exception("Не указано имя работника.");

            var employee = EmployeeService.FindByName(employeeName);

            if (employee == null)
                throw new Exception($"Работник не найден: {employeeName}");

            EmployeeService.SetEmployeeActive(employeeName, true);
        }

        private static string NormalizePaymentMethod(string paymentMethod)
        {
            if (paymentMethod == "Наличные")
                return "Наличные";

            if (paymentMethod == "Безнал")
                return "Безнал";

            return "Наличные";
        }

        private static string NormalizeSaleItemType(string itemType)
        {
            if (itemType == "Service")
                return "Service";

            return "Product";
        }

        private static async Task MarkCommandApplied(
            string commandId,
            FirebaseCommand command,
            string resultMessage)
        {
            command.Status = "applied";
            command.AppliedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            command.ResultMessage = resultMessage;

            await PutAsync($"{GetCommandPath(command)}/{commandId}", command);
        }

        private static async Task MarkCommandError(
            string commandId,
            FirebaseCommand command,
            string errorMessage)
        {
            command.Status = "error";
            command.AppliedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            command.ResultMessage = errorMessage;

            await PutAsync($"{GetCommandPath(command)}/{commandId}", command);
        }

        private static string GetCommandPath(FirebaseCommand command)
        {
            return string.IsNullOrWhiteSpace(command.FirebasePath)
                ? ClubCommandsPath
                : command.FirebasePath;
        }

        private static async Task<T?> GetAsync<T>(string path, string query = "")
        {
            string url = await FirebaseAuthService.BuildDatabaseUrlAsync(path);

            if (!string.IsNullOrWhiteSpace(query))
            {
                string separator = url.Contains('?') ? "&" : "?";
                url += separator + query.TrimStart('?', '&');
            }

            string json = await _httpClient.GetStringAsync(url);

            if (string.IsNullOrWhiteSpace(json) || json == "null")
                return default;

            return JsonSerializer.Deserialize<T>(
                json,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }
            );
        }

        private static async Task PutAsync(string path, object data)
        {
            string url = await FirebaseAuthService.BuildDatabaseUrlAsync(path);

            string json = JsonSerializer.Serialize(
                data,
                new JsonSerializerOptions
                {
                    WriteIndented = true
                }
            );

            using var content = new StringContent(
                json,
                Encoding.UTF8,
                "application/json"
            );

            using var response = await _httpClient.PutAsync(url, content);
            response.EnsureSuccessStatusCode();
        }

        private static async Task PatchAsync(string path, object data)
        {
            string url = await FirebaseAuthService.BuildDatabaseUrlAsync(path);
            string json = JsonSerializer.Serialize(data);

            using var request = new HttpRequestMessage(
                new HttpMethod("PATCH"),
                url
            )
            {
                Content = new StringContent(
                    json,
                    Encoding.UTF8,
                    "application/json"
                )
            };

            using var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
        }

        private class FirebaseCommand
        {
            [JsonIgnore]
            public string FirebasePath { get; set; } = "";

            public string Type { get; set; } = "";
            public string Status { get; set; } = "pending";
            public string TargetClubId { get; set; } = "";
            public string TargetInstallationId { get; set; } = "";

            public string Message { get; set; } = "";

            public string NewClubName { get; set; } = "";

            public string ItemType { get; set; } = "Product";

            public string ProductName { get; set; } = "";

            public string NewProductName { get; set; } = "";

            public int Quantity { get; set; }

            public int InitialQuantity { get; set; }
            public int PurchasePrice { get; set; }
            public int SalePrice { get; set; }
            public int MinimumQuantity { get; set; }

            public List<StockPurchaseItem> PurchaseItems { get; set; } = new List<StockPurchaseItem>();

            public string Title { get; set; } = "";
            public string Description { get; set; } = "";
            public int Amount { get; set; }
            public int ExpectedAmount { get; set; } = -1;

            public string PaymentMethod { get; set; } = "Наличные";

            public string ExpenseCategory { get; set; } = "Другое";
            public string NewExpenseCategory { get; set; } = "";
            public string MonthKey { get; set; } = "";
            public string OwnerWithdrawMode { get; set; } = "";
            public string RecordId { get; set; } = "";
            public string ReconciliationId { get; set; } = "";
            public string ResolutionType { get; set; } = "";
            public string LossKind { get; set; } = "";

            public string EmployeeName { get; set; } = "";
            public string NewEmployeeName { get; set; } = "";
            public string EmployeeId { get; set; } = "";
            public string PinCode { get; set; } = "";

            public int ExpenseReservePercent { get; set; }
            public int SalaryFundPercent { get; set; }
            public int TimeSharePercent { get; set; }
            public int GameRevenueSharePercent { get; set; }
            public int TimeMonthlyFundAmount { get; set; }
            public int TimeMonthlyPlannedHours { get; set; }
            public int ProductRevenueSharePercent { get; set; }
            public int ProductBonusPercent { get; set; }
            public int WorkDayStartHour { get; set; }
            public int WorkDayEndHour { get; set; }
            public int DailyGameRevenueNorm { get; set; }
            public int OverNormBonusPercent { get; set; }
            public int PunctualityBonusAmount { get; set; }
            public int LateActiveSessionBonusAmount { get; set; }
            public string OpeningResponsibleEmployeeName { get; set; } = "";
            public int LateOpeningGraceMinutes { get; set; }
            public int LateOpeningPenaltyStepMinutes { get; set; }
            public int LateOpeningPenaltyStepAmount { get; set; }
            public int LateOpeningMaxAutoMinutes { get; set; }

            public string CreatedAt { get; set; } = "";
            public string AppliedAt { get; set; } = "";
            public string ResultMessage { get; set; } = "";
        }
    }
}
