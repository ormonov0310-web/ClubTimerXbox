using System;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Threading;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public static class CashAcceptancePostingService
    {
        private static readonly object Gate = new();
        private static DispatcherTimer? _timer;

        public static void Start()
        {
            ShiftAcceptanceService.ScheduleProvisionalCashFinalization();
            FinalizeDue();

            if (_timer != null)
                return;

            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
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
            bool finalizedAny = false;
            lock (Gate)
            {
                DateTime now = ClubClock.Current.LocalNow;
                foreach (var item in CashAcceptanceService.GetDueProvisional(now))
                {
                    try
                    {
                        DateTime occurredAt = CashAcceptanceTimelinePolicy
                            .GetObservationTime(item);
                        string operationKey = string.IsNullOrWhiteSpace(item.RootAcceptanceKey)
                            ? item.AcceptanceKey.Trim()
                            : item.RootAcceptanceKey.Trim();
                        if (string.IsNullOrWhiteSpace(operationKey))
                            operationKey = item.Id.ToString("N");
                        Guid investigationId = BuildInvestigationId(operationKey);
                        bool hasPendingCashless = item.PendingCashlessVerification != null;

                        PostLedger(
                            item.CheckedByEmployeeName,
                            item.ResponsibleEmployeeName,
                            item.ExpectedCashAmount,
                            item.ActualCashAmount,
                            item.Note,
                            operationKey,
                            occurredAt,
                            deferSettlement: hasPendingCashless,
                            investigationId: investigationId);
                        PostPendingCashlessVerification(
                            item,
                            operationKey,
                            item.PendingCashlessVerification,
                            investigationId);
                        CashAcceptanceService.MarkFinalized(item.Id, now);
                        finalizedAny = true;
                    }
                    catch
                    {
                        // Тот же operationId безопасно завершит запись при следующей попытке.
                    }
                }
            }

            if (finalizedAny)
            {
                _ = FirebaseSyncService.PushOverviewStateAsync();
                _ = FirebaseSyncService.PushCurrentStateAsync();
            }
        }

        private static void PostLedger(
            string checkedByEmployeeName,
            string responsibleEmployeeName,
            int expectedCashAmount,
            int actualCashAmount,
            string note,
            string acceptanceKey,
            DateTime occurredAt,
            bool deferSettlement = false,
            Guid? investigationId = null)
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
                occurredAt: occurredAt,
                deferSettlement: deferSettlement,
                investigationIdOverride: investigationId);
        }

        private static void PostPendingCashlessVerification(
            CashAcceptanceItem item,
            string operationKey,
            PendingCashlessVerification? verification,
            Guid investigationId)
        {
            if (verification == null)
                return;

            DateTime observedAt = verification.ObservedAt == default
                ? CashAcceptanceTimelinePolicy.GetObservationTime(item)
                : verification.ObservedAt;
            var month = BusinessCalendarService.GetBusinessMonth(observedAt);
            CashReconciliationService.ProcessCashlessVerification(
                month.StartInclusive,
                month.EndExclusive,
                verification.ExpectedAmount,
                verification.ActualAmount,
                verification.SuspectedEmployeeName,
                verification.Note,
                operationId: $"{operationKey.Trim()}:cashless-ledger",
                programExpectedAmount: verification.ProgramExpectedAmount,
                occurredAt: observedAt,
                investigationIdOverride: investigationId);
        }

        private static Guid BuildInvestigationId(string operationKey)
        {
            byte[] hash = SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    $"cash-acceptance:{operationKey.Trim().ToLowerInvariant()}"));
            return new Guid(hash.AsSpan(0, 16));
        }
    }
}
