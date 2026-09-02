using ClubTimerXbox.Models;
using ClubTimerXbox.Services;
using ClubTimerUpdater;
using System.Text.Json;

if (args.Contains("--cash", StringComparer.OrdinalIgnoreCase))
{
    new CashConstitutionTestSuite().Run();
    return;
}

var suite = new CashConstitutionTestSuite();
suite.Run();
new BusinessCalendarTestSuite().Run();
new EmployeeSalaryRuleTestSuite().Run();
new NextUpdateWorkflowTestSuite().Run();
new AppUpdateTestSuite().Run();

internal sealed class EmployeeSalaryRuleTestSuite
{
    private int _passed;
    private static readonly DateTime Start = new(2026, 8, 5, 6, 0, 0);

    public void Run()
    {
        Test("overall rating rounds .5 upward", OverallRatingRounding);
        Test("overlapping penalties add together", OverlappingPenaltiesAdd);
        Test("overlapping rewards add together", OverlappingRewardsAdd);
        Test("expired event leaves the longer event active", ExpiredEventLeavesLongerEventActive);
        Test("time and revenue ratings stay independent", BranchesStayIndependent);
        Test("forgiveness preserves past and restores future", ForgivenessIsProspective);
        Test("rating changes only future game earnings", RatingChangeIsProspective);
        Test("rating financial effect keeps rewards and losses separate", RatingFinancialEffectIsExact);
        Test("club policy changes only future earnings", PolicyChangeIsProspective);
        Test("product bonus ignores employee rating", ProductBonusIgnoresRating);
        Test("over-plan bonus requires two hours", OverNormBonusRequiresTwoHours);
        Test("over-plan bonus follows worked hours", OverNormBonusFollowsWorkedHours);
        Test("rating is capped at 120 percent", RatingCap);
        Test("closed month archive detects later changes", ArchiveDetectsTampering);
        Test("raw history deletion waits six months", ArchiveRetentionGate);
        Test("penalty reduces a lower base rating by its own value", PenaltyReducesLowerBase);
        Test("reward raises rating and expiry restores base", RewardExpiresWithoutReverseMath);
        Test("overlapping reward and penalty use their net value", RewardAndPenaltyNet);
        Test("positive automatic ratings last 50 percent longer", PositiveRuleDurationsAreExtended);
        Test("confirmed shift extra rewards all allowed cash combinations", ConfirmedShiftExtraQualification);
        Test("opening penalty uses current 50 som steps", OpeningPenaltySteps);
        Test("one-minute late session does not earn bonus", LateSessionRequiresTwentyMinutes);
        Test("unattended timer follows 01:00 and last client", UnattendedTimerFollowsLastClient);
        Test("expired TV keeps five complete grace minutes", ExpiredTvGraceBoundary);
        Test("expired TV charges one som per completed late minute", ExpiredTvMinuteCharge);
        Test("simultaneous expired TVs charge independently", ExpiredTvParallelCharge);
        Test("expired TV penalty is a violation, not a cash loss", ExpiredTvIsViolation);
        Console.WriteLine();
        Console.WriteLine($"PASS: {_passed} individual salary and rating scenarios.");
    }

    private void OverallRatingRounding()
    {
        Equal(96, EmployeeSalaryRuleEngine.CalculateOverallRating(91, 100), "overall");
    }

    private void OverlappingPenaltiesAdd()
    {
        var profile = Profile(100, 100);
        var events = new[]
        {
            Event(EmployeeRatingBranch.Time, 97, Start, Start.AddDays(1)),
            Event(EmployeeRatingBranch.Time, 95, Start.AddHours(1), Start.AddDays(3))
        };
        Equal(92, EmployeeSalaryRuleEngine.ResolveRating(
            profile, events, EmployeeRatingBranch.Time, Start.AddHours(2)), "sum");
    }

    private void OverlappingRewardsAdd()
    {
        var profile = Profile(100, 100);
        var events = new[]
        {
            Event(EmployeeRatingBranch.Time, 103, Start, Start.AddDays(1), EmployeeRatingEffectDirection.Reward),
            Event(EmployeeRatingBranch.Time, 105, Start.AddHours(1), Start.AddDays(3), EmployeeRatingEffectDirection.Reward)
        };
        Equal(108, EmployeeSalaryRuleEngine.ResolveRating(
            profile, events, EmployeeRatingBranch.Time, Start.AddHours(2)), "sum");
    }

    private void ExpiredEventLeavesLongerEventActive()
    {
        var profile = Profile(100, 100);
        var events = new[]
        {
            Event(EmployeeRatingBranch.Time, 97, Start, Start.AddDays(1)),
            Event(EmployeeRatingBranch.Time, 95, Start, Start.AddDays(3))
        };
        Equal(92, EmployeeSalaryRuleEngine.ResolveRating(
            profile, events, EmployeeRatingBranch.Time, Start.AddHours(12)), "first day");
        Equal(95, EmployeeSalaryRuleEngine.ResolveRating(
            profile, events, EmployeeRatingBranch.Time, Start.AddDays(2)), "second day");
    }

    private void BranchesStayIndependent()
    {
        var profile = Profile(100, 100);
        var events = new[]
        {
            Event(EmployeeRatingBranch.Time, 90, Start, Start.AddDays(3))
        };
        Equal(90, EmployeeSalaryRuleEngine.ResolveRating(
            profile, events, EmployeeRatingBranch.Time, Start.AddHours(1)), "time");
        Equal(100, EmployeeSalaryRuleEngine.ResolveRating(
            profile, events, EmployeeRatingBranch.Revenue, Start.AddHours(1)), "revenue");
    }

    private void ForgivenessIsProspective()
    {
        var profile = Profile(100, 100);
        var item = Event(EmployeeRatingBranch.Time, 90, Start, Start.AddDays(3));
        item.EndedAt = Start.AddHours(6);
        item.Status = EmployeeRatingEventStatus.Forgiven;
        Equal(90, EmployeeSalaryRuleEngine.ResolveRating(
            profile, new[] { item }, EmployeeRatingBranch.Time, Start.AddHours(5)), "past");
        Equal(100, EmployeeSalaryRuleEngine.ResolveRating(
            profile, new[] { item }, EmployeeRatingBranch.Time, Start.AddHours(7)), "future");
    }

    private void RatingChangeIsProspective()
    {
        var settings = Settings(47, 25, 30, 2);
        double earnedBefore = EmployeeSalaryRuleEngine.CalculateGameAccrual(1000, settings, 100);
        double earnedAfter = EmployeeSalaryRuleEngine.CalculateGameAccrual(1000, settings, 90);
        Equal(175d, earnedBefore, "old earning");
        Equal(157.5d, earnedAfter, "new earning");
        Equal(332.5d, earnedBefore + earnedAfter, "fixed total");
    }

    private void RatingFinancialEffectIsExact()
    {
        RatingFinancialEffect reward =
            EmployeeSalaryRuleEngine.CalculateRatingFinancialEffect(1000, 108);
        RatingFinancialEffect penalty =
            EmployeeSalaryRuleEngine.CalculateRatingFinancialEffect(1000, 95);

        Equal(1080, reward.ActualAmount, "rewarded actual amount");
        Equal(80, reward.EarnedAmount, "rewarded extra amount");
        Equal(0, reward.LostAmount, "rewarded lost amount");
        Equal(950, penalty.ActualAmount, "penalized actual amount");
        Equal(0, penalty.EarnedAmount, "penalized extra amount");
        Equal(50, penalty.LostAmount, "penalized lost amount");
        Equal(30, reward.NetAmount + penalty.NetAmount, "combined net effect");
    }

    private void PolicyChangeIsProspective()
    {
        var oldSettings = Settings(47, 25, 30, 2);
        var newSettings = Settings(60, 20, 30, 2);
        double oldTime = EmployeeSalaryRuleEngine.CalculateTimeAccrual(2, oldSettings, 100);
        double newTime = EmployeeSalaryRuleEngine.CalculateTimeAccrual(2, newSettings, 100);
        Equal(94d, oldTime, "old rate");
        Equal(120d, newTime, "new rate");
        Equal(214d, oldTime + newTime, "combined");
    }

    private void ExpiredTvGraceBoundary()
    {
        Equal(0, ExpiredSessionPenaltyService.CalculateChargeableMinutes(
            Start, Start.AddMinutes(5).AddSeconds(59)), "5:59");
        Equal(1, ExpiredSessionPenaltyService.CalculateChargeableMinutes(
            Start, Start.AddMinutes(6)), "6:00");
    }

    private void ExpiredTvMinuteCharge()
    {
        Equal(2, ExpiredSessionPenaltyService.CalculateChargeableMinutes(
            Start, Start.AddMinutes(7).AddSeconds(59)), "7:59");
        Equal(3, ExpiredSessionPenaltyService.CalculateChargeableMinutes(
            Start, Start.AddMinutes(8)), "8:00");
    }

    private void ExpiredTvParallelCharge()
    {
        int tv1 = ExpiredSessionPenaltyService.CalculateChargeableMinutes(
            Start, Start.AddMinutes(8));
        int tv2 = ExpiredSessionPenaltyService.CalculateChargeableMinutes(
            Start.AddMinutes(1), Start.AddMinutes(8));
        Equal(5, tv1 + tv2, "two TVs");
    }

    private void ExpiredTvIsViolation()
    {
        var item = new EmployeeLossItem
        {
            LossKind = "violation",
            LossType = "Нарушение правил",
            Title = "ТВ 2: не остановлен после тарифа",
            Amount = 3,
            IsFixed = true
        };
        True(EmployeeLossService.IsViolationLoss(item), "violation branch");
        True(!EmployeeLossService.IsMoneyLoss(item), "cash branch isolated");
    }

    private void ProductBonusIgnoresRating()
    {
        var settings = Settings(47, 25, 30, 2);
        Equal(20d, EmployeeSalaryRuleEngine.CalculateProductBonus(1000, settings), "product bonus");
    }

    private void OverNormBonusRequiresTwoHours()
    {
        IReadOnlyDictionary<string, int> result = OverNormBonusAllocator.Allocate(
            500,
            new[]
            {
                new OverNormBonusParticipant("Короткий вход", 1.99),
                new OverNormBonusParticipant("Сотрудник", 2.0)
            });

        True(!result.ContainsKey("Короткий вход"), "короткий вход исключён");
        Equal(500, result["Сотрудник"], "ровно два часа участвуют");
    }

    private void OverNormBonusFollowsWorkedHours()
    {
        IReadOnlyDictionary<string, int> result = OverNormBonusAllocator.Allocate(
            500,
            new[]
            {
                new OverNormBonusParticipant("Арген", 8),
                new OverNormBonusParticipant("Мирбек", 2)
            });

        Equal(400, result["Арген"], "восемь часов");
        Equal(100, result["Мирбек"], "два часа");
        Equal(500, result.Values.Sum(), "фонд сохранён полностью");
    }

    private void RatingCap()
    {
        var profile = Profile(150, 150);
        Equal(120, EmployeeSalaryRuleEngine.ResolveRating(
            profile, Array.Empty<EmployeeRatingEvent>(), EmployeeRatingBranch.Time, Start), "cap");
    }

    private void PenaltyReducesLowerBase()
    {
        var profile = Profile(90, 100);
        var penalty = Event(
            EmployeeRatingBranch.Time,
            95,
            Start,
            Start.AddDays(1),
            EmployeeRatingEffectDirection.Penalty);
        Equal(85, EmployeeSalaryRuleEngine.ResolveRating(
            profile, new[] { penalty }, EmployeeRatingBranch.Time, Start.AddHours(1)), "penalty value");
    }

    private void RewardExpiresWithoutReverseMath()
    {
        var profile = Profile(100, 100);
        var reward = Event(
            EmployeeRatingBranch.Time,
            105,
            Start,
            Start.AddHours(12),
            EmployeeRatingEffectDirection.Reward);
        Equal(105, EmployeeSalaryRuleEngine.ResolveRating(
            profile, new[] { reward }, EmployeeRatingBranch.Time, Start.AddHours(1)), "reward");
        Equal(100, EmployeeSalaryRuleEngine.ResolveRating(
            profile, new[] { reward }, EmployeeRatingBranch.Time, Start.AddHours(13)), "expired");
    }

    private void RewardAndPenaltyNet()
    {
        var profile = Profile(100, 100);
        var events = new[]
        {
            Event(EmployeeRatingBranch.Time, 103, Start, Start.AddDays(1), EmployeeRatingEffectDirection.Reward),
            Event(EmployeeRatingBranch.Time, 95, Start, Start.AddHours(6), EmployeeRatingEffectDirection.Penalty)
        };
        Equal(98, EmployeeSalaryRuleEngine.ResolveRating(
            profile, events, EmployeeRatingBranch.Time, Start.AddHours(1)), "net value");
    }

    private void PositiveRuleDurationsAreExtended()
    {
        var expected = new Dictionary<string, (int Version, TimeSpan Duration)>
        {
            ["TIME_FIRST_OPEN_PUNCTUAL"] = (3, TimeSpan.FromHours(36)),
            ["TIME_FIRST_OPEN_SLIGHTLY_LATE"] = (2, TimeSpan.FromHours(12)),
            ["TIME_FIRST_OPEN_LATE"] = (2, TimeSpan.FromHours(24)),
            ["TIME_PC_LEFT_UNATTENDED"] = (2, TimeSpan.FromDays(4)),
            ["TIME_EXPIRED_TV_UNATTENDED"] = (2, TimeSpan.FromDays(2)),
            ["TIME_LATE_CLIENT_REWARD"] = (4, TimeSpan.FromDays(3)),
            ["TIME_CONFIRMED_SHIFT_EXTRA"] = (3, TimeSpan.FromHours(36)),
            ["REVENUE_CONFIRMED_LOSS_SMALL"] = (2, TimeSpan.FromDays(4)),
            ["REVENUE_CONFIRMED_LOSS_LARGE"] = (2, TimeSpan.FromDays(4)),
            ["REVENUE_CONFIRMED_EXTRA"] = (3, TimeSpan.FromDays(3)),
            ["TIME_OTHER_VIOLATION"] = (2, TimeSpan.FromDays(2))
        };

        foreach (var pair in expected)
        {
            EmployeeRatingRuleDefinition rule = EmployeeRatingRuleCatalog.Get(pair.Key);
            Equal(pair.Value.Version, rule.Version, $"{pair.Key} version");
            Equal(pair.Value.Duration, rule.Duration, $"{pair.Key} duration");
        }

        Equal(
            5,
            EmployeeRatingRuleCatalog.Get("TIME_LATE_CLIENT_REWARD").ChangePercent,
            "late client reward percent"
        );
    }

    private void ConfirmedShiftExtraQualification()
    {
        var shortageCovered = QualifyConfirmedShiftExtra(-100, 150);
        Equal(1, shortageCovered.Count, "-100 cash and +150 cashless");
        Equal(50, shortageCovered.Single().NetExtraAmount, "covered net extra");

        var cashExact = QualifyConfirmedShiftExtra(0, 50);
        Equal(1, cashExact.Count, "zero cash difference and +50 cashless");

        var cashExtra = QualifyConfirmedShiftExtra(101, -50);
        Equal(1, cashExtra.Count, "+101 cash and -50 cashless");
        Equal(51, cashExtra.Single().NetExtraAmount, "cash extra net");

        var excessiveShortage = QualifyConfirmedShiftExtra(-101, 200);
        Equal(0, excessiveShortage.Count, "cash shortage above 100 is rejected");

        var belowThreshold = QualifyConfirmedShiftExtra(0, 49);
        Equal(0, belowThreshold.Count, "net extra below 50 is rejected");

        var ambiguous = new List<CashReconciliationItem>();
        DateTime monthStart = new(2026, 8, 1);
        DateTime nextMonthStart = monthStart.AddMonths(1);
        CashConstitutionEngine.RecordCashAcceptance(
            ambiguous,
            monthStart,
            nextMonthStart,
            Start,
            "Next employee",
            "Employee 1",
            1000,
            1050,
            "First acceptance"
        );
        CashConstitutionEngine.RecordCashAcceptance(
            ambiguous,
            monthStart,
            nextMonthStart,
            Start.AddSeconds(1),
            "Next employee",
            "Employee 1",
            1000,
            1050,
            "Corrected acceptance"
        );
        CashConstitutionEngine.RecordCashlessVerification(
            ambiguous,
            monthStart,
            nextMonthStart,
            Start.AddMinutes(1),
            2000,
            2050,
            "Employee 1",
            "Shared verification"
        );
        Equal(
            0,
            EmployeeRatingService.FindConfirmedShiftExtraRewards(ambiguous).Count,
            "one verification cannot reward two acceptances"
        );
    }

