using System;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public static class ShiftAcceptanceService
    {
        public const int InitialCorrectionWindowMinutes =
            ShiftAcceptanceCorrectionPolicy.OriginalEmployeeResponsibilityMinutes;
        private const string CashCorrectionKeySuffix = ":cash-correction";
        private const string ProductsCorrectionKeySuffix = ":products-correction";

        public static ShiftAcceptanceStatus Current { get; private set; } =
            ShiftAcceptanceStorageService.Load();

        public static int RenameEmployeeReferences(
            string oldEmployeeName,
            string newEmployeeName)
        {
            bool changed = false;

            if (EmployeeReferenceRenameService.Matches(Current.NewEmployeeName, oldEmployeeName))
            {
                Current.NewEmployeeName = newEmployeeName;
                changed = true;
            }

            if (EmployeeReferenceRenameService.Matches(
                    Current.ResponsibleEmployeeName,
                    oldEmployeeName))
            {
                Current.ResponsibleEmployeeName = newEmployeeName;
                changed = true;
            }

            if (EmployeeReferenceRenameService.Matches(
                    Current.DisplayResponsibleEmployeeName,
                    oldEmployeeName))
            {
                Current.DisplayResponsibleEmployeeName = newEmployeeName;
                changed = true;
            }

            if (EmployeeReferenceRenameService.Matches(
                    Current.DisplayNewEmployeeName,
                    oldEmployeeName))
            {
                Current.DisplayNewEmployeeName = newEmployeeName;
                changed = true;
            }

            if (EmployeeReferenceRenameService.Matches(
                    Current.ManualSelfAcceptanceEmployeeName,
                    oldEmployeeName))
            {
                Current.ManualSelfAcceptanceEmployeeName = newEmployeeName;
                changed = true;
            }

            if (EmployeeReferenceRenameService.Matches(
                    Current.CashCorrectionNewEmployeeName,
                    oldEmployeeName))
            {
                Current.CashCorrectionNewEmployeeName = newEmployeeName;
                changed = true;
            }

            if (EmployeeReferenceRenameService.Matches(
                    Current.CashCorrectionResponsibleEmployeeName,
                    oldEmployeeName))
            {
                Current.CashCorrectionResponsibleEmployeeName = newEmployeeName;
                changed = true;
            }

            if (changed)
                Save();

            return changed ? 1 : 0;
        }

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

            bool debtAcceptanceRequired =
                ActionLogService.GetOutstandingCustomerDebts().Count > 0;

            Current = new ShiftAcceptanceStatus
            {
                IsRequired = true,
                ProductsAccepted = false,
                CashAccepted = false,
                DebtAcceptanceRequired = debtAcceptanceRequired,
                DebtsAccepted = !debtAcceptanceRequired,
                AcceptanceKey = acceptanceKey,
                NewEmployeeName = newEmployeeName.Trim(),
                ResponsibleEmployeeName = responsibleEmployeeName.Trim(),
                DisplayResponsibleEmployeeName = responsibleEmployeeName.Trim(),
                DisplayNewEmployeeName = newEmployeeName.Trim(),
                CreatedAt = ClubClock.Current.LocalNow,
                ProductsAcceptedAt = null,
                CashAcceptedAt = null,
                DebtsAcceptedAt = debtAcceptanceRequired ? null : ClubClock.Current.LocalNow,
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
            Current.ManualSelfAcceptanceRecheckRootKey =
                CashAcceptanceRecheckPolicy.FindImmediateHandoverForRecheck(
                    CashAcceptanceService.Items,
                    employeeName,
                    ClubClock.Current.LocalNow,
                    ShiftAcceptanceCorrectionPolicy.OriginalEmployeeResponsibilityMinutes)
                ?.RootAcceptanceKey.Trim() ?? "";
            Current.IsRequired = false;
            Current.ProductsAccepted = true;
            Current.CashAccepted = true;
            Current.DebtAcceptanceRequired = false;
            Current.DebtsAccepted = true;
            Current.DebtsAcceptedAt = ClubClock.Current.LocalNow;
            Current.AcceptanceKey = acceptanceKey;
            Current.NewEmployeeName = employeeName;
            Current.ResponsibleEmployeeName = employeeName;
            Current.DisplayNewEmployeeName = employeeName;
            Current.DisplayResponsibleEmployeeName = employeeName;
            Current.CompletedAt = ClubClock.Current.LocalNow;

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

            string recheckRootAcceptanceKey =
                Current.ManualSelfAcceptanceRecheckRootKey.Trim();

            Current = new ShiftAcceptanceStatus
            {
                IsRequired = true,
                ProductsAccepted = false,
                CashAccepted = false,
                DebtAcceptanceRequired = false,
                DebtsAccepted = true,
                AcceptanceKey = Current.ManualSelfAcceptanceKey.Trim(),
                NewEmployeeName = employeeName,
                ResponsibleEmployeeName = employeeName,
                DisplayResponsibleEmployeeName = employeeName,
                DisplayNewEmployeeName = employeeName,
                CreatedAt = ClubClock.Current.LocalNow,
                ProductsAcceptedAt = null,
                CashAcceptedAt = null,
                DebtsAcceptedAt = ClubClock.Current.LocalNow,
                CompletedAt = null,
                IsManualSelfAcceptance = true,
                ManualSelfAcceptanceAvailable = false,
                ManualSelfAcceptanceEmployeeName = "",
                ManualSelfAcceptanceKey = "",
                ManualSelfAcceptanceRecheckRootKey = recheckRootAcceptanceKey,
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

            Current.ProductsAccepted = true;
            Current.ProductsAcceptedAt = ClubClock.Current.LocalNow;

            TryComplete();
            ScheduleProvisionalCashFinalization();

            if (isProductsCorrection)
            {
                if (!string.IsNullOrWhiteSpace(originalAcceptanceKey))
                    Current.AcceptanceKey = originalAcceptanceKey;

                CompleteSectionCorrection();
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

            Current.CashAccepted = true;
            Current.CashAcceptedAt = ClubClock.Current.LocalNow;
            CashAcceptanceRecountPolicy.Clear(Current);

            TryComplete();
            ScheduleProvisionalCashFinalization();

            if (isCashCorrection)
            {
                if (!string.IsNullOrWhiteSpace(originalAcceptanceKey))
                    Current.AcceptanceKey = originalAcceptanceKey;

                CompleteSectionCorrection();
            }
            else
            {
                TryStartCashCorrectionWindowAfterStandardCompletion();
            }

            Save();
        }

        public static CashRecountDecision CheckCashRecount(
            int expectedAmount,
            int actualAmount)
        {
            var decision = CashAcceptanceRecountPolicy.Evaluate(
                Current,
                Current.AcceptanceKey,
                expectedAmount,
                actualAmount,
                ClubClock.Current.LocalNow);
            Save();
            return decision;
        }

        public static bool IsCashRecountRequired()
        {
            return Current.CashRecountRequired &&
                   Current.CashRecountAcceptanceKey.Equals(
                       Current.AcceptanceKey.Trim(),
                       StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsCashRecountLocked()
        {
            return CashAcceptanceRecountPolicy.IsLocked(
                Current,
                Current.AcceptanceKey,
                ClubClock.Current.LocalNow);
        }

        public static string GetRootAcceptanceKey()
        {
            if ((IsCashCorrectionAcceptanceKey(Current.AcceptanceKey) ||
                 IsProductsCorrectionAcceptanceKey(Current.AcceptanceKey)) &&
                !string.IsNullOrWhiteSpace(Current.CashCorrectionAcceptanceKey))
            {
                return Current.CashCorrectionAcceptanceKey.Trim();
            }

            return Current.AcceptanceKey.Trim();
        }

        public static string GetManualSelfAcceptanceRecheckRootKey()
        {
            return Current.IsManualSelfAcceptance
                ? Current.ManualSelfAcceptanceRecheckRootKey.Trim()
                : "";
        }

        public static bool ShouldStageCashAcceptance(DateTime now)
        {
            return ShiftAcceptanceCorrectionPolicy.ShouldStageInitialCashAcceptance(
                Current,
                GetRootAcceptanceKey(),
                now);
        }

        public static void ScheduleProvisionalCashFinalization()
        {
            if (Current.InitialProductsAndCashAcceptedAt == null)
                return;

            string rootAcceptanceKey = GetRootAcceptanceKey();
            if (string.IsNullOrWhiteSpace(rootAcceptanceKey))
                return;

            CashAcceptanceService.ScheduleProvisional(
                rootAcceptanceKey,
                Current.InitialProductsAndCashAcceptedAt.Value
                    .AddMinutes(InitialCorrectionWindowMinutes));
        }

        public static void AcceptDebts()
        {
            if (!Current.IsRequired || !Current.DebtAcceptanceRequired)
                return;

            Current.DebtsAccepted = true;
            Current.DebtsAcceptedAt = ClubClock.Current.LocalNow;
            TryComplete();
            TryStartCashCorrectionWindowAfterStandardCompletion();
            Save();
        }

        public static void MarkCompleted()
        {
            Current.ProductsAccepted = true;
            Current.CashAccepted = true;
            Current.InitialProductsAndCashAcceptedAt ??= ClubClock.Current.LocalNow;
            Current.DebtAcceptanceRequired = false;
            Current.DebtsAccepted = true;
            Current.DebtsAcceptedAt = ClubClock.Current.LocalNow;
            Current.IsRequired = false;
            Current.CompletedAt = ClubClock.Current.LocalNow;
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
                ManualSelfAcceptanceRecheckRootKey = "",
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
            employeeName = employeeName.Trim();
            if (string.IsNullOrWhiteSpace(employeeName))
                return false;
            if (!Current.CashAccepted || string.IsNullOrWhiteSpace(Current.AcceptanceKey))
                return false;
            if (IsCashCorrectionAcceptanceKey(Current.AcceptanceKey) ||
                IsProductsCorrectionAcceptanceKey(Current.AcceptanceKey))
                return false;
            if (!Current.NewEmployeeName.Trim().Equals(
                    employeeName,
                    StringComparison.OrdinalIgnoreCase))
                return false;
            return true;
        }

        public static bool CanCorrectProductsAcceptance(string employeeName)
        {
            employeeName = employeeName.Trim();
            if (string.IsNullOrWhiteSpace(employeeName))
                return false;
            if (!Current.ProductsAccepted || string.IsNullOrWhiteSpace(Current.AcceptanceKey))
                return false;
            if (IsCashCorrectionAcceptanceKey(Current.AcceptanceKey) ||
                IsProductsCorrectionAcceptanceKey(Current.AcceptanceKey))
                return false;
            if (!Current.NewEmployeeName.Trim().Equals(
                    employeeName,
                    StringComparison.OrdinalIgnoreCase))
                return false;
            return true;
        }

        public static TimeSpan? GetCashCorrectionRemaining()
        {
            return null;
        }

        public static bool StartCashCorrection(string employeeName)
        {
            if (!CanCorrectCashAcceptance(employeeName))
                return false;

            PrepareSectionCorrection();
            string responsibleEmployeeName =
                ShiftAcceptanceCorrectionPolicy.ResolveResponsibleEmployee(
                    Current,
                    Current.CashCorrectionResponsibleEmployeeName,
                    employeeName,
                    ClubClock.Current.LocalNow);

            Current.IsRequired = true;
            Current.CashAccepted = false;
            Current.AcceptanceKey = BuildCorrectionAttemptKey(
                Current.CashCorrectionAcceptanceKey,
                CashCorrectionKeySuffix);
            Current.NewEmployeeName = Current.CashCorrectionNewEmployeeName.Trim();
            Current.ResponsibleEmployeeName = responsibleEmployeeName;
            Current.DisplayResponsibleEmployeeName = responsibleEmployeeName;
            Current.DisplayNewEmployeeName = Current.CashCorrectionNewEmployeeName.Trim();
            Current.CreatedAt = ClubClock.Current.LocalNow;
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

            PrepareSectionCorrection();
            string responsibleEmployeeName =
                ShiftAcceptanceCorrectionPolicy.ResolveResponsibleEmployee(
                    Current,
                    Current.CashCorrectionResponsibleEmployeeName,
                    employeeName,
                    ClubClock.Current.LocalNow);

            Current.IsRequired = true;
            Current.ProductsAccepted = false;
            Current.AcceptanceKey = BuildCorrectionAttemptKey(
                Current.CashCorrectionAcceptanceKey,
                ProductsCorrectionKeySuffix);
            Current.NewEmployeeName = Current.CashCorrectionNewEmployeeName.Trim();
            Current.ResponsibleEmployeeName = responsibleEmployeeName;
            Current.DisplayResponsibleEmployeeName = responsibleEmployeeName;
            Current.DisplayNewEmployeeName = Current.CashCorrectionNewEmployeeName.Trim();
            Current.CreatedAt = ClubClock.Current.LocalNow;
            Current.ProductsAcceptedAt = null;
            Current.CompletedAt = null;
            Current.IsManualSelfAcceptance = false;
            ClearManualSelfAcceptanceAvailability();

            Save();

            return true;
        }

        private static void PrepareSectionCorrection()
        {
            Current.CashCorrectionAvailable = true;
            Current.CashCorrectionAcceptanceKey = Current.AcceptanceKey.Trim();
            Current.CashCorrectionNewEmployeeName = Current.NewEmployeeName.Trim();
            Current.CashCorrectionResponsibleEmployeeName = Current.ResponsibleEmployeeName.Trim();
            Current.CashCorrectionUntil = null;
        }

        private static void TryComplete()
        {
            bool isCorrectionAttempt =
                IsCashCorrectionAcceptanceKey(Current.AcceptanceKey) ||
                IsProductsCorrectionAcceptanceKey(Current.AcceptanceKey);
            ShiftAcceptanceCorrectionPolicy.CaptureInitialProductsAndCashCompletion(
                Current,
                ClubClock.Current.LocalNow,
                isCorrectionAttempt);

            if (Current.ProductsAccepted &&
                Current.CashAccepted &&
                (!Current.DebtAcceptanceRequired || Current.DebtsAccepted))
            {
                Current.IsRequired = false;
                Current.CompletedAt = ClubClock.Current.LocalNow;
                _ = FirebaseEventService.PublishAcceptanceCompletedAsync(Current);
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
            Current.CashCorrectionUntil =
                ClubClock.Current.LocalNow.AddMinutes(InitialCorrectionWindowMinutes);
        }

        private static void ExpireCashCorrectionIfNeeded()
        {
            if (!Current.CashCorrectionAvailable ||
                Current.CashCorrectionUntil == null ||
                Current.CashCorrectionUntil.Value > ClubClock.Current.LocalNow)
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
                Current.DebtAcceptanceRequired = false;
                Current.DebtsAccepted = true;
                Current.DebtsAcceptedAt ??= ClubClock.Current.LocalNow;
                Current.CompletedAt ??= ClubClock.Current.LocalNow;
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

        private static void CompleteSectionCorrection()
        {
            ClearCashCorrection();
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
                Current.CashCorrectionUntil.Value > ClubClock.Current.LocalNow &&
                !string.IsNullOrWhiteSpace(Current.CashCorrectionAcceptanceKey);
        }

        private static void ClearManualSelfAcceptanceAvailability()
        {
            Current.ManualSelfAcceptanceAvailable = false;
            Current.ManualSelfAcceptanceEmployeeName = "";
            Current.ManualSelfAcceptanceKey = "";
            Current.ManualSelfAcceptanceRecheckRootKey = "";
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
            string value = acceptanceKey.Trim();
            return value.EndsWith(CashCorrectionKeySuffix, StringComparison.OrdinalIgnoreCase) ||
                   value.Contains($"{CashCorrectionKeySuffix}:", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsProductsCorrectionAcceptanceKey(string acceptanceKey)
        {
            string value = acceptanceKey.Trim();
            return value.EndsWith(ProductsCorrectionKeySuffix, StringComparison.OrdinalIgnoreCase) ||
                   value.Contains($"{ProductsCorrectionKeySuffix}:", StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildCorrectionAttemptKey(string acceptanceKey, string suffix)
        {
            return $"{acceptanceKey.Trim()}{suffix}:{Guid.NewGuid():N}";
        }
    }
}
