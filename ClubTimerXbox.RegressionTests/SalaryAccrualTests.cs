using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClubTimerXbox.Models;
using ClubTimerXbox.Services;

internal static class SalaryAccrualTests
{
    internal static readonly DateTime From = new(2026, 10, 1, 6, 0, 0);
    internal static readonly DateTime To = From.AddMonths(1);

    internal static void MeasureIdle() => MeasureIdleAsync().GetAwaiter().GetResult();

    private static async Task MeasureIdleAsync()
    {
        string folder = Path.Combine(Path.GetTempPath(), "clubtimer-salary-idle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var fixture = new SalaryAccrualFixture(7377, 1600);
            var delivery = new FirebaseSnapshotDelivery(Path.Combine(folder, "delivery.json"));
            var clock = Stopwatch.StartNew();
            int captures = 0, sends = 0, bytes = 0;
            Console.WriteLine("Salary/pace idle: 300 real seconds, closed-input fixture, 90s existing refresh cadence; no UI/live services/Firebase.");
            while (clock.Elapsed.TotalSeconds < 300)
            {
                if (clock.Elapsed.TotalSeconds >= captures * 90)
                {
                    long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    delivery.Request("fixture:salary-idle", now, true);
                    bool ok = await delivery.PublishCapturedAsync(() =>
                    {
                        captures++;
                        var source = fixture.Capture();
                        int salary = fixture.Profiles.Keys.Sum(name =>
                            SalaryAccrualCalculator.Games(name, From, To, source).Amount +
                            SalaryAccrualCalculator.Products(name, From, To, source));
                        int games = source.Games(From, From.AddDays(1)).Sum(row => row.Amount);
                        var pace = FinancialPaceCalculator.CalculateDay(From, From.AddDays(1), From.AddHours(1) + clock.Elapsed,
                            salary, games, 36000, "OwnerForecast", "", Array.Empty<FinancialPacePoint>());
                        var day = new
                        {
                            businessDateKey = pace.BusinessDateKey, calculatedAt = pace.CalculatedAt.ToString("O"),
                            gameRevenue = pace.GameRevenue, totalExpense = pace.TotalExpense,
                            isClosed = pace.IsClosed, timeline = Array.Empty<object>()
                        };
                        var document = JsonSerializer.SerializeToNode(new
                        {
                            updatedAtUnixMs = now, salary,
                            financialPace = new { days = new[] { day } },
                            reportsByMonth = new Dictionary<string, object>
                            {
                                ["2026-10"] = new { financialPace = new { days = new[] { day } } }
                            },
                            history = new string('x', 7000000)
                        })!.AsObject();
                        return new(document);
                    }, patch =>
                    {
                        sends++; bytes += patch.Utf8.Length;
                        return Task.CompletedTask;
                    }, now);
                    if (!ok) throw new Exception("Salary idle publication failed");
                }
                await Task.Delay(250);
            }
            Equal(4, captures, "real five-minute refresh count");
            Equal(1, sends, "real five-minute closed-input calculation produces no redundant sends");
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                elapsedSeconds = Math.Round(clock.Elapsed.TotalSeconds, 3), captures, sends, bytes,
                redundantWrites = sends - 1,
                scope = "Pure salary/pace + mocked production-shaped delta paths, advancing pre-noon as-of clock over unchanged inputs. Excludes actual service/UI, command/presence, TLS and phone fan-out."
            }));
        }
        finally
        {
            foreach (string file in Directory.EnumerateFiles(folder)) File.Delete(file);
            Directory.Delete(folder);
        }
    }

    internal static void Run(bool benchmark = false)
    {
        LegacyNameMatching();
        var fixture = new SalaryAccrualFixture(430, 120);
        var source = fixture.Capture();
        var oracle = new SalaryAccrualLegacyOracle(fixture);
        var random = new Random(82731);
        string before = JsonSerializer.Serialize(new { fixture.CashRows, fixture.ProductRows, fixture.Policies, fixture.Profiles, fixture.Events });
        foreach (string employee in new[] { "Alice", "alice", "Bob", "", "Nobody" })
        {
            Compare(oracle.Games(employee, From, To), SalaryAccrualCalculator.Games(employee, From, To, source), "month games");
            Equal(oracle.Products(employee, From, To), SalaryAccrualCalculator.Products(employee, From, To, source), "month products");
            for (int day = -1; day <= 31; day++)
            {
                DateTime start = From.AddDays(day);
                Compare(oracle.Games(employee, start, start.AddDays(1)),
                    SalaryAccrualCalculator.Games(employee, start, start.AddDays(1), source), "daily games");
                Equal(oracle.Products(employee, start, start.AddDays(1)),
                    SalaryAccrualCalculator.Products(employee, start, start.AddDays(1), source), "daily products");
            }
            for (int trial = 0; trial < 35; trial++)
            {
                var intervals = Enumerable.Range(0, 7).Select(_ =>
                {
                    DateTime start = From.AddMinutes(random.Next(-1200, 44000));
                    return new PaidInterval(start, start.AddMinutes(random.Next(-5, 720)));
                }).ToList();
                double hours = intervals.Sum(item => Math.Max(0, (item.End - item.Start).TotalHours)) + trial % 3;
                var input = new SalaryAccrualLegacyOracle.Input(employee, hours, intervals);
                Compare(oracle.Time(input, From, To),
                    SalaryAccrualCalculator.Time(employee, hours, intervals, From, source), "time with rating/policy/recovered hours");
            }
        }
        Equal(oracle.GameFund(From, To), SalaryAccrualCalculator.GameFund(From, To, source), "club game fund");
        Equal(before, JsonSerializer.Serialize(new { fixture.CashRows, fixture.ProductRows, fixture.Policies, fixture.Profiles, fixture.Events }),
            "calculation does not mutate inputs");

        foreach (DateTime at in new[] { DateTime.MinValue, From.AddTicks(-1), From, From.AddDays(13), From.AddDays(13).AddTicks(1), To })
            Equal(fixture.PolicyAt(at).Id, source.Rules.PolicyAt(at).Id, "policy ties and fallback");
        foreach (string employee in fixture.Profiles.Keys)
        foreach (EmployeeRatingBranch branch in Enum.GetValues<EmployeeRatingBranch>())
        foreach (DateTime at in fixture.Boundaries(employee, From.AddMonths(-1), To.AddMonths(1))
                     .SelectMany(at => new[] { at.AddTicks(-1), at, at.AddTicks(1) }).Concat(new[] { From, To }))
        {
            var rating = source.Rules.Employee(employee).At(branch, at);
            Equal(fixture.Rating(employee, branch, at), rating.Percent, "exact rating boundary");
            Equal(fixture.Signature(employee, branch, at), rating.Signature, "exact accrual signature");
        }
        var unqueried = fixture.Capture();
        string frozen = JsonSerializer.Serialize(oracle.Games("Alice", From, To));
        fixture.CashRows.Clear(); fixture.ProductRows.Clear();
        foreach (var profile in fixture.Profiles.Values) profile.BaseVersions.Clear();
        foreach (var item in fixture.Events)
        {
            item.ChangePercent = 120;
            item.EffectiveFrom = To.AddYears(1);
            item.EndedAt = From.AddYears(-1);
        }
        fixture.Events.Clear();
        fixture.Policies[0].Settings.SalaryFundPercent = 0;
        Equal(frozen, Task.Run(() => JsonSerializer.Serialize(SalaryAccrualCalculator.Games("Alice", From, To, unqueried)))
            .GetAwaiter().GetResult(), "owned inputs remain usable on worker after live edits");
        Console.WriteLine("Salary accrual: exact 1.6.1 formula oracle, monthly/daily games/products/fund/time, refunds, recovered hours, rating/policy ties/boundaries and owned inputs passed; no live stores/UI/cloud.");
        if (benchmark) Benchmark();
    }

    private static void LegacyNameMatching()
    {
        var profile = new EmployeeRatingProfile
        {
            EmployeeId = "a", EmployeeName = "Alice",
            BaseVersions = new() { new() { EffectiveFrom = DateTime.MinValue } }
        };
        var events = new[]
        {
            new EmployeeRatingEvent { EmployeeName = "Alice", Branch = EmployeeRatingBranch.Time,
                EffectiveFrom = From.AddHours(1), ScheduledUntil = From.AddHours(3), ChangePercent = 15 },
            new EmployeeRatingEvent { EmployeeName = " Alice ", Branch = EmployeeRatingBranch.Time,
                EffectiveFrom = From.AddHours(2), ScheduledUntil = From.AddHours(4), ChangePercent = 9 }
        };
        var captured = new EmployeeRatingCalculation(profile, events, " Alice ");
        foreach (DateTime at in Enumerable.Range(0, 11).Select(index => From.AddMinutes(index * 30)))
        {
            var actual = captured.At(EmployeeRatingBranch.Time, at);
            int expected = EmployeeSalaryRuleEngine.ResolveRating(profile,
                events.Where(item => item.EmployeeName == "Alice"), EmployeeRatingBranch.Time, at);
            string signature = $"{profile.BaseVersions[0].Id:N}|" + string.Join(',', events
                .Where(item => item.EmployeeName == " Alice " && item.EffectiveFrom <= at)
                .OrderBy(item => item.EffectiveFrom).Select(item => $"{item.Id:N}:{item.EffectiveUntil:O}"));
            Equal(expected, actual.Percent, "legacy profile/request name asymmetry");
            Equal(signature, actual.Signature, "legacy requested-name signature");
        }
        Equal(string.Join(',', new[] { events[1].EffectiveFrom, events[1].EffectiveUntil }),
            string.Join(',', captured.Boundaries(From, To)), "legacy requested-name boundaries");
    }

    private static void Benchmark()
    {
        var fixture = new SalaryAccrualFixture(7377, 1600);
        var oracle = new SalaryAccrualLegacyOracle(fixture);
        double[] old = new double[5], capture = new double[5], calculation = new double[5];
        for (int trial = -1; trial < 5; trial++)
        {
            var watch = Stopwatch.StartNew();
            foreach (string employee in fixture.Profiles.Keys)
            for (int day = 0; day < 31; day++)
            {
                _ = oracle.Games(employee, From.AddDays(day), From.AddDays(day + 1));
                _ = oracle.Products(employee, From.AddDays(day), From.AddDays(day + 1));
            }
            double oldTime = watch.Elapsed.TotalMilliseconds;
            watch.Restart();
            var source = fixture.Capture();
            double captureTime = watch.Elapsed.TotalMilliseconds;
            watch.Restart();
            foreach (string employee in fixture.Profiles.Keys)
            for (int day = 0; day < 31; day++)
            {
                _ = SalaryAccrualCalculator.Games(employee, From.AddDays(day), From.AddDays(day + 1), source);
                _ = SalaryAccrualCalculator.Products(employee, From.AddDays(day), From.AddDays(day + 1), source);
            }
            if (trial >= 0) { old[trial] = oldTime; capture[trial] = captureTime; calculation[trial] = watch.Elapsed.TotalMilliseconds; }
        }
        static double Median(double[] values) => Math.Round(values.Order().ElementAt(2), 3);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            cashRows = fixture.CashRows.Count, productRows = fixture.ProductRows.Count,
            employees = fixture.Profiles.Count, days = 31,
            oldFormulaQueriesMilliseconds = Median(old), ownedSourceCaptureMilliseconds = Median(capture),
            indexedFormulaQueriesMilliseconds = Median(calculation),
            scope = "Synthetic accrual only; excludes live service capture, shift/bonus preparation, reconciliation, pace persistence, UI and network."
        }));
    }

    private static void Compare(RatingAccrualBreakdown expected, RatingAccrualBreakdown actual, string label)
        => Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual), label);

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception("Salary accrual: " + label);
    }
}

