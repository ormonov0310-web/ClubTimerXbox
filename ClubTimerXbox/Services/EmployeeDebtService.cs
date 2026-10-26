using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public static class EmployeeDebtService
    {
        private static readonly string FolderPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ClubTimerXbox");
        private static readonly string FilePath = Path.Combine(
            FolderPath,
            "employee_debts.json");
        private static readonly object Gate = new();
        private static EmployeeDebtState State = Load();
        private static string LastDurableState = JsonSerializer.Serialize(State);

        public static bool HasOutstandingContracts
        {
            get
            {
                lock (Gate)
                    return State.Contracts.Any(contract =>
                        GetRemainingLocked(contract.Id, DateTime.MaxValue) > 0);
            }
        }

        public static EmployeeDebtContract? GetContract(string contractId)
        {
            lock (Gate)
            {
                EmployeeDebtContract? contract = FindContractLocked(contractId);
                return contract == null ? null : Clone(contract);
            }
        }

        public static EmployeeDebtContract CreateContract(
            string debtorEmployeeId,
            string creditorEmployeeId,
            int amount,
            int deductionPercent,
            string actorName,
            string operationId,
            IReadOnlyDictionary<DateTime, int>? currentDailyGross = null,
            string note = "",
            bool clubCreditor = false,
            string fundingMethod = "")
        {
            debtorEmployeeId = debtorEmployeeId.Trim();
            creditorEmployeeId = creditorEmployeeId.Trim();
            ValidateAmount(amount);
            ValidatePercent(deductionPercent);
            Employee debtor = RequireEmployee(debtorEmployeeId, "должник");
            Employee? creditor = clubCreditor ? null : RequireEmployee(creditorEmployeeId, "кредитор");
            if (creditor != null && debtor.EmployeeId.Equals(creditor.EmployeeId, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Должник и кредитор не могут быть одним сотрудником.");

            lock (Gate)
            {
                RecoverClubLoanMovementsLocked();
                EmployeeDebtContract? replay = FindOperationContractLocked(operationId);
                if (replay != null)
                    return Clone(replay);

                DateTime now = ClubClock.Current.LocalNow;
                if (clubCreditor) ValidateClubFunding(fundingMethod, amount);
                var contract = new EmployeeDebtContract
                {
                    Id = Guid.NewGuid().ToString("N"),
                    DebtorEmployeeId = debtor.EmployeeId,
                    DebtorNameSnapshot = debtor.Name,
                    CreditorType = clubCreditor ? EmployeeDebtCreditorType.Club : EmployeeDebtCreditorType.Employee,
                    FundingMethod = clubCreditor ? fundingMethod : "",
                    CreditorEmployeeId = creditor?.EmployeeId ?? ClubLoanCashPolicy.CreditorId,
                    CreditorNameSnapshot = creditor?.Name ?? "Касса клуба",
                    CreatedAt = now,
                    UpdatedAt = now,
                    DeductionPercent = deductionPercent,
                    Status = EmployeeDebtStatus.Active,
                    Revision = 1
                };
                State.Contracts.Add(contract);
                AddEventLocked(
                    contract,
                    EmployeeDebtEventKind.Created,
                    amount,
                    actorEmployeeId: "",
                    actorName,
                    operationId,
                    note,
                    newPercent: deductionPercent,
                    paymentMethod: clubCreditor ? fundingMethod : "");
                CaptureBaselinesLocked(debtor.EmployeeId, currentDailyGross);
                SaveLocked();
                ClubLoanCashPolicy.TryRecoverCommitted(State, CashService.EnsureClubLoanMovement, SaveLocked);
                return Clone(contract);
            }
        }

        public static EmployeeDebtContract IncreasePrincipal(
            string contractId,
            int amount,
            long expectedRevision,
            string actorName,
            string operationId,
            string note = "",
            string fundingMethod = "")
        {
            ValidateAmount(amount);
            lock (Gate)
            {
                RecoverClubLoanMovementsLocked();
                EmployeeDebtContract contract = RequireContractLocked(contractId);
                if (FindOperationEventLocked(operationId) != null)
                    return Clone(contract);
                EnsureOpenLocked(contract);
                CheckRevision(contract, expectedRevision);
                if (contract.CreditorType == EmployeeDebtCreditorType.Club)
                {
                    if (string.IsNullOrWhiteSpace(fundingMethod)) fundingMethod = contract.FundingMethod;
                    ValidateClubFunding(fundingMethod, amount);
                }
                AddEventLocked(
                    contract,
                    EmployeeDebtEventKind.Increased,
                    amount,
                    "",
                    actorName,
                    operationId,
                    note,
                    paymentMethod: contract.CreditorType == EmployeeDebtCreditorType.Club ? fundingMethod : "");
                TouchLocked(contract);
                SaveLocked();
                ClubLoanCashPolicy.TryRecoverCommitted(State, CashService.EnsureClubLoanMovement, SaveLocked);
                return Clone(contract);
            }
        }

        public static EmployeeDebtContract ChangeDeductionPercent(
            string contractId,
            int deductionPercent,
            long expectedRevision,
            string actorName,
            string operationId,
            IReadOnlyDictionary<DateTime, int>? currentDailyGross = null)
        {
            ValidatePercent(deductionPercent);
            lock (Gate)
            {
                EmployeeDebtContract contract = RequireContractLocked(contractId);
                if (FindOperationEventLocked(operationId) != null)
                    return Clone(contract);
                EnsureOpenLocked(contract);
                CheckRevision(contract, expectedRevision);
                int previous = contract.DeductionPercent;
                contract.DeductionPercent = deductionPercent;
                AddEventLocked(
                    contract,
                    EmployeeDebtEventKind.RateChanged,
                    0,
                    "",
                    actorName,
                    operationId,
                    "Процент удержания изменён владельцем.",
                    previousPercent: previous,
                    newPercent: deductionPercent);
                CaptureBaselinesLocked(contract.DebtorEmployeeId, currentDailyGross);
                TouchLocked(contract);
                SaveLocked();
                return Clone(contract);
            }
        }

        public static EmployeeDebtContract RequestFreeze(
            string contractId,
            string debtorEmployeeId,
            int durationDays,
            long expectedRevision,
            string operationId,
            string note)
        {
            EmployeeDebtFreezePolicy.ValidateDuration(durationDays);
            lock (Gate)
            {
                EmployeeDebtContract contract = RequireContractLocked(contractId);
                if (FindOperationEventLocked(operationId) != null)
                    return Clone(contract);
                EnsureOpenLocked(contract);
                CheckRevision(contract, expectedRevision);
                EnsureActor(contract.DebtorEmployeeId, debtorEmployeeId, "Запросить заморозку может только должник.");
                if (contract.FreezeRequestedAt.HasValue)
                    throw new InvalidOperationException("Запрос на заморозку уже отправлен.");
                if (contract.Status == EmployeeDebtStatus.Paused)
                    throw new InvalidOperationException("Автоматическое погашение уже заморожено.");

                DateTime now = ClubClock.Current.LocalNow;
                if (EmployeeDebtFreezePolicy.IsRejectionCooldownActive(
                        contract.FreezeRequestAvailableAt,
                        now))
                {
                    TimeSpan remaining = contract.FreezeRequestAvailableAt!.Value - now;
                    throw new InvalidOperationException(
                        $"Новый запрос можно отправить через {EmployeeDebtFreezePolicy.FormatRemaining(remaining)}.");
                }
                contract.FreezeRequestId = Guid.NewGuid().ToString("N");
                contract.FreezeRequestedAt = now;
                contract.FreezeRequestedDurationDays = durationDays;
                contract.FreezeRejectedAt = null;
                contract.FreezeRequestAvailableAt = null;
                AddEventLocked(
                    contract,
                    EmployeeDebtEventKind.FreezeRequested,
                    0,
                    debtorEmployeeId,
                    ResolveEmployeeName(debtorEmployeeId, contract.DebtorNameSnapshot),
                    operationId,
                    note,
                    freezeDurationDays: durationDays);
                TouchLocked(contract);
                SaveLocked();
                return Clone(contract);
            }
        }

        public static EmployeeDebtContract ResolveFreezeRequest(
            string contractId,
            string actorEmployeeId,
            bool isOwner,
            bool approve,
            long expectedRevision,
            string operationId,
            IReadOnlyDictionary<DateTime, int>? currentDailyGross = null,
            string note = "")
        {
            lock (Gate)
            {
                EmployeeDebtContract contract = RequireContractLocked(contractId);
                if (FindOperationEventLocked(operationId) != null)
                    return Clone(contract);
                EnsureOpenLocked(contract);
                CheckRevision(contract, expectedRevision);
                EnsureCreditorOrOwner(contract, actorEmployeeId, isOwner);
                if (!contract.FreezeRequestedAt.HasValue)
                    throw new InvalidOperationException("Активного запроса на заморозку нет.");

                DateTime now = ClubClock.Current.LocalNow;
                int durationDays = contract.FreezeRequestedDurationDays;
                if (!EmployeeDebtFreezePolicy.IsAllowedDuration(durationDays))
                    durationDays = 1;
                contract.FreezeRequestId = "";
                contract.FreezeRequestedAt = null;
                contract.FreezeRequestedDurationDays = 0;
                if (approve)
                {
                    contract.Status = EmployeeDebtStatus.Paused;
                    contract.PauseStartedAt = now;
                    contract.PauseUntil = EmployeeDebtFreezePolicy.CalculatePauseUntil(
                        now,
                        durationDays);
                    contract.FreezeRejectedAt = null;
                    contract.FreezeRequestAvailableAt = null;
                    CaptureBaselinesLocked(contract.DebtorEmployeeId, currentDailyGross);
                }
                else
                {
                    contract.FreezeRejectedAt = now;
                    contract.FreezeRequestAvailableAt =
                        EmployeeDebtFreezePolicy.CalculateRequestAvailableAt(now);
                }

                AddEventLocked(
                    contract,
                    approve
                        ? EmployeeDebtEventKind.FreezeApproved
                        : EmployeeDebtEventKind.FreezeRejected,
                    0,
                    actorEmployeeId,
                    ResolveActorName(actorEmployeeId, isOwner),
                    operationId,
                    note,
                    freezeDurationDays: durationDays,
                    freezeUntil: approve ? contract.PauseUntil : null);
                TouchLocked(contract);
                SaveLocked();
                return Clone(contract);
            }
        }

        public static EmployeeDebtContract SetPausedByOwner(
            string contractId,
            bool paused,
            long expectedRevision,
            string actorName,
            string operationId,
            IReadOnlyDictionary<DateTime, int>? currentDailyGross = null,
            int pauseDurationDays = 0)
        {
            if (paused && pauseDurationDays > 0)
                EmployeeDebtFreezePolicy.ValidateDuration(pauseDurationDays);
            lock (Gate)
            {
                EmployeeDebtContract contract = RequireContractLocked(contractId);
                if (FindOperationEventLocked(operationId) != null)
                    return Clone(contract);
                EnsureOpenLocked(contract);
                CheckRevision(contract, expectedRevision);
                contract.Status = paused
                    ? EmployeeDebtStatus.Paused
                    : EmployeeDebtStatus.Active;
                contract.FreezeRequestId = "";
                contract.FreezeRequestedAt = null;
                contract.FreezeRequestedDurationDays = 0;
                contract.FreezeRejectedAt = null;
                contract.FreezeRequestAvailableAt = null;
                DateTime now = ClubClock.Current.LocalNow;
                contract.PauseStartedAt = paused ? now : null;
                contract.PauseUntil = paused && pauseDurationDays > 0
                    ? EmployeeDebtFreezePolicy.CalculatePauseUntil(now, pauseDurationDays)
                    : null;
                CaptureBaselinesLocked(contract.DebtorEmployeeId, currentDailyGross);
                AddEventLocked(
                    contract,
                    paused
                        ? EmployeeDebtEventKind.PausedByOwner
                        : EmployeeDebtEventKind.Resumed,
                    0,
                    "",
                    actorName,
                    operationId,
                    paused
                        ? "Автоматическое удержание приостановлено владельцем."
                        : "Автоматическое удержание возобновлено владельцем.",
                    freezeDurationDays: paused ? pauseDurationDays : 0,
                    freezeUntil: paused ? contract.PauseUntil : null);
                TouchLocked(contract);
                SaveLocked();
                return Clone(contract);
            }
        }

        public static EmployeeDebtContract ResumeByDebtor(
            string contractId,
            string debtorEmployeeId,
            long expectedRevision,
            string operationId,
            IReadOnlyDictionary<DateTime, int>? currentDailyGross = null,
            string note = "")
        {
            lock (Gate)
            {
                EmployeeDebtContract contract = RequireContractLocked(contractId);
                if (FindOperationEventLocked(operationId) != null)
                    return Clone(contract);
                EnsureOpenLocked(contract);
                CheckRevision(contract, expectedRevision);
                EnsureActor(
                    contract.DebtorEmployeeId,
                    debtorEmployeeId,
                    "Возобновить автоматическое погашение может только должник.");
                if (contract.Status != EmployeeDebtStatus.Paused)
                    throw new InvalidOperationException("Автоматическое погашение уже активно.");

                contract.Status = EmployeeDebtStatus.Active;
                contract.PauseStartedAt = null;
                contract.PauseUntil = null;
                CaptureBaselinesLocked(contract.DebtorEmployeeId, currentDailyGross);
                AddEventLocked(
                    contract,
                    EmployeeDebtEventKind.Resumed,
                    0,
                    debtorEmployeeId,
                    ResolveEmployeeName(debtorEmployeeId, contract.DebtorNameSnapshot),
                    operationId,
                    note);
                TouchLocked(contract);
                SaveLocked();
                return Clone(contract);
            }
        }

        public static EmployeeDebtContract RepayFromSalary(
            string contractId,
            IReadOnlyCollection<EmployeeDebtSalaryAllocation> allocations,
            string actorEmployeeId,
            bool isOwner,
            long expectedRevision,
            string operationId,
            string note = "")
        {
            if (allocations == null || allocations.Count == 0)
                throw new ArgumentException("Не указано распределение зарплаты.", nameof(allocations));
            if (allocations.Any(item => item.Amount <= 0 || string.IsNullOrWhiteSpace(item.SourceMonthKey)))
                throw new ArgumentException("Распределение зарплаты содержит неверные данные.", nameof(allocations));

            lock (Gate)
            {
                EmployeeDebtContract contract = RequireContractLocked(contractId);
                if (FindOperationEventLocked(operationId) != null)
                    return Clone(contract);
                EnsureOpenLocked(contract);
                CheckRevision(contract, expectedRevision);
                if (!isOwner)
                    EnsureActor(contract.DebtorEmployeeId, actorEmployeeId, "Погасить долг из зарплаты может только должник.");

                int total = allocations.Sum(item => item.Amount);
                if (total > GetRemainingLocked(contract.Id, DateTime.MaxValue))
                    throw new InvalidOperationException("Сумма погашения больше остатка долга.");

                foreach (EmployeeDebtSalaryAllocation allocation in allocations)
                {
                    AddEventLocked(
                        contract,
                        EmployeeDebtEventKind.SalaryRepayment,
                        allocation.Amount,
                        actorEmployeeId,
                        ResolveActorName(actorEmployeeId, isOwner),
                        operationId,
                        note,
                        allocation.SourceBusinessDate,
                        allocation.SourceMonthKey);
                }

                CloseIfSettledLocked(contract);
                TouchLocked(contract);
                SaveLocked();
                return Clone(contract);
            }
        }

        public static EmployeeDebtManualRepaymentQuote GetManualRepaymentQuote(
            string contractId)
        {
            EmployeeDebtContract contract = GetContract(contractId)
                ?? throw new InvalidOperationException("Договор долга не найден.");
            Employee debtor = RequireEmployee(contract.DebtorEmployeeId, "должник");
            BusinessPeriodRange period = BusinessCalendarService.GetBusinessMonth(
                ClubClock.Current.LocalNow);
            AutoSalaryEmployeeResult salary = AutoSalaryService
                .BuildReport(period.StartInclusive)
                .Employees
                .FirstOrDefault(item => item.EmployeeId.Equals(
                    debtor.EmployeeId,
                    StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("Начисления сотрудника не найдены.");

            lock (Gate)
            {
                contract = RequireContractLocked(contractId);
                int debtRemaining = GetRemainingLocked(contract.Id, DateTime.MaxValue);
                int availableSalary = Math.Max(0, salary.RemainingAmount);
                int reserve = EmployeeDebtManualRepaymentPolicy.CalculateAutomaticReserve(
                    debtRemaining);
                return new EmployeeDebtManualRepaymentQuote
                {
                    ContractId = contract.Id,
                    Revision = contract.Revision,
                    DebtRemainingAmount = debtRemaining,
                    AvailableSalaryAmount = availableSalary,
                    CurrentPeriodAvailableSalaryAmount = Math.Max(
                        0,
                        salary.CurrentPeriodRemainingAmount),
                    AutomaticReserveAmount = reserve,
                    MaximumManualPaymentAmount =
                        EmployeeDebtManualRepaymentPolicy.CalculateMaximumManualPayment(
                            debtRemaining,
                            availableSalary)
                };
            }
        }

        public static EmployeeDebtContract RepayFromAvailableSalary(
            string contractId,
            int amount,
            string actorEmployeeId,
            bool isOwner,
            long expectedRevision,
            string operationId,
            string note = "")
        {
            ValidateAmount(amount);
            // Снимок экрана может устареть; решает свежая котировка ниже.
            _ = expectedRevision;
            EmployeeDebtManualRepaymentQuote quote = GetManualRepaymentQuote(contractId);
            if (quote.DebtRemainingAmount <= 0)
                throw new InvalidOperationException("Долг уже закрыт автоматическим погашением.");
            if (amount > quote.DebtRemainingAmount)
                throw new InvalidOperationException("Сумма погашения больше остатка долга.");
            if (amount > quote.AvailableSalaryAmount)
            {
                throw new InvalidOperationException(
                    $"Свободно только {quote.AvailableSalaryAmount} сом собственных денег.");
            }
            if (amount > quote.MaximumManualPaymentAmount)
            {
                throw new InvalidOperationException(
                    $"Данные обновились. Сейчас вручную можно оплатить не более " +
                    $"{quote.MaximumManualPaymentAmount} сом; " +
                    $"{quote.AutomaticReserveAmount} сом оставлено для автооплаты.");
            }

            EmployeeDebtContract contract = GetContract(contractId)
                ?? throw new InvalidOperationException("Договор долга не найден.");
            Employee debtor = RequireEmployee(contract.DebtorEmployeeId, "должник");
            BusinessPeriodRange period = BusinessCalendarService.GetBusinessMonth(
                ClubClock.Current.LocalNow);

            IReadOnlyList<EmployeeDebtSalaryAllocation> allocations =
                BusinessAccountingService.BuildDebtSalaryAllocations(
                    debtor.EmployeeId,
                    debtor.Name,
                    amount,
                    period.Key,
                    quote.CurrentPeriodAvailableSalaryAmount);
            return RepayFromSalary(
                contractId,
                allocations,
                actorEmployeeId,
                isOwner,
                quote.Revision,
                operationId,
                note);
        }

        public static EmployeeDebtContract RecordHandToHandRepayment(
            string contractId,
            int amount,
            string actorEmployeeId,
            bool isOwner,
            long expectedRevision,
            string operationId,
            string note = "",
            string paymentMethod = "")
        {
            ValidateAmount(amount);
            lock (Gate)
            {
                RecoverClubLoanMovementsLocked();
                EmployeeDebtContract contract = RequireContractLocked(contractId);
                if (FindOperationEventLocked(operationId) != null)
                    return Clone(contract);
                EnsureOpenLocked(contract);
                CheckRevision(contract, expectedRevision);
                EnsureCreditorOrOwner(contract, actorEmployeeId, isOwner);
                if (contract.CreditorType == EmployeeDebtCreditorType.Club)
                    ClubLoanCashPolicy.ValidateMethod(paymentMethod);
                if (amount > GetRemainingLocked(contract.Id, DateTime.MaxValue))
                    throw new InvalidOperationException("Сумма больше остатка долга.");

                AddEventLocked(
                    contract,
                    EmployeeDebtEventKind.HandToHandRepayment,
                    amount,
                    actorEmployeeId,
                    ResolveActorName(actorEmployeeId, isOwner),
                    operationId,
                    note,
                    paymentMethod: contract.CreditorType == EmployeeDebtCreditorType.Club ? paymentMethod : "");
                CloseIfSettledLocked(contract);
                TouchLocked(contract);
                SaveLocked();
                ClubLoanCashPolicy.TryRecoverCommitted(State, CashService.EnsureClubLoanMovement, SaveLocked);
                return Clone(contract);
            }
        }

        public static EmployeeDebtContract Forgive(
            string contractId,
            int amount,
            string actorEmployeeId,
            bool isOwner,
            long expectedRevision,
            string operationId,
            string note = "")
        {
            ValidateAmount(amount);
            lock (Gate)
            {
                EmployeeDebtContract contract = RequireContractLocked(contractId);
                if (FindOperationEventLocked(operationId) != null)
                    return Clone(contract);
                EnsureOpenLocked(contract);
                CheckRevision(contract, expectedRevision);
                EnsureCreditorOrOwner(contract, actorEmployeeId, isOwner);
                if (amount > GetRemainingLocked(contract.Id, DateTime.MaxValue))
                    throw new InvalidOperationException("Сумма прощения больше остатка долга.");

                AddEventLocked(
                    contract,
                    EmployeeDebtEventKind.Forgiven,
                    amount,
                    actorEmployeeId,
                    ResolveActorName(actorEmployeeId, isOwner),
                    operationId,
                    note);
                CloseIfSettledLocked(contract);
                TouchLocked(contract);
                SaveLocked();
                return Clone(contract);
            }
        }

        public static int WithdrawAvailableCredit(
            string creditorEmployeeId,
            int amount,
            string paymentMethod,
            string actorName,
            string operationId,
            string description = "")
        {
            ValidateAmount(amount);
            creditorEmployeeId = creditorEmployeeId.Trim();
            Employee creditor = RequireEmployee(creditorEmployeeId, "кредитор");
            int postedAmount;
            lock (Gate)
            {
                List<EmployeeDebtEvent> replay = State.Events
                    .Where(item => item.OperationId.Equals(operationId, StringComparison.OrdinalIgnoreCase) &&
                                   item.Kind == EmployeeDebtEventKind.CreditWithdrawal)
                    .ToList();
                if (replay.Count > 0)
                {
                    postedAmount = replay.Sum(item => item.Amount);
                }
                else
                {
                    int available = GetAvailableCreditLocked(creditorEmployeeId, DateTime.MaxValue);
                    if (amount > available)
                        throw new InvalidOperationException($"Доступно к получению только {available} сом.");

                    int remaining = amount;
                    var contracts = State.Contracts
                        .Where(item => item.CreditorEmployeeId.Equals(
                            creditorEmployeeId,
                            StringComparison.OrdinalIgnoreCase))
                        .OrderBy(item => item.CreatedAt)
                        .ToList();
                    foreach (EmployeeDebtContract contract in contracts)
                    {
                        if (remaining <= 0)
                            break;
                        int contractCredit = GetContractCreditLocked(contract.Id, DateTime.MaxValue);
                        if (contractCredit <= 0)
                            continue;
                        int use = Math.Min(contractCredit, remaining);
                        AddEventLocked(
                            contract,
                            EmployeeDebtEventKind.CreditWithdrawal,
                            use,
                            creditorEmployeeId,
                            actorName,
                            operationId,
                            description,
                            paymentMethod: CashService.NormalizePaymentMethod(paymentMethod));
                        remaining -= use;
                        TouchLocked(contract);
                    }

                    if (remaining != 0)
                        throw new InvalidOperationException("Не удалось распределить вывод возврата долга.");
                    SaveLocked();
                    postedAmount = amount;
                }
            }

            string marker = $"[debt-credit-withdrawal:{operationId}]";
            if (!CashService.Records.Any(record =>
                    record.Description?.Contains(marker, StringComparison.Ordinal) == true))
            {
                CashService.AddDebtReturnWithdrawal(
                    actorName,
                    creditor.Name,
                    postedAmount,
                    paymentMethod,
                    string.IsNullOrWhiteSpace(description)
                        ? marker
                        : $"{description}\n{marker}");
            }

            return postedAmount;
        }

        public static string GetWithdrawalSourceDescription(CashRecord record)
        {
            lock (Gate)
                return BuildWithdrawalSourceDescription(State, record);
        }

        internal static string BuildWithdrawalSourceDescription(EmployeeDebtState state, CashRecord record)
        {
            if (record.ExpenseCategory != "Возврат долга")
                return "";

            var contracts = state.Contracts.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
            var portions = state.Events
                .Where(item => item.Kind == EmployeeDebtEventKind.CreditWithdrawal &&
                               !string.IsNullOrWhiteSpace(item.OperationId) &&
                               record.Description.Contains(
                                   $"[debt-credit-withdrawal:{item.OperationId}]", StringComparison.Ordinal) &&
                               contracts.ContainsKey(item.ContractId))
                .GroupBy(item => contracts[item.ContractId].DebtorNameSnapshot)
                .Select(group => $"{group.Key}: {group.Sum(item => item.Amount)} сом")
                .ToList();
            return portions.Count == 0
                ? "Из возвращённых долгов"
                : "Из возвращённых долгов — " + string.Join("; ", portions);
        }

        public static int GetWithdrawalOperationAmount(string operationId)
        {
            lock (Gate)
            {
                return State.Events
                    .Where(item => item.OperationId.Equals(operationId, StringComparison.OrdinalIgnoreCase) &&
                                   item.Kind == EmployeeDebtEventKind.CreditWithdrawal)
                    .Sum(item => item.Amount);
            }
        }

        public static int GetAvailableCredit(string creditorEmployeeId)
        {
            lock (Gate)
                return GetAvailableCreditLocked(creditorEmployeeId, DateTime.MaxValue);
        }

        public static int GetSalaryTransferTotal(string debtorEmployeeId, string monthKey)
        {
            debtorEmployeeId = debtorEmployeeId.Trim();
            monthKey = monthKey.Trim();
            if (string.IsNullOrWhiteSpace(debtorEmployeeId) || string.IsNullOrWhiteSpace(monthKey))
                return 0;

            lock (Gate)
            {
                var contractIds = State.Contracts
                    .Where(item => item.DebtorEmployeeId.Equals(
                        debtorEmployeeId,
                        StringComparison.OrdinalIgnoreCase))
                    .Select(item => item.Id)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                return State.Events
                    .Where(item => contractIds.Contains(item.ContractId) &&
                                   IsSalaryFundedRepayment(item.Kind) &&
                                   item.SalarySourceMonthKey.Equals(
                                       monthKey,
                                       StringComparison.OrdinalIgnoreCase))
                    .Sum(item => item.Amount);
            }
        }

        public static bool ReconcileAutomatic(
            AutoSalaryReport report,
            IReadOnlyDictionary<string, List<AutoSalaryDayEarning>> dailyEarningsByEmployee)
        {
            using var measurement = SnapshotBuildMeasurements.Measure("debt.auto");
            if (report == null || dailyEarningsByEmployee == null)
                return false;

            lock (Gate)
            {
                bool changed = false;
                DateTime now = ClubClock.Current.LocalNow;
                foreach (AutoSalaryEmployeeResult salary in report.Employees)
                {
                    EmployeeDebtContract? contract = GetAutomaticHeadLocked(salary.EmployeeId);
                    if (contract == null)
                        continue;
                    if (!dailyEarningsByEmployee.TryGetValue(salary.EmployeeName, out var days))
                        days = new List<AutoSalaryDayEarning>();

                    if (ResumeExpiredPauseLocked(contract, salary.EmployeeId, days, now))
                        changed = true;

                    int availableSalary = EmployeeDebtAutomaticCalculator.GetAvailableSalary(
                        salary,
                        GetSalaryTransferTotal(salary.EmployeeId, report.MonthKey));
                    DateTime contractBusinessDate = BusinessCalendarService
                        .GetBusinessDate(contract.CreatedAt)
                        .Date;
                    foreach (AutoSalaryDayEarning day in days.OrderBy(item => item.Date))
                    {
                        int gross = Math.Max(0, day.TotalAmount);
                        string cursorKey = BuildCursorKey(salary.EmployeeId, day.Date);
                        if (!State.EarningCursors.TryGetValue(cursorKey, out var cursor))
                        {
                            cursor = new EmployeeDebtEarningCursor
                            {
                                DebtorEmployeeId = salary.EmployeeId,
                                BusinessDate = day.Date.Date,
                                ActiveContractId = contract.Id,
                                GrossHighWater = day.Date.Date <= contractBusinessDate ? gross : 0,
                                UpdatedAt = now
                            };
                            State.EarningCursors[cursorKey] = cursor;
                            changed = true;
                            if (day.Date.Date <= contractBusinessDate)
                                continue;
                        }

                        if (!cursor.ActiveContractId.Equals(
                                contract.Id,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            cursor.ActiveContractId = contract.Id;
                            cursor.GrossHighWater = Math.Max(cursor.GrossHighWater, gross);
                            cursor.FractionRemainder = 0;
                            cursor.UpdatedAt = now;
                            changed = true;
                            continue;
                        }

                        int previousHighWater = cursor.GrossHighWater;
                        int nextHighWater = Math.Max(cursor.GrossHighWater, gross);
                        if (contract.Status == EmployeeDebtStatus.Paused)
                        {
                            if (nextHighWater != cursor.GrossHighWater ||
                                cursor.FractionRemainder != 0)
                            {
                                cursor.GrossHighWater = nextHighWater;
                                cursor.FractionRemainder = 0;
                                cursor.UpdatedAt = now;
                                changed = true;
                            }
                            continue;
                        }

                        int grossDelta = Math.Max(0, gross - previousHighWater);
                        if (grossDelta <= 0)
                            continue;

                        cursor.GrossHighWater = nextHighWater;
                        cursor.UpdatedAt = now;

                        AutomaticDebtDeduction calculation =
                            EmployeeDebtAutomaticCalculator.CalculateWithinBalances(
                                grossDelta,
                                contract.DeductionPercent,
                                cursor.FractionRemainder,
                                GetRemainingLocked(contract.Id, DateTime.MaxValue),
                                availableSalary);
                        cursor.FractionRemainder = calculation.FractionRemainder;
                        int amount = calculation.Amount;
                        changed = true;
                        if (amount <= 0)
                            continue;

                        UpsertAutomaticRepaymentLocked(
                            contract,
                            day.Date.Date,
                            report.MonthKey,
                            amount,
                            now);
                        availableSalary -= amount;
                        CloseIfSettledLocked(contract);
                        TouchLocked(contract);
                        if (contract.Status == EmployeeDebtStatus.Closed)
                            break;
                    }
                }

                if (changed)
                    SaveLocked();
                return changed;
            }
        }

        private static bool ResumeExpiredPauseLocked(
            EmployeeDebtContract contract,
            string debtorEmployeeId,
            IReadOnlyCollection<AutoSalaryDayEarning> days,
            DateTime now)
        {
            if (!EmployeeDebtFreezePolicy.ShouldAutoResume(contract, now))
                return false;

            DateTime expiredAt = contract.PauseUntil!.Value;
            var baseline = days
                .GroupBy(item => item.Date.Date)
                .ToDictionary(
                    group => group.Key,
                    group => Math.Max(0, group.Max(item => item.TotalAmount)));
            contract.Status = EmployeeDebtStatus.Active;
            contract.PauseStartedAt = null;
            contract.PauseUntil = null;
            CaptureBaselinesLocked(debtorEmployeeId, baseline);
            AddEventLocked(
                contract,
                EmployeeDebtEventKind.FreezeExpired,
                0,
                "",
                "Система",
                $"auto-debt-freeze-expired:{contract.Id}:{expiredAt.Ticks}",
                "Срок заморозки завершён. Удержания возобновлены с будущих начислений.",
                freezeUntil: expiredAt,
                createdAt: expiredAt);
            TouchLocked(contract);
            return true;
        }

        public static EmployeeDebtEmployeeSnapshot GetEmployeeSnapshot(
            string employeeId,
            DateTime monthStart)
        {
            employeeId = employeeId.Trim();
            BusinessPeriodRange period = BusinessCalendarService.GetBusinessMonthByAnchor(monthStart);
            lock (Gate)
            {
                return EmployeeDebtLedgerCalculator.BuildEmployeeSnapshot(
                    State,
                    employeeId,
                    period,
                    ResolveEmployeeName);
            }
        }

        public static void Clear()
        {
            lock (Gate)
            {
                State = new EmployeeDebtState();
                SaveLocked();
            }
        }

        private static void UpsertAutomaticRepaymentLocked(
            EmployeeDebtContract contract,
            DateTime businessDate,
            string monthKey,
            int amount,
            DateTime now)
        {
            string operationId = $"auto:{contract.Id}:{businessDate:yyyyMMdd}";
            EmployeeDebtEvent? existing = State.Events.FirstOrDefault(item =>
                item.OperationId.Equals(operationId, StringComparison.OrdinalIgnoreCase) &&
                item.Kind == EmployeeDebtEventKind.AutomaticRepayment);
            if (existing == null)
            {
                AddEventLocked(
                    contract,
                    EmployeeDebtEventKind.AutomaticRepayment,
                    amount,
                    contract.DebtorEmployeeId,
                    ResolveEmployeeName(contract.DebtorEmployeeId, contract.DebtorNameSnapshot),
                    operationId,
                    $"Автоматическое удержание {contract.DeductionPercent}% с новых начислений.",
                    businessDate,
                    monthKey);
                return;
            }

            existing.Amount += amount;
            existing.UpdatedAt = now;
        }

        private static void CaptureBaselinesLocked(
            string debtorEmployeeId,
            IReadOnlyDictionary<DateTime, int>? currentDailyGross)
        {
            if (currentDailyGross == null)
                return;
            EmployeeDebtContract? head = GetAutomaticHeadLocked(debtorEmployeeId);
            string activeContractId = head?.Id ?? "";
            DateTime now = ClubClock.Current.LocalNow;
            foreach (var pair in currentDailyGross)
            {
                string key = BuildCursorKey(debtorEmployeeId, pair.Key);
                State.EarningCursors[key] = new EmployeeDebtEarningCursor
                {
                    DebtorEmployeeId = debtorEmployeeId,
                    BusinessDate = pair.Key.Date,
                    ActiveContractId = activeContractId,
                    GrossHighWater = Math.Max(0, pair.Value),
                    FractionRemainder = 0,
                    UpdatedAt = now
                };
            }
        }

        private static EmployeeDebtContract? GetAutomaticHeadLocked(string debtorEmployeeId)
        {
            return State.Contracts
                .Where(item => item.DebtorEmployeeId.Equals(
                    debtorEmployeeId,
                    StringComparison.OrdinalIgnoreCase))
                .Where(item => GetRemainingLocked(item.Id, DateTime.MaxValue) > 0)
                .OrderBy(item => item.CreatedAt)
                .ThenBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }

        private static int GetRemainingLocked(string contractId, DateTime beforeExclusive)
        {
            int principal = State.Events
                .Where(item => item.ContractId.Equals(contractId, StringComparison.OrdinalIgnoreCase) &&
                               item.CreatedAt < beforeExclusive &&
                               IsPrincipalEvent(item.Kind))
                .Sum(item => item.Amount);
            int settled = State.Events
                .Where(item => item.ContractId.Equals(contractId, StringComparison.OrdinalIgnoreCase) &&
                               item.CreatedAt < beforeExclusive &&
                               (IsRepayment(item.Kind) || item.Kind == EmployeeDebtEventKind.Forgiven))
                .Sum(item => item.Amount);
            return Math.Max(0, principal - settled);
        }

        private static int GetContractCreditLocked(string contractId, DateTime beforeExclusive)
        {
            if (FindContractLocked(contractId)?.CreditorType == EmployeeDebtCreditorType.Club)
                return 0;
            int received = State.Events
                .Where(item => item.ContractId.Equals(contractId, StringComparison.OrdinalIgnoreCase) &&
                               item.CreatedAt < beforeExclusive &&
                               IsSalaryFundedRepayment(item.Kind))
                .Sum(item => item.Amount);
            int withdrawn = State.Events
                .Where(item => item.ContractId.Equals(contractId, StringComparison.OrdinalIgnoreCase) &&
                               item.CreatedAt < beforeExclusive &&
                               item.Kind == EmployeeDebtEventKind.CreditWithdrawal)
                .Sum(item => item.Amount);
            return Math.Max(0, received - withdrawn);
        }

        private static int GetAvailableCreditLocked(string creditorEmployeeId, DateTime beforeExclusive)
        {
            return State.Contracts
                .Where(item => item.CreditorEmployeeId.Equals(
                    creditorEmployeeId,
                    StringComparison.OrdinalIgnoreCase))
                .Sum(item => GetContractCreditLocked(item.Id, beforeExclusive));
        }

        private static void CloseIfSettledLocked(EmployeeDebtContract contract)
        {
            if (GetRemainingLocked(contract.Id, DateTime.MaxValue) > 0)
                return;
            contract.Status = EmployeeDebtStatus.Closed;
            contract.ClosedAt ??= ClubClock.Current.LocalNow;
            contract.FreezeRequestId = "";
            contract.FreezeRequestedAt = null;
            contract.FreezeRequestedDurationDays = 0;
            contract.FreezeRejectedAt = null;
            contract.FreezeRequestAvailableAt = null;
            contract.PauseStartedAt = null;
            contract.PauseUntil = null;
        }

        private static EmployeeDebtEvent AddEventLocked(
            EmployeeDebtContract contract,
            EmployeeDebtEventKind kind,
            int amount,
            string actorEmployeeId,
            string actorName,
            string operationId,
            string note,
            DateTime? sourceBusinessDate = null,
            string salarySourceMonthKey = "",
            int previousPercent = 0,
            int newPercent = 0,
            string paymentMethod = "",
            int freezeDurationDays = 0,
            DateTime? freezeUntil = null,
            DateTime? createdAt = null)
        {
            DateTime now = ClubClock.Current.LocalNow;
            var item = new EmployeeDebtEvent
            {
                Id = Guid.NewGuid().ToString("N"),
                OperationId = NormalizeOperationId(operationId),
                ContractId = contract.Id,
                Kind = kind,
                CreatedAt = createdAt ?? now,
                UpdatedAt = now,
                SourceBusinessDate = sourceBusinessDate?.Date,
                SalarySourceMonthKey = salarySourceMonthKey.Trim(),
                Amount = amount,
                PreviousPercent = previousPercent,
                NewPercent = newPercent,
                ActorEmployeeId = actorEmployeeId.Trim(),
                ActorName = actorName.Trim(),
                PaymentMethod = paymentMethod.Trim(),
                Note = note.Trim(),
                FreezeDurationDays = freezeDurationDays,
                FreezeUntil = freezeUntil
            };
            State.Events.Add(item);
            return item;
        }

        private static void TouchLocked(EmployeeDebtContract contract)
        {
            contract.UpdatedAt = ClubClock.Current.LocalNow;
            contract.Revision++;
        }

        private static EmployeeDebtContract RequireContractLocked(string contractId)
        {
            return FindContractLocked(contractId)
                   ?? throw new InvalidOperationException("Договор долга не найден.");
        }

        private static EmployeeDebtContract? FindContractLocked(string contractId)
        {
            contractId = contractId.Trim();
            return State.Contracts.FirstOrDefault(item =>
                item.Id.Equals(contractId, StringComparison.OrdinalIgnoreCase));
        }

        private static EmployeeDebtContract? FindOperationContractLocked(string operationId)
        {
            EmployeeDebtEvent? item = FindOperationEventLocked(operationId);
            return item == null ? null : FindContractLocked(item.ContractId);
        }

        private static EmployeeDebtEvent? FindOperationEventLocked(string operationId)
        {
            operationId = NormalizeOperationId(operationId);
            return State.Events.FirstOrDefault(item =>
                item.OperationId.Equals(operationId, StringComparison.OrdinalIgnoreCase));
        }

        private static string NormalizeOperationId(string operationId)
        {
            operationId = operationId?.Trim() ?? "";
            return string.IsNullOrWhiteSpace(operationId)
                ? Guid.NewGuid().ToString("N")
                : operationId;
        }

        private static Employee RequireEmployee(string employeeId, string role)
        {
            return EmployeeService.FindById(employeeId)
                   ?? throw new InvalidOperationException($"Не найден {role}.");
        }

        private static void EnsureOpenLocked(EmployeeDebtContract contract)
        {
            if (GetRemainingLocked(contract.Id, DateTime.MaxValue) <= 0 ||
                contract.Status == EmployeeDebtStatus.Closed)
            {
                throw new InvalidOperationException("Долг уже закрыт.");
            }
        }

        private static void CheckRevision(EmployeeDebtContract contract, long expectedRevision)
        {
            if (expectedRevision >= 0 && contract.Revision != expectedRevision)
            {
                throw new InvalidOperationException(
                    "Договор уже изменился. Обновите данные и повторите действие.");
            }
        }

        private static void EnsureActor(string expectedId, string actualId, string message)
        {
            if (!expectedId.Equals(actualId?.Trim(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(message);
        }

        private static void EnsureCreditorOrOwner(
            EmployeeDebtContract contract,
            string actorEmployeeId,
            bool isOwner)
        {
            if (isOwner)
                return;
            if (contract.CreditorType == EmployeeDebtCreditorType.Club)
                throw new InvalidOperationException("Договором с кассой клуба управляет владелец с телефона.");
            EnsureActor(
                contract.CreditorEmployeeId,
                actorEmployeeId,
                "Это действие доступно только кредитору.");
        }

        private static string ResolveActorName(string employeeId, bool isOwner)
        {
            return isOwner ? "Владелец" : ResolveEmployeeName(employeeId, "Сотрудник");
        }

        private static string ResolveEmployeeName(string employeeId, string fallback)
        {
            return EmployeeService.FindById(employeeId)?.Name ?? fallback;
        }

        private static string BuildCursorKey(string employeeId, DateTime businessDate)
        {
            return $"{employeeId.Trim().ToLowerInvariant()}|{businessDate:yyyy-MM-dd}";
        }

        private static bool IsPrincipalEvent(EmployeeDebtEventKind kind)
        {
            return kind == EmployeeDebtEventKind.Created ||
                   kind == EmployeeDebtEventKind.Increased;
        }

        internal static bool IsRepayment(EmployeeDebtEventKind kind)
        {
            return kind == EmployeeDebtEventKind.AutomaticRepayment ||
                   kind == EmployeeDebtEventKind.SalaryRepayment ||
                   kind == EmployeeDebtEventKind.HandToHandRepayment;
        }

        internal static bool IsSalaryFundedRepayment(EmployeeDebtEventKind kind)
        {
            return kind == EmployeeDebtEventKind.AutomaticRepayment ||
                   kind == EmployeeDebtEventKind.SalaryRepayment;
        }

        private static void ValidateAmount(int amount)
        {
            if (amount <= 0)
                throw new ArgumentOutOfRangeException(nameof(amount), "Сумма должна быть больше 0.");
        }

        private static void ValidatePercent(int percent)
        {
            if (percent < 1 || percent > 100)
                throw new ArgumentOutOfRangeException(nameof(percent), "Процент должен быть от 1 до 100.");
        }

        private static EmployeeDebtContract Clone(EmployeeDebtContract contract)
        {
            string json = JsonSerializer.Serialize(contract);
            return JsonSerializer.Deserialize<EmployeeDebtContract>(json)
                   ?? new EmployeeDebtContract();
        }

        private static EmployeeDebtState Load()
        {
            try
            {
                if (!File.Exists(FilePath))
                    return new EmployeeDebtState();
                EmployeeDebtState state = JsonSerializer.Deserialize<EmployeeDebtState>(
                    File.ReadAllText(FilePath)) ?? new EmployeeDebtState();
                state.Contracts ??= new List<EmployeeDebtContract>();
                state.Events ??= new List<EmployeeDebtEvent>();
                state.EarningCursors = new Dictionary<string, EmployeeDebtEarningCursor>(
                    state.EarningCursors ?? new Dictionary<string, EmployeeDebtEarningCursor>(),
                    StringComparer.OrdinalIgnoreCase);
                return state;
            }
            catch
            {
                return new EmployeeDebtState();
            }
        }

        public static void RecoverClubLoanMovements()
        {
            lock (Gate) RecoverClubLoanMovementsLocked();
        }

        private static void RecoverClubLoanMovementsLocked()
        {
            ClubLoanCashPolicy.Recover(State, CashService.EnsureClubLoanMovement, SaveLocked);
        }

        private static void ValidateClubFunding(string method, int amount)
        {
            ClubLoanCashPolicy.ValidateMethod(method);
            DateTime now = ClubClock.Current.LocalNow;
            int available = method == "Наличные"
                ? CashBalanceSummaryService.CalculateExpectedCashBalanceByPeriod(DateTime.MinValue, now.AddTicks(1))
                : CashBalanceSummaryService.CalculateExpectedCashlessBalanceByPeriod(DateTime.MinValue, now.AddTicks(1));
            if (amount > available)
                throw new InvalidOperationException($"В кассе клуба недостаточно денег: {available} сом.");
        }

        private static void SaveLocked()
        {
            string json = JsonSerializer.Serialize(State,
                new JsonSerializerOptions { WriteIndented = true });
            try
            {
                Directory.CreateDirectory(FolderPath);
                AtomicFileStorageService.WriteAllText(FilePath, json);
                LastDurableState = json;
            }
            catch
            {
                State = JsonSerializer.Deserialize<EmployeeDebtState>(LastDurableState)!;
                State.EarningCursors = new(State.EarningCursors, StringComparer.OrdinalIgnoreCase);
                throw;
            }
        }
    }

    internal static class EmployeeDebtFreezePolicy
    {
        private static readonly int[] Durations = { 1, 3, 7, 10 };

        public static IReadOnlyList<int> AllowedDurations => Durations;

        public static TimeSpan RejectionCooldown => TimeSpan.FromHours(12);

        public static bool IsAllowedDuration(int durationDays)
        {
            return Durations.Contains(durationDays);
        }

        public static void ValidateDuration(int durationDays)
        {
            if (!IsAllowedDuration(durationDays))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(durationDays),
                    "Заморозку можно запросить только на 1, 3, 7 или 10 дней.");
            }
        }

        public static DateTime CalculatePauseUntil(DateTime approvedAt, int durationDays)
        {
            ValidateDuration(durationDays);
            return approvedAt.AddDays(durationDays);
        }

        public static DateTime CalculateRequestAvailableAt(DateTime rejectedAt)
        {
            return rejectedAt.Add(RejectionCooldown);
        }

        public static bool IsRejectionCooldownActive(DateTime? requestAvailableAt, DateTime now)
        {
            return requestAvailableAt.HasValue && requestAvailableAt.Value > now;
        }

        public static bool ShouldAutoResume(EmployeeDebtContract contract, DateTime now)
        {
            return contract.Status == EmployeeDebtStatus.Paused &&
                   contract.PauseUntil.HasValue &&
                   contract.PauseUntil.Value <= now;
        }

        public static string FormatRemaining(TimeSpan remaining)
        {
            int totalMinutes = Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes));
            int hours = totalMinutes / 60;
            int minutes = totalMinutes % 60;
            return hours > 0 ? $"{hours} ч {minutes} мин" : $"{minutes} мин";
        }
    }

    internal static class EmployeeDebtManualRepaymentPolicy
    {
        public static int CalculateAutomaticReserve(int debtRemaining)
        {
            debtRemaining = Math.Max(0, debtRemaining);
            if (debtRemaining <= 10)
                return debtRemaining;

            int tail = debtRemaining % 10;
            return tail >= 4 ? tail : 10;
        }

        public static int CalculateMaximumManualPayment(
            int debtRemaining,
            int availableSalary)
        {
            int reserve = CalculateAutomaticReserve(debtRemaining);
            int debtLimit = Math.Max(0, debtRemaining - reserve);
            return Math.Min(debtLimit, Math.Max(0, availableSalary));
        }
    }

    internal readonly record struct AutomaticDebtDeduction(
        int Amount,
        int FractionRemainder);

    internal static class EmployeeDebtAutomaticCalculator
    {
        public const int ProtectedSalaryAmount = 200;

        public static int GetAvailableSalary(
            AutoSalaryEmployeeResult salary,
            int currentTransferredAmount)
        {
            // A report can predate another reconciliation; never reuse its spent balance.
            long laterTransfers = Math.Max(0L,
                (long)currentTransferredAmount - salary.DebtTransferredAmount);
            return (int)Math.Max(0L, Math.Min(
                (long)salary.CurrentPeriodRemainingAmount - laterTransfers,
                (long)salary.RemainingAmount - laterTransfers - ProtectedSalaryAmount));
        }

        public static AutomaticDebtDeduction CalculateWithinBalances(
            int grossDelta,
            int deductionPercent,
            int fractionRemainder,
            int debtRemaining,
            int availableSalary)
        {
            if (availableSalary <= 0 || debtRemaining <= 0)
                return new AutomaticDebtDeduction(0, 0);
            AutomaticDebtDeduction requested = Calculate(
                grossDelta, deductionPercent, fractionRemainder);
            int amount = LimitToBalances(requested.Amount, debtRemaining, availableSalary);
            return new AutomaticDebtDeduction(
                amount, amount < requested.Amount ? 0 : requested.FractionRemainder);
        }

        public static AutomaticDebtDeduction Calculate(
            int grossDelta,
            int deductionPercent,
            int fractionRemainder)
        {
            if (grossDelta <= 0 || deductionPercent <= 0)
                return new AutomaticDebtDeduction(0, Math.Max(0, fractionRemainder));
            int numerator = checked(
                grossDelta * Math.Min(100, deductionPercent) +
                Math.Clamp(fractionRemainder, 0, 99));
            return new AutomaticDebtDeduction(numerator / 100, numerator % 100);
        }

        public static int LimitToBalances(
            int requestedAmount,
            int debtRemaining,
            int availableSalary)
        {
            return Math.Min(
                Math.Max(0, requestedAmount),
                Math.Min(
                    Math.Max(0, debtRemaining),
                    Math.Max(0, availableSalary)));
        }
    }

    internal static class EmployeeDebtLedgerCalculator
    {
        public static EmployeeDebtEmployeeSnapshot BuildEmployeeSnapshot(
            EmployeeDebtState state,
            string employeeId,
            BusinessPeriodRange period,
            Func<string, string, string> resolveEmployeeName)
        {
            var related = state.Contracts
                .Where(item =>
                    item.DebtorEmployeeId.Equals(employeeId, StringComparison.OrdinalIgnoreCase) ||
                    item.CreditorEmployeeId.Equals(employeeId, StringComparison.OrdinalIgnoreCase))
                .Where(item => item.CreatedAt < period.EndExclusive)
                .ToList();
            var result = new EmployeeDebtEmployeeSnapshot
            {
                EmployeeId = employeeId,
                MonthKey = period.Key
            };

            result.OpeningDebtAmount = related
                .Where(item => IsDebtor(item, employeeId))
                .Sum(item => RemainingAt(state, item.Id, period.StartInclusive));
            result.ClosingDebtAmount = related
                .Where(item => IsDebtor(item, employeeId))
                .Sum(item => RemainingAt(state, item.Id, period.EndExclusive));
            result.OpeningCreditAmount = related
                .Where(item => IsCreditor(item, employeeId))
                .Sum(item => CreditAt(state, item.Id, period.StartInclusive));
            result.ClosingCreditAmount = related
                .Where(item => IsCreditor(item, employeeId))
                .Sum(item => CreditAt(state, item.Id, period.EndExclusive));

            var periodEvents = state.Events
                .Where(item => item.CreatedAt >= period.StartInclusive &&
                               item.CreatedAt < period.EndExclusive)
                .Where(item => related.Any(contract => contract.Id.Equals(
                    item.ContractId,
                    StringComparison.OrdinalIgnoreCase)))
                .OrderBy(item => item.CreatedAt)
                .ToList();
            var debtorContractIds = related
                .Where(item => IsDebtor(item, employeeId))
                .Select(item => item.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var creditorContractIds = related
                .Where(item => IsCreditor(item, employeeId))
                .Select(item => item.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            result.AddedDebtAmount = periodEvents
                .Where(item => debtorContractIds.Contains(item.ContractId) &&
                               (item.Kind == EmployeeDebtEventKind.Created ||
                                item.Kind == EmployeeDebtEventKind.Increased))
                .Sum(item => item.Amount);
            result.RepaidDebtAmount = periodEvents
                .Where(item => debtorContractIds.Contains(item.ContractId) &&
                               EmployeeDebtService.IsRepayment(item.Kind))
                .Sum(item => item.Amount);
            result.ForgivenDebtAmount = periodEvents
                .Where(item => debtorContractIds.Contains(item.ContractId) &&
                               item.Kind == EmployeeDebtEventKind.Forgiven)
                .Sum(item => item.Amount);
            result.ReceivedCreditAmount = periodEvents
                .Where(item => creditorContractIds.Contains(item.ContractId) &&
                               EmployeeDebtService.IsSalaryFundedRepayment(item.Kind))
                .Sum(item => item.Amount);
            result.WithdrawnCreditAmount = periodEvents
                .Where(item => creditorContractIds.Contains(item.ContractId) &&
                               item.Kind == EmployeeDebtEventKind.CreditWithdrawal)
                .Sum(item => item.Amount);

            var queueHeads = state.Contracts
                .Where(item => RemainingAt(state, item.Id, period.EndExclusive) > 0)
                .GroupBy(item => item.DebtorEmployeeId, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderBy(item => item.CreatedAt).ThenBy(item => item.Id).First().Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (EmployeeDebtContract contract in related.OrderByDescending(item => item.CreatedAt))
            {
                int remaining = RemainingAt(state, contract.Id, period.EndExclusive);
                int principal = PrincipalAt(state, contract.Id, period.EndExclusive);
                int repaid = RepaidAt(state, contract.Id, period.EndExclusive);
                int forgiven = ForgivenAt(state, contract.Id, period.EndExclusive);
                EmployeeDebtStatus status = StatusAt(state, contract, period.EndExclusive, remaining);
                int percent = PercentAt(state, contract, period.EndExclusive);
                var events = state.Events
                    .Where(item => item.ContractId.Equals(contract.Id, StringComparison.OrdinalIgnoreCase) &&
                                   item.CreatedAt < period.EndExclusive)
                    .OrderByDescending(item => item.CreatedAt)
                    .Take(300)
                    .ToList();
                result.Contracts.Add(new EmployeeDebtContractSnapshot
                {
                    Id = contract.Id,
                    DebtorEmployeeId = contract.DebtorEmployeeId,
                    DebtorName = resolveEmployeeName(
                        contract.DebtorEmployeeId,
                        contract.DebtorNameSnapshot),
                    CreditorEmployeeId = contract.CreditorEmployeeId,
                    CreditorType = contract.CreditorType,
                    FundingMethod = contract.FundingMethod,
                    CreditorName = resolveEmployeeName(
                        contract.CreditorEmployeeId,
                        contract.CreditorNameSnapshot),
                    CreatedAt = contract.CreatedAt,
                    PrincipalAmount = principal,
                    RepaidAmount = repaid,
                    ForgivenAmount = forgiven,
                    RemainingAmount = remaining,
                    DeductionPercent = percent,
                    Status = status,
                    IsAutomaticQueueHead = queueHeads.Contains(contract.Id),
                    HasFreezeRequest = contract.FreezeRequestedAt.HasValue &&
                                       contract.FreezeRequestedAt.Value < period.EndExclusive,
                    FreezeRequestId = contract.FreezeRequestId,
                    FreezeRequestedAt = contract.FreezeRequestedAt,
                    FreezeRequestedDurationDays = contract.FreezeRequestedDurationDays,
                    FreezeRejectedAt = contract.FreezeRejectedAt,
                    FreezeRequestAvailableAt = contract.FreezeRequestAvailableAt,
                    PauseStartedAt = contract.PauseStartedAt,
                    PauseUntil = contract.PauseUntil,
                    Revision = contract.Revision,
                    Events = events
                });
            }

            result.HasPausedDebt = result.Contracts.Any(item =>
                item.DebtorEmployeeId.Equals(employeeId, StringComparison.OrdinalIgnoreCase) &&
                item.RemainingAmount > 0 &&
                item.Status == EmployeeDebtStatus.Paused);
            result.Days = periodEvents
                .GroupBy(item => GetDisplayBusinessDate(item))
                .OrderByDescending(group => group.Key)
                .Select(group => new EmployeeDebtDaySnapshot
                {
                    BusinessDate = group.Key,
                    RepaidAmount = group
                        .Where(item => debtorContractIds.Contains(item.ContractId) &&
                                       EmployeeDebtService.IsRepayment(item.Kind))
                        .Sum(item => item.Amount),
                    ReceivedAmount = group
                        .Where(item => creditorContractIds.Contains(item.ContractId) &&
                                       EmployeeDebtService.IsSalaryFundedRepayment(item.Kind))
                        .Sum(item => item.Amount),
                    WithdrawnAmount = group
                        .Where(item => creditorContractIds.Contains(item.ContractId) &&
                                       item.Kind == EmployeeDebtEventKind.CreditWithdrawal)
                        .Sum(item => item.Amount),
                    ForgivenAmount = group
                        .Where(item => debtorContractIds.Contains(item.ContractId) &&
                                       item.Kind == EmployeeDebtEventKind.Forgiven)
                        .Sum(item => item.Amount),
                    Events = group.OrderByDescending(item => item.CreatedAt).ToList()
                })
                .ToList();
            return result;
        }

        internal static int RemainingAt(
            EmployeeDebtState state,
            string contractId,
            DateTime beforeExclusive)
        {
            return Math.Max(
                0,
                PrincipalAt(state, contractId, beforeExclusive) -
                RepaidAt(state, contractId, beforeExclusive) -
                ForgivenAt(state, contractId, beforeExclusive));
        }

        internal static int CreditAt(
            EmployeeDebtState state,
            string contractId,
            DateTime beforeExclusive)
        {
            if (state.Contracts.FirstOrDefault(item => item.Id == contractId)?.CreditorType == EmployeeDebtCreditorType.Club)
                return 0;
            int received = state.Events
                .Where(item => item.ContractId.Equals(contractId, StringComparison.OrdinalIgnoreCase) &&
                               item.CreatedAt < beforeExclusive &&
                               EmployeeDebtService.IsSalaryFundedRepayment(item.Kind))
                .Sum(item => item.Amount);
            int withdrawn = state.Events
                .Where(item => item.ContractId.Equals(contractId, StringComparison.OrdinalIgnoreCase) &&
                               item.CreatedAt < beforeExclusive &&
                               item.Kind == EmployeeDebtEventKind.CreditWithdrawal)
                .Sum(item => item.Amount);
            return Math.Max(0, received - withdrawn);
        }

        private static int PrincipalAt(EmployeeDebtState state, string contractId, DateTime before)
        {
            return state.Events
                .Where(item => item.ContractId.Equals(contractId, StringComparison.OrdinalIgnoreCase) &&
                               item.CreatedAt < before &&
                               (item.Kind == EmployeeDebtEventKind.Created ||
                                item.Kind == EmployeeDebtEventKind.Increased))
                .Sum(item => item.Amount);
        }

        private static int RepaidAt(EmployeeDebtState state, string contractId, DateTime before)
        {
            return state.Events
                .Where(item => item.ContractId.Equals(contractId, StringComparison.OrdinalIgnoreCase) &&
                               item.CreatedAt < before &&
                               EmployeeDebtService.IsRepayment(item.Kind))
                .Sum(item => item.Amount);
        }

        private static int ForgivenAt(EmployeeDebtState state, string contractId, DateTime before)
        {
            return state.Events
                .Where(item => item.ContractId.Equals(contractId, StringComparison.OrdinalIgnoreCase) &&
                               item.CreatedAt < before &&
                               item.Kind == EmployeeDebtEventKind.Forgiven)
                .Sum(item => item.Amount);
        }

        private static EmployeeDebtStatus StatusAt(
            EmployeeDebtState state,
            EmployeeDebtContract contract,
            DateTime before,
            int remaining)
        {
            if (remaining <= 0)
                return EmployeeDebtStatus.Closed;
            EmployeeDebtEvent? last = state.Events
                .Where(item => item.ContractId.Equals(contract.Id, StringComparison.OrdinalIgnoreCase) &&
                               item.CreatedAt < before &&
                                (item.Kind == EmployeeDebtEventKind.FreezeApproved ||
                                 item.Kind == EmployeeDebtEventKind.PausedByOwner ||
                                 item.Kind == EmployeeDebtEventKind.Resumed ||
                                 item.Kind == EmployeeDebtEventKind.FreezeExpired))
                .OrderByDescending(item => item.CreatedAt)
                .ThenByDescending(item => item.Id)
                .FirstOrDefault();
            return last?.Kind == EmployeeDebtEventKind.FreezeApproved ||
                   last?.Kind == EmployeeDebtEventKind.PausedByOwner
                ? EmployeeDebtStatus.Paused
                : EmployeeDebtStatus.Active;
        }

        private static int PercentAt(
            EmployeeDebtState state,
            EmployeeDebtContract contract,
            DateTime before)
        {
            EmployeeDebtEvent? last = state.Events
                .Where(item => item.ContractId.Equals(contract.Id, StringComparison.OrdinalIgnoreCase) &&
                               item.CreatedAt < before &&
                               (item.Kind == EmployeeDebtEventKind.Created ||
                                item.Kind == EmployeeDebtEventKind.RateChanged))
                .OrderByDescending(item => item.CreatedAt)
                .ThenByDescending(item => item.Id)
                .FirstOrDefault();
            return last?.NewPercent > 0 ? last.NewPercent : contract.DeductionPercent;
        }

        private static DateTime GetDisplayBusinessDate(EmployeeDebtEvent item)
        {
            return item.SourceBusinessDate?.Date ??
                   BusinessCalendarService.GetBusinessDate(item.CreatedAt).Date;
        }

        private static bool IsDebtor(EmployeeDebtContract contract, string employeeId)
        {
            return contract.DebtorEmployeeId.Equals(employeeId, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsCreditor(EmployeeDebtContract contract, string employeeId)
        {
            return contract.CreditorType == EmployeeDebtCreditorType.Employee &&
                contract.CreditorEmployeeId.Equals(employeeId, StringComparison.OrdinalIgnoreCase);
        }
    }
}
