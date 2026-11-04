using ClubTimerXbox.Models;
using ClubTimerXbox.Services;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

internal static class TelecomLoanEvidenceTests
{
    public static void Run(string path, string? supplementPath = null)
    {
        byte[] bytes = File.ReadAllBytes(path);
        if (Convert.ToHexStringLower(SHA256.HashData(bytes)) !=
            "715f2e32020d06d80b7affbef6558c14b640303157d0c6b1466fa19a9084a108")
            throw new InvalidOperationException("Unexpected sanitized evidence hash");
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        var payments = root.GetProperty("payments").Deserialize<PaymentRecord[]>()!;
        var cash = new List<CashRecord>();
        foreach (string key in new[] { "bankExpenses", "loanCash", "bankLoanReturns", "preBaselineBankExpensesExcluded" })
            cash.AddRange(root.GetProperty(key).Deserialize<CashRecord[]>()!);
        int baseline = root.GetProperty("baseline").GetProperty("Amount").GetInt32();
        // Evidence timestamps are club wall times; do not convert them to the test machine's zone.
        DateTime from = WallTime(root.GetProperty("baseline").GetProperty("UpdatedAt").GetString()!);
        var card = root.GetProperty("card");
        DateTime to = WallTime(card.GetProperty("CreatedAt").GetString()!);
        int expected = CashlessVerificationBalancePolicy.Calculate(baseline, payments, cash, from, to, true);
        int actual = card.GetProperty("ActualAmount").GetInt32();
        if (expected != 3017 || actual - expected != 52 ||
            card.GetProperty("ExpectedAmount").GetInt32() != 6017)
            throw new InvalidOperationException("Actual selected cashless records do not reproduce incident");
        var contract = root.GetProperty("contract")[0].Deserialize<EmployeeDebtContract>()!;
        var issue = root.GetProperty("event").Deserialize<EmployeeDebtEvent>()!;
        var movement = ClubLoanCashPolicy.BuildMovement(contract, issue)!;
        if (movement.Id != KnownCashlessLoanRepairPolicy.LoanId ||
            cash.Count(row => row.Id == movement.Id) != 1 || movement.Amount != 3000)
            throw new InvalidOperationException("Actual loan linkage differs from repair anchor");
        Console.WriteLine("Telecom sanitized evidence: SHA256, selected-record arithmetic3017/+52 and single stable loan verified.");
        if (supplementPath != null)
            VerifyRepair(root, supplementPath, contract, cash);
    }

    private static void VerifyRepair(JsonElement evidence, string path, EmployeeDebtContract contract,
        List<CashRecord> cash)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        var root = document.RootElement;
        var options = new JsonSerializerOptions { NumberHandling = JsonNumberHandling.AllowReadingFromString };
        var card = root.GetProperty("card").Deserialize<CashReconciliationItem>(options)!;
        var checkpoints = root.GetProperty("correctionCheckpoints");
        var previous = checkpoints.GetProperty("lastBefore").Deserialize<CashReconciliationItem>(options)!;
        var accepted = checkpoints.GetProperty("latestAfter").Deserialize<CashReconciliationItem>(options)!;
        // Supplement omits Kind/IsTechnicalEvent for the card; the hashed original contains both.
        card.Kind = evidence.GetProperty("card").GetProperty("Kind").Deserialize<CashReconciliationKind>();
        card.IsTechnicalEvent = evidence.GetProperty("card").GetProperty("IsTechnicalEvent").GetBoolean();
        if (root.GetProperty("matchingCardIdCount").GetInt32() != 1 ||
            !root.GetProperty("source").GetProperty("stable").GetBoolean() ||
            !root.GetProperty("comparison").GetProperty("currentFileSameAsTEL002").GetBoolean() ||
            checkpoints.GetProperty("recordsAfterCount").GetInt32() != 1 ||
            !checkpoints.GetProperty("firstAndLatestAreSame").GetBoolean() ||
            card.CheckpointNumber != 639293775022061272L || card.CheckpointNumber != accepted.CheckpointNumber)
            throw new InvalidOperationException("Supplement is not the unchanged exact current card/checkpoint");
        var items = new List<CashReconciliationItem> { previous, card, accepted };
        string acceptedBefore = JsonSerializer.Serialize(accepted);
        string moneyBefore = JsonSerializer.Serialize(cash);
        string contractBefore = JsonSerializer.Serialize(contract);
        DateTime now = WallTime(root.GetProperty("readAtLocal").GetString()!);
        var month = BusinessCalendarService.GetBusinessMonth(now);
        if (CashConstitutionEngine.GetLatestCheckpointBreakdown(items, month.StartInclusive, month.EndExclusive) != -2948 ||
            KnownCashlessLoanRepairPolicy.Apply("club_1", items, contract, cash, Array.Empty<EmployeeLossItem>(), now) !=
                KnownDataRepairResult.Applied ||
            CashConstitutionEngine.GetBreakdown(items, month.StartInclusive, month.EndExclusive) != 0 ||
            CashConstitutionEngine.GetLatestCheckpointBreakdown(items, month.StartInclusive, month.EndExclusive) != 0 ||
            JsonSerializer.Serialize(accepted) != acceptedBefore || JsonSerializer.Serialize(cash) != moneyBefore ||
            JsonSerializer.Serialize(contract) != contractBefore)
            throw new InvalidOperationException("Actual selected rows did not repair without changing loan/baseline");
        CashConstitutionEngine.ValidateConservation(items, month.StartInclusive, month.EndExclusive);
        string repaired = JsonSerializer.Serialize(items);
        items = JsonSerializer.Deserialize<List<CashReconciliationItem>>(repaired)!;
        if (KnownCashlessLoanRepairPolicy.Apply("club_1", items, contract, cash, Array.Empty<EmployeeLossItem>(), now) !=
                KnownDataRepairResult.AlreadyApplied || JsonSerializer.Serialize(items) != repaired)
            throw new InvalidOperationException("Actual selected-row repair is not restart-idempotent");
        Console.WriteLine("Telecom supplement: exact Int64 checkpoint, guarded repair, zero carried false loss, " +
            "unchanged physical baseline/loan and restart idempotence verified on selected in-memory rows only.");
    }

    private static DateTime WallTime(string text) =>
        DateTimeOffset.Parse(text, CultureInfo.InvariantCulture).DateTime;
}
