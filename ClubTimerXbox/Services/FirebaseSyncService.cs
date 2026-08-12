using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
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

        private static string ClubOverviewPath => $"{ClubRootPath}/overview";

        private static string ClubLiveStatePath => $"{ClubRootPath}/liveState";

        private static string ClubCommandsPath => $"{ClubRootPath}/commands";

        private static string ClubMetaPath => $"{ClubRootPath}/meta";

        private static string OwnerClubMetaPath => $"owner/clubs/{PcIdentityService.Current.ClubId}";

        private static IReadOnlyList<ClubPlace> _lastKnownPlaces = Array.Empty<ClubPlace>();
        private static long _lastLiveStateRevision;

        public static async Task<bool> PushOverviewStateAsync(List<ClubPlace> places)
        {
            if (!FirebaseConnectionService.CanSync)
                return false;

            try
            {
                var pcIdentity = PcIdentityService.Current;
                var snapshot = BuildOverviewSnapshot(places);
                long nowUnixMs = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                long revision = NextLiveStateRevision(nowUnixMs);
                var data = BuildOverviewPayload(
                    pcIdentity,
                    snapshot,
                    nowUnixMs,
                    revision
                );

                await PutAsync(ClubOverviewPath, data);
                await PutAsync(
                    ClubLiveStatePath,
                    BuildLiveStatePayload(
                        pcIdentity,
                        snapshot,
                        nowUnixMs,
                        revision,
                        signalType: "overview_update",
                        isOpen: true,
                        connectionState: "online"
                    )
                );
                await PatchAsync(ClubMetaPath, BuildClubMeta(pcIdentity));
                await PatchAsync(OwnerClubMetaPath, BuildClubMeta(pcIdentity));
                _lastKnownPlaces = places.ToList();
                return true;
            }
            catch
            {
                // Потеря интернета не должна мешать работе клуба.
                return false;
            }
        }

        public static async Task<bool> PushHeartbeatAsync(List<ClubPlace> places)
        {
            if (!FirebaseConnectionService.CanSync)
                return false;

            try
            {
                var pcIdentity = PcIdentityService.Current;
                var snapshot = BuildOverviewSnapshot(places);
                long nowUnixMs = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                long revision = NextLiveStateRevision(nowUnixMs);

                await PutAsync(
                    ClubLiveStatePath,
                    BuildLiveStatePayload(
                        pcIdentity,
                        snapshot,
                        nowUnixMs,
                        revision,
                        signalType: "heartbeat",
                        isOpen: true,
                        connectionState: "online"
                    )
                );

                _lastKnownPlaces = places.ToList();
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static async Task<bool> PushClosedStateAsync(string employeeName)
        {
            if (!FirebaseConnectionService.CanSync)
                return false;

            try
            {
                var pcIdentity = PcIdentityService.Current;
                var snapshot = BuildOverviewSnapshot(_lastKnownPlaces);
                long nowUnixMs = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                long revision = NextLiveStateRevision(nowUnixMs);

                await PutAsync(
                    ClubLiveStatePath,
                    BuildLiveStatePayload(
                        pcIdentity,
                        snapshot,
                        nowUnixMs,
                        revision,
                        signalType: "club_closed",
                        isOpen: false,
                        connectionState: "closed",
                        employeeName: employeeName
                    )
                );
                await PatchAsync(
                    ClubOverviewPath,
                    new Dictionary<string, object?>
                    {
                        ["updatedAt"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                        ["updatedAtUnixMs"] = nowUnixMs,
                        ["lastHeartbeatAtUnixMs"] = nowUnixMs,
                        ["revision"] = revision,
                        ["isOpen"] = false,
                        ["connectionState"] = "closed",
                        ["currentEmployeeName"] = ""
                    }
                );
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static string BuildOverviewSignature(IReadOnlyList<ClubPlace> places)
        {
            var latestPayment = PaymentService.Records.LastOrDefault();
            return string.Join(
                "|",
                EmployeeService.CurrentEmployee?.Name ?? "",
                ShiftAcceptanceService.Current.IsRequired,
                ShiftAcceptanceService.Current.IsCompleted,
                PaymentService.Records.Count,
                latestPayment?.Id.ToString() ?? "",
                latestPayment?.TotalAmount ?? 0,
                string.Join(
                    ";",
                    places
                        .OrderBy(place => place.Name, StringComparer.OrdinalIgnoreCase)
                        .Select(place => $"{place.Name}:{place.IsBusy}")
                )
            );
        }

        private static OverviewSnapshot BuildOverviewSnapshot(
            IEnumerable<ClubPlace> places)
        {
            var placeList = places.ToList();
            DateTime todayStart = BusinessCalendarService.GetBusinessDate(
                ClubClock.Current.LocalNow);
            int gamesToday = GetPaymentTotal(
                CashReportSection.Games,
                CashReportPeriodMode.Day,
                todayStart
            );

            return new OverviewSnapshot
            {
                EmployeeName = EmployeeService.CurrentEmployee?.Name ?? "",
                AcceptanceRequired = ShiftAcceptanceService.Current.IsRequired,
                AcceptanceCompleted = ShiftAcceptanceService.Current.IsCompleted,
                GamesToday = gamesToday,
                BusyPlaces = placeList.Count(place => place.IsBusy),
                FreePlaces = placeList.Count(place => !place.IsBusy),
                Places = placeList
            };
        }

        private static Dictionary<string, object?> BuildOverviewPayload(
            PcIdentity identity,
            OverviewSnapshot snapshot,
            long nowUnixMs,
            long revision)
        {
            return new Dictionary<string, object?>
            {
                ["updatedAt"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                ["updatedAtUnixMs"] = nowUnixMs,
                ["lastHeartbeatAtUnixMs"] = nowUnixMs,
                ["revision"] = revision,
                ["isOpen"] = true,
                ["connectionState"] = "online",
                ["club"] = new
                {
                    id = identity.ClubId,
                    name = identity.ClubName,
                    isActivated = identity.IsActivated,
                    installationId = identity.InstallationId,
                    pcName = Environment.MachineName
                },
                ["currentEmployeeName"] = snapshot.EmployeeName,
                ["acceptance"] = new
                {
                    isRequired = snapshot.AcceptanceRequired,
                    isCompleted = snapshot.AcceptanceCompleted
                },
                ["cash"] = new
                {
                    gamesToday = snapshot.GamesToday
                },
                ["busyPlaces"] = snapshot.BusyPlaces,
                ["freePlaces"] = snapshot.FreePlaces,
                ["places"] = snapshot.Places.Select(place => new
                {
                    name = place.Name,
                    type = place.Type.ToString(),
                    isBusy = place.IsBusy
                }).ToList()
            };
        }

        private static Dictionary<string, object?> BuildLiveStatePayload(
            PcIdentity identity,
            OverviewSnapshot snapshot,
            long nowUnixMs,
            long revision,
            string signalType,
            bool isOpen,
            string connectionState,
            string? employeeName = null)
        {
            return new Dictionary<string, object?>
            {
                ["messageType"] = "club_state",
                ["signalType"] = signalType,
                ["clubId"] = identity.ClubId,
                ["clubName"] = identity.ClubName,
                ["sourceInstallationId"] = identity.InstallationId,
                ["revision"] = revision,
                ["updatedAtUnixMs"] = nowUnixMs,
                ["lastHeartbeatAtUnixMs"] = nowUnixMs,
                ["isOpen"] = isOpen,
                ["connectionState"] = connectionState,
                ["employeeName"] = employeeName ?? snapshot.EmployeeName,
                ["busyPlaces"] = snapshot.BusyPlaces,
                ["freePlaces"] = snapshot.FreePlaces,
                ["gamesToday"] = snapshot.GamesToday,
                ["acceptanceRequired"] = snapshot.AcceptanceRequired,
                ["acceptanceCompleted"] = snapshot.AcceptanceCompleted
            };
        }

        private static long NextLiveStateRevision(long nowUnixMs)
        {
            while (true)
            {
                long previous = Interlocked.Read(ref _lastLiveStateRevision);
                long next = Math.Max(nowUnixMs, previous + 1);
                if (Interlocked.CompareExchange(
                        ref _lastLiveStateRevision,
                        next,
                        previous) == previous)
                {
                    return next;
                }
            }
        }

        public static async Task<bool> PushCurrentStateAsync(List<ClubPlace> places)
        {
            if (!FirebaseConnectionService.CanSync)
                return false;

            try
            {
                var pcIdentity = PcIdentityService.Current;
                var businessDay = BusinessCalendarService.GetBusinessDay(
                    ClubClock.Current.LocalNow);
                DateTime todayStart = businessDay.StartInclusive.Date;
                DateTime tomorrowStart = businessDay.EndExclusive;

                var businessMonth = BusinessCalendarService.GetBusinessMonth(
                    ClubClock.Current.LocalNow);
                DateTime monthStart = businessMonth.StartInclusive;
                DateTime nextMonthStart = businessMonth.EndExclusive;

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
                int constitutionBreakdown = CashReconciliationService
                    .GetConstitutionBreakdown(monthStart, nextMonthStart);
                int constitutionRecommendations = CashReconciliationService
                    .GetConstitutionRecommendationTotal(monthStart, nextMonthStart);
                var cycleFormalizedMoneyLossesByEmployee = EmployeeLossService
                    .GetCappedUnpaidMoneyTotalsByEmployee(
                        monthStart,
                        nextMonthStart,
                        null
                    );
                int cycleFormalizedMoneyLosses = cycleFormalizedMoneyLossesByEmployee
                    .Values
                    .Sum();
                int accountabilityShortage = Math.Max(0, -constitutionBreakdown);
                int accountabilityExtra = Math.Max(0, constitutionBreakdown);
                int accountabilityFormalized = cycleFormalizedMoneyLosses;
                int accountabilityPending = constitutionRecommendations;
                string accountabilityResponsible = CashReconciliationService
                    .GetSuggestedResponsibleForOpenShortages(
                        monthStart,
                        nextMonthStart
                    );
                if (string.IsNullOrWhiteSpace(accountabilityResponsible))
                {
                    string historicalResponsible = CashReconciliationService
                        .GetSuggestedResponsibleForShortageHistory(
                            monthStart,
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
                        monthStart,
                        nextMonthStart
                    )
                    : "";
                if (string.IsNullOrWhiteSpace(accountabilityResponsible) &&
                    string.IsNullOrWhiteSpace(accountabilitySuspect))
                {
                    accountabilitySuspect = CashReconciliationService
                        .GetSuggestedSuspectForShortageHistory(
                            monthStart,
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
                OwnerWithdrawalAvailability ownerCashAvailabilityMonth =
                    OwnerWithdrawalAvailability.FromBalances(
                        ownerAvailableCashBalanceMonth,
                        openingCashBalanceMonth);
                OwnerWithdrawalAvailability ownerCashlessAvailabilityMonth =
                    OwnerWithdrawalAvailability.FromBalances(
                        ownerAvailableCashlessBalanceMonth,
                        openingCashlessBalanceMonth);

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
                int salaryOutstandingMonth = autoSalaryReport.Employees.Sum(employee =>
                    Math.Max(0, employee.RemainingAmount));
                int possibleProfitMonth = CalculatePossibleProfit(
                    cashMonth,
                    expensesMonth,
                    stockPurchaseMonth,
                    salaryAccruedMonth,
                    shortagesMonth
                );

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
                        accountingSchemaVersion = item.AccountingSchemaVersion,
                        createdAt = item.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                        kind = item.Kind.ToString(),
                        status = item.Status.ToString(),
                        origin = item.Origin.ToString(),
                        stage = item.Stage.ToString(),
                        responsibilityLevel = item.ResponsibilityLevel.ToString(),
                        resolution = item.Resolution.ToString(),
                        investigationId = item.InvestigationId.ToString(),
                        checkpointNumber = item.CheckpointNumber,
                        amountAtCheckpoint = item.AmountAtCheckpoint,
                        closedAtCheckpointNumber = item.ClosedAtCheckpointNumber,
                        amount = item.Amount,
                        originalAmount = item.OriginalAmount,
                        resolvedAmount = item.ResolvedAmount,
                        formalizedAmount = item.FormalizedAmount,
                        remainingAmount = item.Amount,
                        expectedAmount = item.ExpectedAmount,
                        actualAmount = item.ActualAmount,
                        programExpectedAmount = item.ProgramExpectedAmount,
                        checkedByEmployeeName = item.CheckedByEmployeeName,
                        responsibleEmployeeName = item.ResponsibleEmployeeName,
                        suspectedEmployeeName = item.SuspectedEmployeeName,
                        title = item.Title,
                        note = item.Note,
                        resolvedAt = item.ResolvedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "",
                        resolvedBy = item.ResolvedBy,
                        resolutionNote = item.ResolutionNote,
                        extraContributions = (item.ExtraContributions ??
                            new List<CashExtraContribution>())
                            .Select(contribution => new
                            {
                                id = contribution.Id.ToString(),
                                investigationId = contribution.InvestigationId.ToString(),
                                createdAt = contribution.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                                employeeName = contribution.EmployeeName,
                                kind = contribution.Kind.ToString(),
                                origin = contribution.Origin.ToString(),
                                stage = contribution.Stage.ToString(),
                                originalAmount = contribution.OriginalAmount,
                                amount = contribution.Amount,
                                resolvedAmount = contribution.ResolvedAmount,
                                expectedAmount = contribution.ExpectedAmount,
                                actualAmount = contribution.ActualAmount,
                                programExpectedAmount = contribution.ProgramExpectedAmount,
                                operationId = contribution.OperationId
                            })
                            .ToList(),
                        settlements = (item.Settlements ??
                            new List<CashSettlementEntry>())
                            .Select(settlement => new
                            {
                                id = settlement.Id.ToString(),
                                createdAt = settlement.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                                kind = settlement.Kind.ToString(),
                                sourceId = settlement.SourceId.ToString(),
                                targetId = settlement.TargetId.ToString(),
                                amount = settlement.Amount,
                                cashDelta = settlement.CashDelta,
                                cashlessDelta = settlement.CashlessDelta,
                                note = settlement.Note
                            })
                            .ToList(),
                        lossAllocations = (item.LossAllocations ??
                            new List<CashLossAllocation>())
                            .Select(allocation => new
                            {
                                id = allocation.Id.ToString(),
                                createdAt = allocation.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                                employeeName = allocation.EmployeeName,
                                amount = allocation.Amount,
                                postedAmount = allocation.PostedAmount,
                                source = allocation.Source.ToString(),
                                reason = allocation.Reason
                            })
                            .ToList()
                    })
                    .ToList();

                var data = new
                {
                    updatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    updatedAtUnixMs = DateTimeOffset.Now.ToUnixTimeMilliseconds(),
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
                        ownerCurrentCashAvailableMonth = ownerCashAvailabilityMonth.CurrentMonthAmount,
                        ownerCurrentCashlessAvailableMonth = ownerCashlessAvailabilityMonth.CurrentMonthAmount,
                        ownerCarriedCashAvailableMonth = ownerCashAvailabilityMonth.CarriedAmount,
                        ownerCarriedCashlessAvailableMonth = ownerCashlessAvailabilityMonth.CarriedAmount,
                        cashlessVerifiedMonth,
                        expensesMonth,
                        cashExpenseMonth,
                        cashlessExpenseMonth,

                        salaryToday,
                        salaryMonth,
                        salaryAccruedMonth,
                        salaryOutstandingMonth,
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
                        possibleProfitMonth,
                        retainedOwnerIncome = BusinessAccountingService.RetainedOwnerIncome
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

                    lateOpeningPenalties = LateOpeningPenaltyService
                        .GetPendingRecommendations()
                        .Select(item => new
                        {
                            id = item.Id.ToString(),
                            createdAt = item.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                            employeeName = item.ResponsibleEmployeeName,
                            title = item.Title,
                            description = item.Description,
                            amount = item.Amount,
                            decisionDueAt = item.DecisionDueAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "",
                            status = item.ResolutionStatus
                        })
                        .ToList(),

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
                                isFixed = item.IsFixed,
                                isPaid = item.IsPaid,
                                resolutionStatus = item.ResolutionStatus,
                                sourceCode = item.SourceCode
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
                                isFixed = item.IsFixed,
                                isPaid = item.IsPaid,
                                resolutionStatus = item.ResolutionStatus,
                                sourceCode = item.SourceCode
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
                                isFixed = item.IsFixed,
                                isPaid = item.IsPaid,
                                resolutionStatus = item.ResolutionStatus,
                                sourceCode = item.SourceCode
                            })
                            .ToList();

                        var employeeSalaryHistory = BuildEmployeeSalaryHistory(
                            monthStart,
                            nextMonthStart,
                            employee.Name);

                        return new
                        {
                            employeeId = employee.EmployeeId,
                            name = employee.Name,
                            pinCode = employee.PinCode,
                            isActive = employee.IsActive,

                            todayWorkTime = EmployeeStatsService.FormatTime(summary.TodayWorkTime),
                            monthWorkTime = EmployeeStatsService.FormatTime(
                                TimeSpan.FromHours(
                                    autoSalary?.WorkHours ??
                                    summary.MonthWorkTime.TotalHours
                                )
                            ),

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
                                    remainingAmount = autoSalary.RemainingAmount,
                                    timeRatingPercent = autoSalary.TimeRatingPercent,
                                    revenueRatingPercent = autoSalary.RevenueRatingPercent,
                                    overallRatingPercent = autoSalary.OverallRatingPercent,
                                    timeRatingEarnedAmount = autoSalary.TimeRatingEarnedAmount,
                                    timeRatingLostAmount = autoSalary.TimeRatingLostAmount,
                                    timeRatingNetAmount = autoSalary.TimeRatingEarnedAmount -
                                        autoSalary.TimeRatingLostAmount,
                                    gameRatingEarnedAmount = autoSalary.GameRatingEarnedAmount,
                                    gameRatingLostAmount = autoSalary.GameRatingLostAmount,
                                    gameRatingNetAmount = autoSalary.GameRatingEarnedAmount -
                                        autoSalary.GameRatingLostAmount,
                                    ratingHasWarning = autoSalary.RatingHasWarning,
                                    ratingEvents = BuildRatingEventsPayload(autoSalary)
                                },

                            closedGameSessionsCount = summary.ClosedGameSessionsCount,
                            productServiceOperationsCount = summary.ProductServiceOperationsCount,
                            shortageCount = summary.ShortageCount,

                            journal = allJournal,
                            incomeJournal = incomeJournal,
                            shortageJournal = shortageJournal,
                            salaryHistory = employeeSalaryHistory
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
                return true;
            }
            catch
            {
                // Если интернет пропал, программа должна продолжать работать.
                return false;
            }
        }

        public static async Task CheckCommandsAsync(IReadOnlyList<ClubPlace>? places = null)
        {
            if (!FirebaseConnectionService.CanSync)
                return;

            try
            {
                var currentPlaces = places ?? Array.Empty<ClubPlace>();
                _lastKnownPlaces = currentPlaces;
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
                updatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                updatedAtUnixMs = DateTimeOffset.Now.ToUnixTimeMilliseconds()
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

            if (section == CashReportSection.ProductsAndServices)
            {
                return ProductServiceRevenueService.GetTotal(
                    CashReportService.GetPeriodStart(filter),
                    CashReportService.GetPeriodEndExclusive(filter));
            }

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
                .Items
                .Where(item => item.CreatedAt < toExclusive)
                .OrderByDescending(item => item.CreatedAt)
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
            return CashBalanceSummaryService.CalculateExpectedCashBalanceByPeriod(
                fromInclusive,
                toExclusive);
        }

        private static int? CalculateProgramCashBalanceByPeriod(
            DateTime fromInclusive,
            DateTime toExclusive)
        {
            return CashBalanceSummaryService.CalculateProgramCashBalanceByPeriod(
                fromInclusive,
                toExclusive);
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
                .Where(record => record.UpdatedAt < toExclusive)
                .OrderByDescending(record => record.UpdatedAt)
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
            return CashBalanceSummaryService.CalculateExpectedCashlessBalanceByPeriod(
                fromInclusive,
                toExclusive);
        }

        private static int? CalculateProgramCashlessBalanceByPeriod(
            DateTime fromInclusive,
            DateTime toExclusive)
        {
            return CashBalanceSummaryService.CalculateProgramCashlessBalanceByPeriod(
                fromInclusive,
                toExclusive);
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

            AddMonth(BusinessCalendarService.GetBusinessDate(ClubClock.Current.LocalNow));

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
                settingsEffectiveFrom = report.SettingsEffectiveFrom.ToString("O"),
                hasPendingSettings = report.HasPendingSettings,
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
                    employeeId = employee.EmployeeId,
                    employeeName = employee.EmployeeName,
                    workHours = employee.WorkHours,
                    gameRevenue = employee.GameRevenue,
                    productRevenue = employee.ProductRevenue,
                    timeAmount = employee.TimeAmount,
                    gameRevenueAmount = employee.GameRevenueAmount,
                    productShareAmount = employee.ProductShareAmount,
                    productBonusAmount = employee.ProductBonusAmount,
                    bonusAmount = employee.BonusAmount,
                    timeRatingPercent = employee.TimeRatingPercent,
                    revenueRatingPercent = employee.RevenueRatingPercent,
                    overallRatingPercent = employee.OverallRatingPercent,
                    timeRatingEarnedAmount = employee.TimeRatingEarnedAmount,
                    timeRatingLostAmount = employee.TimeRatingLostAmount,
                    timeRatingNetAmount = employee.TimeRatingEarnedAmount -
                        employee.TimeRatingLostAmount,
                    gameRatingEarnedAmount = employee.GameRatingEarnedAmount,
                    gameRatingLostAmount = employee.GameRatingLostAmount,
                    gameRatingNetAmount = employee.GameRatingEarnedAmount -
                        employee.GameRatingLostAmount,
                    ratingHasWarning = employee.RatingHasWarning,
                    ratingEvents = employee.RatingEvents.Select(item => new
                    {
                        id = item.Id.ToString(),
                        branch = item.Branch.ToString(),
                        ruleCode = item.RuleCode,
                        ruleVersion = item.RuleVersion,
                        direction = item.Direction.ToString(),
                        changePercent = item.ChangePercent,
                        basePercentAtCreation = item.BasePercentAtCreation,
                        sourceType = item.SourceType,
                        title = item.Title,
                        description = item.Description,
                        createdAt = item.CreatedAt.ToString("O"),
                        effectiveFrom = item.EffectiveFrom.ToString("O"),
                        effectiveUntil = item.EffectiveUntil.ToString("O"),
                        targetPercent = item.TargetPercent,
                        status = item.Status.ToString(),
                        compensationAmount = item.CompensationAmount,
                        resolutionNote = item.ResolutionNote
                    }).ToList(),
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
                    carryInAmount = employee.CarryInAmount,
                    currentPeriodRemainingAmount = employee.CurrentPeriodRemainingAmount,
                    remainingAmount = employee.RemainingAmount
                }).ToList()
            };
        }

        private static object BuildRatingEventsPayload(AutoSalaryEmployeeResult employee)
        {
            return employee.RatingEvents.Select(item => new
            {
                id = item.Id.ToString(),
                branch = item.Branch.ToString(),
                ruleCode = item.RuleCode,
                ruleVersion = item.RuleVersion,
                direction = item.Direction.ToString(),
                changePercent = item.ChangePercent,
                basePercentAtCreation = item.BasePercentAtCreation,
                sourceType = item.SourceType,
                title = item.Title,
                description = item.Description,
                createdAt = item.CreatedAt.ToString("O"),
                effectiveFrom = item.EffectiveFrom.ToString("O"),
                effectiveUntil = item.EffectiveUntil.ToString("O"),
                targetPercent = item.TargetPercent,
                status = item.Status.ToString(),
                compensationAmount = item.CompensationAmount,
                resolutionNote = item.ResolutionNote
            }).ToList();
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
                foreach (var line in session.SaleLines.Where(line =>
                             line.CreatedAt >= fromInclusive &&
                             line.CreatedAt < toExclusive))
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

        private static OwnerWithdrawalAvailability CalculateOwnerWithdrawalAvailabilityForPayment(
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

                return OwnerWithdrawalAvailability.FromBalances(available, opening);
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

                return OwnerWithdrawalAvailability.FromBalances(available, opening);
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
            var businessMonth = BusinessCalendarService.GetBusinessMonthByAnchor(monthStart);
            monthStart = businessMonth.StartInclusive;
            DateTime nextMonthStart = businessMonth.EndExclusive;

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
            int salaryOutstanding = salaryReport.Employees.Sum(employee =>
                Math.Max(0, employee.RemainingAmount));
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
            OwnerWithdrawalAvailability ownerCashAvailability =
                OwnerWithdrawalAvailability.FromBalances(
                    ownerAvailableCashBalance,
                    openingCashBalance);
            OwnerWithdrawalAvailability ownerCashlessAvailability =
                OwnerWithdrawalAvailability.FromBalances(
                    ownerAvailableCashlessBalance,
                    openingCashlessBalance);

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
                    salaryOutstandingMonth = salaryOutstanding,
                    salaryGrossMonth = salaryGross,
                    salaryLossesMonth = salaryLosses,
                    salaryMonth = salary,
                    salaryCashMonth = salaryCash,
                    salaryCashlessMonth = salaryCashless,
                    shortagesMonth = losses,
                    possibleProfitMonth = possibleProfit,
                    retainedOwnerIncome = BusinessAccountingService.RetainedOwnerIncome,
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
                    ownerCurrentCashAvailableMonth = ownerCashAvailability.CurrentMonthAmount,
                    ownerCurrentCashlessAvailableMonth = ownerCashlessAvailability.CurrentMonthAmount,
                    ownerCarriedCashAvailableMonth = ownerCashAvailability.CarriedAmount,
                    ownerCarriedCashlessAvailableMonth = ownerCashlessAvailability.CarriedAmount,
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
                            isFixed = item.IsFixed,
                            isPaid = item.IsPaid,
                            resolutionStatus = item.ResolutionStatus,
                            sourceCode = item.SourceCode
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
                            isFixed = item.IsFixed,
                            isPaid = item.IsPaid,
                            resolutionStatus = item.ResolutionStatus,
                            sourceCode = item.SourceCode
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
                            isFixed = item.IsFixed,
                            isPaid = item.IsPaid,
                            resolutionStatus = item.ResolutionStatus,
                            sourceCode = item.SourceCode
                        })
                        .ToList();

                    var employeeSalaryHistory = BuildEmployeeSalaryHistory(
                        monthStart,
                        nextMonthStart,
                        employee.Name);

                    return new
                    {
                        employeeId = employee.EmployeeId,
                        name = employee.Name,
                        pinCode = employee.PinCode,
                        isActive = employee.IsActive,
                        monthWorkTime = EmployeeStatsService.FormatTime(
                            TimeSpan.FromHours(
                                autoSalary?.WorkHours ??
                                summary.MonthWorkTime.TotalHours
                            )
                        ),
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
                                remainingAmount = autoSalary.RemainingAmount,
                                timeRatingPercent = autoSalary.TimeRatingPercent,
                                revenueRatingPercent = autoSalary.RevenueRatingPercent,
                                overallRatingPercent = autoSalary.OverallRatingPercent,
                                timeRatingEarnedAmount = autoSalary.TimeRatingEarnedAmount,
                                timeRatingLostAmount = autoSalary.TimeRatingLostAmount,
                                timeRatingNetAmount = autoSalary.TimeRatingEarnedAmount -
                                    autoSalary.TimeRatingLostAmount,
                                gameRatingEarnedAmount = autoSalary.GameRatingEarnedAmount,
                                gameRatingLostAmount = autoSalary.GameRatingLostAmount,
                                gameRatingNetAmount = autoSalary.GameRatingEarnedAmount -
                                    autoSalary.GameRatingLostAmount,
                                ratingHasWarning = autoSalary.RatingHasWarning,
                                ratingEvents = BuildRatingEventsPayload(autoSalary)
                            },
                        closedGameSessionsCount = summary.ClosedGameSessionsCount,
                        productServiceOperationsCount = summary.ProductServiceOperationsCount,
                        shortageCount = summary.ShortageCount,
                        journal = allJournal,
                        incomeJournal = incomeJournal,
                        shortageJournal = shortageJournal,
                        salaryHistory = employeeSalaryHistory
                    };
                })
                .Cast<object>()
                .ToArray();
        }

        private static object[] BuildEmployeeSalaryHistory(
            DateTime monthStart,
            DateTime nextMonthStart,
            string employeeName)
        {
            return CashService
                .GetSalaryRecordsByPeriod(monthStart, nextMonthStart)
                .Where(record => string.Equals(
                    record.RelatedEmployeeName,
                    employeeName,
                    StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(record => record.CreatedAt)
                .Take(150)
                .Select(record =>
                {
                    bool employeeTookSalary = string.Equals(
                        record.EmployeeName,
                        record.RelatedEmployeeName,
                        StringComparison.OrdinalIgnoreCase);
                    string source = employeeTookSalary
                        ? "EmployeeSelf"
                        : string.Equals(
                            record.EmployeeName,
                            "Владелец",
                            StringComparison.OrdinalIgnoreCase)
                            ? "Owner"
                            : "Other";

                    return (object)new
                    {
                        id = record.Id.ToString(),
                        createdAt = record.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                        amount = record.Amount,
                        paymentMethod = record.PaymentMethod,
                        source,
                        addedBy = record.EmployeeName,
                        employeeName = record.RelatedEmployeeName,
                        salaryMonthKey = record.SalaryMonthKey,
                        description = record.Description
                    };
                })
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
                if (FirebaseCommandLedgerService.TryGet(
                        commandId,
                        out FirebaseAppliedCommand appliedCommand))
                {
                    await MarkCommandApplied(
                        commandId,
                        command,
                        appliedCommand.ResultMessage
                    );
                    return;
                }

                if (command.Type == "RefreshOverview")
                {
                    bool published = await PushOverviewStateAsync(places.ToList());
                    if (!published)
                    {
                        await MarkCommandError(
                            commandId,
                            command,
                            "ПК не смог выгрузить краткие данные в Firebase."
                        );
                        return;
                    }
                    await MarkCommandApplied(
                        commandId,
                        command,
                        "Краткие данные клуба обновлены.",
                        pushCurrentState: false
                    );
                    return;
                }

                if (command.Type == "RefreshCurrentData")
                {
                    bool published = await PushCurrentStateAsync(places.ToList());
                    if (!published)
                    {
                        await MarkCommandError(
                            commandId,
                            command,
                            "ПК не смог выгрузить данные в Firebase."
                        );
                        return;
                    }
                    await MarkCommandApplied(
                        commandId,
                        command,
                        "Данные клуба обновлены.",
                        pushCurrentState: false
                    );
                    return;
                }

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
                    ApplyAddExpense(command, commandId);

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
                    ApplyAddManualEmployeeMoneyLoss(commandId, command);

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

                if (command.Type == "FormalizeLateOpeningPenalty")
                {
                    ApplyFormalizeLateOpeningPenalty(command);

                    await MarkCommandApplied(
                        commandId,
                        command,
                        "Штраф за опоздание оформлен владельцем.");

                    return;
                }

                if (command.Type == "CancelLateOpeningPenalty")
                {
                    ApplyCancelLateOpeningPenalty(command);

                    await MarkCommandApplied(
                        commandId,
                        command,
                        "Рекомендация штрафа за опоздание отменена.");

                    return;
                }

                if (command.Type == "VerifyCashlessActual")
                {
                    string message = ApplyVerifyCashlessActual(commandId, command);

                    await MarkCommandApplied(
                        commandId,
                        command,
                        message
                    );

                    return;
                }

                if (command.Type == "BalanceCashlessActual")
                {
                    string message = ApplyBalanceCashlessActual(commandId, command);

                    await MarkCommandApplied(
                        commandId,
                        command,
                        message
                    );

                    return;
                }

                if (command.Type == "AddSalaryPayment")
                {
                    ApplyAddSalaryPayment(commandId, command);
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

                if (command.Type == "RestoreEmployeeWorkHours")
                {
                    var restored = ApplyRestoreEmployeeWorkHours(command);

                    await MarkCommandApplied(
                        commandId,
                        command,
                        $"Часы восстановлены: {restored.EmployeeName}, " +
                        $"{restored.WorkHours:0.00} ч, месяц: {restored.MonthStart:yyyy-MM}. " +
                        $"Остаток ЗП: {restored.RemainingAmount} сом."
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

                if (command.Type == "UpdateEmployeeRating")
                {
                    var rating = ApplyUpdateEmployeeRating(command);
                    await MarkCommandApplied(
                        commandId,
                        command,
                        $"Рейтинг сохранён: {command.EmployeeName}, время {rating.TimePercent}%, игры {rating.RevenuePercent}%.");
                    return;
                }

                if (command.Type == "AddEmployeeRatingEvent")
                {
                    var item = ApplyAddEmployeeRatingEvent(command);
                    await MarkCommandApplied(
                        commandId,
                        command,
                        $"Временное изменение рейтинга добавлено: {item.EmployeeName}, {item.TargetPercent}% до {item.EffectiveUntil:dd.MM.yyyy HH:mm}.");
                    return;
                }

                if (command.Type == "EndEmployeeRatingEvent")
                {
                    var item = ApplyEndEmployeeRatingEvent(command);
                    await MarkCommandApplied(
                        commandId,
                        command,
                        $"Изменение рейтинга завершено: {item.EmployeeName}, статус {item.Status}.");
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
                    var result = await AppUpdateService.InstallLatestUpdateAsync(
                        places,
                        mode: AppUpdateInstallMode.RemoteResume);

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
                if (FirebaseCommandLedgerService.TryGet(commandId, out _))
                    return;

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

        private static void ApplyAddExpense(FirebaseCommand command, string commandId)
        {
            if (command.Amount <= 0)
                throw new Exception("amount должен быть больше 0.");

            string expenseCategory = CashService.NormalizeExpenseCategory(command.ExpenseCategory);
            string title = string.IsNullOrWhiteSpace(command.Title)
                ? expenseCategory
                : command.Title.Trim();
            string paymentMethod = NormalizePaymentMethod(command.PaymentMethod);
            bool isOwnerWithdrawal = expenseCategory.Equals(
                "Владелец",
                StringComparison.OrdinalIgnoreCase);

            if (isOwnerWithdrawal)
            {
                var (monthStart, nextMonthStart) = ParseCommandMonth(command.MonthKey);
                bool openingBalanceMode = command.OwnerWithdrawMode.Equals(
                    "OpeningBalance",
                    StringComparison.OrdinalIgnoreCase
                );
                OwnerWithdrawalAvailability availability =
                    CalculateOwnerWithdrawalAvailabilityForPayment(
                        monthStart,
                        nextMonthStart,
                        paymentMethod);
                int available = openingBalanceMode
                    ? availability.CarriedAmount
                    : availability.TotalAmount;

                if (command.Amount > available)
                {
                    throw new Exception(
                        $"Недостаточно денег за {monthStart:yyyy-MM}. " +
                        $"Доступно: {available} сом, запрошено: {command.Amount} сом."
                    );
                }

                string currentMonthKey = monthStart.ToString("yyyy-MM");
                string carriedMonthKey = monthStart.AddMonths(-1).ToString("yyyy-MM");
                IReadOnlyList<OwnerWithdrawalAllocation> allocations =
                    OwnerWithdrawalAllocator.Allocate(
                        command.Amount,
                        availability,
                        currentMonthKey,
                        carriedMonthKey,
                        openingBalanceMode);
                for (int index = 0; index < allocations.Count; index++)
                {
                    OwnerWithdrawalAllocation allocation = allocations[index];
                    string sourceTitle = allocation.IsCarriedBalance
                        ? "Владелец забрал остаток"
                        : title;
                    string sourceDescription =
                        $"{command.Description}\n" +
                        (allocation.IsCarriedBalance
                            ? $"Источник: переходящий остаток {allocation.SourceMonthKey}."
                            : $"Источник: доход {allocation.SourceMonthKey}.");
                    BusinessAccountingService.WithdrawOwnerIncome(
                        allocation.Amount,
                        paymentMethod,
                        allocation.SourceMonthKey,
                        sourceTitle,
                        sourceDescription,
                        $"firebase:{commandId}:{index}");
                }
                return;
            }

            CashService.AddExpense(
                employeeName: "Владелец",
                title: title,
                description: command.Description,
                amount: command.Amount,
                paymentMethod: paymentMethod,
                expenseCategory: expenseCategory,
                accountingMonthKey: ""
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
            if (BusinessCalendarService.TryParseMonthKey(monthKey, out DateTime parsed))
            {
                var period = BusinessCalendarService.GetBusinessMonthByAnchor(parsed);
                return (period.StartInclusive, period.EndExclusive);
            }

            var current = BusinessCalendarService.GetBusinessMonth(ClubClock.Current.LocalNow);
            return (current.StartInclusive, current.EndExclusive);
        }

        private static CashReconciliationItem ApplyResolveCashReconciliation(FirebaseCommand command)
        {
            CashPenaltyPostingService.Recover();
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

            if (command.ResolutionType == "ConfirmedExtra" &&
                (item.Kind == CashReconciliationKind.CashExtra ||
                 item.Kind == CashReconciliationKind.CashlessExtra))
            {
                var monthStart = ResolveReconciliationMonth(command, item);
                var nextMonthStart = monthStart.AddMonths(1);
                int? actualCash = CalculateActualCashBalanceByPeriod(
                    monthStart,
                    nextMonthStart
                );
                int? actualCashless = CalculateActualCashlessBalanceByPeriod(
                    monthStart,
                    nextMonthStart
                );

                if (actualCash.HasValue)
                {
                    CashBalanceCheckpointService.AddCurrentMonthCheckpoint(
                        actualCash.Value,
                        $"Излишек {actionAmount} сом принят владельцем в новый остаток."
                    );
                }
                if (actualCashless.HasValue)
                {
                    CashlessService.SetAmountForToday(
                        actualCashless.Value,
                        $"Излишек {actionAmount} сом принят владельцем в новый остаток.",
                        actualCashless.Value
                    );
                }

                CashReconciliationService.RecordConstitutionCheckpoint(
                    monthStart,
                    nextMonthStart,
                    DateTime.UtcNow.Ticks,
                    $"owner-baseline:{item.Id}",
                    actualCash,
                    actualCashless
                );
            }

            if (command.ResolutionType == "RealShortage" &&
                actionAmount > 0 &&
                (item.Kind == CashReconciliationKind.CashShortage ||
                 item.Kind == CashReconciliationKind.CashlessShortage))
            {
                var allocation = item.LossAllocations
                    .OrderByDescending(entry => entry.CreatedAt)
                    .ThenByDescending(entry => entry.Id)
                    .First(entry => entry.Amount == actionAmount);
                CashPenaltyPostingService.Post(
                    new[]
                    {
                        new CashAccountingAssignment
                        {
                            AllocationId = allocation.Id,
                            EmployeeName = allocation.EmployeeName,
                            Amount = actionAmount,
                            ReconciliationId = item.Id,
                            Reason =
                                $"Владелец подтвердил недостачу кассы. " +
                                $"Должно быть {item.ExpectedAmount} сом, " +
                                $"фактически {item.ActualAmount} сом."
                        }
                    },
                    "Недостача закрыта владельцем как реальная потеря."
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

        private static void ApplyAddManualEmployeeMoneyLoss(
            string commandId,
            FirebaseCommand command)
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

            if (lossKind == "money")
            {
                var result = CashReconciliationService.ApplyConstitutionManualLoss(
                    reconciliationFrom,
                    nextMonthStart,
                    employee.Name,
                    command.Amount,
                    commandId
                );
                CashPenaltyPostingService.Post(
                    result.Assignments,
                    $"{title}. {fullDescription}"
                );
                return;
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
        }

        private static void ApplyDeleteEmployeeViolationLoss(FirebaseCommand command)
        {
            if (!Guid.TryParse(command.RecordId, out Guid lossId))
                throw new Exception("Не указан корректный id штрафа.");

            string sourceId = "loss:" + lossId.ToString("N");
            bool deleted = EmployeeLossService.DeleteFixedViolation(lossId);

            if (!deleted)
            {
                throw new Exception(
                    "Можно удалить только оформленный штраф за нарушение. " +
                    "Кассовые потери, товарные потери и рекомендации этим действием не удаляются."
                );
            }

            EmployeeRatingService.EndBySource(
                sourceId,
                cancelAsError: true,
                compensationAmount: Math.Max(0, command.CompensationAmount),
                note: "Штраф удалён владельцем как ошибочный.");
            ExpiredSessionViolationService.MarkPenaltyCancelled(
                lossId,
                ClubClock.Current.LocalNow);
        }

        private static void ApplyFormalizeLateOpeningPenalty(FirebaseCommand command)
        {
            if (!Guid.TryParse(command.RecordId, out Guid id))
                throw new Exception("Не указан корректный id рекомендации опоздания.");
            if (!LateOpeningPenaltyService.FormalizeNow(id))
                throw new Exception("Рекомендация уже оформлена, отменена или не найдена.");
        }

        private static void ApplyCancelLateOpeningPenalty(FirebaseCommand command)
        {
            if (!Guid.TryParse(command.RecordId, out Guid id))
                throw new Exception("Не указан корректный id рекомендации опоздания.");
            if (!LateOpeningPenaltyService.Cancel(id, command.Reason))
                throw new Exception("Рекомендация уже оформлена, отменена или не найдена.");
        }

        private static string ApplyVerifyCashlessActual(
            string commandId,
            FirebaseCommand command)
        {
            if (command.Amount < 0)
                throw new Exception("amount не может быть меньше 0.");

            var currentMonth = BusinessCalendarService.GetBusinessMonth(
                ClubClock.Current.LocalNow);
            var monthStart = currentMonth.StartInclusive;
            var nextMonthStart = currentMonth.EndExclusive;
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
            int programExpectedCashlessBalance = CalculateExpectedCashlessBalanceForVerification(
                monthStart,
                nextMonthStart,
                command.ExpectedAmount >= 0
                    ? command.ExpectedAmount
                    : calculatedExpectedCashlessBalance
            );
            int observationExpectedCashlessBalance =
                CalculateExpectedCashlessBalanceForObservation(
                    monthStart,
                    nextMonthStart,
                    programExpectedCashlessBalance);
            DateTime cashlessSuspectFrom = GetLatestCashlessVerificationTime(
                monthStart,
                nextMonthStart
            ) ?? reconciliationCycleStart;
            if (cashlessSuspectFrom < reconciliationCycleStart)
                cashlessSuspectFrom = reconciliationCycleStart;
            int difference = actualCashless - observationExpectedCashlessBalance;
            int programDifference = actualCashless - programExpectedCashlessBalance;
            int shortage = Math.Max(0, -difference);
            string suspectedEmployee = shortage > 0
                ? FindCashlessShortageSuspect(
                    shortage,
                    cashlessSuspectFrom,
                    DateTime.Now
                )
                : "";
            var notes = new List<string>
            {
                $"Поступило безнала по программе: {expectedCashlessIncome} сом.",
                $"Расходы безнала: {cashlessExpenses} сом.",
                $"Безнал по программе: {programExpectedCashlessBalance} сом.",
                $"Ожидаемый факт от прошлого снимка: {observationExpectedCashlessBalance} сом.",
                $"Фактический остаток: {actualCashless} сом.",
                $"Новая дельта разбора: {difference:+#;-#;0} сом.",
                $"Разница с программой: {programDifference:+#;-#;0} сом."
            };
            if (!string.IsNullOrWhiteSpace(suspectedEmployee))
                notes.Add($"Рекомендация системы: проверить безнал-операции сотрудника {suspectedEmployee}.");

            var result = CashReconciliationService.ProcessCashlessVerification(
                monthStart,
                nextMonthStart,
                observationExpectedCashlessBalance,
                actualCashless,
                suspectedEmployee,
                string.Join("\n", notes),
                commandId,
                programExpectedCashlessBalance
            );
            CashlessService.SetAmountForToday(
                amount: actualCashless,
                note: string.IsNullOrWhiteSpace(command.Description)
                    ? "Сверка безнала владельцем"
                    : command.Description,
                expectedAmount: programExpectedCashlessBalance
            );

            if (result.PairedAmount > 0)
                notes.Add($"Связанной парой нал/безнал закрыто: {result.PairedAmount} сом.");
            if (result.SettledAmount > 0)
                notes.Add($"Свободным излишком закрыто по приоритету: {result.SettledAmount} сом.");
            notes.Add($"Разбор после сверки: {result.Breakdown:+#;-#;0} сом.");
            notes.Add($"Рекомендации: {result.RecommendationTotal} сом.");
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

            if (latestActual == null || !latestActual.ExpectedAmount.HasValue)
                return Math.Max(0, fallbackExpectedCashless);

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
                latestActual.ExpectedAmount.Value +
                incomeAfterLatestActual -
                expensesAfterLatestActual
            );
        }

        private static int CalculateExpectedCashlessBalanceForObservation(
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
                return Math.Max(0, fallbackExpectedCashless);

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

        private static string ApplyBalanceCashlessActual(
            string commandId,
            FirebaseCommand command)
        {
            if (command.Amount < 0)
                throw new Exception("amount не может быть меньше 0.");

            string note = string.IsNullOrWhiteSpace(command.Description)
                ? "Баланс безнала владельцем"
                : command.Description;
            if (CashReconciliationService.TryGetConstitutionCorrectionCommit(
                    commandId,
                    out DateTime committedAt,
                    out int? committedCash,
                    out int committedCashless))
            {
                CompleteCommittedCashCorrection(
                    commandId,
                    committedAt,
                    committedCash,
                    committedCashless,
                    note
                );
                return "Корректировка уже была применена ранее.";
            }

            var currentMonth = BusinessCalendarService.GetBusinessMonth(
                ClubClock.Current.LocalNow);
            var monthStart = currentMonth.StartInclusive;
            var nextMonthStart = currentMonth.EndExclusive;
            DateTime reconciliationCycleStart = CashBalanceCheckpointService
                .GetCurrentCycleStart(monthStart, nextMonthStart);
            int actualCashless = command.Amount;
            int? actualCash = CalculateActualCashBalanceByPeriod(
                monthStart,
                nextMonthStart
            );
            int programExpectedCashless = CalculateExpectedCashlessBalanceForReconciliation(
                monthStart,
                nextMonthStart,
                actualCashless
            );
            int observationExpectedCashless =
                CalculateExpectedCashlessBalanceForObservation(
                    monthStart,
                    nextMonthStart,
                    programExpectedCashless);
            int cashlessDifference = actualCashless - observationExpectedCashless;
            string suggestedSuspect = cashlessDifference < 0
                ? FindCashlessShortageSuspect(
                    Math.Abs(cashlessDifference),
                    reconciliationCycleStart,
                    DateTime.Now
                )
                : "";
            bool cashlessAlreadyVerified = CashReconciliationService
                .HasCurrentCashlessVerification(
                    reconciliationCycleStart,
                    nextMonthStart,
                    observationExpectedCashless,
                    actualCashless
                );
            var verificationResult = cashlessAlreadyVerified
                ? new CashAccountingResult()
                : CashReconciliationService.ProcessCashlessVerification(
                    monthStart,
                    nextMonthStart,
                    observationExpectedCashless,
                    actualCashless,
                    suggestedSuspect,
                    "Связанная сверка безнала внутри итоговой корректировки.",
                    $"{commandId}:verification",
                    programExpectedCashless
                );

            long checkpointNumber = DateTime.UtcNow.Ticks;
            var correctionResult = CashReconciliationService
                .ApplyConstitutionCorrection(
                    monthStart,
                    nextMonthStart,
                    checkpointNumber,
                    commandId,
                    actualCash,
                    actualCashless
                );
            PersistCashAssignments(
                correctionResult.Assignments,
                "Оформлено итоговой корректировкой по Конституции кассы."
            );

            CompleteCommittedCashCorrection(
                commandId,
                DateTime.Now,
                actualCash,
                actualCashless,
                note
            );
            int finalBreakdown = CashReconciliationService.GetConstitutionBreakdown(
                monthStart,
                nextMonthStart
            );
            int finalRecommendations =
                CashReconciliationService.GetConstitutionRecommendationTotal(
                    monthStart,
                    nextMonthStart
                );
            int formalized = correctionResult.Assignments.Sum(item => item.Amount);
            int settled = verificationResult.PairedAmount +
                           verificationResult.SettledAmount +
                           correctionResult.SettledAmount;
            string cashFact = actualCash.HasValue
                ? $"{actualCash.Value} сом"
                : "нет приёмки";
            return
                $"Корректировка завершена. Наличные факт: {cashFact}. " +
                $"Безнал факт: {actualCashless} сом. " +
                $"Связано и взаимно зачтено: {settled} сом. " +
                $"Оформлено потерь: {formalized} сом. " +
                $"Разбор: {finalBreakdown:+#;-#;0} сом. " +
                $"Рекомендации: {finalRecommendations} сом.";
        }

        private static void CompleteCommittedCashCorrection(
            string commandId,
            DateTime committedAt,
            int? actualCash,
            int actualCashless,
            string note)
        {
            CashPenaltyPostingService.Recover();
            CashlessService.SetAmountForTodayIfNotNewerThan(
                committedAt,
                actualCashless,
                note,
                actualCashless
            );
            if (actualCash.HasValue)
            {
                CashBalanceCheckpointService.AddCurrentMonthCheckpoint(
                    actualCash.Value,
                    note,
                    commandId
                );
            }
        }

        private static void PersistCashAssignments(
            IEnumerable<CashAccountingAssignment> assignments,
            string source)
        {
            CashPenaltyPostingService.Post(assignments, source);
        }

        private static void ApplyAddSalaryPayment(
            string commandId,
            FirebaseCommand command)
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

            BusinessAccountingService.PaySalaryFifo(
                ownerName: "Владелец",
                employeeName: employee.Name,
                amount: command.Amount,
                paymentMethod: NormalizePaymentMethod(command.PaymentMethod),
                description: string.IsNullOrWhiteSpace(command.Description)
                    ? $"Выплата зарплаты за {monthStart:yyyy-MM}"
                    : $"{command.Description}\nМесяц зарплаты: {monthStart:yyyy-MM}",
                throughMonthKey: monthStart.ToString("yyyy-MM"),
                operationId: $"firebase:{commandId}"
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

        private static (
            string EmployeeName,
            DateTime MonthStart,
            double WorkHours,
            int RemainingAmount)
            ApplyRestoreEmployeeWorkHours(FirebaseCommand command)
        {
            string employeeName = command.EmployeeName.Trim();
            var employee = EmployeeService.FindByName(employeeName);

            if (employee == null)
                throw new Exception($"Работник не найден: {employeeName}");

            if (command.WorkHours < 0 || command.WorkHours > 24 * 366)
                throw new Exception("Количество восстанавливаемых часов некорректно.");

            var (monthStart, _) = ParseCommandMonth(command.MonthKey);

            AutoSalaryService.SetRecoveredWorkHours(
                monthStart,
                employee.Name,
                command.WorkHours
            );

            var salary = AutoSalaryService
                .BuildReport(monthStart)
                .Employees
                .FirstOrDefault(item =>
                    item.EmployeeName.Equals(
                        employee.Name,
                        StringComparison.OrdinalIgnoreCase));

            return (
                EmployeeName: employee.Name,
                MonthStart: monthStart,
                WorkHours: salary?.WorkHours ?? command.WorkHours,
                RemainingAmount: salary?.RemainingAmount ?? 0
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

            return AutoSalaryService.UpdateSettings(settings).Settings;
        }

        private static EmployeeRatingSnapshot ApplyUpdateEmployeeRating(
            FirebaseCommand command)
        {
            var employee = EmployeeService.FindByName(command.EmployeeName)
                ?? throw new Exception("Сотрудник не найден.");
            if (command.TimeRatingPercent == 100 &&
                command.RevenueRatingPercent == 100 &&
                command.Reason.Contains("восстанов", StringComparison.OrdinalIgnoreCase))
            {
                EmployeeRatingService.ResetTo100(employee.Name, command.Reason);
            }
            else
            {
                EmployeeRatingService.SetBaseRatings(
                    employee.Name,
                    command.TimeRatingPercent,
                    command.RevenueRatingPercent,
                    command.Reason);
            }
            return EmployeeRatingService.GetSnapshot(
                employee.Name,
                ClubClock.Current.LocalNow);
        }

        private static EmployeeRatingEvent ApplyAddEmployeeRatingEvent(
            FirebaseCommand command)
        {
            var branch = command.RatingBranch.Equals(
                "Time",
                StringComparison.OrdinalIgnoreCase)
                ? EmployeeRatingBranch.Time
                : EmployeeRatingBranch.Revenue;
            return EmployeeRatingService.AddManualEvent(
                command.EmployeeName,
                branch,
                command.TargetPercent,
                command.DurationDays,
                command.Title,
                command.Description);
        }

        private static EmployeeRatingEvent ApplyEndEmployeeRatingEvent(
            FirebaseCommand command)
        {
            if (!Guid.TryParse(command.RatingEventId, out Guid eventId))
                throw new Exception("Не указан корректный id записи рейтинга.");
            return EmployeeRatingService.EndEvent(
                eventId,
                command.CancelAsError,
                command.CompensationAmount,
                command.Reason);
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
            SalaryWorkTimeProtectionService.Clear();
            CashService.Clear();
            CashBalanceCheckpointService.Clear();
            CashlessService.Clear();
            CashlessBalanceCheckpointService.Clear();
            CashAcceptanceService.Clear();
            CashReconciliationService.Clear();
            EmployeeLossService.Clear();
            ExpiredSessionViolationService.Clear();
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
            string resultMessage,
            bool pushCurrentState = true)
        {
            FirebaseCommandLedgerService.MarkApplied(
                commandId,
                command.Type,
                resultMessage
            );

            if (pushCurrentState)
                await PushCurrentStateAsync(_lastKnownPlaces.ToList());

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
            url = AppendQuery(url, "print=silent");

            string json = JsonSerializer.Serialize(data);

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
            url = AppendQuery(url, "print=silent");
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

        private static string AppendQuery(string url, string query)
        {
            string separator = url.Contains('?') ? "&" : "?";
            return url + separator + query.TrimStart('?', '&');
        }

        private sealed class OverviewSnapshot
        {
            public string EmployeeName { get; init; } = "";
            public bool AcceptanceRequired { get; init; }
            public bool AcceptanceCompleted { get; init; }
            public int GamesToday { get; init; }
            public int BusyPlaces { get; init; }
            public int FreePlaces { get; init; }
            public List<ClubPlace> Places { get; init; } = new List<ClubPlace>();
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
            public double WorkHours { get; set; }
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

            public int TimeRatingPercent { get; set; } = 100;
            public int RevenueRatingPercent { get; set; } = 100;
            public string RatingBranch { get; set; } = "";
            public int TargetPercent { get; set; } = 100;
            public int DurationDays { get; set; } = 1;
            public string RatingEventId { get; set; } = "";
            public bool CancelAsError { get; set; }
            public int CompensationAmount { get; set; }
            public string Reason { get; set; } = "";

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
