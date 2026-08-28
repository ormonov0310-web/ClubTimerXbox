using System;

namespace ClubTimerXbox.Models
{
    public class ShiftAcceptanceStatus
    {
        public bool IsRequired { get; set; }

        public bool ProductsAccepted { get; set; }

        public bool CashAccepted { get; set; }

        public bool DebtAcceptanceRequired { get; set; }

        public bool DebtsAccepted { get; set; }

        public string AcceptanceKey { get; set; } = "";

        public string NewEmployeeName { get; set; } = "";

        public string ResponsibleEmployeeName { get; set; } = "";

        public string DisplayResponsibleEmployeeName { get; set; } = "";

        public string DisplayNewEmployeeName { get; set; } = "";

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public DateTime? ProductsAcceptedAt { get; set; }

        public DateTime? CashAcceptedAt { get; set; }

        public DateTime? DebtsAcceptedAt { get; set; }

        public DateTime? CompletedAt { get; set; }

        public DateTime? InitialProductsAndCashAcceptedAt { get; set; }

        public bool IsManualSelfAcceptance { get; set; }

        public bool ManualSelfAcceptanceAvailable { get; set; }

        public string ManualSelfAcceptanceEmployeeName { get; set; } = "";

        public string ManualSelfAcceptanceKey { get; set; } = "";

        // Короткая повторная проверка не должна создавать вторую кассовую передачу.
        public string ManualSelfAcceptanceRecheckRootKey { get; set; } = "";

        public bool CashCorrectionAvailable { get; set; }

        public string CashCorrectionAcceptanceKey { get; set; } = "";

        public string CashCorrectionNewEmployeeName { get; set; } = "";

        public string CashCorrectionResponsibleEmployeeName { get; set; } = "";

        public DateTime? CashCorrectionUntil { get; set; }

        public bool CashRecountRequired { get; set; }

        public string CashRecountAcceptanceKey { get; set; } = "";

        public int CashRecountFirstAmount { get; set; }

        public DateTime? CashRecountUnlockAt { get; set; }

        public bool IsCompleted
        {
            get
            {
                return !IsRequired ||
                       (ProductsAccepted && CashAccepted &&
                        (!DebtAcceptanceRequired || DebtsAccepted));
            }
        }
    }
}
