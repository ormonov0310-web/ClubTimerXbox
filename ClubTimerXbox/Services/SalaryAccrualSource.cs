using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services;

// Owned rule inputs. Queries use the existing salary engine and never open a store.
internal sealed class EmployeeRatingCalculation
{
    private readonly EmployeeRatingProfile _profile;
    private readonly EmployeeRatingEvent[] _ratingEvents;
    private readonly EmployeeRatingEvent[] _signatureEvents;
    private readonly DateTime[] _boundaries;
    private readonly DateTime[] _segmentBoundaries;
    private readonly Dictionary<(EmployeeRatingBranch, int), (int Percent, string Signature)> _segments = new();

    internal EmployeeRatingCalculation(EmployeeRatingProfile profile, IEnumerable<EmployeeRatingEvent> events,
        string? requestedName = null)
    {
        requestedName ??= profile.EmployeeName;
        _profile = JsonSerializer.Deserialize<EmployeeRatingProfile>(JsonSerializer.SerializeToUtf8Bytes(profile))!;
        var selected = events.Where(item => Matches(item, profile.EmployeeId, profile.EmployeeName) ||
            Matches(item, profile.EmployeeId, requestedName)).ToArray();
        var owned = JsonSerializer.Deserialize<EmployeeRatingEvent[]>(JsonSerializer.SerializeToUtf8Bytes(selected))!;
        // Legacy lookup uses the profile name for percent, but the requested name for signatures/boundaries.
        _ratingEvents = owned.Where(item => Matches(item, _profile.EmployeeId, _profile.EmployeeName)).ToArray();
        _signatureEvents = owned.Where(item => Matches(item, _profile.EmployeeId, requestedName)).ToArray();
        _boundaries = _profile.BaseVersions.Select(item => item.EffectiveFrom)
            .Concat(_signatureEvents.SelectMany(item => new[] { item.EffectiveFrom, item.EffectiveUntil }))
            .Distinct().Order().ToArray();
        _segmentBoundaries = _boundaries.Concat(_ratingEvents.SelectMany(item =>
                new[] { item.EffectiveFrom, item.EffectiveUntil })).Distinct().Order().ToArray();
    }

    private static bool Matches(EmployeeRatingEvent item, string id, string name)
        => (!string.IsNullOrWhiteSpace(id) && item.EmployeeId.Equals(id, StringComparison.OrdinalIgnoreCase)) ||
            item.EmployeeName.Equals(name, StringComparison.OrdinalIgnoreCase);

    internal IEnumerable<DateTime> Boundaries(DateTime from, DateTime to)
        => _boundaries.Where(at => at > from && at < to);

    internal (int Percent, string Signature) At(EmployeeRatingBranch branch, DateTime at)
    {
        int index = Array.BinarySearch(_segmentBoundaries, at);
        if (index < 0) index = ~index - 1;
        var key = (branch, index);
        if (_segments.TryGetValue(key, out var cached)) return cached;
        int percent = EmployeeSalaryRuleEngine.ResolveRating(_profile, _ratingEvents, branch, at);
        Guid baseId = _profile.BaseVersions.Where(item => item.EffectiveFrom <= at)
            .OrderByDescending(item => item.EffectiveFrom).ThenByDescending(item => item.CreatedAt)
            .Select(item => item.Id).FirstOrDefault();
        string events = string.Join(",", _signatureEvents.Where(item => item.Branch == branch && item.EffectiveFrom <= at)
            .OrderBy(item => item.EffectiveFrom).Select(item => $"{item.Id:N}:{item.EffectiveUntil:O}"));
        return _segments[key] = (percent, $"{baseId:N}|{events}");
    }
}

internal readonly record struct SalaryAccrualPolicy(Guid Id, DateTime EffectiveFrom, DateTime CreatedAt,
    int MonthlyFund, int PlannedHours, int ReservePercent, int SalaryPercent, int ProductPercent);

internal sealed class SalaryAccrualRules
{
    private readonly SalaryAccrualPolicy[] _policies;
    private readonly SalaryAccrualPolicy _fallback;
    private readonly Dictionary<string, EmployeeRatingCalculation> _employees;

