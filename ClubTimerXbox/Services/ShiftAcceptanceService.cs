using System;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public static class ShiftAcceptanceService
    {
        public static ShiftAcceptanceStatus Current { get; private set; } =
            ShiftAcceptanceStorageService.Load();

        public static bool IsAcceptanceRequired()
        {
            return Current.IsRequired && !Current.IsCompleted;
        }

        public static void StartRequiredAcceptance(
            string newEmployeeName,
            string responsibleEmployeeName)
        {
            Current = new ShiftAcceptanceStatus
            {
                IsRequired = true,
                ProductsAccepted = false,
                CashAccepted = false,
                NewEmployeeName = newEmployeeName,
                ResponsibleEmployeeName = responsibleEmployeeName,
                CreatedAt = DateTime.Now,
                ProductsAcceptedAt = null,
                CashAcceptedAt = null,
                CompletedAt = null
            };

            Save();
        }

        public static void AcceptProducts()
        {
            if (!Current.IsRequired)
                return;

            Current.ProductsAccepted = true;
            Current.ProductsAcceptedAt = DateTime.Now;

            TryComplete();

            Save();
        }

        public static void AcceptCash()
        {
            if (!Current.IsRequired)
                return;

            Current.CashAccepted = true;
            Current.CashAcceptedAt = DateTime.Now;

            TryComplete();

            Save();
        }

        public static void MarkCompleted()
        {
            Current.ProductsAccepted = true;
            Current.CashAccepted = true;
            Current.IsRequired = false;
            Current.CompletedAt = DateTime.Now;

            Save();
        }

        public static void Reset()
        {
            Current = new ShiftAcceptanceStatus();
            ShiftAcceptanceStorageService.Clear();
        }

        private static void TryComplete()
        {
            if (Current.ProductsAccepted && Current.CashAccepted)
            {
                Current.IsRequired = false;
                Current.CompletedAt = DateTime.Now;
            }
        }

        private static void Save()
        {
            ShiftAcceptanceStorageService.Save(Current);
        }
    }
}