internal sealed class SalaryAccrualFixture
{
    internal readonly List<CashRecord> CashRows = new();
    internal readonly List<ProductServiceRevenueEntry> ProductRows = new();
    internal readonly List<SalaryPolicyVersion> Policies = new();
    internal readonly Dictionary<string, EmployeeRatingProfile> Profiles = new(StringComparer.OrdinalIgnoreCase);
    internal readonly List<EmployeeRatingEvent> Events = new();

    internal SalaryAccrualFixture(int cashCount, int productCount)
    {
        DateTime from = SalaryAccrualTests.From;
        var random = new Random(7435);
        foreach (int index in Enumerable.Range(0, 4))
            Policies.Add(new SalaryPolicyVersion
            {
                EffectiveFrom = index == 0 ? DateTime.MinValue : from.AddDays(index == 3 ? 13 : index * 6),
                CreatedAt = from.AddMinutes(index), Settings = new()
                {
                    TimeMonthlyFundAmount = 9000 + index * 31, TimeMonthlyPlannedHours = 240 - index,
                    SalaryFundPercent = 10 + index, ExpenseReservePercent = index * 3, ProductBonusPercent = index * 5
                }
            });
        Policies.Add(new SalaryPolicyVersion
        {
            EffectiveFrom = Policies[2].EffectiveFrom, CreatedAt = Policies[2].CreatedAt,
            Settings = new() { SalaryFundPercent = 99, TimeMonthlyFundAmount = 123, TimeMonthlyPlannedHours = 60 }
        });
        foreach (string name in new[] { "Alice", "Bob", "", "Nobody" })
        {
            var profile = new EmployeeRatingProfile
            {
                EmployeeId = "fixture:" + name, EmployeeName = name,
                BaseVersions = new()
                {
                    new() { EffectiveFrom = DateTime.MinValue, TimePercent = 100, RevenuePercent = 99 },
                    new() { EffectiveFrom = from.AddDays(11), TimePercent = 92, RevenuePercent = 110 }
                }
            };
            Profiles.Add(name, profile);
            for (int index = 0; index < 24; index++)
                Events.Add(new EmployeeRatingEvent
                {
                    EmployeeId = profile.EmployeeId, EmployeeName = name,
                    Branch = index % 2 == 0 ? EmployeeRatingBranch.Time : EmployeeRatingBranch.Revenue,
                    Direction = index % 3 == 0 ? EmployeeRatingEffectDirection.Reward : EmployeeRatingEffectDirection.Penalty,
                    EffectiveFrom = from.AddDays(index - 3).AddMinutes(index),
                    ScheduledUntil = from.AddDays(index + 1).AddMinutes(index),
                    EndedAt = index % 7 == 0 ? from.AddDays(index) : null,
                    Status = index % 7 == 0 ? EmployeeRatingEventStatus.Forgiven : EmployeeRatingEventStatus.Active,
                    ChangePercent = index % 5, BasePercentAtCreation = 100, TargetPercent = 96
                });
        }
        for (int index = 0; index < cashCount; index++)
        {
            DateTime at = from.AddMinutes(random.Next(-2000, 47000));
            CashRows.Add(new CashRecord
            {
                CreatedAt = index % 3 == 0 ? at.AddDays(2) : at,
                BusinessOccurredAt = index % 8 == 0 ? default : at,
                Category = index % 9 == 0 ? "Коррекция" : "Игры",
                IncomeEmployeeName = index % 3 == 0 ? "alice" : index % 2 == 0 ? "Bob" : "",
                Amount = index % 7 == 0 ? -120 : random.Next(0, 501)
            });
        }
        for (int index = 0; index < productCount; index++)
            ProductRows.Add(new()
            {
                OccurredAt = from.AddMinutes(random.Next(-2000, 47000)), EmployeeName = index % 2 == 0 ? "ALICE" : "Bob",
                Amount = index % 4 == 0 ? -50 : random.Next(0, 201)
            });
    }