    private static IReadOnlyList<ConfirmedShiftExtraReward>
        QualifyConfirmedShiftExtra(
            int cashDifference,
            int cashlessDifference)
    {
        var items = new List<CashReconciliationItem>();
        DateTime monthStart = new(2026, 8, 1);
        DateTime nextMonthStart = monthStart.AddMonths(1);
        CashConstitutionEngine.RecordCashAcceptance(
            items,
            monthStart,
            nextMonthStart,
            Start,
            "Next employee",
            "Employee 1",
            expectedAmount: 1000,
            actualAmount: 1000 + cashDifference,
            note: "Acceptance"
        );
        CashConstitutionEngine.RecordCashlessVerification(
            items,
            monthStart,
            nextMonthStart,
            Start.AddMinutes(1),
            expectedAmount: 2000,
            actualAmount: 2000 + cashlessDifference,
            suspectedEmployeeName: "Employee 1",
            note: "Cashless verification"
        );

        return EmployeeRatingService.FindConfirmedShiftExtraRewards(items);
    }

    private void OpeningPenaltySteps()
    {
        var settings = new AutoSalarySettings
        {
            LateOpeningGraceMinutes = 30,
            LateOpeningPenaltyStepMinutes = 30,
            LateOpeningPenaltyStepAmount = 50,
            LateOpeningMaxAutoMinutes = 150
        };
        DateTime opening = new(2026, 8, 5, 11, 0, 0);
        Equal(0, LateOpeningPenaltyService.CalculatePenaltyAmount(
            opening.AddMinutes(30), opening, settings), "11:30");
        Equal(50, LateOpeningPenaltyService.CalculatePenaltyAmount(
            opening.AddMinutes(31), opening, settings), "11:31");
        Equal(200, LateOpeningPenaltyService.CalculatePenaltyAmount(
            opening.AddHours(5), opening, settings), "cap");
    }

    private void LateSessionRequiresTwentyMinutes()
    {
        DateTime oneAm = new(2026, 8, 6, 1, 0, 0);
        True(!EmployeeNightRatingService.IsQualifiedLateSession(
            oneAm.AddMinutes(-1), oneAm.AddMinutes(1), oneAm), "00:59 exploit");
        True(EmployeeNightRatingService.IsQualifiedLateSession(
            oneAm.AddMinutes(-20), oneAm.AddMinutes(1), oneAm), "twenty minutes");
    }

    private void UnattendedTimerFollowsLastClient()
    {
        DateTime oneAm = new(2026, 8, 6, 1, 0, 0);
        Equal(new DateTime(2026, 8, 6, 3, 0, 0),
            EmployeeNightRatingService.CalculateUnattendedViolationAt(
                oneAm, oneAm.AddHours(-10), oneAm.AddHours(-2)), "client left at 23");
        Equal(new DateTime(2026, 8, 6, 4, 0, 0),
            EmployeeNightRatingService.CalculateUnattendedViolationAt(
                oneAm, oneAm.AddHours(-10), oneAm.AddHours(1)), "client left at 02");
    }

    private void ArchiveDetectsTampering()
    {
        var month = ArchivedMonth();
        BusinessArchiveService.Seal(month, new DateTime(2026, 9, 1, 6, 0, 0));
        True(BusinessArchiveService.Verify(month), "sealed");
        month.GameRevenue++;
        True(!BusinessArchiveService.Verify(month), "tampered");

        month = ArchivedMonth();
        BusinessArchiveService.Seal(month, new DateTime(2026, 9, 1, 6, 0, 0));
        month.Payroll[0].DailyEarnings[0].TimeAmount++;
        True(!BusinessArchiveService.Verify(month), "daily earning tampered");
    }

    private void ArchiveRetentionGate()
    {
        var month = ArchivedMonth();
        BusinessArchiveService.Seal(month, new DateTime(2026, 9, 1, 6, 0, 0));
        True(!BusinessArchiveService.CanDeleteRawMonth(
            month, new DateTime(2027, 1, 1), new DateTime(2026, 8, 1)), "five months");
        True(BusinessArchiveService.CanDeleteRawMonth(
            month, new DateTime(2027, 2, 1), new DateTime(2026, 8, 1)), "six months");
    }

    private static BusinessMonthLedger ArchivedMonth() => new()
    {
        MonthKey = "2026-08",
        IsClosed = true,
        GameRevenue = 1000,
        Payroll = new List<EmployeePayrollObligation>
        {
            new()
            {
                EmployeeId = "emp_test",
                EmployeeName = "Test",
                AccruedAmount = 100,
                DailyEarnings = new List<AutoSalaryDayEarning>
                {
                    new()
                    {
                        Date = new DateTime(2026, 8, 31),
                        TimeAmount = 100,
                        TimeRatingPercents = new List<int> { 100 }
                    }
                }
            }
        }
    };

    private static EmployeeRatingProfile Profile(int time, int revenue) => new()
    {
        EmployeeId = "emp_test",
        EmployeeName = "Test",
        BaseVersions = new List<EmployeeRatingBaseVersion>
        {
            new()
            {
                EffectiveFrom = DateTime.MinValue,
                TimePercent = time,
                RevenuePercent = revenue
            }
        }
    };

    private static EmployeeRatingEvent Event(
        EmployeeRatingBranch branch,
        int target,
        DateTime from,
        DateTime until,
        EmployeeRatingEffectDirection direction = EmployeeRatingEffectDirection.Penalty) => new()
    {
        EmployeeId = "emp_test",
        EmployeeName = "Test",
        Branch = branch,
        Direction = direction,
        ChangePercent = Math.Abs(target - 100),
        BasePercentAtCreation = 100,
        TargetPercent = target,
        EffectiveFrom = from,
        ScheduledUntil = until
    };

    private static AutoSalarySettings Settings(
        int hourlyRate,
        int salaryPercent,
        int reservePercent,
        int productPercent) => new()
    {
        TimeMonthlyFundAmount = hourlyRate * 420,
        TimeMonthlyPlannedHours = 420,
        SalaryFundPercent = salaryPercent,
        ExpenseReservePercent = reservePercent,
        ProductBonusPercent = productPercent
    };

    private void Test(string name, Action action)
    {
        action();
        _passed++;
        Console.WriteLine($"PASS: {name}");
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{label}: expected {expected}, actual {actual}");
    }

    private static void True(bool value, string label)
    {
        if (!value)
            throw new InvalidOperationException($"{label}: expected true");
    }
}

internal sealed class AppUpdateTestSuite
{
    private int _passed;

    public void Run()
    {
        Test("Повреждённый пакет не касается рабочей версии", CorruptPackageDoesNotTouchTarget);
        Test("Проверенный пакет устанавливается целиком", ValidPackageInstalls);
        Test("Ошибка во время замены возвращает старую версию", CommitFailureRollsBack);
        Test("Более новая версия вытесняет скачанный пакет", NewerReleaseReplacesPrepared);
        Test("Новый SHA той же версии вытесняет скачанный пакет", RepackedReleaseReplacesPrepared);
        Test("Готовый пакет используется после оборванного состояния", ExistingPackageSurvivesInterruptedState);
        Test("Скачанный пакет освобождается перед публикацией", DownloadedPackageIsReleasedBeforePromotion);
        Test("Старые пакеты удаляются, активный сохраняется", OldDownloadsAreRemovedButActivePackageIsKept);
        Console.WriteLine();
        Console.WriteLine($"PASS: {_passed} сценариев безопасного обновления.");
    }

    private void CorruptPackageDoesNotTouchTarget()
    {
        using var sandbox = new UpdateSandbox();
        UpdateTransactionRequest request = sandbox.CreateRequest("wrong-hash");
        bool rejected = false;
        try
        {
            using PreparedUpdatePackage _ = UpdateTransactionEngine.PreparePackage(request);
        }
        catch (InvalidOperationException)
        {
            rejected = true;
        }

        True(rejected, "пакет отклонён");
        Equal("old-main", File.ReadAllText(Path.Combine(sandbox.TargetDir, "ClubTimerXbox.exe")), "рабочий exe");
        Equal("old-library", File.ReadAllText(Path.Combine(sandbox.TargetDir, "core.dll")), "рабочая библиотека");
    }

    private void ValidPackageInstalls()
    {
        using var sandbox = new UpdateSandbox();
        UpdateTransactionRequest request = sandbox.CreateRequest(sandbox.PackageSha256);
        using PreparedUpdatePackage prepared = UpdateTransactionEngine.PreparePackage(request);
        string backup = UpdateTransactionEngine.InstallPrepared(prepared, request);

        Equal("new-main", File.ReadAllText(Path.Combine(sandbox.TargetDir, "ClubTimerXbox.exe")), "новый exe");
        Equal("new-library", File.ReadAllText(Path.Combine(sandbox.TargetDir, "core.dll")), "новая библиотека");
        Equal("old-main", File.ReadAllText(Path.Combine(backup, "ClubTimerXbox.exe")), "резервный exe");
        True(!File.Exists(request.JournalPath), "журнал завершённой транзакции удалён");
    }

    private void CommitFailureRollsBack()
    {
        using var sandbox = new UpdateSandbox();
        UpdateTransactionRequest request = sandbox.CreateRequest(sandbox.PackageSha256);
        using PreparedUpdatePackage prepared = UpdateTransactionEngine.PreparePackage(request);
        bool rolledBack = false;
        try
        {
            UpdateTransactionEngine.InstallPrepared(
                prepared,
                request,
                failureHook: phase =>
                {
                    if (phase == UpdateTransactionPhase.Committing)
                        throw new IOException("simulated write failure");
                });
        }
        catch (UpdateRolledBackException)
        {
            rolledBack = true;
        }

        True(rolledBack, "откат выполнен");
        Equal("old-main", File.ReadAllText(Path.Combine(sandbox.TargetDir, "ClubTimerXbox.exe")), "exe после отката");
        Equal("old-library", File.ReadAllText(Path.Combine(sandbox.TargetDir, "core.dll")), "библиотека после отката");
    }

    private void NewerReleaseReplacesPrepared()
    {
        True(AppUpdateService.ShouldReplacePreparedPackage(
            "1.4.3", "new", "url-new", "1.4.2", "old", "url-old"), "новая версия");
        True(!AppUpdateService.ShouldReplacePreparedPackage(
            "1.4.1", "older", "url", "1.4.2", "newer", "url"), "старая версия");
    }

    private void RepackedReleaseReplacesPrepared()
    {
        True(AppUpdateService.ShouldReplacePreparedPackage(
            "1.4.2", "new-sha", "same-url", "1.4.2", "old-sha", "same-url"), "новый SHA");
        True(!AppUpdateService.ShouldReplacePreparedPackage(
            "1.4.2", "same", "same-url", "1.4.2", "same", "same-url"), "тот же пакет");
    }

    private void ExistingPackageSurvivesInterruptedState()
    {
        using var sandbox = new UpdateSandbox();
        long size = new FileInfo(sandbox.PackagePath).Length;

        True(AppUpdateService.IsReusablePreparedPackageAsync(
            sandbox.PackagePath,
            size,
            sandbox.PackageSha256).GetAwaiter().GetResult(), "целый пакет используется повторно");
        True(!AppUpdateService.IsReusablePreparedPackageAsync(
            sandbox.PackagePath,
            size + 1,
            sandbox.PackageSha256).GetAwaiter().GetResult(), "неверный размер отклонён");
        True(!AppUpdateService.IsReusablePreparedPackageAsync(
            sandbox.PackagePath,
            size,
            "wrong-sha").GetAwaiter().GetResult(), "неверный SHA отклонён");
    }

    private void DownloadedPackageIsReleasedBeforePromotion()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "ClubTimerDownloadTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string partialPath = Path.Combine(root, "update.zip.partial");
            string packagePath = Path.Combine(root, "update.zip");
            byte[] payload = new byte[512 * 1024];
            Random.Shared.NextBytes(payload);
            using var source = new MemoryStream(payload);

            long written = AppUpdateService.WritePackageFileAsync(
                source,
                partialPath).GetAwaiter().GetResult();
            File.Move(partialPath, packagePath);

            Equal((long)payload.Length, written, "размер записанного пакета");
            True(File.Exists(packagePath), "временный файл переименован без блокировки");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private void OldDownloadsAreRemovedButActivePackageIsKept()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "ClubTimerCleanupTests",
            Guid.NewGuid().ToString("N"));
        string oldOne = Path.Combine(root, "1.4.1");
        string oldTwo = Path.Combine(root, "1.4.2");
        string active = Path.Combine(root, "1.4.4");
        Directory.CreateDirectory(oldOne);
        Directory.CreateDirectory(oldTwo);
        Directory.CreateDirectory(active);
        File.WriteAllBytes(Path.Combine(oldOne, "old-one.zip"), new byte[10]);
        File.WriteAllBytes(Path.Combine(oldTwo, "old-two.zip"), new byte[20]);
        File.WriteAllBytes(Path.Combine(active, "active.zip"), new byte[30]);

        try
        {
            AppUpdateService.UpdateCleanupResult result =
                AppUpdateService.CleanupDownloadDirectories(root, active);

            Equal(2, result.DeletedDirectories, "удалённые папки");
            Equal(2, result.DeletedFiles, "удалённые файлы");
            Equal(30L, result.FreedBytes, "освобождённые байты");
            Equal(1, result.ProtectedDirectories, "защищённые папки");
            True(!Directory.Exists(oldOne), "первый старый пакет удалён");
            True(!Directory.Exists(oldTwo), "второй старый пакет удалён");
            True(Directory.Exists(active), "активный пакет сохранён");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private void Test(string title, Action action)
    {
        action();
        _passed++;
        Console.WriteLine($"PASS: {title}");
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception($"{label}: ожидалось {expected}, получено {actual}");
    }

    private static void True(bool value, string label)
    {
        if (!value)
            throw new Exception($"{label}: ожидалось true");
    }

    private sealed class UpdateSandbox : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(),
            "ClubTimerUpdateTests",
            Guid.NewGuid().ToString("N"));

        public string TargetDir { get; }
        public string PackagePath { get; }
        public string PackageSha256 { get; }

        public UpdateSandbox()
        {
            TargetDir = Path.Combine(_root, "app");
            string packageSource = Path.Combine(_root, "package");
            PackagePath = Path.Combine(_root, "update.zip");
            Directory.CreateDirectory(TargetDir);
            Directory.CreateDirectory(packageSource);
            File.WriteAllText(Path.Combine(TargetDir, "ClubTimerXbox.exe"), "old-main");
            File.WriteAllText(Path.Combine(TargetDir, "core.dll"), "old-library");
            File.WriteAllText(Path.Combine(packageSource, "ClubTimerXbox.exe"), "new-main");
            File.WriteAllText(Path.Combine(packageSource, "core.dll"), "new-library");
            System.IO.Compression.ZipFile.CreateFromDirectory(packageSource, PackagePath);
            using FileStream stream = File.OpenRead(PackagePath);
            PackageSha256 = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(stream)).ToLowerInvariant();
        }

        public UpdateTransactionRequest CreateRequest(string sha256) => new UpdateTransactionRequest
        {
            PackagePath = PackagePath,
            ExpectedSha256 = sha256,
            TargetDir = TargetDir,
            BackupRoot = Path.Combine(_root, "backups"),
            JournalPath = Path.Combine(_root, "update-transaction.json"),
            MainExe = "ClubTimerXbox.exe",
            Version = "9.9.9",
            RecoveryUpdaterPath = ""
        };

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_root))
                    Directory.Delete(_root, recursive: true);
            }
            catch
            {
            }
        }
    }
}

