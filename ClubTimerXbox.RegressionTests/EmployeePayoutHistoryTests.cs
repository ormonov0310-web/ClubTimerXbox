using ClubTimerXbox.Models;
using ClubTimerXbox.Services;

internal sealed class EmployeePayoutHistoryTests
{
    public void Run()
    {
        var start = new DateTime(2026, 9, 1, 6, 0, 0);
        var end = start.AddMonths(1);
        var salary = Cash("Зарплата", "Арген", 63, start.AddDays(2));
        var returned = Cash("Возврат долга", "Арген", 77, start.AddDays(3));
        var wrongEmployee = Cash("Возврат долга", "Другой", 99, start.AddDays(3));
        var transfer = Cash("Другое", "Арген", 100, start.AddDays(3));
        var outside = Cash("Возврат долга", "Арген", 20, end);
        var records = new[] { salary, returned, wrongEmployee, transfer, outside, returned };
        var history = CashService.GetEmployeePayoutRecordsByPeriod(records, start, end, "арген");
        Check(history.Count == 2 && history.Sum(item => item.Amount) == 140,
            "salary and actual debt cash withdrawals counted once, recipient scoped");
        Check(history[0].Id == returned.Id, "newest withdrawal first");
        Check(CashService.GetEmployeePayoutRecordsByPeriod(new[] { salary }, start, end, "Арген")
            .Single().Amount == 63, "salary-only history unchanged");
        var carriedSalary = Cash("Зарплата", "Арген", 25, end.AddDays(1));
        carriedSalary.SalaryMonthKey = "2026-09";
        Check(CashService.GetEmployeePayoutRecordsByPeriod(new[] { carriedSalary, outside }, start, end, "Арген")
            .Single().Id == carriedSalary.Id, "salary source month retained, debt withdrawal uses physical date");
        returned.Description = "Аванс\n[debt-credit-withdrawal:operation:debt-credit]";
        var state = new EmployeeDebtState
        {
            Contracts = new()
            {
                new() { Id = "one", DebtorNameSnapshot = "Бектур" },
                new() { Id = "two", DebtorNameSnapshot = "Сталбек" }
            },
            Events = new()
            {
                new() { ContractId = "one", OperationId = "operation:debt-credit", Kind = EmployeeDebtEventKind.CreditWithdrawal, Amount = 50 },
                new() { ContractId = "two", OperationId = "operation:debt-credit", Kind = EmployeeDebtEventKind.CreditWithdrawal, Amount = 27 },
                new() { ContractId = "one", OperationId = "unrelated", Kind = EmployeeDebtEventKind.CreditWithdrawal, Amount = 500 },
                new() { ContractId = "one", OperationId = "operation:debt-credit", Kind = EmployeeDebtEventKind.SalaryRepayment, Amount = 500 }
            }
        };
        Check(EmployeeDebtService.BuildWithdrawalSourceDescription(state, returned)
            == "Из возвращённых долгов — Бектур: 50 сом; Сталбек: 27 сом",
            "old cash marker resolves exact debtors and portions, excluding incoming repayments");
        Check(EmployeeDebtService.BuildWithdrawalSourceDescription(state, salary) == "",
            "ordinary salary never labelled as debt return");
        Check(EmployeeDebtService.BuildWithdrawalSourceDescription(new EmployeeDebtState(), returned)
            == "Из возвращённых долгов", "legacy cash withdrawal visible when attribution unavailable");
        Check(returned.Amount == 77 && returned.Description.StartsWith("Аванс"),
            "history projection leaves financial records untouched");
        Console.WriteLine("PASS: 8 employee payout history scenarios.");
    }

    private static CashRecord Cash(string category, string recipient, int amount, DateTime at) => new()
    {
        Type = CashRecordType.Expense,
        Category = "Расходы",
        ExpenseCategory = category,
        RelatedEmployeeName = recipient,
        EmployeeName = recipient,
        Amount = amount,
        CreatedAt = at,
        PaymentMethod = "Наличные"
    };

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