    internal static DateTime BusinessTime(CashRecord row) => row.BusinessOccurredAt == default ? row.CreatedAt : row.BusinessOccurredAt;
    internal List<CashRecord> Cash(DateTime from, DateTime to) => CashRows
        .Where(row => BusinessTime(row) >= from && BusinessTime(row) < to).OrderByDescending(row => row.CreatedAt).ToList();
    internal List<ProductServiceRevenueEntry> Products(DateTime from, DateTime to, string employee) => ProductRows
        .Where(row => row.OccurredAt >= from && row.OccurredAt < to &&
            (string.IsNullOrWhiteSpace(employee) || row.EmployeeName.Equals(employee, StringComparison.OrdinalIgnoreCase)))
        .OrderByDescending(row => row.OccurredAt).ToList();
    internal SalaryPolicyVersion PolicyAt(DateTime at) => Policies.Where(item => item.EffectiveFrom <= at)
        .OrderByDescending(item => item.EffectiveFrom).ThenByDescending(item => item.CreatedAt).FirstOrDefault()
        ?? Policies.OrderBy(item => item.EffectiveFrom).First();
    internal IEnumerable<SalaryPolicyVersion> Versions(DateTime from, DateTime to) => Policies.Where(item => item.EffectiveFrom > from && item.EffectiveFrom < to);
    private IEnumerable<EmployeeRatingEvent> EmployeeEvents(string employee) => Events.Where(item =>
        item.EmployeeId.Equals(Profiles[employee].EmployeeId, StringComparison.OrdinalIgnoreCase) ||
        item.EmployeeName.Equals(employee, StringComparison.OrdinalIgnoreCase));
    internal int Rating(string employee, EmployeeRatingBranch branch, DateTime at)
        => EmployeeSalaryRuleEngine.ResolveRating(Profiles[employee], EmployeeEvents(employee), branch, at);
    internal string Signature(string employee, EmployeeRatingBranch branch, DateTime at)
    {
        Guid id = Profiles[employee].BaseVersions.Where(item => item.EffectiveFrom <= at)
            .OrderByDescending(item => item.EffectiveFrom).ThenByDescending(item => item.CreatedAt).Select(item => item.Id).FirstOrDefault();
        return $"{id:N}|" + string.Join(',', EmployeeEvents(employee).Where(item => item.Branch == branch && item.EffectiveFrom <= at)
            .OrderBy(item => item.EffectiveFrom).Select(item => $"{item.Id:N}:{item.EffectiveUntil:O}"));
    }
    internal IReadOnlyList<DateTime> Boundaries(string employee, DateTime from, DateTime to)
        => Profiles[employee].BaseVersions.Select(item => item.EffectiveFrom)
            .Concat(EmployeeEvents(employee).SelectMany(item => new[] { item.EffectiveFrom, item.EffectiveUntil }))
            .Where(at => at > from && at < to).Distinct().Order().ToArray();

    internal SalaryAccrualSource Capture() => new(
        CashRows.OrderByDescending(row => row.CreatedAt).Where(row => row.Category == "Игры")
            .Select(row => new SalaryGameRevenue(BusinessTime(row), row.IncomeEmployeeName, row.Amount)),
        ProductRows.OrderByDescending(row => row.OccurredAt).Select(row => new SalaryProductRevenue(row.OccurredAt, row.EmployeeName, row.Amount)),
        new SalaryAccrualRules(Policies, Profiles.Select(pair => new KeyValuePair<string, EmployeeRatingCalculation>(pair.Key,
            new(pair.Value, EmployeeEvents(pair.Key))))));
}