internal sealed class BusinessCalendarTestSuite
{
    private int _passed;

    public void Run()
    {
        Test("Рабочий день меняется ровно в 06:00", BusinessDayBoundary);
        Test("Рабочий месяц меняется 1 числа в 06:00", BusinessMonthBoundary);
        Test("Постоянный расход плавно растёт с 11 до 1", FinancialPaceExpenseRamp);
        Test("Новая база темпа сразу догоняет текущий час дня", FinancialPaceBaselineStartsToday);
        Test("Финансовый процент сравнивает игры и расходы", FinancialPacePercent);
        Test("Темп учитывает начисление сотруднику без рабочих часов", FinancialPaceIncludesBonusWithoutHours);
        Test("Предварительная приёмка сохраняет весь снимок для телефона", CashAcceptanceOwnerSnapshot);
        Test("Смена делится по рабочим месяцам", ShiftIsSplitAtBoundary);
        Test("Ручные часы не меняют системные часы после теста", ManualClockIsScoped);
        Test("Закрытие месяца не обнуляет наличные и безнал", ContinuousBalancesSurviveClose);
        Test("Закрытие месяца после сбоя продолжается без дублей", MonthCloseResumesAfterEveryStep);
        Test("Откат часов не открывает закрытый месяц", ClockRollbackDoesNotReopenMonth);
        Test("Недостача без рабочих часов сохраняется", NoHoursDefersUnknownShortage);
        Test("Недостача распределяется пропорционально 99 к 1", ShortageUsesWorkedHours);
        Test("Зарплата выплачивается от старых обязательств к новым", SalaryPaymentUsesFifo);
        Test("Выплата сверх остатка запрещена", SalaryOverpaymentIsBlocked);
        Test("Себестоимость прихода считается средневзвешенно", WeightedInventoryCost);
        Test("Прибыль использует себестоимость проданного товара", ProfitUsesCostOfGoodsSold);
        Test("Убыток уменьшает накопленный доход владельца", LossReducesRetainedIncome);
        Test("Миграционный снимок не удваивает прибыль месяца", ActivationSnapshotPreventsDoubleProfit);
        Test("Отрицательный остаток сотрудника переносится", NegativeEmployeeBalanceSurvivesClose);
        Test("Снятие владельца сначала использует текущий месяц", OwnerWithdrawalUsesCurrentMonthFirst);
        Test("Снятие владельца затем использует переходящий остаток", OwnerWithdrawalThenUsesCarriedBalance);
        Test("Остаток владельца показывается последней секундой прошлого месяца", OwnerWithdrawalCarryUsesSourceMonthDisplayTime);
        Test("Прошлый месяц снимает только перенесённый остаток", OwnerWithdrawalCarryOnlyUsesPreviousMonth);
        Test("Снятие сверх физически доступной суммы запрещено", OwnerWithdrawalOverBalanceIsBlocked);
#if DEBUG
        Test("DEBUG-стенд полностью изолирован от Firebase и AppData", DebugHarnessIsIsolated);
        Test("Выплата зарплаты физически уменьшает выбранную кассу", SalaryPaymentMovesMoney);
        Test("Вывод владельца не ограничен закрытой прибылью", OwnerWithdrawalCanPrecedeMonthClose);
#endif

        Console.WriteLine();
        Console.WriteLine($"PASS: {_passed} сценариев рабочего календаря и бюджета.");
    }

    private void BusinessDayBoundary()
    {
        Equal("2026-07-31", BusinessCalendarService.GetBusinessDay(
            new DateTime(2026, 8, 1, 5, 59, 59)).Key, "до границы");
        Equal("2026-08-01", BusinessCalendarService.GetBusinessDay(
            new DateTime(2026, 8, 1, 6, 0, 0)).Key, "на границе");
    }

    private void BusinessMonthBoundary()
    {
        Equal("2026-07", BusinessCalendarService.GetBusinessMonth(
            new DateTime(2026, 8, 1, 5, 59, 59)).Key, "до границы");
        Equal("2026-08", BusinessCalendarService.GetBusinessMonth(
            new DateTime(2026, 8, 1, 6, 0, 0)).Key, "на границе");
    }

    private void FinancialPaceExpenseRamp()
    {
        DateTime dayStart = new(2026, 9, 1, 6, 0, 0);
        Equal(0, FinancialPaceCalculator.CalculateFixedExpenseAccrued(
            3000, dayStart, new DateTime(2026, 9, 1, 11, 0, 0)), "начало");
        Equal(1500, FinancialPaceCalculator.CalculateFixedExpenseAccrued(
            3000, dayStart, new DateTime(2026, 9, 1, 18, 0, 0)), "середина");
        Equal(3000, FinancialPaceCalculator.CalculateFixedExpenseAccrued(
            3000, dayStart, new DateTime(2026, 9, 2, 1, 0, 0)), "конец");
        Equal(3000, FinancialPaceCalculator.CalculateFixedExpenseAccrued(
            3000, dayStart, new DateTime(2026, 9, 2, 5, 59, 0)), "после графика");
    }

    private void FinancialPaceBaselineStartsToday()
    {
        DateTime enteredAt = new(2026, 9, 1, 18, 0, 0);
        DateTime dayStart = new(2026, 9, 1, 6, 0, 0);

        Equal(
            dayStart,
            FinancialPaceService.ResolveManualExpenseEffectiveFrom(enteredAt),
            "начало действия базы");
        Equal(
            600,
            FinancialPaceCalculator.CalculateFixedExpenseAccrued(
                1200,
                dayStart,
                enteredAt),
            "накопление к 18:00");
    }

    private void FinancialPacePercent()
    {
        Equal(30, FinancialPaceCalculator.CalculatePercent(2600, 2000), "плюс");
        Equal(-30, FinancialPaceCalculator.CalculatePercent(1400, 2000), "минус");
        Equal(0, FinancialPaceCalculator.CalculatePercent(500, 0), "нет знаменателя");
    }

    private void FinancialPaceIncludesBonusWithoutHours()
    {
        DateTime businessDate = new(2026, 9, 1);
        var dailyEarnings = new Dictionary<string, List<AutoSalaryDayEarning>>
        {
            ["Мирбек"] = new()
            {
                new AutoSalaryDayEarning { Date = businessDate, TimeAmount = 498 }
            },
            ["Сталбек"] = new()
            {
                new AutoSalaryDayEarning { Date = businessDate, TimeAmount = 669 }
            },
            ["Тест"] = new()
            {
                new AutoSalaryDayEarning { Date = businessDate, OtherBonusAmount = 324 }
            }
        };

        Dictionary<string, int> salaryByDay =
            FinancialPaceService.BuildSalaryByDay(dailyEarnings);
        Equal(1491, salaryByDay["2026-09-01"], "TeleCom 01.09");
    }

    private void CashAcceptanceOwnerSnapshot()
    {
        DateTime createdAt = new(2026, 9, 2, 11, 30, 0);
        var item = new CashAcceptanceItem
        {
            AcceptanceKey = "acceptance-attempt-1",
            RootAcceptanceKey = "acceptance-root",
            CheckedByEmployeeName = "Мирбек",
            ResponsibleEmployeeName = "Сталбек",
            ExpectedCashAmount = 3079,
            ActualCashAmount = 2901,
            Difference = -178,
            IsProvisional = true,
            CreatedAt = createdAt,
            UpdatedAt = createdAt.AddSeconds(1),
            FinalizeAt = createdAt.AddMinutes(10),
            PendingCashlessVerification = new PendingCashlessVerification()
        };

        Dictionary<string, object?> payload =
            FirebaseSyncService.BuildCashAcceptancePayload(item)
            ?? throw new Exception("Снимок приёмки не создан.");
        Equal("acceptance-root", payload["rootAcceptanceKey"] as string, "корень передачи");
        Equal(2901, (int)payload["actualAmount"]!, "факт налички");
        Equal(-178, (int)payload["difference"]!, "разница налички");
        Equal(true, (bool)payload["isProvisional"]!, "льготный период");
        Equal(true, (bool)payload["hasPendingCashlessVerification"]!, "отложенная сверка");
        Equal(
            new DateTimeOffset(item.FinalizeAt.Value).ToUnixTimeMilliseconds(),
            (long)payload["finalizeAtUnixMs"]!,
            "срок обратного отсчёта");

        string firstSignature = FirebaseSyncService.BuildCashAcceptanceSignature(item);
        item.ActualCashAmount = 2921;
        item.Difference = -158;
        item.UpdatedAt = item.UpdatedAt.AddMinutes(1);
        string secondSignature = FirebaseSyncService.BuildCashAcceptanceSignature(item);
        True(firstSignature != secondSignature, "повторный пересчёт меняет снимок");
    }

    private void ShiftIsSplitAtBoundary()
    {
        var split = BusinessCalendarService.SplitByBusinessMonth(
            new DateTime(2026, 8, 1, 5, 0, 0),
            new DateTime(2026, 8, 1, 7, 0, 0));
        Equal(TimeSpan.FromHours(1), split["2026-07"], "июль");
        Equal(TimeSpan.FromHours(1), split["2026-08"], "август");
    }

    private void ManualClockIsScoped()
    {
        IClubClock before = ClubClock.Current;
        var manual = new ManualClubClock(new DateTime(2030, 1, 1, 5, 59, 0));
        using (ClubClock.UseForTesting(manual))
        {
            Equal(new DateTime(2030, 1, 1, 5, 59, 0), ClubClock.Current.LocalNow, "ручное время");
            manual.Advance(TimeSpan.FromMinutes(1));
            Equal("2030-01-01", BusinessCalendarService.GetBusinessDay(
                ClubClock.Current.LocalNow).Key, "переход ручного времени");
        }
        Same(before, ClubClock.Current, "восстановление часов");
    }

    private void ContinuousBalancesSurviveClose()
    {
        var state = NewState();
        BusinessMonthTransitionEngine.CloseMonth(
            state, "2026-07", new DateTime(2026, 8, 1, 6, 0, 0));
        Equal(10000, state.CashBalance, "наличные");
        Equal(10000, state.CashlessBalance, "безнал");
        Equal(5000, state.Months["2026-07"].Payroll.Single().RemainingAmount, "остаток зарплаты");
        Equal(5000, state.RetainedOwnerIncome, "чистая прибыль");
    }

    private void MonthCloseResumesAfterEveryStep()
    {
        foreach (BusinessMonthCloseStep step in Enum.GetValues<BusinessMonthCloseStep>()
                     .Where(value => value > BusinessMonthCloseStep.None))
        {
            var state = NewState();
            bool interrupted = false;
            try
            {
                BusinessMonthTransitionEngine.CloseMonth(
                    state,
                    "2026-07",
                    new DateTime(2026, 8, 1, 6, 0, 0),
                    completedStep =>
                    {
                        if (completedStep != step)
                            return;
                        interrupted = true;
                        throw new InvalidOperationException("test interruption");
                    });
            }
            catch (InvalidOperationException) when (interrupted)
            {
            }

            var journal = BusinessMonthTransitionEngine.CloseMonth(
                state, "2026-07", new DateTime(2026, 8, 1, 11, 0, 0));
            Equal(BusinessMonthCloseStep.Completed, journal.LastCompletedStep, $"шаг {step}");
            Equal(5000, state.RetainedOwnerIncome, $"доход после шага {step}");
        }
    }

    private void ClockRollbackDoesNotReopenMonth()
    {
        var state = NewState();
        BusinessMonthTransitionEngine.CloseMonth(
            state, "2026-07", new DateTime(2026, 8, 1, 6, 0, 0));
        BusinessMonthTransitionEngine.CloseMonth(
            state, "2026-07", new DateTime(2026, 7, 31, 23, 0, 0));
        Equal(5000, state.RetainedOwnerIncome, "доход не задвоен");
        True(state.Months["2026-07"].IsClosed, "месяц закрыт");
    }

    private void NoHoursDefersUnknownShortage()
    {
        var state = NewState();
        state.Months["2026-07"].UnknownCashShortage = 100;
        state.Months["2026-07"].WorkedHours.Clear();
        var journal = BusinessMonthTransitionEngine.CloseMonth(
            state, "2026-07", new DateTime(2026, 8, 1, 6, 0, 0));
        True(journal.IsDeferred, "закрытие отложено");
        Equal(100, state.Months["2026-07"].UnknownCashShortage, "потеря сохранена");
        Equal(BusinessMonthCloseStep.Prepared, journal.LastCompletedStep, "последний шаг");
    }

    private void ShortageUsesWorkedHours()
    {
        var state = NewState();
        var month = state.Months["2026-07"];
        month.Payroll.Clear();
        month.UnknownCashShortage = 100;
        month.WorkedHours.Clear();
        month.WorkedHours["Первый"] = 99;
        month.WorkedHours["Второй"] = 1;
        BusinessMonthTransitionEngine.CloseMonth(
            state, "2026-07", new DateTime(2026, 8, 1, 6, 0, 0));
        Equal(99, month.Payroll.Single(item => item.EmployeeName == "Первый").PenaltyAmount, "первый");
        Equal(1, month.Payroll.Single(item => item.EmployeeName == "Второй").PenaltyAmount, "второй");
    }

    private void SalaryPaymentUsesFifo()
    {
        var obligations = new List<EmployeePayrollObligation>
        {
            NewObligation("Арген", "2026-07", 5000),
            NewObligation("Арген", "2026-08", 100)
        };
        var allocations = PayrollPaymentAllocator.AllocateFifo(obligations, "Арген", 5100);
        Equal(2, allocations.Count, "число проводок");
        Equal("2026-07", allocations[0].SourceMonthKey, "первая проводка");
        Equal(5000, allocations[0].Amount, "старый остаток");
        Equal("2026-08", allocations[1].SourceMonthKey, "вторая проводка");
        Equal(100, allocations[1].Amount, "новая зарплата");
    }

    private void SalaryOverpaymentIsBlocked()
    {
        bool blocked = false;
        try
        {
            PayrollPaymentAllocator.AllocateFifo(
                new[] { NewObligation("Арген", "2026-07", 100) },
                "Арген",
                101);
        }
        catch (InvalidOperationException)
        {
            blocked = true;
        }
        True(blocked, "переплата заблокирована");
    }

    private void WeightedInventoryCost()
    {
        Equal(133, InventoryCostService.CalculateWeightedAverageUnitCost(
            10, 100, 5, 200), "средняя цена");
    }

    private void ProfitUsesCostOfGoodsSold()
    {
        var state = NewState();
        var month = state.Months["2026-07"];
        month.GameRevenue = 1000;
        month.ProductRevenue = 1000;
        month.ProductCostOfGoodsSold = 400;
        month.ClubExpenses = 100;
        month.Payroll.Single().AccruedAmount = 200;
        BusinessMonthTransitionEngine.CloseMonth(
            state, "2026-07", new DateTime(2026, 8, 1, 6, 0, 0));
        Equal(1300, month.ClosedNetProfit, "чистая прибыль");
    }

