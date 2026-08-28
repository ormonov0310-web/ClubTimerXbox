using System;
using System.Windows.Threading;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public static class CashAcceptancePostingService
    {
        private static DispatcherTimer? _timer;

        public static void Start()
        {
            ShiftAcceptanceService.ScheduleProvisionalCashFinalization();
            FinalizeDue();

            if (_timer != null)
                return;

            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(10)
            };
            _timer.Tick += (_, _) => FinalizeDue();
            _timer.Start();
        }

        public static CashAcceptanceItem PostFinalAcceptance(
            string checkedByEmployeeName,
            string responsibleEmployeeName,
            int expectedCashAmount,
            int actualCashAmount,
            string note,
            string acceptanceKey,
            DateTime occurredAt)
        {
            PostLedger(
                checkedByEmployeeName,
                responsibleEmployeeName,
                expectedCashAmount,
                actualCashAmount,
                note,
                acceptanceKey,
                occurredAt);

            return CashAcceptanceService.AddItem(
                checkedByEmployeeName,
                responsibleEmployeeName,
                expectedCashAmount,
                actualCashAmount,
                note,
                acceptanceKey);
        }

        public static void FinalizeDue()
        {
            DateTime now = ClubClock.Current.LocalNow;
            bool finalizedAny = false;
            foreach (var item in CashAcceptanceService.GetDueProvisional(now))
            {
                try
                {
                    DateTime occurredAt = item.UpdatedAt == default
                        ? item.CreatedAt
                        : item.UpdatedAt;
                    string operationKey = string.IsNullOrWhiteSpace(item.RootAcceptanceKey)
                        ? item.AcceptanceKey.Trim()
                        : item.RootAcceptanceKey.Trim();

                    PostLedger(
                        item.CheckedByEmployeeName,
                        item.ResponsibleEmployeeName,
                        item.ExpectedCashAmount,
                        item.ActualCashAmount,
                        item.Note,
                        operationKey,
                        occurredAt);
                    CashAcceptanceService.MarkFinalized(item.Id, now);
                    finalizedAny = true;
                }
                catch
                {
                    // Тот же operationId безопасно завершит запись при следующей попытке.
                }
            }

            if (finalizedAny)
                _ = FirebaseSyncService.PushCurrentStateAsync();
        }

        private static void PostLedger(
            string checkedByEmployeeName,
            string responsibleEmployeeName,
            int expectedCashAmount,
            int actualCashAmount,
            string note,
            string acceptanceKey,
            DateTime occurredAt)
        {
            var month = BusinessCalendarService.GetBusinessMonth(occurredAt);
            CashReconciliationService.ProcessCashAcceptance(
                month.StartInclusive,
                month.EndExclusive,
                checkedByEmployeeName,
                responsibleEmployeeName,
                expectedCashAmount,
                actualCashAmount,
                note,
                operationId: $"{acceptanceKey.Trim()}:ledger",
                occurredAt: occurredAt);
        }
    }
}
