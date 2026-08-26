using System;
using System.Collections.Generic;
using System.Linq;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public static class ActionLogService
    {
        private static readonly TimeSpan SameEmployeeRequiredAcceptanceGap =
            TimeSpan.FromHours(2);

        // Старый простой журнал.
        // Пока оставляем для совместимости.
        public static List<ActionLogItem> Items { get; private set; } = new List<ActionLogItem>();

        // Умный журнал смен.
        public static List<ShiftLogItem> Shifts { get; private set; }

        // Умный журнал игровых сессий.
        public static List<GameSessionLogItem> GameSessions { get; private set; }

        static ActionLogService()
        {
            var data = LogStorageService.Load();

            Shifts = data.Shifts ?? new List<ShiftLogItem>();
            GameSessions = data.GameSessions ?? new List<GameSessionLogItem>();

            bool changed = false;
            foreach (var session in GameSessions.Where(item => !item.IsClosed))
            {
                foreach (var line in session.SaleLines)
                {
                    changed |= SessionSaleSettlementService.NormalizeActiveUnpaidLine(line);
                }
            }

            if (changed)
                SaveLogs();
        }

        public static ShiftLogItem? CurrentShift
        {
            get
            {
                return Shifts.LastOrDefault(shift => !shift.IsClosed);
            }
        }

        private static void SaveLogs()
        {
            LogStorageService.Save(Shifts, GameSessions);
        }

        public static int RenameEmployeeReferences(
            string oldEmployeeName,
            string newEmployeeName)
        {
            int changed = 0;
            bool persistedDataChanged = false;

            foreach (var item in Items)
            {
                bool itemChanged = false;

                if (EmployeeReferenceRenameService.Matches(item.EmployeeName, oldEmployeeName))
                {
                    item.EmployeeName = newEmployeeName;
                    itemChanged = true;
                }

                if (EmployeeReferenceRenameService.Matches(item.IncomeEmployeeName, oldEmployeeName))
                {
                    item.IncomeEmployeeName = newEmployeeName;
                    itemChanged = true;
                }

                if (!itemChanged)
                    continue;

                item.Description = EmployeeReferenceRenameService.RenameText(
                    item.Description,
                    oldEmployeeName,
                    newEmployeeName);
                changed++;
            }

            foreach (var shift in Shifts)
            {
                if (!EmployeeReferenceRenameService.Matches(shift.EmployeeName, oldEmployeeName))
                    continue;

                shift.EmployeeName = newEmployeeName;
                persistedDataChanged = true;
                changed++;
            }

            foreach (var session in GameSessions)
            {
                bool sessionChanged = false;

                if (EmployeeReferenceRenameService.Matches(session.StartedByEmployeeName, oldEmployeeName))
                {
                    session.StartedByEmployeeName = newEmployeeName;
                    sessionChanged = true;
                }

                if (EmployeeReferenceRenameService.Matches(session.ClosedByEmployeeName, oldEmployeeName))
                {
                    session.ClosedByEmployeeName = newEmployeeName;
                    sessionChanged = true;
                }

                if (EmployeeReferenceRenameService.Matches(session.IncomeEmployeeName, oldEmployeeName))
                {
                    session.IncomeEmployeeName = newEmployeeName;
                    sessionChanged = true;
                }

                foreach (var extra in session.ExtraLines)
                {
                    if (!EmployeeReferenceRenameService.Matches(extra.EmployeeName, oldEmployeeName))
                        continue;

                    extra.EmployeeName = newEmployeeName;
                    extra.Description = EmployeeReferenceRenameService.RenameText(
                        extra.Description,
                        oldEmployeeName,
                        newEmployeeName);
                    sessionChanged = true;
                }

                foreach (var sale in session.SaleLines)
                {
                    if (EmployeeReferenceRenameService.Matches(sale.EmployeeName, oldEmployeeName))
                    {
                        sale.EmployeeName = newEmployeeName;
                        sessionChanged = true;
                    }

                    if (EmployeeReferenceRenameService.Matches(sale.CreatedByEmployeeName, oldEmployeeName))
                    {
                        sale.CreatedByEmployeeName = newEmployeeName;
                        sessionChanged = true;
                    }

                    if (EmployeeReferenceRenameService.Matches(sale.PaidByEmployeeName, oldEmployeeName))
                    {
                        sale.PaidByEmployeeName = newEmployeeName;
                        sessionChanged = true;
                    }

                    if (EmployeeReferenceRenameService.Matches(sale.DebtResponsibleEmployeeName, oldEmployeeName))
                    {
                        sale.DebtResponsibleEmployeeName = newEmployeeName;
                        sessionChanged = true;
                    }
                }

                foreach (var item in session.DeferredCheckoutItems ?? new List<CheckoutItem>())
                {
                    if (EmployeeReferenceRenameService.Matches(
                            item.CreatedByEmployeeName,
                            oldEmployeeName))
                    {
                        item.CreatedByEmployeeName = newEmployeeName;
                        sessionChanged = true;
                    }

                    if (EmployeeReferenceRenameService.Matches(
                            item.DebtResponsibleEmployeeName,
                            oldEmployeeName))
                    {
                        item.DebtResponsibleEmployeeName = newEmployeeName;
                        sessionChanged = true;
                    }
                }

                if (!sessionChanged)
                    continue;

                persistedDataChanged = true;
                changed++;
            }

            if (persistedDataChanged)
                SaveLogs();

            return changed;
        }

        // ------------------------------------------------------------
        // СТАРЫЙ ЖУРНАЛ
        // ------------------------------------------------------------

        public static void Add(
            string employeeName,
            string actionType,
            string placeName,
            string description,
            int amount = 0,
            string incomeEmployeeName = "")
        {
            Items.Add(new ActionLogItem
            {
                CreatedAt = ClubClock.Current.LocalNow,
                EmployeeName = employeeName,
                ActionType = actionType,
                PlaceName = placeName,
                Description = description,
                Amount = amount,
                IncomeEmployeeName = incomeEmployeeName
            });
        }

        public static List<ActionLogItem> GetAll()
        {
            return Items;
        }

        public static void Clear()
        {
            Items.Clear();
            Shifts.Clear();
            GameSessions.Clear();

            ShiftAcceptanceService.Reset();

            SaveLogs();
        }

        // ------------------------------------------------------------
        // УМНЫЙ ЖУРНАЛ СМЕН
        // ------------------------------------------------------------

        public static ShiftLogItem StartShift(string employeeName)
        {
            string cleanEmployeeName = employeeName.Trim();
            var currentShift = CurrentShift;

            if (currentShift != null &&
                currentShift.EmployeeName.Trim().Equals(cleanEmployeeName, StringComparison.OrdinalIgnoreCase))
            {
                EnsureAcceptanceForCurrentShift();
                return currentShift;
            }

            var previousShift = currentShift ?? GetLastClosedShift();

            CloseCurrentShift();

            var shift = new ShiftLogItem
            {
                EmployeeName = cleanEmployeeName,
                StartedAt = ClubClock.Current.LocalNow,
                ClosedAt = null,
                IsClosed = false
            };

            Shifts.Add(shift);

            Add(
                employeeName: cleanEmployeeName,
                actionType: "Смена открыта",
                placeName: "",
                description: $"Смена сотрудника {cleanEmployeeName} открыта."
            );

            StartAcceptanceIfNeeded(
                newEmployeeName: cleanEmployeeName,
                responsibleShift: previousShift,
                newShift: shift
            );

            SaveLogs();
            LateOpeningPenaltyService.EvaluateOpenedShift(shift);

            return shift;
        }

        public static void CloseCurrentShift()
        {
            CloseCurrentShiftAt(ClubClock.Current.LocalNow);
        }

        public static void CloseCurrentShiftAt(DateTime closeTime)
        {
            var currentShift = CurrentShift;

            if (currentShift == null)
                return;

            if (closeTime < currentShift.StartedAt)
                closeTime = currentShift.StartedAt;

            currentShift.ClosedAt = closeTime;
            currentShift.IsClosed = true;

            Add(
                employeeName: currentShift.EmployeeName,
                actionType: "Смена закрыта",
                placeName: "",
                description:
                    $"Смена сотрудника {currentShift.EmployeeName} закрыта. " +
                    $"Начало: {currentShift.StartedAt:dd.MM.yyyy HH:mm}. " +
                    $"Конец: {currentShift.ClosedAt:dd.MM.yyyy HH:mm}."
            );

            SaveLogs();
        }

        public static ShiftLogItem SwitchShift(string newEmployeeName)
        {
            newEmployeeName = newEmployeeName.Trim();
            var oldShift = CurrentShift;

            if (oldShift != null &&
                oldShift.EmployeeName.Trim().Equals(newEmployeeName, StringComparison.OrdinalIgnoreCase))
            {
                EnsureAcceptanceForCurrentShift();
                return oldShift;
            }

            var responsibleShift = oldShift ?? GetLastClosedShift();
            string oldEmployeeName = responsibleShift?.EmployeeName ?? "Неизвестно";

            CloseCurrentShift();

            var newShift = new ShiftLogItem
            {
                EmployeeName = newEmployeeName.Trim(),
                StartedAt = ClubClock.Current.LocalNow,
                ClosedAt = null,
                IsClosed = false
            };

            Shifts.Add(newShift);

            Add(
                employeeName: newEmployeeName,
                actionType: "Смена сотрудника",
                placeName: "",
                description:
                    $"Смена переключена: {oldEmployeeName} → {newEmployeeName}. " +
                    "Активные места не сброшены, таймеры продолжают работать."
            );

            if (responsibleShift != null &&
                !string.IsNullOrWhiteSpace(oldEmployeeName) &&
                !oldEmployeeName.Equals("Неизвестно", StringComparison.OrdinalIgnoreCase) &&
                !oldEmployeeName.Equals(newEmployeeName, StringComparison.OrdinalIgnoreCase))
            {
                ShiftAcceptanceService.StartRequiredAcceptance(
                    newEmployeeName: newEmployeeName.Trim(),
                    responsibleEmployeeName: oldEmployeeName,
                    acceptanceKey: BuildAcceptanceKey(responsibleShift.Id, newShift.Id)
                );
            }

            SaveLogs();
            LateOpeningPenaltyService.EvaluateOpenedShift(newShift);

            return newShift;
        }

        private static ShiftLogItem? GetLastClosedShift()
        {
            return Shifts
                .Where(shift => shift.IsClosed && shift.ClosedAt != null)
                .OrderByDescending(shift => shift.ClosedAt)
                .FirstOrDefault();
        }

        private static void StartAcceptanceIfNeeded(
            string newEmployeeName,
            ShiftLogItem? responsibleShift,
            ShiftLogItem newShift)
        {
            if (responsibleShift == null)
                return;

            string responsibleEmployeeName = responsibleShift.EmployeeName;

            if (string.IsNullOrWhiteSpace(responsibleEmployeeName))
                return;

            if (responsibleEmployeeName.Equals(newEmployeeName, StringComparison.OrdinalIgnoreCase))
            {
                CancelStaleAcceptanceIfNeeded(
                    newEmployeeName,
                    "Предыдущая незавершённая приёмка отменена: тот же сотрудник снова открыл смену."
                );

                string acceptanceKey = BuildAcceptanceKey(responsibleShift.Id, newShift.Id);

                if (ShouldRequireSameEmployeeAcceptance(responsibleShift, newShift))
                {
                    ShiftAcceptanceService.StartRequiredAcceptance(
                        newEmployeeName: newEmployeeName,
                        responsibleEmployeeName: newEmployeeName,
                        acceptanceKey: acceptanceKey
                    );
                }
                else
                {
                    ShiftAcceptanceService.AllowManualSelfAcceptanceAfterReentry(
                        newEmployeeName,
                        acceptanceKey
                    );
                }

                return;
            }

            if (ShiftAcceptanceService.IsAcceptanceRequired())
            {
                if (ShiftAcceptanceService.IsPendingForEmployee(newEmployeeName))
                    return;

                CancelStaleAcceptanceIfNeeded(
                    newEmployeeName,
                    "Предыдущая незавершённая приёмка отменена: открыта новая актуальная смена."
                );
            }

            ShiftAcceptanceService.StartRequiredAcceptance(
                newEmployeeName: newEmployeeName,
                responsibleEmployeeName: responsibleEmployeeName,
                acceptanceKey: BuildAcceptanceKey(responsibleShift.Id, newShift.Id)
            );
        }

        private static string BuildAcceptanceKey(Guid responsibleShiftId, Guid newShiftId)
        {
            return $"{responsibleShiftId:N}->{newShiftId:N}";
        }

        private static bool ShouldRequireSameEmployeeAcceptance(
            ShiftLogItem responsibleShift,
            ShiftLogItem newShift)
        {
            if (responsibleShift.ClosedAt == null)
                return false;

            return newShift.StartedAt - responsibleShift.ClosedAt.Value >=
                SameEmployeeRequiredAcceptanceGap;
        }

        public static void EnsureAcceptanceForCurrentShift()
        {
            var currentShift = CurrentShift;

            if (currentShift == null)
                return;

            string newEmployeeName = currentShift.EmployeeName.Trim();

            if (string.IsNullOrWhiteSpace(newEmployeeName))
                return;

            if (ShiftAcceptanceService.IsAcceptanceRequired())
            {
                if (ShiftAcceptanceService.IsPendingForEmployee(newEmployeeName))
                    return;

                if (ShiftAcceptanceService.IsResponsibleEmployee(newEmployeeName))
                {
                    CancelStaleAcceptanceIfNeeded(
                        newEmployeeName,
                        "Предыдущая незавершённая приёмка отменена: ответственный сотрудник снова вошёл сам."
                    );

                    ShiftAcceptanceService.AllowManualSelfAcceptanceAfterReentry(
                        newEmployeeName,
                        $"manual-self:{ClubClock.Current.LocalNow:yyyyMMddHHmmss}:{Guid.NewGuid():N}"
                    );

                    return;
                }

                CancelStaleAcceptanceIfNeeded(
                    newEmployeeName,
                    "Предыдущая незавершённая приёмка отменена: она относится к другой смене."
                );
            }

            var responsibleShift = Shifts
                .Where(shift =>
                    shift.Id != currentShift.Id &&
                    shift.IsClosed &&
                    shift.ClosedAt != null &&
                    shift.EmployeeName.Trim().Length > 0)
                .OrderByDescending(shift => shift.ClosedAt)
                .FirstOrDefault();

            if (responsibleShift == null)
                return;

            if (responsibleShift.EmployeeName.Trim().Equals(
                    newEmployeeName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            string acceptanceKey = BuildAcceptanceKey(responsibleShift.Id, currentShift.Id);

            if (ShiftAcceptanceService.Current.AcceptanceKey.Equals(
                    acceptanceKey,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (ShiftAcceptanceService.IsAcceptanceRequired() &&
                ShiftAcceptanceService.Current.NewEmployeeName.Trim().Equals(
                    newEmployeeName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            ShiftAcceptanceService.StartRequiredAcceptance(
                newEmployeeName: newEmployeeName,
                responsibleEmployeeName: responsibleShift.EmployeeName,
                acceptanceKey: acceptanceKey
            );
        }

        private static void CancelStaleAcceptanceIfNeeded(string employeeName, string reason)
        {
            if (!ShiftAcceptanceService.IsAcceptanceRequired())
                return;

            var state = ShiftAcceptanceService.Current;

            Add(
                employeeName: employeeName,
                actionType: "Приёмка отменена",
                placeName: "",
                description:
                    $"{reason} " +
                    $"Было: {state.ResponsibleEmployeeName} → {state.NewEmployeeName}."
            );

            ShiftAcceptanceService.CancelPendingAcceptance();
        }

        public static List<ShiftLogItem> GetAllShifts()
        {
            return Shifts;
        }

        // ------------------------------------------------------------
        // УМНЫЙ ЖУРНАЛ ИГРОВЫХ СЕССИЙ
        // ------------------------------------------------------------

        public static GameSessionLogItem StartGameSession(
            string placeName,
            string employeeName,
            bool isOpenMode,
            string tariffText,
            int paidAmount)
        {
            var session = new GameSessionLogItem
            {
                PlaceName = placeName,
                StartedByEmployeeName = employeeName,
                StartedAt = ClubClock.Current.LocalNow,
                IsOpenMode = isOpenMode,
                TariffText = tariffText,
                PaidAmount = paidAmount,
                IsClosed = false
            };

            GameSessions.Add(session);

            SaveLogs();

            return session;
        }

        public static GameSessionLogItem? GetActiveGameSessionByPlace(string placeName)
        {
            return GameSessions.LastOrDefault(session =>
                !session.IsClosed &&
                session.PlaceName == placeName
            );
        }

        public static void AddExtraToActiveSession(
            string placeName,
            string type,
            string employeeName,
            string description,
            int amount = 0)
        {
            var session = GetActiveGameSessionByPlace(placeName);

            if (session == null)
                return;

            session.ExtraLines.Add(new GameSessionExtraLine
            {
                CreatedAt = ClubClock.Current.LocalNow,
                Type = type,
                EmployeeName = employeeName,
                Description = description,
                Amount = amount
            });

            SaveLogs();
        }

        public static void AddSaleToActiveSession(
            string placeName,
            string employeeName,
            SaleItem item,
            int quantity)
        {
            var session = GetActiveGameSessionByPlace(placeName);

            if (session == null)
                return;

            if (quantity <= 0)
                return;

            int totalAmount = item.SalePrice * quantity;
            var employee = EmployeeService.FindByName(employeeName);

            session.SaleLines.Add(new GameSessionSaleLine
            {
                CreatedAt = ClubClock.Current.LocalNow,
                EmployeeName = employeeName,
                SettlementSchemaVersion = SessionSaleSettlementService.CurrentSchemaVersion,
                CreatedByEmployeeId = employee?.EmployeeId ?? "",
                CreatedByEmployeeName = employeeName,
                CreatedShiftId = CurrentShift?.Id,
                ItemName = item.Name,
                ItemType = item.Type,
                UnitPrice = item.SalePrice,
                PurchasePrice = item.PurchasePrice,
                Quantity = quantity,
                TotalAmount = totalAmount,
                IsPaid = false
            });

            SaveLogs();
        }

        public static int GetActiveSessionProductsAndServicesTotal(string placeName)
        {
            var session = GetActiveGameSessionByPlace(placeName);

            if (session == null)
                return 0;

            return session.SaleLines
                .Where(line => !line.IsPaid)
                .Sum(line => line.TotalAmount);
        }

        public static int GetActiveSessionDeferredCheckoutTotal(string placeName)
        {
            var session = GetActiveGameSessionByPlace(placeName);

            if (session == null)
                return 0;

            session.DeferredCheckoutItems ??= new List<CheckoutItem>();

            return session.DeferredCheckoutItems.Sum(item => item.TotalAmount);
        }

        public static List<CheckoutItem> GetActiveSessionDeferredCheckoutItems(string placeName)
        {
            var session = GetActiveGameSessionByPlace(placeName);

            if (session == null)
                return new List<CheckoutItem>();

            session.DeferredCheckoutItems ??= new List<CheckoutItem>();

            return session.DeferredCheckoutItems
                .Select(CloneCheckoutItem)
                .ToList();
        }

        public static void AddDeferredCheckoutItemsToActiveSession(
            string targetPlaceName,
            string sourcePlaceName,
            string employeeName,
            List<CheckoutItem> items)
        {
            var session = GetActiveGameSessionByPlace(targetPlaceName);

            if (session == null || items == null || items.Count == 0)
                return;

            session.DeferredCheckoutItems ??= new List<CheckoutItem>();

            foreach (var item in items)
            {
                if (item.TotalAmount <= 0)
                    continue;

                var copy = CloneCheckoutItem(item);
                copy.Name = $"{copy.Name} ({sourcePlaceName})";
                session.DeferredCheckoutItems.Add(copy);
            }

            session.ExtraLines.Add(new GameSessionExtraLine
            {
                CreatedAt = ClubClock.Current.LocalNow,
                Type = "Перенос оплаты",
                EmployeeName = employeeName,
                Description =
                    $"На {targetPlaceName} перенесены позиции к оплате с {sourcePlaceName}. " +
                    $"Сумма: {items.Sum(item => item.TotalAmount)} сом.",
                Amount = items.Sum(item => item.TotalAmount)
            });

            SaveLogs();
        }

        public static void MarkActiveSessionSalesAsPaid(string placeName)
        {
            var session = GetActiveGameSessionByPlace(placeName);

            if (session == null)
                return;

            // Старый вход оставлен только для двоичной совместимости.
            // Новые операции обязаны передавать ID платежа через MarkCheckoutItemsPaid.
        }

        public static int MarkCheckoutItemsPaid(
            IEnumerable<CheckoutItem> checkoutItems,
            PaymentRecord payment,
            string paidByEmployeeName)
        {
            if (checkoutItems == null || payment == null)
                return 0;

            var lineIds = checkoutItems
                .Where(item => item.SourceSaleLineId.HasValue)
                .Select(item => item.SourceSaleLineId!.Value)
                .Distinct()
                .ToHashSet();

            if (lineIds.Count == 0)
                return 0;

            var employee = EmployeeService.FindByName(paidByEmployeeName);
            Guid? shiftId = CurrentShift?.Id;
            int changed = 0;

            foreach (var line in GameSessions.SelectMany(item => item.SaleLines))
            {
                if (!lineIds.Contains(line.Id))
                    continue;

                if (SessionSaleSettlementService.IsFinanciallyPaid(line) &&
                    line.PaymentRecordId == payment.Id)
                {
                    continue;
                }

                if (line.IsPaid && line.PaymentRecordId != payment.Id)
                    continue;

                SessionSaleSettlementService.MarkPaid(
                    line,
                    payment.Id,
                    payment.CreatedAt,
                    employee?.EmployeeId ?? "",
                    paidByEmployeeName,
                    shiftId);
                changed++;
            }

            if (changed > 0)
                SaveLogs();

            return changed;
        }

        public static bool TryMarkGameIncomePosted(
            Guid? gameSessionId,
            string incomeEmployeeName,
            DateTime postedAt)
        {
            if (!gameSessionId.HasValue)
                return false;

            var session = GameSessions.FirstOrDefault(item => item.Id == gameSessionId.Value);

            if (session == null || session.IsGameIncomePosted)
                return false;

            session.IsGameIncomePosted = true;
            session.GameIncomePostedAt = postedAt;
            session.GameIncomeEmployeeName = incomeEmployeeName;
            SaveLogs();
            return true;
        }

        public static GameSessionLogItem? GetGameSession(Guid? gameSessionId)
        {
            if (!gameSessionId.HasValue)
                return null;

            return GameSessions.FirstOrDefault(item => item.Id == gameSessionId.Value);
        }

        public static IReadOnlyList<OutstandingCustomerDebtItem> GetOutstandingCustomerDebts()
        {
            var activeDeferredSaleLineIds = GameSessions
                .Where(session => !session.IsClosed)
                .SelectMany(session => session.DeferredCheckoutItems ?? new List<CheckoutItem>())
                .Where(item => item.SourceSaleLineId.HasValue)
                .Select(item => item.SourceSaleLineId!.Value)
                .ToHashSet();
            var result = new List<OutstandingCustomerDebtItem>();

            foreach (var session in GameSessions)
            {
                foreach (var line in session.SaleLines.Where(line => !line.IsPaid))
                {
                    if (activeDeferredSaleLineIds.Contains(line.Id))
                        continue;

                    result.Add(new OutstandingCustomerDebtItem
                    {
                        SessionId = session.Id,
                        SaleLineId = line.Id,
                        PlaceName = session.PlaceName,
                        ItemName = line.ItemName,
                        Quantity = line.Quantity,
                        Amount = line.TotalAmount,
                        CreatedAt = line.CreatedAt,
                        CreatedByEmployeeName =
                            SessionSaleSettlementService.GetCreatedByEmployeeName(line),
                        ResponsibleEmployeeName = line.DebtResponsibleEmployeeName
                    });
                }

                if (session.IsClosed)
                    continue;

                foreach (var item in session.DeferredCheckoutItems ?? new List<CheckoutItem>())
                {
                    result.Add(new OutstandingCustomerDebtItem
                    {
                        SessionId = session.Id,
                        SaleLineId = item.SourceSaleLineId,
                        PlaceName = session.PlaceName,
                        ItemName = item.Name,
                        Quantity = item.Quantity,
                        Amount = item.TotalAmount,
                        CreatedAt = item.SourceCreatedAt ?? session.StartedAt,
                        CreatedByEmployeeName = item.CreatedByEmployeeName,
                        ResponsibleEmployeeName = item.DebtResponsibleEmployeeName
                    });
                }
            }

            return result
                .OrderBy(item => item.CreatedAt)
                .ThenBy(item => item.PlaceName)
                .ToList();
        }

        public static int AcceptOutstandingDebtResponsibility(
            string employeeName,
            Guid? shiftId)
        {
            DateTime acceptedAt = ClubClock.Current.LocalNow;
            int logicalDebtCount = GetOutstandingCustomerDebts().Count;
            int changed = 0;

            foreach (var line in GameSessions
                         .SelectMany(session => session.SaleLines)
                         .Where(line => !line.IsPaid))
            {
                SessionSaleSettlementService.AcceptDebtResponsibility(
                    line,
                    employeeName,
                    shiftId,
                    acceptedAt);
                changed++;
            }

            foreach (var item in GameSessions
                         .Where(session => !session.IsClosed)
                         .SelectMany(session => session.DeferredCheckoutItems ??
                             new List<CheckoutItem>()))
            {
                item.DebtResponsibleEmployeeName = employeeName.Trim();
                item.DebtResponsibleShiftId = shiftId;
                item.DebtAcceptedAt = acceptedAt;
                changed++;
            }

            if (changed > 0)
            {
                Add(
                    employeeName,
                    "Приняты долги клиентов",
                    "",
                    $"Сотрудник принял ответственность за {logicalDebtCount} неоплаченных позиций.");
                SaveLogs();
            }

            return logicalDebtCount;
        }

        public static void MoveActiveGameSession(
            string oldPlaceName,
            string newPlaceName,
            string employeeName,
            string description,
            int amount = 0)
        {
            var session = GetActiveGameSessionByPlace(oldPlaceName);

            if (session == null)
                return;

            session.ExtraLines.Add(new GameSessionExtraLine
            {
                CreatedAt = ClubClock.Current.LocalNow,
                Type = "Пересадка",
                EmployeeName = employeeName,
                Description = description,
                Amount = amount
            });

            session.PlaceName = newPlaceName;

            SaveLogs();
        }

        public static void CloseActiveGameSession(
            string placeName,
            string closedByEmployeeName,
            int actualPlayedAmount,
            int refundAmount,
            int needToPayAmount,
            int cashIncomeAmount,
            string incomeEmployeeName)
        {
            var session = GetActiveGameSessionByPlace(placeName);

            if (session == null)
                return;

            session.DeferredCheckoutItems ??= new List<CheckoutItem>();

            int productsAndServicesAmount = session.SaleLines.Sum(line => line.TotalAmount);
            int deferredAmount = session.DeferredCheckoutItems.Sum(item => item.TotalAmount);

            session.ClosedByEmployeeName = closedByEmployeeName;
            session.ClosedAt = ClubClock.Current.LocalNow;
            session.ActualPlayedAmount = actualPlayedAmount;
            session.RefundAmount = refundAmount;
            session.NeedToPayAmount = needToPayAmount;
            session.CashIncomeAmount = cashIncomeAmount;
            session.ProductsAndServicesAmount = productsAndServicesAmount;
            session.TotalToPayAmount = cashIncomeAmount + productsAndServicesAmount + deferredAmount;
            session.IncomeEmployeeName = incomeEmployeeName;
            session.IsClosed = true;

            SaveLogs();
        }

        private static CheckoutItem CloneCheckoutItem(CheckoutItem item)
        {
            return new CheckoutItem
            {
                Name = item.Name,
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice,
                PurchasePrice = item.PurchasePrice,
                Category = item.Category,
                ItemType = item.ItemType,
                SourceSaleLineId = item.SourceSaleLineId,
                SourceGameSessionId = item.SourceGameSessionId,
                SourcePlaceName = item.SourcePlaceName,
                CreatedByEmployeeName = item.CreatedByEmployeeName,
                SourceCreatedAt = item.SourceCreatedAt,
                DebtResponsibleEmployeeName = item.DebtResponsibleEmployeeName,
                DebtResponsibleShiftId = item.DebtResponsibleShiftId,
                DebtAcceptedAt = item.DebtAcceptedAt
            };
        }

        public static List<GameSessionLogItem> GetAllGameSessions()
        {
            return GameSessions;
        }

        public static List<GameSessionLogItem> GetClosedGameSessions()
        {
            return GameSessions
                .Where(session => session.IsClosed)
                .ToList();
        }

        public static List<GameSessionLogItem> GetActiveGameSessions()
        {
            return GameSessions
                .Where(session => !session.IsClosed)
                .ToList();
        }
    }
}