    private void LossReducesRetainedIncome()
    {
        var state = NewState();
        state.RetainedOwnerIncome = 100;
        var month = state.Months["2026-07"];
        month.GameRevenue = 0;
        month.ClubExpenses = 200;
        month.Payroll.Clear();
        BusinessMonthTransitionEngine.CloseMonth(
            state, "2026-07", new DateTime(2026, 8, 1, 6, 0, 0));
        Equal(-100, state.RetainedOwnerIncome, "накопленный доход");
    }

    private void NegativeEmployeeBalanceSurvivesClose()
    {
        var state = NewState();
        var obligation = state.Months["2026-07"].Payroll.Single();
        obligation.AccruedAmount = 100;
        obligation.PenaltyAmount = 150;
        BusinessMonthTransitionEngine.CloseMonth(
            state, "2026-07", new DateTime(2026, 8, 1, 6, 0, 0));
        Equal(-50, obligation.RemainingAmount, "остаток сотрудника");
    }

    private void ActivationSnapshotPreventsDoubleProfit()
    {
        var state = NewState();
        state.RetainedOwnerIncome = 15900;
        var month = state.Months["2026-07"];
        month.GameRevenue = 6000;
        month.ClubExpenses = 0;
        month.Payroll.Single().AccruedAmount = 5000;
        month.ProfitIncludedAtActivation = 900;
        BusinessMonthTransitionEngine.CloseMonth(
            state, "2026-07", new DateTime(2026, 8, 1, 6, 0, 0));
        Equal(16000, state.RetainedOwnerIncome, "накопленный доход");
    }

    private void OwnerWithdrawalUsesCurrentMonthFirst()
    {
        OwnerWithdrawalAvailability availability =
            OwnerWithdrawalAvailability.FromBalances(2000, 1500);
        Equal(500, availability.CurrentMonthAmount, "деньги сентября");
        Equal(1500, availability.CarriedAmount, "остаток августа");

        IReadOnlyList<OwnerWithdrawalAllocation> allocations =
            OwnerWithdrawalAllocator.Allocate(
                500,
                availability,
                "2026-09",
                "2026-08");
        Equal(1, allocations.Count, "количество проводок");
        Equal("2026-09", allocations[0].SourceMonthKey, "месяц проводки");
        Equal(500, allocations[0].Amount, "сумма сентября");
        True(!allocations[0].IsCarriedBalance, "текущий источник");
    }

    private void OwnerWithdrawalThenUsesCarriedBalance()
    {
        OwnerWithdrawalAvailability availability =
            OwnerWithdrawalAvailability.FromBalances(2000, 1500);
        IReadOnlyList<OwnerWithdrawalAllocation> allocations =
            OwnerWithdrawalAllocator.Allocate(
                2000,
                availability,
                "2026-09",
                "2026-08");

        Equal(2, allocations.Count, "количество проводок");
        Equal("2026-09", allocations[0].SourceMonthKey, "текущий месяц");
        Equal(500, allocations[0].Amount, "часть сентября");
        Equal("2026-08", allocations[1].SourceMonthKey, "месяц остатка");
        Equal(1500, allocations[1].Amount, "часть остатка");
        True(allocations[1].IsCarriedBalance, "переходящий источник");
    }

    private void OwnerWithdrawalCarryUsesSourceMonthDisplayTime()
    {
        var carried = new CashRecord
        {
            CreatedAt = new DateTime(2026, 9, 1, 11, 26, 10),
            Category = "Расходы",
            ExpenseCategory = "Владелец",
            AccountingMonthKey = "2026-08"
        };
        var current = new CashRecord
        {
            CreatedAt = new DateTime(2026, 9, 1, 11, 26, 10),
            Category = "Расходы",
            ExpenseCategory = "Владелец",
            AccountingMonthKey = "2026-09"
        };

        Equal(
            new DateTime(2026, 8, 31, 23, 59, 59),
            CashService.GetOwnerWithdrawalDisplayTime(carried),
            "дата остатка августа");
        Equal(
            current.CreatedAt,
            CashService.GetOwnerWithdrawalDisplayTime(current),
            "дата дохода сентября");
        True(
            CashService.IsOwnerWithdrawalInPeriod(
                carried,
                new DateTime(2026, 8, 1),
                new DateTime(2026, 9, 1)),
            "остаток входит в августовскую историю");
        True(
            !CashService.IsOwnerWithdrawalInPeriod(
                carried,
                new DateTime(2026, 9, 1),
                new DateTime(2026, 10, 1)),
            "остаток не дублируется в сентябрьской истории");
    }

    private void OwnerWithdrawalOverBalanceIsBlocked()
    {
        OwnerWithdrawalAvailability availability =
            OwnerWithdrawalAvailability.FromBalances(2000, 1500);
        bool blocked = false;
        try
        {
            OwnerWithdrawalAllocator.Allocate(
                2001,
                availability,
                "2026-09",
                "2026-08");
        }
        catch (InvalidOperationException)
        {
            blocked = true;
        }

        True(blocked, "лимит физического баланса");
    }

    private void OwnerWithdrawalCarryOnlyUsesPreviousMonth()
    {
        OwnerWithdrawalAvailability availability =
            OwnerWithdrawalAvailability.FromBalances(2000, 1500);
        IReadOnlyList<OwnerWithdrawalAllocation> allocations =
            OwnerWithdrawalAllocator.Allocate(
                1000,
                availability,
                "2026-09",
                "2026-08",
                carriedOnly: true);

        Equal(1, allocations.Count, "количество проводок");
        Equal("2026-08", allocations[0].SourceMonthKey, "прошлый месяц");
        Equal(1000, allocations[0].Amount, "часть остатка");
        True(allocations[0].IsCarriedBalance, "только переходящий источник");
    }

#if DEBUG
    private void DebugHarnessIsIsolated()
    {
        using var harness = new BusinessScenarioHarness(
            new DateTime(2026, 8, 1, 5, 59, 0));
        harness.SetMoney(10000, 10000)
            .AddSalary("2026-07", "Арген", 5000);
        harness.Month("2026-07").GameRevenue = 12000;
        harness.Month("2026-07").ClubExpenses = 2000;
        harness.Advance(TimeSpan.FromMinutes(1));
        harness.CloseMonth("2026-07");

        True(!harness.UsesFirebase, "Firebase отключён");
        True(!harness.UsesApplicationData, "AppData отключён");
        Equal(10000, harness.State.CashBalance, "тестовая наличка");
        Equal(10000, harness.State.CashlessBalance, "тестовый безнал");
    }

    private void SalaryPaymentMovesMoney()
    {
        using var harness = new BusinessScenarioHarness(
            new DateTime(2026, 8, 1, 7, 0, 0));
        harness.SetMoney(10000, 10000)
            .AddSalary("2026-07", "Арген", 5000)
            .AddSalary("2026-08", "Арген", 100);
        var allocations = harness.PaySalary("Арген", 5100, "Наличные");
        Equal(2, allocations.Count, "число источников");
        Equal(4900, harness.State.CashBalance, "наличные после выплаты");
        Equal(10000, harness.State.CashlessBalance, "безнал не изменился");
    }

    private void OwnerWithdrawalCanPrecedeMonthClose()
    {
        using var harness = new BusinessScenarioHarness(
            new DateTime(2026, 8, 1, 7, 0, 0));
        harness.SetMoney(10000, 10000);
        harness.State.RetainedOwnerIncome = 1000;
        harness.WithdrawOwnerIncome(600, "Безнал");
        Equal(400, harness.State.RetainedOwnerIncome, "остаток дохода");
        Equal(9400, harness.State.CashlessBalance, "остаток безнала");

        harness.WithdrawOwnerIncome(401, "Наличные");
        Equal(-1, harness.State.RetainedOwnerIncome, "аванс открытого месяца");
        Equal(9599, harness.State.CashBalance, "наличка после аванса");
    }
#endif

    private static BusinessLedgerState NewState()
    {
        var state = new BusinessLedgerState
        {
            CashBalance = 10000,
            CashlessBalance = 10000
        };
        state.Months["2026-07"] = new BusinessMonthLedger
        {
            MonthKey = "2026-07",
            GameRevenue = 12000,
            ClubExpenses = 2000,
            WorkedHours = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["Арген"] = 100
            },
            Payroll = new List<EmployeePayrollObligation>
            {
                NewObligation("Арген", "2026-07", 5000)
            }
        };
        return state;
    }

    private static EmployeePayrollObligation NewObligation(
        string employeeName,
        string monthKey,
        int amount)
    {
        return new EmployeePayrollObligation
        {
            EmployeeName = employeeName,
            MonthKey = monthKey,
            AccruedAmount = amount
        };
    }

    private void Test(string title, Action action)
    {
        action();
        _passed++;
        Console.WriteLine($"PASS: {title}");
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception($"{label}: ожидалось {expected}, получено {actual}");
    }

    private static void True(bool value, string label)
    {
        if (!value)
            throw new Exception($"{label}: ожидалось true");
    }

    private static void Same(object expected, object actual, string label)
    {
        if (!ReferenceEquals(expected, actual))
            throw new Exception($"{label}: экземпляр не восстановлен");
    }
}

internal sealed class CashConstitutionTestSuite
{
    private static readonly DateTime MonthStart = new(2026, 7, 1);
    private static readonly DateTime NextMonthStart = MonthStart.AddMonths(1);
    private static readonly DateTime Now = new(2026, 7, 28, 12, 0, 0);

    private int _passed;

    public void Run()
    {
        Test("Последовательные снимки безнала учитывают только дельту", SuccessiveCashlessSnapshotsUseDelta);
        Test("Цепочка TeleCom оставляет ровно 4 сома", TelecomSnapshotChainEndsAtFour);
        Test("Парная ошибка оплаты хранит двойную проводку с общим нулём", PairedTenderHasBalancedSettlement);
        Test("Повтор приёмки с тем же ID не создаёт воду", RepeatedAcceptanceOperationIsIdempotent);
        Test("Быстрая самоприёмка остаётся повторной проверкой передачи", ImmediateSelfAcceptanceKeepsOriginalHandover);
        Test("Owner baseline records a zero checkpoint", OwnerBaselineRecordsZeroCheckpoint);
        Test("Known accumulated snapshot incident is repaired once", KnownAccumulatedSnapshotIncidentIsRepairedOnce);
        Test("Связанная сверка полностью закрывает ошибку типа оплаты", PairedFullSettlement);
        Test("Связанная сверка закрывает часть, а корректировка оформляет остаток", PairedPartialSettlement);
        Test("Излишек за 8 часов покрывает подтверждённую недостачу", RecentOldExtraSettlesConfirmedLoss);
        Test("Излишек старше 24 часов не покрывает подтверждённую недостачу", ExpiredOldExtraDoesNotSettleConfirmedLoss);
        Test("Излишек ровно через 24 часа уже не покрывает подтверждённую недостачу", ExactBoundaryExtraDoesNotSettleConfirmedLoss);
        Test("Новый излишек через 25 часов не покрывает старую подтверждённую недостачу", ExpiredNewExtraDoesNotSettleConfirmedLoss);
        Test("Сценарий 500 на 500 закрывается без штрафа, старая потеря не участвует", RecentFiveHundredSettlesWithoutTouchingOldLoss);
        Test("Старый вклад излишка расходуется раньше нового", OldestExtraContributionIsConsumedFirst);
        Test("Отложенная проводка хранит время факта отдельно от времени зачёта", DeferredAcceptanceKeepsEventAndSettlementTimes);
        Test("Остаток связанного излишка пополняет общую карту", PairedExtraRemainderJoinsPool);
        Test("Свободный излишек сначала закрывает неизвестную потерю", ExtraSettlesUnknownFirst);
        Test("После неизвестной потери излишек закрывает подозреваемую", ExtraSettlesSuspectedSecond);
        Test("Новый излишек закрывает старую известную потерю последней", NewExtraSettlesOlderConfirmedLast);
        Test("Внутри одного типа излишек закрывает старые карты раньше новых", ExtraUsesOldestWithinType);
        Test("В клубе остаётся одна активная карта излишка", OnlyOneOpenExtraCard);
        Test("Одновременно разрешены несколько карт недостачи", MultipleShortageCards);
        Test("Повторная корректировка идемпотентна", RepeatedCorrectionIsIdempotent);
        Test("Повторная сверка безнала не создаёт дубль", RepeatedCashlessVerificationIsRecognized);
        Test("Нулевая сверка также защищена от повторного выполнения", RepeatedZeroVerificationIsRecognized);
        Test("Новая приёмка открывает новый цикл сверки", NewCashAcceptanceRequiresNewCashlessVerification);
        Test("Нулевая приёмка тоже открывает новый цикл сверки", ZeroAcceptanceRequiresNewVerification);
        Test("Неизвестная потеря переживает повторную корректировку", UnknownLossSurvivesRepeatedCorrection);
        Test("Корректировка не создаёт зеркальную карточку", CorrectionDoesNotCreateMirroredDifference);
        Test("Ручной штраф меньше потери уменьшает карту", ManualLossPartiallyConsumesShortage);
        Test("Частичный штраф не меняет статус и сотрудника остатка", PartialLossKeepsRemainingResponsibility);
        Test("Частичные штрафы разным сотрудникам не перезаписываются", PartialLossAllocationsRemainSeparate);
        Test("Ручной штраф расходует известные, подозреваемые и неизвестные по порядку", ManualLossUsesConstitutionPriority);
        Test("Повтор ID ручного штрафа не выполняет его второй раз", ManualLossOperationIdIsIdempotent);
        Test("Ручной штраф ровно по потере закрывает карту", ManualLossExactlyClosesShortage);
        Test("Ручной штраф сверх потери создаёт излишек", ManualLossOverageCreatesExtra);
        Test("Сверхштраф создаёт вклад излишка с отдельным источником", ManualLossOverageHasDedicatedOrigin);
        Test("Свободный ручной штраф хранит равную проводку потери и излишка", FreeManualLossIsBalanced);
        Test("Реальная цепочка 1811 оставляет только 42 излишка", RealPartialPenaltyThenLaterExtra);
        Test("Сверка оставляет известный остаток корректировке", VerificationLeavesConfirmedRemainderForCorrection);
        Test("Положительный остаток месяца архивируется", PositiveMonthClose);
        Test("Минус месяца распределяется 99 к 1 по часам", NegativeMonthCloseDistribution);
        Test("Закрытие месяца распределяет только чистый минус", MonthCloseDistributesNetShortage);
        Test("Месяц без рабочих часов не теряет отрицательный остаток", MonthCloseWithoutHoursIsDeferred);
        Test("Повтор закрытия месяца после перезапуска возвращает тот же результат", MonthCloseSurvivesRestart);
        Test("Закрытые карты не воскресают после контрольной точки", ClosedCardsNeverReturn);
        Test("Контрольная точка учитывает выживший разбор без зеркальной карты", CheckpointBaselinePreventsMirroredDifference);
        Test("Нулевая контрольная точка заменяет предыдущий ненулевой снимок", ZeroCheckpointReplacesPreviousBaseline);
        Test("Перезапуск сохраняет частичные назначения и статус остатка", RestartPreservesPartialAllocations);
        Test("Случайные последовательности сохраняют инварианты", RandomizedInvariantTest);

        Test("Корректировка оформляет ожидающую подозреваемую недостачу", CorrectionFormalizesAwaitingSuspected);
        Test("Корректировка сохраняет ожидающую неизвестную недостачу", CorrectionKeepsAwaitingUnknown);
        Test("Старые закрытые карты мигрируют без повторного открытия", LegacyClosedCardMigrationIsStable);
        Test("Контрольная точка не отражает старую недостачу в излишек", NegativeCheckpointDoesNotMirror);
        Test("Предварительный этап корректировки не ставит контрольную точку", PreliminaryCorrectionHasNoCheckpoint);
        Test("Финальная точка команды сохраняется один раз вместе с фактами", CorrectionCommitIsIdempotent);

        Console.WriteLine();
        Console.WriteLine($"PASS: {_passed} сценариев Конституции кассы.");
    }

