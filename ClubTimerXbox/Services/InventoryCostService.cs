using System;

namespace ClubTimerXbox.Services
{
    public static class InventoryCostService
    {
        public static int CalculateWeightedAverageUnitCost(
            int currentQuantity,
            int currentUnitCost,
            int incomingQuantity,
            int incomingUnitCost)
        {
            int safeCurrentQuantity = Math.Max(0, currentQuantity);
            int safeIncomingQuantity = Math.Max(0, incomingQuantity);
            int totalQuantity = safeCurrentQuantity + safeIncomingQuantity;
            if (totalQuantity == 0)
                return Math.Max(0, incomingUnitCost);

            decimal totalCost =
                (decimal)safeCurrentQuantity * Math.Max(0, currentUnitCost) +
                (decimal)safeIncomingQuantity * Math.Max(0, incomingUnitCost);
            return (int)Math.Round(
                totalCost / totalQuantity,
                MidpointRounding.AwayFromZero);
        }
    }
}
