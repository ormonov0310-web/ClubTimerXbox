using ClubTimerXbox.Models;
using ClubTimerXbox.Services;
using System.Text.Json;

internal sealed class CashAcceptanceOwnerTestSuite
{
    private static readonly DateTime From = new(2026, 9, 1, 6, 0, 0);
    private static readonly DateTime To = From.AddMonths(1);
    private static readonly DateTime Start = new(2026, 9, 7, 19, 2, 52);
    private int _passed;

    public void Run()
    {
        Test("early correction: 100 + 77 - 64 = 113 to Bektur", Salikhov);
        Test("early correction replay posts no second loss", Replay);
        Test("natural posting and owner correction share operation IDs", NaturalPostingFirst);
        Test("cashless surplus first covers the cash shortage", PaymentMistake);
        Test("unknown loss survives owner finalization", UnknownSurvives);
        Test("unspent surplus survives owner finalization", ExtraSurvives);
        Test("review rejects different handover", DifferentHandover);
        Test("review rejects changed cash fact", ChangedCash);
        Test("review rejects changed cashless fact", ChangedCashless);
        Test("review requires linked cashless verification", MissingCashless);
        Test("natural expiry preserves reviewed revision", RevisionAfterExpiry);
        Test("forced expiry makes next recount self acceptance", ResponsibilityEnds);
        Test("forced expiry prevents new provisional handover", NoResurrection);
        Test("owner completion marker survives restart", ClosureSurvivesRestart);
        Test("closed historical responsible cannot mask new suspect", OldHistoryExcluded);
        Test("provisional recommendations retain both employees", MultipleRecommendations);
        Test("provisional money is included exactly once", NoDoublePreview);
        Test("provisional payload includes amount and suspect", PendingPayload);
        Test("wrong month is rejected before ledger mutation", WrongMonth);
        Test("queued owner review rejects newer verification after timer expiry", NewerVerification);
        Test("cashless recovery retains original day and receipt boundary", CashlessRecoveryDate);
        Test("cashless recovery cannot overwrite a newer fact", CashlessRecoveryNewerFact);
        Test("cashless recovery replay is idempotent", CashlessRecoveryReplay);
        Console.WriteLine($"PASS: {_passed} acceptance owner-finalization scenarios.");
    }

    private static CashAcceptanceItem Acceptance(int cashDifference = -100, int cashlessDifference = -77, string suspect = "Bektur") => new()
    {
        Id = Guid.NewGuid(), RootAcceptanceKey = "bektur->argen", AcceptanceKey = "bektur->argen",
        CheckedByEmployeeName = "Argen", ResponsibleEmployeeName = "Bektur",
        CreatedAt = Start, UpdatedAt = Start, IsProvisional = true, FinalizeAt = Start.AddMinutes(10),
        ExpectedCashAmount = 11050, ActualCashAmount = 11050 + cashDifference, Difference = cashDifference,
        PendingCashlessVerification = new()
        {
            CommandId = "verify-1", ExpectedAmount = 20904, ActualAmount = 20904 + cashlessDifference,
            ProgramExpectedAmount = 20904, SuspectedEmployeeName = suspect,
            ObservedAt = Start.AddMinutes(2), SuspectFrom = Start.AddHours(-8)
        }
    };

    private static List<CashReconciliationItem> WithExtra(int amount)
    {
        var items = new List<CashReconciliationItem>();
        CashConstitutionEngine.RecordCashlessVerification(items, From, To, Start.AddHours(-8),
            1000, 1000 + amount, "", "old extra", "old-extra");
        return items;
    }

    private static CashAccountingResult Apply(List<CashReconciliationItem> items, CashAcceptanceItem acceptance, string id = "owner-1") =>
        CashAcceptanceOwnerCorrectionPolicy.Apply(items, acceptance, From, To, Start.AddMinutes(5), id,
            acceptance.ActualCashAmount, acceptance.PendingCashlessVerification!.ActualAmount);

    private static void Validate(CashAcceptanceItem item, string? revision = null) =>
        CashAcceptanceOwnerCorrectionPolicy.Validate(item, item.Id.ToString(), revision ?? CashAcceptanceOwnerCorrectionPolicy.ReviewRevision(item));

    private static void Salikhov()
    {
        var items = WithExtra(64);
        var result = Apply(items, Acceptance());
        Equal(113, result.Assignments.Sum(row => row.Amount));
        True(result.Assignments.All(row => row.EmployeeName == "Bektur"));
        Equal(0, result.Breakdown);
        Equal(0, result.RecommendationTotal);
        True(items.Where(row => !row.IsTechnicalEvent && row.Kind == CashReconciliationKind.CashShortage)
            .All(row => row.OriginalAmount == row.Amount + row.ResolvedAmount + row.FormalizedAmount));
    }