    private void PairedFullSettlement()
    {
        var items = NewLedger();
        CashConstitutionEngine.RecordCashAcceptance(
            items, MonthStart, NextMonthStart, Now,
            "Новый", "Старый", 1000, 900, "Приёмка");
        var result = CashConstitutionEngine.RecordCashlessVerification(
            items, MonthStart, NextMonthStart, Now.AddMinutes(1),
            1000, 1100, "", "Сверка");

        Equal(100, result.PairedAmount, "Связанная сумма");
        Equal(0, result.Breakdown, "Разбор");
        Equal(0, result.Assignments.Count, "Штрафы");
        Equal(0, Open(items).Count, "Открытые карты");
    }

    private void ImmediateSelfAcceptanceKeepsOriginalHandover()
    {
        var items = new List<CashAcceptanceItem>
        {
            new()
            {
                AcceptanceKey = "argen-to-test",
                RootAcceptanceKey = "argen-to-test",
                IsProvisional = true,
                CheckedByEmployeeName = "Тест",
                ResponsibleEmployeeName = "Арген",
                CreatedAt = Now,
                UpdatedAt = Now
            }
        };

        var target = CashAcceptanceRecheckPolicy.FindImmediateHandoverForRecheck(
            items,
            "Тест",
            Now.AddMinutes(9),
            ShiftAcceptanceCorrectionPolicy.OriginalEmployeeResponsibilityMinutes);

        Assert(target != null, "Повторная проверка не нашла исходную передачу.");
        Equal("argen-to-test", target!.RootAcceptanceKey, "Корень передачи");
        Equal("Арген", target.ResponsibleEmployeeName, "Ответственный сохраняется");

        var expired = CashAcceptanceRecheckPolicy.FindImmediateHandoverForRecheck(
            items,
            "Тест",
            Now.AddMinutes(10),
            ShiftAcceptanceCorrectionPolicy.OriginalEmployeeResponsibilityMinutes);

        Assert(expired == null, "После окна это уже новая самоприёмка.");
    }

    private void PairedPartialSettlement()
    {
        var items = NewLedger();
        CashConstitutionEngine.RecordCashAcceptance(
            items, MonthStart, NextMonthStart, Now,
            "Новый", "Старый", 1000, 900, "Приёмка");
        var result = CashConstitutionEngine.RecordCashlessVerification(
            items, MonthStart, NextMonthStart, Now.AddMinutes(1),
            1000, 1040, "", "Сверка");

        Equal(40, result.PairedAmount, "Связанная сумма");
        Equal(0, result.Assignments.Count, "Сверка не оформляет штраф");
        Equal(-60, result.Breakdown, "Разбор до точки");

        var correction = CashConstitutionEngine.ApplyCorrection(
            items, MonthStart, NextMonthStart, Now.AddMinutes(2), 1);
        Equal(1, correction.Assignments.Count, "Число штрафов");
        Equal("Старый", correction.Assignments[0].EmployeeName, "Ответственный");
        Equal(60, correction.Assignments[0].Amount, "Оформленный остаток");
        Equal(0, correction.Breakdown, "Разбор после точки");
    }

    private void RecentOldExtraSettlesConfirmedLoss()
    {
        var items = NewLedger();
        AddReadyExtra(items, 100, Now.AddHours(-8));

        CashConstitutionEngine.RecordCashAcceptance(
            items, MonthStart, NextMonthStart, Now,
            "Новый", "Старый", 1000, 900, "Приёмка");
        var result = CashConstitutionEngine.RecordCashlessVerification(
            items, MonthStart, NextMonthStart, Now.AddMinutes(1),
            1000, 1000, "", "Сверка");

        Equal(0, result.Assignments.Count, "Сверка не штрафует");
        Equal(0, result.Breakdown, "Излишек закрыл потерю в пределах суток");
        Equal(0, Open(items).Count, "Открытые карты");

        var correction = CashConstitutionEngine.ApplyCorrection(
            items, MonthStart, NextMonthStart, Now.AddMinutes(2), 1);
        Equal(0, correction.Assignments.Count, "Штраф не создаётся");
        Equal(0, correction.Breakdown, "Разбор после точки");
    }

    private void ExpiredOldExtraDoesNotSettleConfirmedLoss()
    {
        var items = NewLedger();
        AddReadyExtra(items, 100, Now.AddHours(-25));

        CashConstitutionEngine.RecordCashAcceptance(
            items, MonthStart, NextMonthStart, Now,
            "Новый", "Старый", 1000, 900, "Приёмка");
        var result = CashConstitutionEngine.RecordCashlessVerification(
            items, MonthStart, NextMonthStart, Now.AddMinutes(1),
            1000, 1000, "", "Сверка");

        Equal(0, result.Assignments.Count, "Сверка не штрафует");
        Equal(0, result.Breakdown, "Оба ведра остаются до точки");
        Equal(100, Open(items).Single(IsExtra).Amount, "Старый излишек сохранён");

        var correction = CashConstitutionEngine.ApplyCorrection(
            items, MonthStart, NextMonthStart, Now.AddMinutes(2), 1);
        Equal(1, correction.Assignments.Count, "Число штрафов");
        Equal(100, correction.Assignments[0].Amount, "Штраф");
        Equal(100, correction.Breakdown, "Старый излишек после оформления");
    }

    private void ExactBoundaryExtraDoesNotSettleConfirmedLoss()
    {
        var items = NewLedger();
        AddReadyExtra(items, 100, Now.AddHours(-24));

        CashConstitutionEngine.RecordCashAcceptance(
            items, MonthStart, NextMonthStart, Now,
            "Новый", "Старый", 1000, 900, "Приёмка");
        CashConstitutionEngine.RecordCashlessVerification(
            items, MonthStart, NextMonthStart, Now.AddMinutes(1),
            1000, 1000, "", "Сверка");

        var correction = CashConstitutionEngine.ApplyCorrection(
            items, MonthStart, NextMonthStart, Now.AddMinutes(2), 1);
        Equal(1, correction.Assignments.Count, "Граница 24 часа считается истёкшей");
        Equal(100, correction.Breakdown, "Излишек не израсходован");
    }

    private void ExpiredNewExtraDoesNotSettleConfirmedLoss()
    {
        var items = NewLedger();
        AddReadyShortage(
            items,
            100,
            CashResponsibilityLevel.Confirmed,
            Now,
            responsible: "Ответственный");

        CashConstitutionEngine.RecordCashlessVerification(
            items, MonthStart, NextMonthStart, Now.AddHours(25),
            1000, 1100, "", "Поздний излишек");
        var correction = CashConstitutionEngine.ApplyCorrection(
            items, MonthStart, NextMonthStart, Now.AddHours(25).AddMinutes(1), 1);

        Equal(1, correction.Assignments.Count, "Старая потеря оформлена");
        Equal(100, correction.Assignments[0].Amount, "Сумма штрафа");
        Equal(100, correction.Breakdown, "Поздний излишек сохранён");
    }

    private void RecentFiveHundredSettlesWithoutTouchingOldLoss()
    {
        var items = NewLedger();
        AddReadyShortage(
            items,
            5,
            CashResponsibilityLevel.Confirmed,
            Now.AddDays(-8),
            responsible: "Старый долг");
        AddReadyExtra(items, 500, Now);
        AddReadyShortage(
            items,
            500,
            CashResponsibilityLevel.Confirmed,
            Now.AddMinutes(14),
            responsible: "Арген");

        var correction = CashConstitutionEngine.ApplyCorrection(
            items, MonthStart, NextMonthStart, Now.AddMinutes(15), 1);

        Equal(1, correction.Assignments.Count, "Оформлена только старая потеря");
        Equal("Старый долг", correction.Assignments[0].EmployeeName, "Сотрудник");
        Equal(5, correction.Assignments[0].Amount, "Старая сумма");
        Equal(0, correction.Breakdown, "Свежие 500 взаимно закрыты");
        Equal(0, Open(items).Count, "Открытые карты");
    }

    private void OldestExtraContributionIsConsumedFirst()
    {
        var items = NewLedger();
        AddReadyExtra(items, 500, Now.AddHours(-30));
        AddReadyExtra(items, 1, Now.AddHours(-1));
        AddReadyShortage(
            items,
            500,
            CashResponsibilityLevel.Unknown,
            Now);

        CashConstitutionEngine.Normalize(items, MonthStart, NextMonthStart);
        var correction = CashConstitutionEngine.ApplyCorrection(
            items, MonthStart, NextMonthStart, Now.AddMinutes(1), 1);
        var contributions = Open(items).Single(IsExtra).ExtraContributions
            .OrderBy(item => item.CreatedAt)
            .ToList();

        Equal(0, contributions[0].Amount, "Старый вклад");
        Equal(1, contributions[1].Amount, "Новый вклад сохранён");
        Equal(1, correction.Breakdown, "Остаток излишка");
    }

    private void DeferredAcceptanceKeepsEventAndSettlementTimes()
    {
        var items = NewLedger();
        DateTime acceptedAt = Now;
        DateTime cashlessAt = Now.AddMinutes(5);
        DateTime finalizedAt = Now.AddMinutes(10);

        CashConstitutionEngine.RecordCashlessVerification(
            items, MonthStart, NextMonthStart, cashlessAt,
            1000, 900, "", "Недостача безнала");
        CashConstitutionEngine.RecordCashAcceptance(
            items, MonthStart, NextMonthStart, finalizedAt,
            "Новый", "Старый", 1000, 1100, "Отложенная приёмка",
            operationId: "deferred-1",
            occurredAt: acceptedAt);

        var contribution = items
            .Where(IsExtra)
            .SelectMany(item => item.ExtraContributions)
            .Single();
        var settlement = items
            .SelectMany(item => item.Settlements)
            .Single(item => item.Kind == CashSettlementKind.PairedTender);

        Equal(acceptedAt, contribution.CreatedAt, "Время финансового факта");
        Equal(finalizedAt, settlement.CreatedAt, "Время выполненного зачёта");
        Equal(0, Open(items).Count, "Карты закрыты");
    }

    private void PairedExtraRemainderJoinsPool()
    {
        var items = NewLedger();
        AddReadyExtra(items, 100, Now.AddHours(-1));

        CashConstitutionEngine.RecordCashAcceptance(
            items, MonthStart, NextMonthStart, Now,
            "Новый", "Старый", 1000, 900, "Приёмка");
        var result = CashConstitutionEngine.RecordCashlessVerification(
            items, MonthStart, NextMonthStart, Now.AddMinutes(1),
            1000, 1150, "", "Сверка");

        Equal(100, result.PairedAmount, "Связанная сумма");
        Equal(150, result.Breakdown, "Общий излишек");
        Equal(1, Open(items).Count(IsExtra), "Число карт излишка");
        Equal(150, Open(items).Single(IsExtra).Amount, "Сумма карты");
    }

    private void ExtraSettlesUnknownFirst()
    {
        var items = NewLedger();
        AddReadyShortage(items, 100, CashResponsibilityLevel.Unknown, Now.AddHours(-2));
        AddReadyShortage(items, 100, CashResponsibilityLevel.Suspected, Now.AddHours(-1), suspect: "Тест");
        var result = CashConstitutionEngine.RecordCashlessVerification(
            items, MonthStart, NextMonthStart, Now,
            1000, 1120, "", "Сверка");

        var openShortages = Open(items).Where(IsShortage).ToList();
        Equal(1, openShortages.Count, "Оставшиеся карты");
        Equal(CashResponsibilityLevel.Suspected, openShortages[0].ResponsibilityLevel, "Приоритет");
        Equal(80, openShortages[0].Amount, "Остаток подозреваемой карты");
        Equal(0, result.Assignments.Count, "Сверка не оформляет подозреваемую");
        Equal(-80, result.Breakdown, "Итог");
    }

    private void ExtraSettlesSuspectedSecond()
    {
        var items = NewLedger();
        AddReadyShortage(items, 100, CashResponsibilityLevel.Suspected, Now, suspect: "Тест");
        AddReadyExtra(items, 60, Now.AddMinutes(1));

        CashConstitutionEngine.Normalize(items, MonthStart, NextMonthStart);
        var result = CashConstitutionEngine.ApplyCorrection(
            items, MonthStart, NextMonthStart, Now.AddMinutes(2), 1);

        Equal(1, result.Assignments.Count, "Оформленная рекомендация");
        Equal(40, result.Assignments[0].Amount, "Остаток после излишка");
        Equal(0, result.Breakdown, "После оформления");
    }

    private void NewExtraSettlesOlderConfirmedLast()
    {
        var items = NewLedger();
        AddReadyShortage(
            items,
            100,
            CashResponsibilityLevel.Confirmed,
            Now.AddHours(-1),
            responsible: "Ответственный"
        );

        var result = CashConstitutionEngine.RecordCashlessVerification(
            items,
            MonthStart,
            NextMonthStart,
            Now,
            1000,
            1100,
            "",
            "Новый излишек"
        );

        Equal(0, result.Breakdown, "Разбор");
        Equal(0, result.Assignments.Count, "Штрафы");
        Equal(0, Open(items).Count, "Активные карты");
    }

    private void ExtraUsesOldestWithinType()
    {
        var items = NewLedger();
        Guid oldId = AddReadyShortage(
            items,
            70,
            CashResponsibilityLevel.Unknown,
            Now.AddHours(-2)
        );
        Guid newId = AddReadyShortage(
            items,
            70,
            CashResponsibilityLevel.Unknown,
            Now.AddHours(-1)
        );

        CashConstitutionEngine.RecordCashlessVerification(
            items,
            MonthStart,
            NextMonthStart,
            Now,
            1000,
            1100,
            "",
            "Излишек 100"
        );

        Equal(0, Open(items).Count(item => item.Id == oldId), "Старая карта");
        Equal(40, Open(items).Single(item => item.Id == newId).Amount, "Новая карта");
    }

    private void OnlyOneOpenExtraCard()
    {
        var items = NewLedger();
        AddReadyExtra(items, 10, Now);
        AddReadyExtra(items, 20, Now.AddMinutes(1));
        AddReadyExtra(items, 30, Now.AddMinutes(2));

        CashConstitutionEngine.Normalize(items, MonthStart, NextMonthStart);

        Equal(1, Open(items).Count(IsExtra), "Число активных карт");
        Equal(60, Open(items).Single(IsExtra).Amount, "Сумма");
        Equal(3, Open(items).Single(IsExtra).ExtraContributions.Count, "Внутренние вклады");
    }

    private void MultipleShortageCards()
    {
        var items = NewLedger();
        AddReadyShortage(items, 10, CashResponsibilityLevel.Unknown, Now);
        AddReadyShortage(items, 20, CashResponsibilityLevel.Suspected, Now.AddMinutes(1), suspect: "А");
        AddReadyShortage(items, 30, CashResponsibilityLevel.Confirmed, Now.AddMinutes(2), responsible: "Б");

        CashConstitutionEngine.Normalize(items, MonthStart, NextMonthStart);

        Equal(3, Open(items).Count(IsShortage), "Число карт");
        Equal(-60, CashConstitutionEngine.GetBreakdown(items, MonthStart, NextMonthStart), "Разбор");
    }

