using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace ClubTimerXbox.Services
{
    public readonly record struct EmployeePayoutResult(
        int TotalAmount,
        int DebtCreditAmount,
        int SalaryAmount);

    public static class EmployeePayoutService
    {
        private static readonly string FolderPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ClubTimerXbox");
        private static readonly string FilePath = Path.Combine(
            FolderPath,
            "employee_payout_transactions.json");
        private static readonly object Gate = new();
        private static List<EmployeePayoutTransaction> Transactions = Load();

        public static EmployeePayoutResult Pay(
            string actorName,
            string employeeId,
            int amount,
            string paymentMethod,
            string throughMonthKey,
            string description,
            string operationId)
        {
            if (amount <= 0)
                throw new ArgumentOutOfRangeException(nameof(amount));
            var employee = EmployeeService.FindById(employeeId)
                           ?? throw new InvalidOperationException("Сотрудник не найден.");
            DateTime monthStart = BusinessCalendarService
                .GetBusinessMonthByKey(throughMonthKey)
                .StartInclusive;

            EmployeePayoutTransaction transaction;
            lock (Gate)
            {
                transaction = Transactions.FirstOrDefault(item =>
                    item.OperationId.Equals(operationId, StringComparison.OrdinalIgnoreCase))
                    ?? new EmployeePayoutTransaction();
                if (string.IsNullOrWhiteSpace(transaction.OperationId))
                {
                    var salary = AutoSalaryService
                        .BuildReport(monthStart)
                        .Employees
                        .FirstOrDefault(item => item.EmployeeId.Equals(
                            employee.EmployeeId,
                            StringComparison.OrdinalIgnoreCase));
                    int salaryAvailable = Math.Max(0, salary?.RemainingAmount ?? 0);
                    int creditAvailable = EmployeeDebtService.GetAvailableCredit(employee.EmployeeId);
                    if (amount > salaryAvailable + creditAvailable)
                    {
                        throw new InvalidOperationException(
                            $"Доступно всего {salaryAvailable + creditAvailable} сом: " +
                            $"зарплата {salaryAvailable}, возврат долга {creditAvailable}.");
                    }

                    int debtCreditAmount = Math.Min(creditAvailable, amount);
                    transaction = new EmployeePayoutTransaction
                    {
                        OperationId = operationId,
                        CreatedAt = ClubClock.Current.LocalNow,
                        ActorName = actorName.Trim(),
                        EmployeeId = employee.EmployeeId,
                        EmployeeName = employee.Name,
                        ThroughMonthKey = throughMonthKey,
                        PaymentMethod = CashService.NormalizePaymentMethod(paymentMethod),
                        Description = description.Trim(),
                        TotalAmount = amount,
                        DebtCreditAmount = debtCreditAmount,
                        SalaryAmount = amount - debtCreditAmount
                    };
                    Transactions.Add(transaction);
                    SaveLocked();
                }
                else if (transaction.TotalAmount != amount ||
                         !transaction.EmployeeId.Equals(employee.EmployeeId, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("Операция выплаты уже существует с другими данными.");
                }
            }

            if (!transaction.DebtCreditCompleted && transaction.DebtCreditAmount > 0)
            {
                EmployeeDebtService.WithdrawAvailableCredit(
                    transaction.EmployeeId,
                    transaction.DebtCreditAmount,
                    transaction.PaymentMethod,
                    transaction.ActorName,
                    $"{transaction.OperationId}:debt-credit",
                    transaction.Description);
                lock (Gate)
                {
                    transaction.DebtCreditCompleted = true;
                    SaveLocked();
                }
            }

            if (!transaction.SalaryCompleted && transaction.SalaryAmount > 0)
            {
                BusinessAccountingService.PaySalaryFifo(
                    transaction.ActorName,
                    transaction.EmployeeName,
                    transaction.SalaryAmount,
                    transaction.PaymentMethod,
                    transaction.ThroughMonthKey,
                    transaction.Description,
                    $"{transaction.OperationId}:salary");
                lock (Gate)
                {
                    transaction.SalaryCompleted = true;
                    SaveLocked();
                }
            }

            lock (Gate)
            {
                transaction.IsCompleted =
                    (transaction.DebtCreditAmount == 0 || transaction.DebtCreditCompleted) &&
                    (transaction.SalaryAmount == 0 || transaction.SalaryCompleted);
                SaveLocked();
            }

            return new EmployeePayoutResult(
                transaction.TotalAmount,
                transaction.DebtCreditAmount,
                transaction.SalaryAmount);
        }

        public static void Clear()
        {
            lock (Gate)
            {
                Transactions = new List<EmployeePayoutTransaction>();
                SaveLocked();
            }
        }

        private static List<EmployeePayoutTransaction> Load()
        {
            try
            {
                if (!File.Exists(FilePath))
                    return new List<EmployeePayoutTransaction>();
                return JsonSerializer.Deserialize<List<EmployeePayoutTransaction>>(
                    File.ReadAllText(FilePath)) ?? new List<EmployeePayoutTransaction>();
            }
            catch
            {
                return new List<EmployeePayoutTransaction>();
            }
        }

        private static void SaveLocked()
        {
            Directory.CreateDirectory(FolderPath);
            AtomicFileStorageService.WriteAllText(
                FilePath,
                JsonSerializer.Serialize(
                    Transactions,
                    new JsonSerializerOptions { WriteIndented = true }));
        }

        private sealed class EmployeePayoutTransaction
        {
            public string OperationId { get; set; } = "";
            public DateTime CreatedAt { get; set; }
            public string ActorName { get; set; } = "";
            public string EmployeeId { get; set; } = "";
            public string EmployeeName { get; set; } = "";
            public string ThroughMonthKey { get; set; } = "";
            public string PaymentMethod { get; set; } = "";
            public string Description { get; set; } = "";
            public int TotalAmount { get; set; }
            public int DebtCreditAmount { get; set; }
            public int SalaryAmount { get; set; }
            public bool DebtCreditCompleted { get; set; }
            public bool SalaryCompleted { get; set; }
            public bool IsCompleted { get; set; }
        }
    }
}
