using ClubTimerXbox.Models;
using ClubTimerXbox.Services;
using System.Text.Json;

internal sealed class ClubLoanTests
{
    private int _passed;
    public void Run()
    {
        var day = new DateTime(2026, 10, 12, 6, 0, 0);
        var contract = new EmployeeDebtContract
        {
            Id = "loan", CreatedAt = day, DebtorEmployeeId = "argen", DebtorNameSnapshot = "Арген",
            CreditorEmployeeId = ClubLoanCashPolicy.CreditorId, CreditorNameSnapshot = "Касса клуба",
            CreditorType = EmployeeDebtCreditorType.Club, FundingMethod = "Наличные", DeductionPercent = 30
        };
        var issue = Event(EmployeeDebtEventKind.Created, 1000, day.AddHours(6), "Наличные");
        var automatic = Event(EmployeeDebtEventKind.AutomaticRepayment, 300, day.AddHours(7), "");
        var cashReturn = Event(EmployeeDebtEventKind.HandToHandRepayment, 200, day.AddHours(8), "Наличные");
        var bankReturn = Event(EmployeeDebtEventKind.HandToHandRepayment, 100, day.AddHours(9), "Безнал");
        var state = new EmployeeDebtState { Contracts = new() { contract }, Events = new() { issue, automatic, cashReturn, bankReturn } };
        var movement = ClubLoanCashPolicy.BuildMovement(contract, issue)!;
        Check(movement.Type == CashRecordType.ClubLoanIssued && movement.Amount == 1000,
            "club issue creates exact funding movement");
        Check(!CashService.IsClubExpense(movement), "loan is not operating expense or pace baseline");
        Check(ClubLoanCashPolicy.BuildMovement(contract, automatic) == null,
            "salary offset cannot create cash");
        Check(EmployeeDebtLedgerCalculator.CreditAt(state, "loan", day.AddDays(1)) == 0,
            "club repayment cannot create employee creditor credit");
        var debtor = EmployeeDebtLedgerCalculator.BuildEmployeeSnapshot(state, "argen",
            BusinessCalendarService.GetBusinessMonth(day), (id, fallback) => fallback);
        Check(debtor.ClosingDebtAmount == 400 && debtor.RepaidDebtAmount == 600,
            "debtor debt includes salary offset and physical returns once");
        var owner = EmployeeDebtLedgerCalculator.BuildEmployeeSnapshot(state, "test",
            BusinessCalendarService.GetBusinessMonth(day), (id, fallback) => fallback);
        Check(owner.ClosingCreditAmount == 0 && owner.Contracts.Count == 0,
            "owner Test receives no club credit");
        var personal = JsonSerializer.Deserialize<EmployeeDebtContract>("{\"Id\":\"old\"}")!;
        Check(personal.CreditorType == EmployeeDebtCreditorType.Employee,
            "legacy contracts remain personal");
        Check(ClubLoanCashPolicy.BuildMovement(personal, issue) == null,
            "personal debt never moves club money");
        var rows = state.Events.Select(item => ClubLoanCashPolicy.BuildMovement(contract, item))
            .OfType<CashRecord>().ToList();
        Check(ClubLoanCashPolicy.NetOut(rows, "Наличные", day, day.AddDays(1)) == 800,
            "cash issue and cash return net correctly");
        Check(ClubLoanCashPolicy.NetOut(rows, "Безнал", day, day.AddDays(1)) == -100,
            "cash-funded loan can be returned by bank");
        var checkpoint = new CashBalanceCheckpointItem { CreatedAt = day.AddHours(5), CashAmount = 2000 };
        var balance = CashPhysicalBalancePolicy.Calculate(Array.Empty<CashAcceptanceItem>(), new[] { checkpoint },
            Array.Empty<PaymentRecord>(), rows, day, day.AddDays(1));
        Check(balance.Amount == 1200, "handover expectation includes loans but not salary offset");
        Check(ClubLoanCashPolicy.NetOut(rows, "Наличные", cashReturn.CreatedAt, day.AddDays(1)) == 0,
            "checkpoint excludes prior loan movements");
        Check(rows.Select(item => item.Id).Distinct().Count() == 3 &&
            movement.Id == ClubLoanCashPolicy.BuildMovement(contract, issue)!.Id,
            "stable movement IDs prevent repeat postings");

        // Intent saved but cash write failed: recovery posts once on restart.
        string intent = JsonSerializer.Serialize(state);
        var persistedCash = new Dictionary<Guid, CashRecord>();
        int writes = 0;
        void Ensure(CashRecord row) { if (persistedCash.TryAdd(row.Id, row)) writes++; }
        var recovered = JsonSerializer.Deserialize<EmployeeDebtState>(intent)!;
        ClubLoanCashPolicy.Recover(recovered, Ensure, () => { });
        ClubLoanCashPolicy.Recover(recovered, Ensure, () => { });
        Check(writes == 3, "repeated recovery posts no duplicate cash");
        var restarted = JsonSerializer.Deserialize<EmployeeDebtState>(intent)!;
        ClubLoanCashPolicy.Recover(restarted, Ensure, () => { });
        Check(writes == 3, "cash posted before debt acknowledgment survives restart");
        var delayed = JsonSerializer.Deserialize<EmployeeDebtState>(intent)!;
        bool complete = ClubLoanCashPolicy.TryRecoverCommitted(delayed,
            row => throw new IOException("simulated write failure"), () => { });
        Check(!complete && delayed.Events.All(item => !item.CashMovementPosted),
            "committed loan posting failure remains pending instead of rejecting the loan");
        complete = ClubLoanCashPolicy.TryRecoverCommitted(delayed, Ensure, () => { });
        Check(complete && writes == 3,
            "committed posting resumes without duplicate funding");
        var bad = Event(EmployeeDebtEventKind.HandToHandRepayment, 10, day, "");
        bool rejected = false;
        try { ClubLoanCashPolicy.BuildMovement(contract, bad); }
        catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "actual channel required for club cash receipt");
        Check(ClubLoanCashPolicy.BuildMovement(contract,
            Event(EmployeeDebtEventKind.Forgiven, 50, day, "")) == null,
            "forgiveness does not invent banknotes");
        Check(EmployeeDebtAutomaticCalculator.CalculateWithinBalances(1000, 30, 0, 1000, 1000).Amount == 300,
            "club uses same repayment percentage");
        int available = EmployeeDebtAutomaticCalculator.GetAvailableSalary(
            new AutoSalaryEmployeeResult { RemainingAmount = 220, CurrentPeriodRemainingAmount = 220 }, 0);
        Check(EmployeeDebtAutomaticCalculator.CalculateWithinBalances(1000, 30, 0, available, 1000).Amount == 20,
            "club uses same protected 200-som reserve");
        Console.WriteLine($"Club loans: {_passed} checks passed.");
    }

    private static EmployeeDebtEvent Event(EmployeeDebtEventKind kind, int amount, DateTime date, string method) => new()
    {
        Id = Guid.NewGuid().ToString("N"), ContractId = "loan", Kind = kind, Amount = amount,
        CreatedAt = date, UpdatedAt = date, PaymentMethod = method, ActorName = "Владелец"
    };

    private void Check(bool ok, string text)
    {
        if (!ok) throw new InvalidOperationException("Club loan: " + text);
        _passed++;
    }
}