    private void RepeatedCorrectionIsIdempotent()
    {
        var items = NewLedger();
        AddReadyShortage(items, 75, CashResponsibilityLevel.Suspected, Now, suspect: "Тест");

        var first = CashConstitutionEngine.ApplyCorrection(
            items, MonthStart, NextMonthStart, Now.AddMinutes(1), 1);
        var second = CashConstitutionEngine.ApplyCorrection(
            items, MonthStart, NextMonthStart, Now.AddMinutes(2), 2);

        Equal(1, first.Assignments.Count, "Первое оформление");
        Equal(75, first.Assignments[0].Amount, "Первая сумма");
        Equal(0, second.Assignments.Count, "Повторное оформление");
        Equal(0, second.Breakdown, "Повторный итог");
    }

    private void UnknownLossSurvivesRepeatedCorrection()
    {
        var items = NewLedger();
        Guid id = AddReadyShortage(
            items,
            13,
            CashResponsibilityLevel.Unknown,
            Now
        );

        var first = CashConstitutionEngine.ApplyCorrection(
            items, MonthStart, NextMonthStart, Now.AddMinutes(1), 10);
        var second = CashConstitutionEngine.ApplyCorrection(
            items, MonthStart, NextMonthStart, Now.AddMinutes(2), 11);

        Equal(-13, first.Breakdown, "Первая корректировка");
        Equal(-13, second.Breakdown, "Повторная корректировка");
        Equal(id, Open(items).Single(IsShortage).Id, "ID выжившей карты");
        Equal(0, first.Assignments.Count + second.Assignments.Count, "Штрафы");
    }

    private void SuccessiveCashlessSnapshotsUseDelta()
    {
        var items = NewLedger();
        AddReadyShortage(
            items,
            284,
            CashResponsibilityLevel.Suspected,
            Now.AddMinutes(-1),
            suspect: "Employee 1"
        );

        CashConstitutionEngine.RecordCashlessVerification(
            items,
            MonthStart,
            NextMonthStart,
            Now,
            17526,
            17060,
            "Employee 2",
            "Initial shortage"
        );
        CashConstitutionEngine.RecordCashlessVerification(
            items,
            MonthStart,
            NextMonthStart,
            Now.AddMinutes(1),
            17060,
            17560,
            "",
            "First transfer"
        );
        CashConstitutionEngine.RecordCashlessVerification(
            items,
            MonthStart,
            NextMonthStart,
            Now.AddMinutes(2),
            17560,
            17860,
            "",
            "Second transfer"
        );
        var beforeCorrection = CashConstitutionEngine.RecordCashlessVerification(
            items,
            MonthStart,
            NextMonthStart,
            Now.AddMinutes(3),
            17860,
            17880,
            "",
            "Final transfer"
        );

        Equal(70, beforeCorrection.Breakdown, "Разбор до корректировки");
        Equal(1, Open(items).Count(IsShortage), "Недостача ещё ждёт точку");
        Equal(536, Open(items).Single(IsExtra).Amount, "Излишек ещё ждёт точку");

        var correction = CashConstitutionEngine.ApplyCorrection(
            items,
            MonthStart,
            NextMonthStart,
            Now.AddMinutes(4),
            checkpointNumber: 0
        );
        Equal(0, correction.Assignments.Sum(item => item.Amount), "Штрафы");
        Equal(70, correction.Breakdown, "Корректировка сохраняет излишек");
        Equal(0, Open(items).Count(IsShortage), "Недостача закрыта на точке");
        Equal(70, Open(items).Single(IsExtra).Amount, "Остаток после точки");
    }

    private void TelecomSnapshotChainEndsAtFour()
    {
        var items = NewLedger();
        CashConstitutionEngine.RecordCashAcceptance(
            items, MonthStart, NextMonthStart, Now,
            "Арген", "Арген", 100, 80, "Нал -20", "cash-1");
        CashConstitutionEngine.RecordCashlessVerification(
            items, MonthStart, NextMonthStart, Now.AddMinutes(1),
            200, 222, "", "Безнал +22", "cashless-1", 200);
        CashConstitutionEngine.RecordCashAcceptance(
            items, MonthStart, NextMonthStart, Now.AddMinutes(2),
            "Тест", "Арген", 100, 122, "Нал +22", "cash-2");
        var result = CashConstitutionEngine.RecordCashlessVerification(
            items, MonthStart, NextMonthStart, Now.AddMinutes(3),
            222, 202, "", "Безнал -20", "cashless-2", 200);

        Equal(4, result.Breakdown, "Итог двух ведер");
        Equal(4, Open(items).Single(IsExtra).Amount, "Выживший излишек");
        Equal(0, Open(items).Count(IsShortage), "Выжившие недостачи");
        CashConstitutionEngine.ValidateConservation(items, MonthStart, NextMonthStart);
    }

    private void PairedTenderHasBalancedSettlement()
    {
        var items = NewLedger();
        CashConstitutionEngine.RecordCashAcceptance(
            items, MonthStart, NextMonthStart, Now,
            "Новый", "Старый", 1000, 900, "Приёмка", "cash");
        CashConstitutionEngine.RecordCashlessVerification(
            items, MonthStart, NextMonthStart, Now.AddMinutes(1),
            1000, 1100, "", "Сверка", "cashless", 1000);

        var settlement = items
            .Where(item => item.IsTechnicalEvent)
            .SelectMany(item => item.Settlements)
            .Single();
        Equal(100, settlement.Amount, "Сумма проводки");
        Equal(0, settlement.CashDelta + settlement.CashlessDelta, "Общая касса");
        Equal(-100, settlement.CashDelta, "Нал");
        Equal(100, settlement.CashlessDelta, "Безнал");
    }

    private void RepeatedAcceptanceOperationIsIdempotent()
    {
        var items = NewLedger();
        var first = CashConstitutionEngine.RecordCashAcceptance(
            items, MonthStart, NextMonthStart, Now,
            "Новый", "Старый", 1000, 900, "Приёмка", "acceptance-1");
        var repeated = CashConstitutionEngine.RecordCashAcceptance(
            items, MonthStart, NextMonthStart, Now.AddMinutes(1),
            "Новый", "Старый", 1000, 900, "Приёмка", "acceptance-1");

        Equal(-100, first.Breakdown, "Первый результат");
        Equal(-100, repeated.Breakdown, "Повторный результат");
        Equal(1, Open(items).Count(IsShortage), "Одна карта недостачи");
    }

    private void OwnerBaselineRecordsZeroCheckpoint()
    {
        var items = NewLedger();
        items.Add(new CashReconciliationItem
        {
            AccountingSchemaVersion = 3,
            Id = Guid.NewGuid(),
            InvestigationId = Guid.NewGuid(),
            IsTechnicalEvent = true,
            CreatedAt = Now,
            Kind = CashReconciliationKind.Other,
            Origin = CashReconciliationOrigin.CorrectionCheckpoint,
            Status = CashReconciliationStatus.Resolved,
            Stage = CashReconciliationStage.Ready,
            CheckpointNumber = 1,
            AmountAtCheckpoint = 100
        });
        CashConstitutionEngine.RecordCheckpoint(
            items,
            MonthStart,
            NextMonthStart,
            Now.AddMinutes(1),
            checkpointNumber: 2,
            operationId: "owner-baseline",
            actualCashAtCheckpoint: 100,
            actualCashlessAtCheckpoint: 200
        );

        Equal(
            0,
            CashConstitutionEngine.GetLatestCheckpointBreakdown(
                items,
                MonthStart,
                NextMonthStart),
            "Latest checkpoint"
        );
    }

    private void KnownAccumulatedSnapshotIncidentIsRepairedOnce()
    {
        Guid extraId = Guid.NewGuid();
        Guid shortageId = Guid.NewGuid();
        Guid allocationId = Guid.NewGuid();
        const long checkpointNumber = 12345;
        var items = NewLedger();
        items.Add(new CashReconciliationItem
        {
            AccountingSchemaVersion = 3,
            Id = extraId,
            InvestigationId = Guid.NewGuid(),
            CreatedAt = Now,
            Kind = CashReconciliationKind.CashlessExtra,
            Origin = CashReconciliationOrigin.BalanceRawDifference,
            Status = CashReconciliationStatus.Open,
            Stage = CashReconciliationStage.Ready,
            Amount = 1330,
            OriginalAmount = 1330,
            AmountAtCheckpoint = 1330,
            CheckpointNumber = checkpointNumber,
            ExtraContributions = new List<CashExtraContribution>
            {
                new()
                {
                    InvestigationId = Guid.NewGuid(),
                    CreatedAt = Now,
                    Kind = CashReconciliationKind.CashlessExtra,
                    Origin = CashReconciliationOrigin.BalanceRawDifference,
                    Stage = CashReconciliationStage.Ready,
                    Amount = 1330,
                    OriginalAmount = 1330
                }
            }
        });
        items.Add(new CashReconciliationItem
        {
            AccountingSchemaVersion = 3,
            Id = shortageId,
            InvestigationId = Guid.NewGuid(),
            CreatedAt = Now.AddMinutes(-1),
            Kind = CashReconciliationKind.CashlessShortage,
            Origin = CashReconciliationOrigin.CashlessVerification,
            Status = CashReconciliationStatus.Resolved,
            Stage = CashReconciliationStage.Ready,
            Amount = 0,
            OriginalAmount = 466,
            ResolvedAmount = 438,
            FormalizedAmount = 28,
            PostedFormalizedAmount = 28,
            Resolution = CashReconciliationResolution.FormalizedLoss,
            LossAllocations = new List<CashLossAllocation>
            {
                new()
                {
                    Id = allocationId,
                    CreatedAt = Now,
                    EmployeeName = "Employee",
                    Amount = 28,
                    PostedAmount = 28,
                    Source = CashLossAllocationSource.AutomaticCorrection
                }
            }
        });
        items.Add(new CashReconciliationItem
        {
            AccountingSchemaVersion = 3,
            Id = Guid.NewGuid(),
            InvestigationId = Guid.NewGuid(),
            IsTechnicalEvent = true,
            CreatedAt = Now,
            Kind = CashReconciliationKind.Other,
            Origin = CashReconciliationOrigin.CorrectionCheckpoint,
            Status = CashReconciliationStatus.Resolved,
            Stage = CashReconciliationStage.Ready,
            CheckpointNumber = checkpointNumber,
            AmountAtCheckpoint = 1330
        });

        bool repaired = CashConstitutionEngine
            .TryRepairKnownAccumulatedCashlessSnapshots(
                items,
                extraId,
                shortageId,
                allocationId,
                incorrectExtraAmount: 1330,
                incorrectFormalizedAmount: 28,
                employeeName: "Employee"
            );
        bool repeated = CashConstitutionEngine
            .TryRepairKnownAccumulatedCashlessSnapshots(
                items,
                extraId,
                shortageId,
                allocationId,
                incorrectExtraAmount: 1330,
                incorrectFormalizedAmount: 28,
                employeeName: "Employee"
            );

        Assert(repaired, "Repair recognized");
        Assert(repeated, "Repeated repair recognized");
        var extra = items.Single(item => item.Id == extraId);
        var shortage = items.Single(item => item.Id == shortageId);
        Equal(0, extra.Amount, "No open extra");
        Equal(1330, extra.OriginalAmount, "Historical original extra");
        Equal(1330, extra.ResolvedAmount, "Cancelled technical extra");
        Equal(0, extra.AmountAtCheckpoint, "Zero item checkpoint");
        Equal(CashReconciliationStatus.Resolved, extra.Status, "Closed extra");
        Equal(0, extra.ExtraContributions.Single().Amount, "No open contribution");
        Equal(438, shortage.ResolvedAmount, "Settled shortage part");
        Equal(28, shortage.FormalizedAmount, "Confirmed penalty remains");
        Equal(28, shortage.PostedFormalizedAmount, "Posted penalty remains");
        Equal(1, shortage.LossAllocations.Count, "Penalty allocation remains");
        Equal(
            0,
            CashConstitutionEngine.GetLatestCheckpointBreakdown(
                items,
                MonthStart,
                NextMonthStart),
            "Correct technical checkpoint"
        );
    }

    private void RepeatedCashlessVerificationIsRecognized()
    {
        var items = NewLedger();
        CashConstitutionEngine.RecordCashlessVerification(
            items,
            MonthStart,
            NextMonthStart,
            Now,
            22792,
            20922,
            "",
            "Первая сверка"
        );

        bool alreadyVerified = CashConstitutionEngine.HasCurrentCashlessVerification(
            items,
            MonthStart,
            NextMonthStart,
            22792,
            20922
        );

        Assert(alreadyVerified, "Повторная сверка должна использовать существующее расследование.");
        int countBeforeRepeat = items.Count;
        var repeated = CashConstitutionEngine.RecordCashlessVerification(
            items,
            MonthStart,
            NextMonthStart,
            Now.AddSeconds(1),
            22792,
            20922,
            "",
            "Повторная сверка"
        );
        Equal(
            1,
            items.Count(item =>
                item.IsTechnicalEvent &&
                item.Origin == CashReconciliationOrigin.CashlessVerification),
            "Количество событий сверки безнала"
        );
        Equal(countBeforeRepeat, items.Count, "Количество записей после повтора");
        Equal(0, repeated.Assignments.Count, "Повторные штрафы");
    }

    private void NewCashAcceptanceRequiresNewCashlessVerification()
    {
        var items = NewLedger();
        CashConstitutionEngine.RecordCashlessVerification(
            items,
            MonthStart,
            NextMonthStart,
            Now,
            22792,
            20922,
            "",
            "Первая сверка"
        );
        CashConstitutionEngine.RecordCashAcceptance(
            items,
            MonthStart,
            NextMonthStart,
            Now.AddMinutes(1),
            "Новый",
            "Старый",
            500,
            490,
            "Новая приёмка"
        );

        bool alreadyVerified = CashConstitutionEngine.HasCurrentCashlessVerification(
            items,
            MonthStart,
            NextMonthStart,
            22792,
            20922
        );

        Assert(!alreadyVerified, "После новой приёмки нужна новая сверка безнала.");
    }

    private void RepeatedZeroVerificationIsRecognized()
    {
        var items = NewLedger();
        CashConstitutionEngine.RecordCashlessVerification(
            items, MonthStart, NextMonthStart, Now, 1000, 1000, "", "Ноль");
        int countBeforeRepeat = items.Count;

        CashConstitutionEngine.RecordCashlessVerification(
            items, MonthStart, NextMonthStart, Now.AddSeconds(1), 1000, 1000, "", "Повтор");

        Equal(countBeforeRepeat, items.Count, "Количество событий");
        Equal(
            1,
            items.Count(item =>
                item.IsTechnicalEvent &&
                item.Origin == CashReconciliationOrigin.CashlessVerification),
            "Журнал сверки"
        );
    }

    private void ZeroAcceptanceRequiresNewVerification()
    {
        var items = NewLedger();
        CashConstitutionEngine.RecordCashlessVerification(
            items, MonthStart, NextMonthStart, Now, 1000, 1000, "", "Сверка");
        CashConstitutionEngine.RecordCashAcceptance(
            items,
            MonthStart,
            NextMonthStart,
            Now.AddMinutes(1),
            "Новый",
            "Старый",
            1000,
            1000,
            "Нулевая приёмка"
        );

        Assert(
            !CashConstitutionEngine.HasCurrentCashlessVerification(
                items, MonthStart, NextMonthStart, 1000, 1000),
            "Даже нулевая новая приёмка требует новой сверки."
        );
    }

    private void CorrectionFormalizesAwaitingSuspected()
    {
        var items = NewLedger();
        CashConstitutionEngine.RecordCashlessVerification(
            items,
            MonthStart,
            NextMonthStart,
            Now,
            expectedAmount: 100,
            actualAmount: 90,
            suspectedEmployeeName: "Админ 1",
            note: "Проверка"
        );

        var result = CashConstitutionEngine.ApplyCorrection(
            items,
            MonthStart,
            NextMonthStart,
            Now.AddMinutes(1),
            checkpointNumber: 10
        );

        Equal(10, result.Assignments.Sum(item => item.Amount), "Оформленная сумма");
        Equal("Админ 1", result.Assignments.Single().EmployeeName, "Сотрудник");
        Equal(0, Open(items).Count(IsShortage), "Открытые недостачи");
    }

