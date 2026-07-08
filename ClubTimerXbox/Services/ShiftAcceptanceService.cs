using System;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public static class ShiftAcceptanceService
    {
        private const int CashCorrectionWindowMinutes = 15;
        private const string CashCorrectionKeySuffix = ":cash-correction";
        private const string ProductsCorrectionKeySuffix = ":products-correction";

        public static ShiftAcceptanceStatus Current { get; private set; } =
            ShiftAcceptanceStorageService.Load();

        public static bool IsAcceptanceRequired()
        {
            ExpireCashCorrectionIfNeeded();

            return IsAcceptanceActive() && !Current.IsManualSelfAcceptance;
        }

        public static bool IsAcceptanceActive()
        {
            ExpireCashCorrectionIfNeeded();

            return Current.IsRequired && !Current.IsCompleted;
        }

        public static bool CanEmployeeAccept(string employeeName)
        {
            if (!IsAcceptanceActive())
                return false;

            employeeName = employeeName.Trim();

            if (string.IsNullOrWhiteSpace(Current.NewEmployeeName))
                return true;

            return Current.NewEmployeeName.Trim().Equals(
                employeeName,
                StringComparison.OrdinalIgnoreCase
            );
        }

        public static bool IsPendingForEmployee(string employeeName)
        {
            if (!IsAcceptanceRequired())
                return false;

            employeeName = employeeName.Trim();

            if (string.IsNullOrWhiteSpace(employeeName))
                return false;

            return Current.NewEmployeeName.Trim().Equals(
                employeeName,
                StringComparison.OrdinalIgnoreCase
            );
        }

        public static bool IsResponsibleEmployee(string employeeName)
        {
            if (!IsAcceptanceRequired())
                return false;

            employeeName = employeeName.Trim();

            if (string.IsNullOrWhiteSpace(employeeName))
                return false;

            return Current.ResponsibleEmployeeName.Trim().Equals(
                employeeName,
                StringComparison.OrdinalIgnoreCase
            );
        }

        public static void StartRequiredAcceptance(
            string newEmployeeName,
            string responsibleEmployeeName,
            string acceptanceKey = "")
        {
            ExpireCashCorrectionIfNeeded();

            if (IsAcceptanceRequired())
                return;

            if (!string.IsNullOrWhiteSpace(acceptanceKey) &&
                Current.AcceptanceKey.Equals(acceptanceKey, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            Current = new ShiftAcceptanceStatus
            {
                IsRequired = true,
                ProductsAccepted = false,
                CashAccepted = false,
                AcceptanceKey = acceptanceKey,
                NewEmployeeName = newEmployeeName.Trim(),
                ResponsibleEmployeeName = responsibleEmployeeName.Trim(),
                DisplayResponsibleEmployeeName = responsibleEmployeeName.Trim(),
                DisplayNewEmployeeName = newEmployeeName.Trim(),
                CreatedAt = DateTime.Now,
                ProductsAcceptedAt = null,
                CashAcceptedAt = null,
                CompletedAt = null,
                IsManualSelfAcceptance = false,
                ManualSelfAcceptanceAvailable = false,
                ManualSelfAcceptanceEmployeeName = "",
                ManualSelfAcceptanceKey = "",
                CashCorrectionAvailable = false,
                CashCorrectionAcceptanceKey = "",
                CashCorrectionNewEmployeeName = "",
                CashCorrectionResponsibleEmployeeName = "",
                CashCorrectionUntil = null
            };

            Save();
        }

        public static void AllowManualSelfAcceptanceAfterReentry(
            string employeeName,
            string acceptanceKey)
        {
            ExpireCashCorrectionIfNeeded();

            employeeName = employeeName.Trim();
            acceptanceKey = acceptanceKey.Trim();

            if (string.IsNullOrWhiteSpace(employeeName) ||
                string.IsNullOrWhiteSpace(acceptanceKey) ||
                IsAcceptanceActive() ||
                HasActiveCorrectionWindow())
            {
                return;
            }

            Current.ManualSelfAcceptanceAvailable = true;
            Current.ManualSelfAcceptanceEmployeeName = employeeName;
            Current.ManualSelfAcceptanceKey = acceptanceKey;
            Current.IsRequired = false;
            Current.ProductsAccepted = true;
            Current.CashAccepted = true;
            Current.AcceptanceKey = acceptanceKey;
            Current.NewEmployeeName = employeeName;
            Current.ResponsibleEmployeeName = employeeName;
            Current.DisplayNewEmployeeName = employeeName;
            Current.DisplayResponsibleEmployeeName = employeeName;
            Current.CompletedAt = DateTime.Now;

            Save();
        }

        public static bool CanStartManualSelfAcceptance(string employeeName)
        {
            ExpireCashCorrectionIfNeeded();

            employeeName = employeeName.Trim();

            if (IsAcceptanceActive())
                return Current.IsManualSelfAcceptance && CanEmployeeAccept(employeeName);

            if (string.IsNullOrWhiteSpace(employeeName))
                return false;

            if (HasActiveCorrectionWindow())
                return false;

            if (!Current.ManualSelfAcceptanceAvailable)
                return false;

            return Current.ManualSelfAcceptanceEmployeeName.Trim().Equals(
                employeeName,
                StringComparison.OrdinalIgnoreCase
            );
        }

        public static bool StartManualSelfAcceptance(string employeeName)
        {
            ExpireCashCorrectionIfNeeded();

            employeeName = employeeName.Trim();

            if (string.IsNullOrWhiteSpace(employeeName))
                return false;

            if (IsAcceptanceActive())
                return Current.IsManualSelfAcceptance && CanEmployeeAccept(employeeName);

            if (HasActiveCorrectionWindow())
                return false;

            if (!CanStartManualSelfAcceptance(employeeName))
                return false;

            Current = new ShiftAcceptanceStatus
            {
                IsRequired = true,
                ProductsAccepted = false,
                CashAccepted = false,
                AcceptanceKey = Current.ManualSelfAcceptanceKey.Trim(),
                NewEmployeeName = employeeName,
                ResponsibleEmployeeName = employeeName,
                DisplayResponsibleEmployeeName = employeeName,
                DisplayNewEmployeeName = employeeName,
                CreatedAt = DateTime.Now,
                ProductsAcceptedAt = null,
                CashAcceptedAt = null,
                CompletedAt = null,
                IsManualSelfAcceptance = true,
                ManualSelfAcceptanceAvailable = false,
                ManualSelfAcceptanceEmployeeName = "",
                ManualSelfAcceptanceKey = "",
                CashCorrectionAvailable = false,
                CashCorrectionAcceptanceKey = "",
                CashCorrectionNewEmployeeName = "",
                CashCorrectionResponsibleEmployeeName = "",
                CashCorrectionUntil = null
            };

            Save();

            return true;
        }

        public static void AcceptProducts()
        {
            if (!Current.IsRequired)
                return;

            bool isProductsCorrection = IsProductsCorrectionAcceptanceKey(Current.AcceptanceKey);
            string originalAcceptanceKey = Current.CashCorrectionAcceptanceKey.Trim();
            string correctionNewEmployeeName = Current.CashCorrectionNewEmployeeName.Trim();

            Current.ProductsAccepted = true;
            Current.ProductsAcceptedAt = DateTime.Now;

            TryComplete();

            if (isProductsCorrection)
            {
                if (!string.IsNullOrWhiteSpace(originalAcceptanceKey))
                    Current.AcceptanceKey = originalAcceptanceKey;

                CompleteSectionCorrection(correctionNewEmployeeName);
            }
            else
            {
                TryStartCashCorrectionWindowAfterStandardCompletion();
            }

            Save();
        }

        public static void AcceptCash()
        {
            if (!Current.IsRequired)
                return;

            bool isCashCorrection = IsCashCorrectionAcceptanceKey(Current.AcceptanceKey);
            string originalAcceptanceKey = Current.CashCorrectionAcceptanceKey.Trim();
            string correctionNewEmployeeName = Current.CashCorrectionNewEmployeeName.Trim();

            Current.CashAccepted = true;
            Current.CashAcceptedAt = DateTime.Now;

            TryComplete();

            if (isCashCorrection)
            {
                if (!string.IsNullOrWhiteSpace(originalAcceptanceKey))
                    Current.AcceptanceKey = originalAcceptanceKey;

                CompleteSectionCorrection(correctionNewEmployeeName);
            }
            else
            {
                TryStartCashCorrectionWindowAfterStandardCompletion();
            }

            Save();
        }

        public static void MarkCompleted()
        {
            Current.ProductsAccepted = true;
            Current.CashAccepted = true;
            Current.IsRequired = false;
            Current.CompletedAt = DateTime.Now;
            Current.IsManualSelfAcceptance = false;
            ClearManualSelfAcceptanceAvailability();
            ClearCashCorrection();

            Save();
        }

        public static void Reset()
        {
            Current = new ShiftAcceptanceStatus();
            ShiftAcceptanceStorageService.Clear();
        }

        public static void CancelPendingAcceptance()
        {
            Current = new ShiftAcceptanceStatus();
            Save();
        }

        public static void ClearCompletedManualSelfAcceptance()
        {
            ExpireCashCorrectionIfNeeded();

            if (IsAcceptanceActive())
                return;

            if (!Current.IsManualSelfAcceptance)
                return;

            Current = new ShiftAcceptanceStatus
            {
                ManualSelfAcceptanceAvailable = Current.ManualSelfAcceptanceAvailable,
                ManualSelfAcceptanceEmployeeName = Current.ManualSelfAcceptanceEmployeeName,
                ManualSelfAcceptanceKey = Current.ManualSelfAcceptanceKey,
                CashCorrectionAvailable = Current.CashCorrectionAvailable,
                CashCorrectionAcceptanceKey = Current.CashCorrectionAcceptanceKey,
                CashCorrectionNewEmployeeName = Current.CashCorrectionNewEmployeeName,
                CashCorrectionResponsibleEmployeeName = Current.CashCorrectionResponsibleEmployeeName,
                CashCorrectionUntil = Current.CashCorrectionUntil
            };

            Save();
        }

        public static bool CanCorrectCashAcceptance(string employeeName)
        {
            ExpireCashCorrectionIfNeeded();

            employeeName = employeeName.Trim();

            if (!Current.CashCorrectionAvailable)
                return false;

            if (string.IsNullOrWhiteSpace(employeeName))
                return false;

            if (Current.CashCorrectionUntil == null ||
                Current.CashCorrectionUntil.Value <= DateTime.Now)
            {
                ExpireCashCorrectionIfNeeded();
                return false;
            }

            if (string.IsNullOrWhiteSpace(Current.CashCorrectionAcceptanceKey))
                return false;

            if (!Current.CashCorrectionNewEmployeeName.Trim().Equals(
                    employeeName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string correctionKey = BuildCashCorrectionAcceptanceKey(Current.CashCorrectionAcceptanceKey);

            return !CashAcceptanceService.HasAcceptanceKey(correctionKey);
        }

        public static bool CanCorrectProductsAcceptance(string employeeName)
        {
            ExpireCashCorrectionIfNeeded();

            employeeName = employeeName.Trim();

            if (!Current.CashCorrectionAvailable)
                return false;

            if (string.IsNullOrWhiteSpace(employeeName))
                return false;

            if (Current.CashCorrectionUntil == null ||
                Current.CashCorrectionUntil.Value <= DateTime.Now)
            {
                ExpireCashCorrectionIfNeeded();
                return false;
            }

            if (string.IsNullOrWhiteSpace(Current.CashCorrectionAcceptanceKey))
                return false;

            if (!Current.CashCorrectionNewEmployeeName.Trim().Equals(
                    employeeName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string correctionKey = BuildProductsCorrectionAcceptanceKey(Current.CashCorrectionAcceptanceKey);

            return !StockAuditService.HasAcceptanceKey(correctionKey);
        }

        public static TimeSpan? GetCashCorrectionRemaining()
        {
            ExpireCashCorrectionIfNeeded();

            if (!Current.CashCorrectionAvailable ||
                Current.CashCorrectionUntil == null)
            {
                return null;
            }

            var remaining = Current.CashCorrectionUntil.Value - DateTime.Now;

            if (remaining <= TimeSpan.Zero)
                return null;

            return remaining;
        }

        public static bool StartCashCorrection(string employeeName)
        {
            if (!CanCorrectCashAcceptance(employeeName))
                return false;

            Current.IsRequired = true;
            Current.ProductsAccepted = true;
            Current.CashAccepted = false;
            Current.AcceptanceKey = BuildCashCorrectionAcceptanceKey(Current.CashCorrectionAcceptanceKey);
            Current.NewEmployeeName = Current.CashCorrectionNewEmployeeName.Trim();
            Current.ResponsibleEmployeeName = Current.CashCorrectionResponsibleEmployeeName.Trim();
            Current.DisplayResponsibleEmployeeName = Current.CashCorrectionResponsibleEmployeeName.Trim();
            Current.DisplayNewEmployeeName = Current.CashCorrectionNewEmployeeName.Trim();
            Current.CreatedAt = DateTime.Now;
            Current.ProductsAcceptedAt = DateTime.Now;
            Current.CashAcceptedAt = null;
            Current.CompletedAt = null;
            Current.IsManualSelfAcceptance = false;
            ClearManualSelfAcceptanceAvailability();

            Save();

            return true;
        }

        public static bool StartProductsCorrection(string employeeName)
        {
            if (!CanCorrectProductsAcceptance(employeeName))
                return false;

            Current.IsRequired = true;
            Current.ProductsAccepted = false;
            Current.CashAccepted = true;
            Current.AcceptanceKey = BuildProductsCorrectionAcceptanceKey(Current.CashCorrectionAcceptanceKey);
            Current.NewEmployeeName = Current.CashCorrectionNewEmployeeName.Trim();
            Current.ResponsibleEmployeeName = Current.CashCorrectionResponsibleEmployeeName.Trim();
            Current.DisplayResponsibleEmployeeName = Current.CashCorrectionResponsibleEmployeeName.Trim();
            Current.DisplayNewEmployeeName = Current.CashCorrectionNewEmployeeName.Trim();
            Current.CreatedAt = DateTime.Now;
            Current.ProductsAcceptedAt = null;
            Current.CashAcceptedAt = DateTime.Now;
            Current.CompletedAt = null;
            Current.IsManualSelfAcceptance = false;
            ClearManualSelfAcceptanceAvailability();

            Save();

            return true;
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

        private static void TryStartCashCorrectionWindowAfterStandardCompletion()
        {
            if (Current.IsRequired)
                return;

            if (!Current.ProductsAccepted || !Current.CashAccepted)
                return;

            if (Current.IsManualSelfAcceptance)
                return;

            if (string.IsNullOrWhiteSpace(Current.AcceptanceKey))
                return;

            if (Current.NewEmployeeName.Trim().Equals(
                    Current.ResponsibleEmployeeName.Trim(),
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (IsCashCorrectionAcceptanceKey(Current.AcceptanceKey) ||
                IsProductsCorrectionAcceptanceKey(Current.AcceptanceKey))
                return;

            Current.CashCorrectionAvailable = true;
            Current.CashCorrectionAcceptanceKey = Current.AcceptanceKey.Trim();
            Current.CashCorrectionNewEmployeeName = Current.NewEmployeeName.Trim();
            Current.CashCorrectionResponsibleEmployeeName = Current.ResponsibleEmployeeName.Trim();
            Current.CashCorrectionUntil = DateTime.Now.AddMinutes(CashCorrectionWindowMinutes);
        }

        private static void ExpireCashCorrectionIfNeeded()
        {
            if (!Current.CashCorrectionAvailable ||
                Current.CashCorrectionUntil == null ||
                Current.CashCorrectionUntil.Value > DateTime.Now)
            {
                return;
            }

            if (IsCashCorrectionAcceptanceKey(Current.AcceptanceKey) ||
                IsProductsCorrectionAcceptanceKey(Current.AcceptanceKey))
            {
                string originalAcceptanceKey = Current.CashCorrectionAcceptanceKey.Trim();

                if (!string.IsNullOrWhiteSpace(originalAcceptanceKey))
                    Current.AcceptanceKey = originalAcceptanceKey;

                Current.IsRequired = false;
                Current.ProductsAccepted = true;
                Current.CashAccepted = true;
                Current.CompletedAt ??= DateTime.Now;
                Current.IsManualSelfAcceptance = false;
            }

            ClearCashCorrection();
            Save();
        }

        private static void ClearCashCorrection()
        {
            Current.CashCorrectionAvailable = false;
            Current.CashCorrectionAcceptanceKey = "";
            Current.CashCorrectionNewEmployeeName = "";
            Current.CashCorrectionResponsibleEmployeeName = "";
            Current.CashCorrectionUntil = null;
        }

        private static void CompleteSectionCorrection(string correctionNewEmployeeName)
        {
            if (!HasPendingCorrectionOpportunity())
            {
                if (!string.IsNullOrWhiteSpace(correctionNewEmployeeName))
                    Current.ResponsibleEmployeeName = correctionNewEmployeeName;

                ClearCashCorrection();
            }
        }

        private static bool HasPendingCorrectionOpportunity()
        {
            if (!HasActiveCorrectionWindow())
                return false;

            string originalKey = Current.CashCorrectionAcceptanceKey.Trim();

            if (string.IsNullOrWhiteSpace(originalKey))
                return false;

            bool productsCorrectionDone =
                StockAuditService.HasAcceptanceKey(BuildProductsCorrectionAcceptanceKey(originalKey));
            bool cashCorrectionDone =
                CashAcceptanceService.HasAcceptanceKey(BuildCashCorrectionAcceptanceKey(originalKey));

            return !productsCorrectionDone || !cashCorrectionDone;
        }

        private static bool HasActiveCorrectionWindow()
        {
            return Current.CashCorrectionAvailable &&
                Current.CashCorrectionUntil != null &&
                Current.CashCorrectionUntil.Value > DateTime.Now &&
                !string.IsNullOrWhiteSpace(Current.CashCorrectionAcceptanceKey);
        }

        private static void ClearManualSelfAcceptanceAvailability()
        {
            Current.ManualSelfAcceptanceAvailable = false;
            Current.ManualSelfAcceptanceEmployeeName = "";
            Current.ManualSelfAcceptanceKey = "";
        }

        private static string BuildCashCorrectionAcceptanceKey(string acceptanceKey)
        {
            acceptanceKey = acceptanceKey.Trim();

            if (IsCashCorrectionAcceptanceKey(acceptanceKey))
                return acceptanceKey;

            return $"{acceptanceKey}{CashCorrectionKeySuffix}";
        }

        private static string BuildProductsCorrectionAcceptanceKey(string acceptanceKey)
        {
            acceptanceKey = acceptanceKey.Trim();

            if (IsProductsCorrectionAcceptanceKey(acceptanceKey))
                return acceptanceKey;

            return $"{acceptanceKey}{ProductsCorrectionKeySuffix}";
        }

        private static bool IsCashCorrectionAcceptanceKey(string acceptanceKey)
        {
            return acceptanceKey
                .Trim()
                .EndsWith(CashCorrectionKeySuffix, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsProductsCorrectionAcceptanceKey(string acceptanceKey)
        {
            return acceptanceKey
                .Trim()
                .EndsWith(ProductsCorrectionKeySuffix, StringComparison.OrdinalIgnoreCase);
        }
    }
}