    private static void Replay()
    {
        var items = WithExtra(64);
        var acceptance = Acceptance();
        Apply(items, acceptance);
        string before = JsonSerializer.Serialize(items);
        Equal(0, Apply(items, acceptance).Assignments.Count);
        Equal(before, JsonSerializer.Serialize(items));
    }

    private static void NaturalPostingFirst()
    {
        var items = WithExtra(64);
        var acceptance = Acceptance();
        var pending = acceptance.PendingCashlessVerification!;
        string key = CashAcceptanceOwnerCorrectionPolicy.OperationKey(acceptance);
        Guid investigation = CashAcceptanceOwnerCorrectionPolicy.InvestigationId(acceptance);
        CashConstitutionEngine.RecordCashAcceptance(items, From, To, Start.AddMinutes(10), "Argen", "Bektur",
            11050, 10950, "", key + ":ledger", Start, true, investigation);
        CashConstitutionEngine.RecordCashlessVerification(items, From, To, pending.ObservedAt,
            pending.ExpectedAmount, pending.ActualAmount, "Bektur", "", key + ":cashless-ledger", pending.ProgramExpectedAmount, investigation);
        Equal(113, Apply(items, acceptance).Assignments.Sum(row => row.Amount));
        Equal(1, items.Count(row => !row.IsTechnicalEvent && row.Kind == CashReconciliationKind.CashShortage));
    }

    private static void PaymentMistake()
    {
        var result = Apply(new(), Acceptance(-100, 80));
        Equal(20, result.Assignments.Sum(row => row.Amount));
        Equal(0, result.Breakdown);
    }

    private static void UnknownSurvives()
    {
        var result = Apply(new(), Acceptance(0, -77, ""));
        Equal(0, result.Assignments.Count);
        Equal(-77, result.Breakdown);
    }

    private static void ExtraSurvives()
    {
        var result = Apply(new(), Acceptance(-100, 150));
        Equal(0, result.Assignments.Count);
        Equal(50, result.Breakdown);
    }

    private static void DifferentHandover()
    {
        var item = Acceptance();
        Throws(() => CashAcceptanceOwnerCorrectionPolicy.Validate(item, Guid.NewGuid().ToString(), CashAcceptanceOwnerCorrectionPolicy.ReviewRevision(item)));
    }

    private static void ChangedCash()
    {
        var item = Acceptance();
        string revision = CashAcceptanceOwnerCorrectionPolicy.ReviewRevision(item);
        item.ActualCashAmount++;
        Throws(() => Validate(item, revision));
    }

    private static void ChangedCashless()
    {
        var item = Acceptance();
        string revision = CashAcceptanceOwnerCorrectionPolicy.ReviewRevision(item);
        string signature = FirebaseSyncService.BuildCashAcceptanceSignature(item);
        item.PendingCashlessVerification!.ActualAmount++;
        Throws(() => Validate(item, revision));
        True(signature != FirebaseSyncService.BuildCashAcceptanceSignature(item));
    }

    private static void MissingCashless()
    {
        var item = Acceptance();
        item.PendingCashlessVerification = null;
        Throws(() => Validate(item));
    }

    private static void RevisionAfterExpiry()
    {
        var item = Acceptance();
        string revision = CashAcceptanceOwnerCorrectionPolicy.ReviewRevision(item);
        item.FinalizedReviewRevision = revision;
        item.IsProvisional = false;
        item.PendingCashlessVerification = null;
        Validate(item, revision);
    }

    private static ShiftAcceptanceStatus ClosedState() => new()
    {
        InitialCashAcceptedAt = Start, NewEmployeeName = "Argen", ResponsibleEmployeeName = "Bektur",
        CashResponsibilityClosedAt = Start.AddMinutes(3), ProductsAccepted = true, CashAccepted = true
    };

    private static void ResponsibilityEnds() => Equal("Argen", ShiftAcceptanceCorrectionPolicy.ResolveResponsibleEmployee(
        ClosedState(), "Bektur", "Argen", Start.AddMinutes(4)));

    private static void NoResurrection() => True(!ShiftAcceptanceCorrectionPolicy.ShouldStageInitialCashAcceptance(
        ClosedState(), "bektur->argen", Start.AddMinutes(4)));

