using System;
using System.Collections.Generic;
using System.Linq;

namespace ClubTimerXbox.Services
{
    public sealed record OverNormBonusParticipant(string EmployeeName, double Hours);

    public static class OverNormBonusAllocator
    {
        public const double MinimumParticipationHours = 2.0;

        public static IReadOnlyDictionary<string, int> Allocate(
            int fund,
            IEnumerable<OverNormBonusParticipant> participants)
        {
            if (fund <= 0)
                return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            var eligible = participants
                .Where(item =>
                    !string.IsNullOrWhiteSpace(item.EmployeeName) &&
                    item.Hours >= MinimumParticipationHours)
                .OrderBy(item => item.EmployeeName, StringComparer.OrdinalIgnoreCase)
                .ToList();
            double totalHours = eligible.Sum(item => item.Hours);
            if (totalHours <= 0)
                return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            var shares = eligible
                .Select(item =>
                {
                    double exact = fund * item.Hours / totalHours;
                    return new
                    {
                        item.EmployeeName,
                        BaseAmount = (int)Math.Floor(exact),
                        Remainder = exact - Math.Floor(exact)
                    };
                })
                .ToList();
            int undistributed = fund - shares.Sum(item => item.BaseAmount);
            var extraRecipients = shares
                .OrderByDescending(item => item.Remainder)
                .ThenBy(item => item.EmployeeName, StringComparer.OrdinalIgnoreCase)
                .Take(undistributed)
                .Select(item => item.EmployeeName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            return shares.ToDictionary(
                item => item.EmployeeName,
                item => item.BaseAmount + (extraRecipients.Contains(item.EmployeeName) ? 1 : 0),
                StringComparer.OrdinalIgnoreCase);
        }
    }
}