    private void CorrectionKeepsAwaitingUnknown()
    {
        var items = NewLedger();
        CashConstitutionEngine.RecordCashlessVerification(
            items,
            MonthStart,
            NextMonthStart,
            Now,
            expectedAmount: 100,
            actualAmount: 90,
            suspectedEmployeeName: "",
            note: "Проверка"
        );

        var result = CashConstitutionEngine.ApplyCorrection(
            items,
            MonthStart,
            NextMonthStart,
            Now.AddMinutes(1),
            checkpointNumber: 11
        );

        Equal(0, result.Assignments.Sum(item => item.Amount), "Оформленная сумма");
        var shortage = Open(items).Single(IsShortage);
        Equal(10, shortage.Amount, "Остаток");
        Equal(CashResponsibilityLevel.Unknown, shortage.ResponsibilityLevel, "Тип");
        Equal(CashReconciliationStage.Ready, shortage.Stage, "Стадия");
    }

    private void LegacyClosedCardMigrationIsStable()
    {
        Guid cardId = Guid.NewGuid();
        var items = new List<CashReconciliationItem>
        {
            new()
            {
                AccountingSchemaVersion = 2,
                Id = cardId,
                InvestigationId = cardId,
                CreatedAt = Now,
                Kind = CashReconciliationKind.CashShortage,
                Origin = CashReconciliationOrigin.CashAcceptance,
                Status = CashReconciliationStatus.Resolved,
                Stage = CashReconciliationStage.Ready,
                ResponsibilityLevel = CashResponsibilityLevel.Confirmed,
                Amount = 0,
                OriginalAmount = 100,
                FormalizedAmount = 100,
                PostedFormalizedAmount = 100,
                ResponsibleEmployeeName = "Старый сотрудник",
                Resolution = CashReconciliationResolution.FormalizedLoss,
                ResolvedAt = Now.AddMinutes(1)
            }
        };

        CashConstitutionEngine.Normalize(items, MonthStart, NextMonthStart);
        CashConstitutionEngine.Normalize(items, MonthStart, NextMonthStart);

        var card = items.Single(item => item.Id == cardId);
        Equal(CashConstitutionEngine.CurrentSchemaVersion, card.AccountingSchemaVersion, "Версия схемы");
        Equal(CashReconciliationStatus.Resolved, card.Status, "Статус");
        Equal(0, card.Amount, "Остаток");
        Equal(1, card.LossAllocations.Count, "Исторические назначения");
        Equal(100, card.LossAllocations.Single().Amount, "Сумма назначения");
        Equal(100, card.LossAllocations.Single().PostedAmount, "Проведённая сумма");
        Equal(0, Open(items).Count, "Открытые карты");
    }

    private void NegativeCheckpointDoesNotMirror()
    {
        var items = NewLedger();
        AddReadyShortage(items, 100, CashResponsibilityLevel.Unknown, Now);
        CashConstitutionEngine.ApplyCorrection(
            items,
            MonthStart,
            NextMonthStart,
            Now.AddMinutes(1),
            checkpointNumber: 20
        );

        Equal(
            -100,
            CashConstitutionEngine.GetLatestCheckpointBreakdown(
                items,
                MonthStart,
                NextMonthStart),
            "Разбор на контрольной точке"
        );
        int missing = CashConstitutionEngine.CalculateUnrepresentedDifference(
            observedDifference: 100,
            alreadyFormalizedLosses: 0,
            representedCycleDifference: 0,
            checkpointBreakdown: -100
        );
        Equal(0, missing, "Зеркальная карта");
    }

    private void PreliminaryCorrectionHasNoCheckpoint()
    {
        var items = NewLedger();
        AddReadyShortage(
            items,
            25,
            CashResponsibilityLevel.Unknown,
            Now
        );

        CashConstitutionEngine.ApplyCorrection(
            items,
            MonthStart,
            NextMonthStart,
            Now.AddMinutes(1),
            checkpointNumber: 0
        );

        Equal(
            0,
            items.Count(item =>
                item.IsTechnicalEvent &&
                item.Origin == CashReconciliationOrigin.CorrectionCheckpoint),
            "Контрольные точки"
        );
        Equal(
            0,
            CashConstitutionEngine.GetLatestCheckpointBreakdown(
                items,
                MonthStart,
                NextMonthStart),
            "Снимок разбора"
        );
    }

    private void CorrectionCommitIsIdempotent()
    {
        var items = NewLedger();
        AddReadyShortage(
            items,
            25,
            CashResponsibilityLevel.Unknown,
            Now
        );
        const string operationId = "correction-command-1";

        CashConstitutionEngine.ApplyCorrection(
            items,
            MonthStart,
            NextMonthStart,
            Now.AddMinutes(1),
            checkpointNumber: 30,
            operationId: operationId,
            actualCashAtCheckpoint: 100,
            actualCashlessAtCheckpoint: 200
        );
        CashConstitutionEngine.ApplyCorrection(
            items,
            MonthStart,
            NextMonthStart,
            Now.AddMinutes(2),
            checkpointNumber: 31,
            operationId: operationId,
            actualCashAtCheckpoint: 999,
            actualCashlessAtCheckpoint: 999
        );

        var marker = items.Single(item =>
            item.IsTechnicalEvent &&
            item.Origin == CashReconciliationOrigin.CorrectionCheckpoint &&
            item.OperationId == operationId);
        Equal(30L, marker.CheckpointNumber, "Номер контрольной точки");
        Equal(100, marker.ExpectedAmount, "Факт наличных");
        Equal(200, marker.ActualAmount, "Факт безнала");
        Equal(-25, marker.AmountAtCheckpoint, "Разбор");
    }

    private void CorrectionDoesNotCreateMirroredDifference()
    {
        var items = NewLedger();
        Guid shortageId = AddReadyShortage(
            items, 322, CashResponsibilityLevel.Unknown, Now);

        var first = CashConstitutionEngine.ApplyCorrection(
            items, MonthStart, NextMonthStart, Now.AddMinutes(1), 1, "correction-1");
        var repeated = CashConstitutionEngine.ApplyCorrection(
            items, MonthStart, NextMonthStart, Now.AddMinutes(2), 2, "correction-1");

        Equal(-322, first.Breakdown, "Первая точка");
        Equal(-322, repeated.Breakdown, "Повтор точки");
        Equal(shortageId, Open(items).Single(IsShortage).Id, "Та же карта");
        Equal(0, Open(items).Count(IsExtra), "Нет зеркального излишка");
    }

    private void ManualLossPartiallyConsumesShortage()
    {
        var items = NewLedger();
        AddReadyShortage(items, 100, CashResponsibilityLevel.Unknown, Now);

        var result = CashConstitutionEngine.ApplyManualLoss(
            items, MonthStart, NextMonthStart, Now.AddMinutes(1), "Владелец выбрал", 40);

        Equal(60, Open(items).Single(IsShortage).Amount, "Остаток потери");
        Equal(-60, result.Breakdown, "Разбор");
        Equal(40, result.Assignments.Sum(item => item.Amount), "Штраф");
    }

    private void PartialLossKeepsRemainingResponsibility()
    {
        var items = NewLedger();
        Guid id = AddReadyShortage(
            items,
            100,
            CashResponsibilityLevel.Suspected,
            Now,
            suspect: "Админ 1"
        );

        CashConstitutionEngine.ApplyManualLoss(
            items,
            MonthStart,
            NextMonthStart,
            Now.AddMinutes(1),
            "Админ 2",
            40
        );

        var remaining = Open(items).Single(item => item.Id == id);
        Equal(60, remaining.Amount, "Остаток");
        Equal(CashResponsibilityLevel.Suspected, remaining.ResponsibilityLevel, "Тип");
        Equal("Админ 1", remaining.SuspectedEmployeeName, "Подозреваемый");
        Equal("", remaining.ResponsibleEmployeeName, "Ответственный");
    }

    private void PartialLossAllocationsRemainSeparate()
    {
        var items = NewLedger();
        Guid id = AddReadyShortage(
            items,
            1000,
            CashResponsibilityLevel.Suspected,
            Now,
            suspect: "Исходный подозреваемый"
        );

        CashConstitutionEngine.ApplyManualLoss(
            items, MonthStart, NextMonthStart, Now.AddMinutes(1), "Сталбек", 300);
        CashConstitutionEngine.ApplyManualLoss(
            items, MonthStart, NextMonthStart, Now.AddMinutes(2), "Мирбек", 200);

        var card = items.Single(item => item.Id == id);
        Equal(500, card.Amount, "Остаток");
        Equal(CashResponsibilityLevel.Suspected, card.ResponsibilityLevel, "Тип остатка");
        Equal("Исходный подозреваемый", card.SuspectedEmployeeName, "Подозреваемый");
        Equal(2, card.LossAllocations.Count, "Назначения");
        Equal(300, card.LossAllocations.Single(item => item.EmployeeName == "Сталбек").Amount, "Сталбек");
        Equal(200, card.LossAllocations.Single(item => item.EmployeeName == "Мирбек").Amount, "Мирбек");
    }

    private void ManualLossUsesConstitutionPriority()
    {
        var items = NewLedger();
        Guid confirmedId = AddReadyShortage(
            items,
            100,
            CashResponsibilityLevel.Confirmed,
            Now,
            responsible: "Админ 1"
        );
        Guid suspectedId = AddReadyShortage(
            items,
            100,
            CashResponsibilityLevel.Suspected,
            Now.AddMinutes(1),
            suspect: "Админ 1"
        );
        Guid unknownId = AddReadyShortage(
            items,
            100,
            CashResponsibilityLevel.Unknown,
            Now.AddMinutes(2)
        );

        CashConstitutionEngine.ApplyManualLoss(
            items, MonthStart, NextMonthStart, Now.AddMinutes(3), "Админ 1", 150);

        Equal(0, Open(items).Count(item => item.Id == confirmedId), "Известная карта");
        Equal(50, Open(items).Single(item => item.Id == suspectedId).Amount, "Подозрение");
        Equal(100, Open(items).Single(item => item.Id == unknownId).Amount, "Неизвестная");

        CashConstitutionEngine.ApplyManualLoss(
            items, MonthStart, NextMonthStart, Now.AddMinutes(4), "Админ 2", 100);

        Equal(0, Open(items).Count(item => item.Id == suspectedId), "Подозрение после второго штрафа");
        Equal(50, Open(items).Single(item => item.Id == unknownId).Amount, "Итог неизвестной");
        Equal(CashResponsibilityLevel.Unknown, items.Single(item => item.Id == unknownId).ResponsibilityLevel, "Тип остатка");
    }

    private void ManualLossOperationIdIsIdempotent()
    {
        var items = NewLedger();
        AddReadyShortage(items, 100, CashResponsibilityLevel.Unknown, Now);

        var first = CashConstitutionEngine.ApplyManualLoss(
            items,
            MonthStart,
            NextMonthStart,
            Now.AddMinutes(1),
            "Админ",
            40,
            "command-105103"
        );
        var repeated = CashConstitutionEngine.ApplyManualLoss(
            items,
            MonthStart,
            NextMonthStart,
            Now.AddMinutes(2),
            "Админ",
            40,
            "command-105103"
        );

        Equal(40, first.Assignments.Sum(item => item.Amount), "Первое назначение");
        Equal(0, repeated.Assignments.Count, "Повторные назначения");
        Equal(60, Open(items).Single(IsShortage).Amount, "Остаток");
        Equal(
            1,
            items.SelectMany(item => item.LossAllocations).Count(),
            "Количество назначений"
        );
    }

    private void ManualLossExactlyClosesShortage()
    {
        var items = NewLedger();
        AddReadyShortage(items, 100, CashResponsibilityLevel.Unknown, Now);

        var result = CashConstitutionEngine.ApplyManualLoss(
            items, MonthStart, NextMonthStart, Now.AddMinutes(1), "Владелец выбрал", 100);

        Equal(0, Open(items).Count, "Открытые карты");
        Equal(0, result.Breakdown, "Разбор");
    }

    private void ManualLossOverageCreatesExtra()
    {
        var items = NewLedger();
        AddReadyShortage(items, 100, CashResponsibilityLevel.Unknown, Now);

        var result = CashConstitutionEngine.ApplyManualLoss(
            items, MonthStart, NextMonthStart, Now.AddMinutes(1), "Владелец выбрал", 125);

        Equal(25, result.Breakdown, "Новый излишек");
        Equal(25, Open(items).Single(IsExtra).Amount, "Карта излишка");
        Equal(125, result.Assignments.Sum(item => item.Amount), "Полный штраф");
    }

    private void ManualLossOverageHasDedicatedOrigin()
    {
        var items = NewLedger();
        AddReadyShortage(items, 100, CashResponsibilityLevel.Unknown, Now);

        CashConstitutionEngine.ApplyManualLoss(
            items, MonthStart, NextMonthStart, Now.AddMinutes(1), "Выбранный", 130);

        var contribution = Open(items)
            .Single(IsExtra)
            .ExtraContributions
            .Single(item => item.Amount > 0);
        Equal(CashReconciliationOrigin.OwnerPenaltyOverage, contribution.Origin, "Источник");
        Equal(30, contribution.Amount, "Излишек");
    }

    private void RealPartialPenaltyThenLaterExtra()
    {
        var items = NewLedger();
        Guid shortageId = AddReadyShortage(
            items,
            1811,
            CashResponsibilityLevel.Suspected,
            Now.AddHours(-2),
            suspect: "Мирбек"
        );

        CashConstitutionEngine.RecordRawDifference(
            items,
            MonthStart,
            NextMonthStart,
            Now.AddHours(-1),
            353,
            0,
            353,
            "",
            "",
            "Ранее найденные излишки"
        );
        CashConstitutionEngine.ApplyManualLoss(
            items, MonthStart, NextMonthStart, Now.AddMinutes(-30), "Сталбек", 300);
        CashConstitutionEngine.ApplyManualLoss(
            items, MonthStart, NextMonthStart, Now.AddMinutes(-20), "Мирбек", 200);

        var beforeVerification = items.Single(item => item.Id == shortageId);
        Equal(958, beforeVerification.Amount, "Остаток до сверки");
        Equal(CashResponsibilityLevel.Suspected, beforeVerification.ResponsibilityLevel, "Тип до сверки");

        var result = CashConstitutionEngine.RecordCashlessVerification(
            items,
            MonthStart,
            NextMonthStart,
            Now,
            1000,
            2000,
            "",
            "Новый излишек 1000"
        );

        Equal(42, result.Breakdown, "Итоговый излишек");
        Equal(0, result.Assignments.Count, "Новые штрафы");
        Equal(0, Open(items).Count(IsShortage), "Открытые недостачи");
        Equal(42, Open(items).Single(IsExtra).Amount, "Остаток карты излишка");
        Equal(300, beforeVerification.LossAllocations.Single(item => item.EmployeeName == "Сталбек").Amount, "Сталбек");
        Equal(200, beforeVerification.LossAllocations.Single(item => item.EmployeeName == "Мирбек").Amount, "Мирбек");
    }

