using ClubTimerXbox.Models;
using ClubTimerXbox.Services;
using System.Text.Json;

internal static class KnownCashlessLoanRepairTests
{
    private static readonly DateTime Now = new(2026, 11, 4, 17, 0, 0);
    public static void Run()
    {
        int checks = 0;
        void Check(bool result, string message)
        {
            if (!result) throw new InvalidOperationException("Loan repair: " + message);
            checks++;
        }
        var data = Fixture();
        string moneyBefore = JsonSerializer.Serialize(data.Cash);
        string contractBefore = JsonSerializer.Serialize(data.Contract);
        string oldCheckpoint = JsonSerializer.Serialize(data.Items[1]);
        Check(Apply(data) == KnownDataRepairResult.Applied, "recognizes exact open card and posted loan");
        var card = data.Items[0];
        Check(card.Amount == 0 && card.Status == CashReconciliationStatus.Resolved && card.ResolvedAmount == 2948 &&
            card.FormalizedAmount == 0, "cancel false shortage without salary penalty");
        Check(card.ExpectedAmount == 6017 && card.ActualAmount == 3069 && card.OriginalAmount == 2948 &&
            card.OperationId == "cashless_verify_1793780688649", "original observation and source identity retained");
        Check(JsonSerializer.Serialize(data.Cash) == moneyBefore && JsonSerializer.Serialize(data.Contract) == contractBefore &&
            JsonSerializer.Serialize(data.Items[1]) == oldCheckpoint, "loan, money rows, old physical checkpoint unchanged");
        var month = BusinessCalendarService.GetBusinessMonth(Now);
        Check(CashConstitutionEngine.GetBreakdown(data.Items, month.StartInclusive, month.EndExclusive) == 0 &&
            CashConstitutionEngine.GetLatestCheckpointBreakdown(data.Items, month.StartInclusive, month.EndExclusive) == 0,
            "active and carried false shortage both removed");
        var residual = data.Items.Single(row => row.Id == KnownCashlessLoanRepairPolicy.ResidualId);
        Check(residual.IsTechnicalEvent && residual.Amount == 0 && residual.OriginalAmount == 52 &&
            residual.ResolvedAmount == 52 && residual.ExpectedAmount == 3017 && residual.ActualAmount == 3069,
            "unexplained 52 retained separately, never recredited as new money");
        string repaired = JsonSerializer.Serialize(data.Items);
        Check(Apply(data) == KnownDataRepairResult.AlreadyApplied && JsonSerializer.Serialize(data.Items) == repaired,
            "second run does not change rows or duplicate repair events");
        data.Items = JsonSerializer.Deserialize<List<CashReconciliationItem>>(repaired)!;
        Check(Apply(data) == KnownDataRepairResult.AlreadyApplied && JsonSerializer.Serialize(data.Items) == repaired,
            "restart after durable save is idempotent");
        CashConstitutionEngine.ValidateConservation(data.Items, month.StartInclusive, month.EndExclusive);
        Check(true, "repaired ledger passes production conservation invariants");
        CashConstitutionEngine.RecordCashlessVerification(data.Items, month.StartInclusive, month.EndExclusive,
            Now.AddMinutes(1), 3069, 3069, "", "next verification", "test:next-verification");
        Check(CashConstitutionEngine.GetBreakdown(data.Items, month.StartInclusive, month.EndExclusive) == 0 &&
            !data.Items.Any(row => row.Kind == CashReconciliationKind.CashlessShortage &&
                row.Status == CashReconciliationStatus.Open && row.Amount > 0),
            "next verification does not recreate the canceled shortage");

        var other = Fixture();
        other.Items.Add(new CashReconciliationItem
        {
            Kind = CashReconciliationKind.CashShortage, CreatedAt = Now.AddHours(-1),
            OriginalAmount = 100, Amount = 100, CheckpointNumber = 1000, AmountAtCheckpoint = 100
        });
        other.Items[1].AmountAtCheckpoint = -3048;
        Check(Apply(other) == KnownDataRepairResult.Applied &&
            CashConstitutionEngine.GetBreakdown(other.Items, month.StartInclusive, month.EndExclusive) == -100 &&
            CashConstitutionEngine.GetLatestCheckpointBreakdown(other.Items, month.StartInclusive, month.EndExclusive) == -100,
            "unrelated real shortages and their carried amount are preserved");
        var legacy = Fixture();
        legacy.Cash.Add(new CashRecord { Description = null! });
        legacy.Items.Add(new CashReconciliationItem { OperationId = null! });
        Check(Apply(legacy) == KnownDataRepairResult.Applied, "unrelated nullable legacy descriptions do not break startup repair");

        Action<Scenario>[] invalid =
        {
            s => s.Items[0].Id = Guid.NewGuid(),
            s => s.Items[0].InvestigationId = Guid.NewGuid(),
            s => s.Items[0].OperationId = "another-command",
            s => s.Items[0].ActualAmount++,
            s => s.Items[0].Amount--,
            s => s.Items[0].ResolvedAmount = 1,
            s => s.Items[0].FormalizedAmount = 1,
            s => s.Items[0].PostedFormalizedAmount = 1,
            s => s.Items[0].LossAllocations.Add(new CashLossAllocation { Amount = 1 }),
            s => s.Items[0].Settlements.Add(new CashSettlementEntry { Amount = 1 }),
            s => s.Items[0].ResponsibleEmployeeName = "someone",
            s => s.Items[0].AmountAtCheckpoint--,
            s => s.Contract.CreditorType = EmployeeDebtCreditorType.Employee,
            s => s.Contract.DebtorEmployeeId = "another-employee",
            s => s.Cash.Clear(),
            s => s.Cash.Add(JsonSerializer.Deserialize<CashRecord>(JsonSerializer.Serialize(s.Cash[0]))!),
            s => s.Cash[0].PaymentMethod = "Наличные",
            s => s.Cash[0].Amount++,
            s => s.Cash[0].Description = "unlinked",
            s => s.Losses.Add(new EmployeeLossItem { SourceCode = KnownCashlessLoanRepairPolicy.InvestigationId.ToString("N") }),
            s => s.Items[1].ActualAmount = 3000,
            s => s.Items.Add(new CashReconciliationItem { OperationId = KnownCashlessLoanRepairPolicy.OperationId + ":foreign" })
        };
        foreach (var mutate in invalid)
        {
            var candidate = Fixture();
            mutate(candidate);
            string before = JsonSerializer.Serialize(candidate);
            Check(Apply(candidate) == KnownDataRepairResult.NotMatched && JsonSerializer.Serialize(candidate) == before,
                "changed, posted, unlinked or conflicting evidence must not be modified");
        }
        var untouched = Fixture();
        Check(KnownCashlessLoanRepairPolicy.Apply("club_2", untouched.Items, untouched.Contract,
            untouched.Cash, untouched.Losses, Now) == KnownDataRepairResult.NotMatched, "other club never repaired");
        Check(KnownCashlessLoanRepairPolicy.Apply("club_1", untouched.Items, untouched.Contract,
            untouched.Cash, untouched.Losses, Now.AddMonths(1)) == KnownDataRepairResult.NotMatched,
            "do not rewrite a historical closed-month incident");
        Console.WriteLine($"Known cashless loan repair: {checks} checks passed.");
    }