    internal SalaryAccrualRules(IEnumerable<SalaryPolicyVersion> policies,
        IEnumerable<KeyValuePair<string, EmployeeRatingCalculation>> employees)
    {
        var captured = policies.Select(item => new SalaryAccrualPolicy(item.Id, item.EffectiveFrom, item.CreatedAt,
            item.Settings.TimeMonthlyFundAmount, item.Settings.TimeMonthlyPlannedHours,
            item.Settings.ExpenseReservePercent, item.Settings.SalaryFundPercent, item.Settings.ProductBonusPercent)).ToArray();
        _policies = captured.Select((policy, order) => (policy, order))
            .OrderBy(item => item.policy.EffectiveFrom).ThenBy(item => item.policy.CreatedAt)
            .ThenByDescending(item => item.order).Select(item => item.policy).ToArray();
        if (_policies.Length == 0) throw new InvalidOperationException("Salary policy capture is empty.");
        _fallback = captured.OrderBy(item => item.EffectiveFrom).First();
        _employees = new(employees, StringComparer.OrdinalIgnoreCase);
    }

    internal SalaryAccrualPolicy PolicyAt(DateTime at)
    {
        int lo = 0, hi = _policies.Length;
        while (lo < hi)
        {
            int mid = lo + (hi - lo) / 2;
            if (_policies[mid].EffectiveFrom <= at) lo = mid + 1;
            else hi = mid;
        }
        // The legacy fallback uses the first version if the query precedes all policies.
        return lo == 0 ? _fallback : _policies[lo - 1];
    }

    internal EmployeeRatingCalculation Employee(string name) => _employees[name];

    internal IEnumerable<DateTime> Boundaries(string name, DateTime from, DateTime to)
        => _policies.Select(item => item.EffectiveFrom).Concat(Employee(name).Boundaries(from, to))
            .Where(at => at > from && at < to).Distinct().Order();
}

internal readonly record struct SalaryGameRevenue(DateTime OccurredAt, string EmployeeName, int Amount);
internal readonly record struct SalaryProductRevenue(DateTime OccurredAt, string EmployeeName, int Amount);

internal sealed class SalaryAccrualSource
{
    private readonly RevenueIndex<SalaryGameRevenue> _games;
    private readonly RevenueIndex<SalaryProductRevenue> _products;
    internal SalaryAccrualRules Rules { get; }

    internal SalaryAccrualSource(IEnumerable<SalaryGameRevenue> games, IEnumerable<SalaryProductRevenue> products,
        SalaryAccrualRules rules)
    {
        Rules = rules;
        _games = new(games, item => item.OccurredAt, item => item.EmployeeName);
        _products = new(products, item => item.OccurredAt, item => item.EmployeeName);
    }

    internal IReadOnlyList<SalaryGameRevenue> Games(DateTime from, DateTime to, string? employee = null)
        => _games.Query(from, to, employee);
    internal IReadOnlyList<SalaryProductRevenue> Products(DateTime from, DateTime to, string? employee = null)
        => _products.Query(from, to, string.IsNullOrWhiteSpace(employee) ? null : employee);

    private sealed class RevenueIndex<T>
    {
        private readonly Func<T, DateTime> _at;
        private readonly Dictionary<string, Dictionary<DateTime, List<(int Order, T Value)>>> _employees = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<DateTime, List<(int Order, T Value)>> _all = new();

        internal RevenueIndex(IEnumerable<T> rows, Func<T, DateTime> at, Func<T, string> employee)
        {
            _at = at;
            int order = 0;
            foreach (T row in rows)
            {
                DateTime day = BusinessCalendarService.GetBusinessDate(at(row));
                string name = employee(row);
                if (!_employees.TryGetValue(name, out var days)) _employees.Add(name, days = new());
                Add(days, day, (order, row));
                Add(_all, day, (order++, row));
            }
        }

        private static void Add(Dictionary<DateTime, List<(int, T)>> days, DateTime day, (int, T) row)
        {
            if (!days.TryGetValue(day, out var list)) days.Add(day, list = new());
            list.Add(row);
        }

        internal IReadOnlyList<T> Query(DateTime from, DateTime to, string? employee)
        {
            if (to <= from) return Array.Empty<T>();
            var days = _all;
            if (employee != null && !_employees.TryGetValue(employee, out days)) return Array.Empty<T>();
            DateTime first = BusinessCalendarService.GetBusinessDate(from);
            DateTime last = BusinessCalendarService.GetBusinessDate(to.AddTicks(-1));
            return days.Where(pair => pair.Key >= first && pair.Key <= last).SelectMany(pair => pair.Value)
                .Where(row => _at(row.Value) >= from && _at(row.Value) < to)
                .OrderBy(row => row.Order).Select(row => row.Value).ToArray();
        }
    }
}