    private void VerificationLeavesConfirmedRemainderForCorrection()
    {
        var items = NewLedger();
        AddReadyShortage(items, 50, CashResponsibilityLevel.Unknown, Now.AddHours(-3));
        AddReadyShortage(
            items,
            50,
            CashResponsibilityLevel.Suspected,
            Now.AddHours(-2),
            suspect: "Подозреваемый"
        );
        AddReadyShortage(
            items,
            100,
            CashResponsibilityLevel.Confirmed,
            Now.AddHours(-1),
            responsible: "Ответственный"
        );

        var result = CashConstitutionEngine.RecordCashlessVerification(
            items,
            MonthStart,
            NextMonthStart,
            Now,
            1000,
            1120,
            "",
            "Излишек 120"
        );

        Equal(120, result.SettledAmount, "Зачтено");
        Equal(0, result.Assignments.Count, "Сверка не оформляет");
        Equal(-80, result.Breakdown, "Остаток известной карты");

        var correction = CashConstitutionEngine.ApplyCorrection(
            items, MonthStart, NextMonthStart, Now.AddMinutes(1), 1);
        Equal(1, correction.Assignments.Count, "Оформления");
        Equal("Ответственный", correction.Assignments[0].EmployeeName, "Сотрудник");
        Equal(80, correction.Assignments[0].Amount, "Оформленный остаток");
        Equal(0, correction.Breakdown, "Разбор после точки");
    }

    private void FreeManualLossIsBalanced()
    {
        var items = NewLedger();

        var result = CashConstitutionEngine.ApplyManualLoss(
            items, MonthStart, NextMonthStart, Now, "Выбранный", 100);

        Equal(100, result.Breakdown, "Искусственный излишек");
        Equal(
            100,
            items.Where(IsShortage).Sum(item => item.FormalizedAmount),
            "Оформленная встречная проводка"
        );
        Equal(
            0,
            result.Breakdown - items.Where(IsShortage).Sum(item => item.FormalizedAmount),
            "Физическая разница не изменилась"
        );
    }

    private void PositiveMonthClose()
    {
        var items = NewLedger();
        AddReadyExtra(items, 90, Now);

        var result = CashConstitutionEngine.CloseMonth(
            items, MonthStart, NextMonthStart, NextMonthStart.AddMinutes(-2),
            new Dictionary<string, double> { ["А"] = 10 });

        Equal(90, result.ClosingBreakdown, "Итог месяца");
        Equal(90, result.ArchivedExtra, "Архивированный излишек");
        Equal(0, result.Assignments.Count, "Штрафы");
        Equal(0, Open(items).Count, "Открытые карты");
    }

    private void NegativeMonthCloseDistribution()
    {
        var items = NewLedger();
        AddReadyShortage(items, 100, CashResponsibilityLevel.Unknown, Now);

        var result = CashConstitutionEngine.CloseMonth(
            items, MonthStart, NextMonthStart, NextMonthStart.AddMinutes(-2),
            new Dictionary<string, double>
            {
                ["Работник 1"] = 99,
                ["Работник 2"] = 1,
                ["Работник 3"] = 0
            });

        Equal(-100, result.ClosingBreakdown, "Итог месяца");
        Equal(99, result.Assignments.Single(item => item.EmployeeName == "Работник 1").Amount, "99 часов");
        Equal(1, result.Assignments.Single(item => item.EmployeeName == "Работник 2").Amount, "1 час");
        Equal(0, result.Assignments.Count(item => item.EmployeeName == "Работник 3"), "0 часов");
        Equal(100, result.Assignments.Sum(item => item.Amount), "Сохранение суммы");
    }

    private void MonthCloseDistributesNetShortage()
    {
        var items = NewLedger();
        AddReadyShortage(items, 100, CashResponsibilityLevel.Confirmed, Now, responsible: "Старый");
        AddReadyExtra(items, 40, Now.AddMinutes(1));

        var result = CashConstitutionEngine.CloseMonth(
            items, MonthStart, NextMonthStart, NextMonthStart.AddMinutes(-2),
            new Dictionary<string, double>
            {
                ["Работник 1"] = 3,
                ["Работник 2"] = 1
            });

        Equal(-60, result.ClosingBreakdown, "Чистый итог");
        Equal(60, result.Assignments.Sum(item => item.Amount), "Распределённая сумма");
        Equal(45, result.Assignments.Single(item => item.EmployeeName == "Работник 1").Amount, "3/4");
        Equal(15, result.Assignments.Single(item => item.EmployeeName == "Работник 2").Amount, "1/4");
    }

    private void MonthCloseWithoutHoursIsDeferred()
    {
        var items = NewLedger();
        Guid id = AddReadyShortage(
            items,
            70,
            CashResponsibilityLevel.Unknown,
            Now
        );

        var result = CashConstitutionEngine.CloseMonth(
            items, MonthStart, NextMonthStart, NextMonthStart.AddMinutes(-2),
            new Dictionary<string, double>
            {
                ["Работник 1"] = 0
            });

        Assert(result.IsDeferred, "Закрытие должно быть отложено.");
        Equal(-70, result.ClosingBreakdown, "Сохранённый итог");
        Equal(id, Open(items).Single(IsShortage).Id, "Сохранённая карта");
        Equal(70, Open(items).Single(IsShortage).Amount, "Сохранённая сумма");
    }

    private void MonthCloseSurvivesRestart()
    {
        var items = NewLedger();
        AddReadyShortage(items, 100, CashResponsibilityLevel.Unknown, Now);

        var hours = new Dictionary<string, double>
        {
            ["Работник 1"] = 3,
            ["Работник 2"] = 1
        };
        DateTime closeAt = NextMonthStart.AddMinutes(-2);
        var first = CashConstitutionEngine.CloseMonth(
            items, MonthStart, NextMonthStart, closeAt, hours);

        string json = JsonSerializer.Serialize(items);
        var restored = JsonSerializer.Deserialize<List<CashReconciliationItem>>(json)
            ?? throw new InvalidOperationException("Не удалось восстановить журнал.");
        var second = CashConstitutionEngine.CloseMonth(
            restored, MonthStart, NextMonthStart, closeAt.AddSeconds(30), hours);

        Equal(-100, second.ClosingBreakdown, "Итог месяца");
        Equal(100, second.Assignments.Sum(item => item.Amount), "Сумма назначений");
        Assert(
            first.Assignments.Select(item => item.AllocationId).OrderBy(id => id)
                .SequenceEqual(
                    second.Assignments.Select(item => item.AllocationId).OrderBy(id => id)),
            "После перезапуска изменились идентификаторы назначений."
        );
        var marker = restored.Single(item =>
            item.IsTechnicalEvent &&
            item.Origin == CashReconciliationOrigin.MonthClose);
        Equal(
            marker.LossAllocations.Sum(item => item.Amount),
            marker.LossAllocations.Sum(item => item.PostedAmount),
            "Месячный акт не должен попадать в обычное восстановление проводок"
        );
        Equal(0, Open(restored).Count, "Открытые карты");
    }

    private void ClosedCardsNeverReturn()
    {
        var items = NewLedger();
        AddReadyShortage(items, 50, CashResponsibilityLevel.Suspected, Now, suspect: "Тест");
        var first = CashConstitutionEngine.ApplyCorrection(
            items, MonthStart, NextMonthStart, Now.AddMinutes(1), 5);

        CashConstitutionEngine.Normalize(items, MonthStart, NextMonthStart);
        var second = CashConstitutionEngine.ApplyCorrection(
            items, MonthStart, NextMonthStart, Now.AddMinutes(2), 6);

        Equal(50, first.Assignments.Sum(item => item.Amount), "Первое оформление");
        Equal(0, second.Assignments.Sum(item => item.Amount), "Повторное оформление");
        Equal(0, Open(items).Count, "Нет воскресших карт");
    }

    private void CheckpointBaselinePreventsMirroredDifference()
    {
        var items = NewLedger();
        AddReadyExtra(items, 100, Now);
        CashConstitutionEngine.ApplyCorrection(
            items,
            MonthStart,
            NextMonthStart,
            Now.AddMinutes(1),
            105103
        );

        Equal(
            100,
            CashConstitutionEngine.GetLatestCheckpointBreakdown(
                items, MonthStart, NextMonthStart),
            "Разбор на контрольной точке"
        );

        int missing = CashConstitutionEngine.CalculateUnrepresentedDifference(
            observedDifference: -100,
            alreadyFormalizedLosses: 0,
            representedCycleDifference: 0,
            checkpointBreakdown: 100
        );
        Equal(0, missing, "Зеркальная карта");
    }

    private void ZeroCheckpointReplacesPreviousBaseline()
    {
        var items = NewLedger();
        AddReadyExtra(items, 100, Now);
        CashConstitutionEngine.ApplyCorrection(
            items, MonthStart, NextMonthStart, Now.AddMinutes(1), 1);

        CashConstitutionEngine.RecordRawDifference(
            items,
            MonthStart,
            NextMonthStart,
            Now.AddMinutes(2),
            -100,
            100,
            0,
            "",
            "Подозреваемый",
            "Новая недостача"
        );
        CashConstitutionEngine.ApplyCorrection(
            items, MonthStart, NextMonthStart, Now.AddMinutes(3), 2);

        Equal(
            0,
            CashConstitutionEngine.GetLatestCheckpointBreakdown(
                items, MonthStart, NextMonthStart),
            "Новый снимок"
        );
    }

    private void RestartPreservesPartialAllocations()
    {
        var items = NewLedger();
        Guid id = AddReadyShortage(
            items,
            100,
            CashResponsibilityLevel.Suspected,
            Now,
            suspect: "Админ 1"
        );
        CashConstitutionEngine.ApplyManualLoss(
            items, MonthStart, NextMonthStart, Now.AddMinutes(1), "Админ 2", 40);

        string json = JsonSerializer.Serialize(items);
        var restored = JsonSerializer.Deserialize<List<CashReconciliationItem>>(json)
            ?? throw new InvalidOperationException("Не удалось восстановить журнал.");
        CashConstitutionEngine.Normalize(restored, MonthStart, NextMonthStart);

        var card = restored.Single(item => item.Id == id);
        Equal(60, card.Amount, "Остаток");
        Equal(CashResponsibilityLevel.Suspected, card.ResponsibilityLevel, "Тип");
        Equal("Админ 1", card.SuspectedEmployeeName, "Подозреваемый");
        Equal(1, card.LossAllocations.Count, "Назначения");
        Equal("Админ 2", card.LossAllocations[0].EmployeeName, "Получатель штрафа");
    }

    private void RandomizedInvariantTest()
    {
        var random = new Random(105103);

        for (int run = 0; run < 250; run++)
        {
            var items = NewLedger();
            DateTime time = Now;

            for (int step = 0; step < 80; step++)
            {
                time = time.AddSeconds(1);
                int operation = random.Next(5);
                int amount = random.Next(0, 301);

                if (operation == 0)
                {
                    CashConstitutionEngine.RecordCashAcceptance(
                        items, MonthStart, NextMonthStart, time,
                        "Новый", "Старый", 1000, 1000 + random.Next(-amount, amount + 1), "Random");
                }
                else if (operation == 1)
                {
                    CashConstitutionEngine.RecordCashlessVerification(
                        items, MonthStart, NextMonthStart, time,
                        1000, 1000 + random.Next(-amount, amount + 1),
                        random.Next(2) == 0 ? "" : "Подозреваемый", "Random");
                }
                else if (operation == 2)
                {
                    CashConstitutionEngine.ApplyCorrection(
                        items, MonthStart, NextMonthStart, time, step + 1);
                }
                else if (operation == 3 && amount > 0)
                {
                    CashConstitutionEngine.ApplyManualLoss(
                        items, MonthStart, NextMonthStart, time, "Выбранный", amount);
                }
                else
                {
                    CashConstitutionEngine.Normalize(items, MonthStart, NextMonthStart);
                }

                AssertInvariants(items);
            }
        }
    }

    private void AssertInvariants(List<CashReconciliationItem> items)
    {
        Equal(0, items.Count(item => item.Amount < 0), "Отрицательная активная сумма");
        Equal(0, items.Count(item => item.ResolvedAmount < 0), "Отрицательная закрытая сумма");
        Equal(0, items.Count(item => item.FormalizedAmount < 0), "Отрицательный штраф");
        Equal(
            0,
            items.Count(item =>
                (item.LossAllocations ?? new List<CashLossAllocation>())
                    .Sum(allocation => Math.Max(0, allocation.Amount)) !=
                Math.Max(0, item.FormalizedAmount) &&
                item.AccountingSchemaVersion >= 3 &&
                item.Resolution != CashReconciliationResolution.MonthClosed),
            "Сумма назначений не равна оформленной сумме"
        );
        Equal(
            items.SelectMany(item => item.LossAllocations ?? new List<CashLossAllocation>())
                .Count(),
            items.SelectMany(item => item.LossAllocations ?? new List<CashLossAllocation>())
                .Select(item => item.Id)
                .Distinct()
                .Count(),
            "Уникальные ID назначений"
        );
        Equal(0, items.Count(item =>
            item.Status == CashReconciliationStatus.Resolved && item.Amount != 0),
            "Закрытая карта с активной суммой");
        Assert(Open(items).Count(IsExtra) <= 1, "Больше одной активной карты излишка.");
        Assert(items.All(item =>
            item.OriginalAmount <= 0 ||
            item.Amount + item.ResolvedAmount + item.FormalizedAmount <= item.OriginalAmount ||
            IsExtra(item)),
            "Недостача создала деньги.");
        CashConstitutionEngine.ValidateConservation(items, MonthStart, NextMonthStart);
    }

    private void Test(string name, Action test)
    {
        try
        {
            test();
            _passed++;
            Console.WriteLine($"PASS  {name}");
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"FAIL  {name}");
            Console.Error.WriteLine($"      {exception.Message}");
            throw;
        }
    }

    private static List<CashReconciliationItem> NewLedger() => new();

    private static void AddReadyExtra(
        List<CashReconciliationItem> items,
        int amount,
        DateTime createdAt)
    {
        items.Add(new CashReconciliationItem
        {
            Id = Guid.NewGuid(),
            InvestigationId = Guid.NewGuid(),
            CreatedAt = createdAt,
            Kind = CashReconciliationKind.CashExtra,
            Origin = CashReconciliationOrigin.BalanceRawDifference,
            Status = CashReconciliationStatus.Open,
            Stage = CashReconciliationStage.Ready,
            Amount = amount,
            OriginalAmount = amount,
            Title = "Общий излишек"
        });
    }

    private static Guid AddReadyShortage(
        List<CashReconciliationItem> items,
        int amount,
        CashResponsibilityLevel level,
        DateTime createdAt,
        string responsible = "",
        string suspect = "")
    {
        var item = new CashReconciliationItem
        {
            Id = Guid.NewGuid(),
            InvestigationId = Guid.NewGuid(),
            CreatedAt = createdAt,
            Kind = CashReconciliationKind.CashShortage,
            Origin = CashReconciliationOrigin.BalanceRawDifference,
            Status = CashReconciliationStatus.Open,
            Stage = CashReconciliationStage.Ready,
            ResponsibilityLevel = level,
            Amount = amount,
            OriginalAmount = amount,
            ResponsibleEmployeeName = responsible,
            SuspectedEmployeeName = suspect,
            Title = "Потеря"
        };
        items.Add(item);
        return item.Id;
    }

    private static List<CashReconciliationItem> Open(IEnumerable<CashReconciliationItem> items)
    {
        return items
            .Where(item =>
                item.Status == CashReconciliationStatus.Open &&
                item.Amount > 0)
            .ToList();
    }

    private static bool IsExtra(CashReconciliationItem item)
    {
        return item.Kind == CashReconciliationKind.CashExtra ||
               item.Kind == CashReconciliationKind.CashlessExtra;
    }

    private static bool IsShortage(CashReconciliationItem item)
    {
        return item.Kind == CashReconciliationKind.CashShortage ||
               item.Kind == CashReconciliationKind.CashlessShortage;
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"{label}: ожидалось {expected}, получено {actual}.");
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