    private static KnownDataRepairResult Apply(Scenario s) => KnownCashlessLoanRepairPolicy.Apply(
        "club_1", s.Items, s.Contract, s.Cash, s.Losses, Now);

    private static Scenario Fixture() => new()
    {
        Contract = new EmployeeDebtContract
        {
            Id = KnownCashlessLoanRepairPolicy.ContractId, CreditorType = EmployeeDebtCreditorType.Club,
            CreditorEmployeeId = "@club", FundingMethod = "Безнал",
            DebtorEmployeeId = "emp_3a6b71d8d38143288f43102200d8f2ca"
        },
        Cash = new()
        {
            new CashRecord
            {
                Id = KnownCashlessLoanRepairPolicy.LoanId, Type = CashRecordType.ClubLoanIssued,
                Amount = 3000, PaymentMethod = "Безнал", CreatedAt = new DateTime(2026, 11, 4, 0, 14, 36),
                Description = KnownCashlessLoanRepairPolicy.ContractId +
                    " [club-loan:" + KnownCashlessLoanRepairPolicy.EventId + "]"
            }
        },
        Items = new()
        {
            new CashReconciliationItem
            {
                Id = KnownCashlessLoanRepairPolicy.CardId, InvestigationId = KnownCashlessLoanRepairPolicy.InvestigationId,
                OperationId = "cashless_verify_1793780688649", CreatedAt = new DateTime(2026, 11, 4, 14, 24, 50),
                Kind = CashReconciliationKind.CashlessShortage, Origin = CashReconciliationOrigin.CashlessVerification,
                OriginalAmount = 2948, Amount = 2948, ExpectedAmount = 6017, ProgramExpectedAmount = 6017,
                ActualAmount = 3069, CheckpointNumber = 1000, AmountAtCheckpoint = 2948
            },
            new CashReconciliationItem
            {
                IsTechnicalEvent = true, Origin = CashReconciliationOrigin.CorrectionCheckpoint,
                Status = CashReconciliationStatus.Resolved, CreatedAt = new DateTime(2026, 11, 4, 14, 25, 2),
                CheckpointNumber = 1000, AmountAtCheckpoint = -2948, ExpectedAmount = 430, ActualAmount = 3069
            }
        }
    };

    private sealed class Scenario
    {
        public List<CashReconciliationItem> Items { get; set; } = new();
        public EmployeeDebtContract Contract { get; set; } = new();
        public List<CashRecord> Cash { get; set; } = new();
        public List<EmployeeLossItem> Losses { get; set; } = new();
    }
}