    private static void ClosureSurvivesRestart()
    {
        var restored = JsonSerializer.Deserialize<ShiftAcceptanceStatus>(JsonSerializer.Serialize(ClosedState()))!;
        Equal("Argen", ShiftAcceptanceCorrectionPolicy.ResolveResponsibleEmployee(restored, "Bektur", "Argen", Start.AddMinutes(4)));
        var items = WithExtra(64);
        var acceptance = Acceptance();
        acceptance.OwnerCorrectionCommandId = "owner-1";
        Apply(items, acceptance);
        var restarted = JsonSerializer.Deserialize<List<CashReconciliationItem>>(JsonSerializer.Serialize(items))!;
        True(CashConstitutionEngine.HasAppliedOperation(restarted, "owner-1"));
        Equal(0, Apply(restarted, acceptance).Assignments.Count);
    }

    private static void OldHistoryExcluded()
    {
        var items = new List<CashReconciliationItem>();
        CashConstitutionEngine.RecordCashAcceptance(items, From, To, Start.AddHours(-8), "Bektur", "Argen", 1000, 900, "", "old-cash");
        CashConstitutionEngine.ApplyCorrection(items, From, To, Start.AddHours(-7), 1, "old-correction");
        CashConstitutionEngine.RecordCashlessVerification(items, From, To, Start, 1000, 923, "Bektur", "", "new-verify");
        var rows = CashAccountabilityPolicy.Recommendations(items, From, To);
        Equal(1, rows.Count);
        Equal("Bektur", rows[0].EmployeeName);
        Equal(77, rows[0].Amount);
    }

    private static void MultipleRecommendations()
    {
        var rows = CashAccountabilityPolicy.Recommendations(new List<CashReconciliationItem>(), From, To, Acceptance(-100, -77, "Third"));
        Equal(177, rows.Sum(row => row.Amount));
        True(rows.Any(row => row.EmployeeName == "Bektur" && row.Amount == 100));
        True(rows.Any(row => row.EmployeeName == "Third" && row.Amount == 77));
        True(rows.All(row => row.EmployeeName != "Argen"));
    }

    private static void NoDoublePreview()
    {
        var item = Acceptance();
        var items = WithExtra(64);
        Equal(-177, CashAccountabilityPolicy.UnpostedDifference(items, item));
        Apply(items, item);
        Equal(0, CashAccountabilityPolicy.UnpostedDifference(items, item));
        Equal(0, CashAccountabilityPolicy.Recommendations(items, From, To, item).Count);
    }

    private static void PendingPayload()
    {
        var payload = FirebaseSyncService.BuildCashAcceptancePayload(Acceptance())!;
        var pending = (Dictionary<string, object?>)payload["pendingCashlessVerification"]!;
        Equal(-77, (int)pending["difference"]!);
        Equal("Bektur", (string)pending["suspectedEmployeeName"]!);
        True((bool)payload["supportsOwnerFinalization"]!);
        True(((string)payload["reviewRevision"]!).Length > 0);
    }

    private static void WrongMonth()
    {
        var items = new List<CashReconciliationItem>();
        Throws(() => CashAcceptanceOwnerCorrectionPolicy.Apply(items, Acceptance(), To, To.AddMonths(1), To, "wrong", 1, 1));
        Equal(0, items.Count);
    }

    private static void NewerVerification()
    {
        var item = Acceptance();
        item.IsProvisional = false;
        item.FinalizedAt = Start.AddMinutes(10);
        CashAcceptanceOwnerCorrectionPolicy.ValidateFinalizedReview(item, Start.AddMinutes(2));
        Throws(() => CashAcceptanceOwnerCorrectionPolicy.ValidateFinalizedReview(item, Start.AddMinutes(11)));
    }

    private static void CashlessRecoveryDate()
    {
        var commit = new DateTime(2026, 9, 8, 5, 59, 0);
        var record = CashlessCheckpointPolicy.Build(null, commit, 500, "correction", 500)!;
        Equal(new DateTime(2026, 9, 7), record.Date);
        Equal(commit, record.UpdatedAt);
        Equal(500, record.Amount);
    }

    private static void CashlessRecoveryNewerFact()
    {
        var record = new CashlessDayRecord { Amount = 600, UpdatedAt = Start.AddMinutes(1) };
        True(CashlessCheckpointPolicy.Build(record, Start, 500, "correction", 500) == null);
        Equal(600, record.Amount);
    }

    private static void CashlessRecoveryReplay()
    {
        var record = CashlessCheckpointPolicy.Build(null, Start, 500, "correction", 500)!;
        True(CashlessCheckpointPolicy.Build(record, Start, 500, "correction", 500) == null);
    }

    private void Test(string name, Action test) { test(); _passed++; Console.WriteLine("PASS: " + name); }
    private static void True(bool value) { if (!value) throw new Exception("Assertion failed"); }
    private static void Equal<T>(T expected, T actual) { if (!Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}"); }
    private static void Throws(Action action) { try { action(); } catch (InvalidOperationException) { return; } throw new Exception("Expected rejection"); }
}
