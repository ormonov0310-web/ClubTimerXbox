using System;
using System.Collections.Generic;
using System.Linq;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public sealed class ProductServiceRevenueEntry
    {
        public DateTime OccurredAt { get; init; }

        public string EmployeeName { get; init; } = "";

        public string Title { get; init; } = "";

        public string Description { get; init; } = "";

        public string PlaceName { get; init; } = "";

        public int Amount { get; init; }
    }

    public static class ProductServiceRevenueService
    {
        public static List<ProductServiceRevenueEntry> GetEntries(
            DateTime fromInclusive,
            DateTime toExclusive,
            string employeeName = "")
        {
            bool MatchesEmployee(string value)
            {
                return string.IsNullOrWhiteSpace(employeeName) ||
                       value.Equals(employeeName, StringComparison.OrdinalIgnoreCase);
            }

            var standalone = CashService.Records
                .Where(record =>
                    record.Category == "Товары и услуги" &&
                    record.GameSessionId == null &&
                    MatchesEmployee(record.EmployeeName))
                .Select(record => new ProductServiceRevenueEntry
                {
                    OccurredAt = CashService.GetBusinessTime(record),
                    EmployeeName = record.EmployeeName,
                    Title = record.Title,
                    Description = record.Description,
                    PlaceName = record.PlaceName,
                    Amount = record.Amount
                });

            var attached = ActionLogService.GetAllGameSessions()
                .SelectMany(session => session.SaleLines.Select(line => new
                {
                    Session = session,
                    Line = line
                }))
                .Where(item => MatchesEmployee(item.Line.EmployeeName))
                .Select(item => new ProductServiceRevenueEntry
                {
                    OccurredAt = item.Line.CreatedAt,
                    EmployeeName = item.Line.EmployeeName,
                    Title = item.Line.ItemName,
                    Description =
                        $"Количество: {item.Line.Quantity}. " +
                        $"Цена: {item.Line.UnitPrice} сом.",
                    PlaceName = item.Session.PlaceName,
                    Amount = item.Line.TotalAmount
                });

            return standalone
                .Concat(attached)
                .Where(item => item.OccurredAt >= fromInclusive && item.OccurredAt < toExclusive)
                .OrderByDescending(item => item.OccurredAt)
                .ToList();
        }

        public static int GetTotal(DateTime fromInclusive, DateTime toExclusive)
        {
            return GetEntries(fromInclusive, toExclusive).Sum(item => item.Amount);
        }
    }
}
