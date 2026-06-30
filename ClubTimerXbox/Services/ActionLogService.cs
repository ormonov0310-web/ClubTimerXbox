using System;
using System.Collections.Generic;
using System.Linq;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public static class ActionLogService
    {
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
                CreatedAt = DateTime.Now,
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
                StartedAt = DateTime.Now,
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

            return shift;
        }

        public static void CloseCurrentShift()
        {
            CloseCurrentShiftAt(DateTime.Now);
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
                StartedAt = DateTime.Now,
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
            if (ShiftAcceptanceService.IsAcceptanceRequired())
                return;

            if (responsibleShift == null)
                return;

            string responsibleEmployeeName = responsibleShift.EmployeeName;

            if (string.IsNullOrWhiteSpace(responsibleEmployeeName))
                return;

            if (responsibleEmployeeName.Equals(newEmployeeName, StringComparison.OrdinalIgnoreCase))
                return;

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

        public static void EnsureAcceptanceForCurrentShift()
        {
            if (ShiftAcceptanceService.IsAcceptanceRequired())
                return;

            var currentShift = CurrentShift;

            if (currentShift == null)
                return;

            string newEmployeeName = currentShift.EmployeeName.Trim();

            if (string.IsNullOrWhiteSpace(newEmployeeName))
                return;

            var responsibleShift = Shifts
                .Where(shift =>
                    shift.Id != currentShift.Id &&
                    shift.IsClosed &&
                    shift.ClosedAt != null &&
                    shift.EmployeeName.Trim().Length > 0 &&
                    !shift.EmployeeName.Trim().Equals(newEmployeeName, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(shift => shift.ClosedAt)
                .FirstOrDefault();

            if (responsibleShift == null)
                return;

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
                StartedAt = DateTime.Now,
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
                CreatedAt = DateTime.Now,
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

            session.SaleLines.Add(new GameSessionSaleLine
            {
                CreatedAt = DateTime.Now,
                EmployeeName = employeeName,
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
                CreatedAt = DateTime.Now,
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

            foreach (var line in session.SaleLines)
            {
                line.IsPaid = true;
            }

            session.ProductsAndServicesAmount = session.SaleLines.Sum(line => line.TotalAmount);

            SaveLogs();
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
                CreatedAt = DateTime.Now,
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

            foreach (var line in session.SaleLines)
            {
                line.IsPaid = true;
            }

            session.ClosedByEmployeeName = closedByEmployeeName;
            session.ClosedAt = DateTime.Now;
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
                ItemType = item.ItemType
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
