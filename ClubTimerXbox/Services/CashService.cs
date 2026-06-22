using System;
using System.Collections.Generic;
using System.Linq;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public static class CashService
    {
        private static readonly List<CashRecord> _records = CashStorageService.Load();
        private static readonly HashSet<string> NonClubExpenseCategories = new HashSet<string>(
            new[] { "Зарплата", "Закупка", "Владелец" },
            StringComparer.OrdinalIgnoreCase
        );

        public static IReadOnlyList<CashRecord> Records => _records;

        public static void AddRecord(CashRecord record)
        {
            _records.Add(record);
            Save();
        }

        private static void Save()
        {
            CashStorageService.Save(_records);
        }

        public static void AddGameSessionIncome(
            string employeeName,
            string incomeEmployeeName,
            string placeName,
            string title,
            string description,
            int amount,
            Guid? gameSessionId = null)
        {
            if (amount <= 0)
                return;

            AddRecord(new CashRecord
            {
                CreatedAt = DateTime.Now,
                EmployeeName = employeeName,
                IncomeEmployeeName = incomeEmployeeName,
                RelatedEmployeeName = "",
                Type = CashRecordType.GameSession,
                Title = title,
                Description = description,
                Amount = amount,
                Category = "Игры",
                ExpenseCategory = "",
                PaymentMethod = "Не указано",
                PlaceName = placeName,
                GameSessionId = gameSessionId,
                IsAttachedToGameSession = gameSessionId != null
            });
        }

        public static void AddProductOrServiceIncome(
            string employeeName,
            string title,
            string description,
            int amount,
            string placeName = "",
            Guid? gameSessionId = null)
        {
            if (amount <= 0)
                return;

            AddRecord(new CashRecord
            {
                CreatedAt = DateTime.Now,
                EmployeeName = employeeName,
                IncomeEmployeeName = employeeName,
                RelatedEmployeeName = "",
                Type = CashRecordType.ProductOrService,
                Title = title,
                Description = description,
                Amount = amount,
                Category = "Товары и услуги",
                ExpenseCategory = "",
                PaymentMethod = "Не указано",
                PlaceName = placeName,
                GameSessionId = gameSessionId,
                IsAttachedToGameSession = gameSessionId != null
            });
        }

        public static void AddShortage(
            string checkedByEmployeeName,
            string responsibleEmployeeName,
            string title,
            string description,
            int amount)
        {
            if (amount <= 0)
                return;

            AddRecord(new CashRecord
            {
                CreatedAt = DateTime.Now,
                EmployeeName = checkedByEmployeeName,
                IncomeEmployeeName = responsibleEmployeeName,
                RelatedEmployeeName = responsibleEmployeeName,
                Type = CashRecordType.Shortage,
                Title = title,
                Description = description,
                Amount = amount,
                Category = "Недостачи",
                ExpenseCategory = "",
                PaymentMethod = "Не указано",
                PlaceName = ""
            });
        }

        public static int ReduceCashShortagesByPaymentMistake(
            int amount,
            DateTime fromInclusive,
            DateTime toExclusive,
            string titleKeyword)
        {
            if (amount <= 0)
                return 0;

            titleKeyword = titleKeyword.Trim();
            int remaining = amount;
            int reduced = 0;

            var records = _records
                .Where(record =>
                    record.CreatedAt >= fromInclusive &&
                    record.CreatedAt < toExclusive &&
                    record.Category == "Недостачи" &&
                    record.Amount > 0 &&
                    (string.IsNullOrWhiteSpace(titleKeyword) ||
                        record.Title.Contains(titleKeyword, StringComparison.OrdinalIgnoreCase) ||
                        record.Description.Contains(titleKeyword, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(record => record.CreatedAt)
                .ToList();

            foreach (var record in records)
            {
                if (remaining <= 0)
                    break;

                int useAmount = Math.Min(record.Amount, remaining);

                record.Amount -= useAmount;
                reduced += useAmount;
                remaining -= useAmount;

                string note = $"Зачтено излишком безнала как ошибка типа оплаты: {useAmount} сом.";
                record.Description = string.IsNullOrWhiteSpace(record.Description)
                    ? note
                    : $"{record.Description}\n{note}";
            }

            if (reduced > 0)
                Save();

            return reduced;
        }

        public static void AddExpense(
            string employeeName,
            string title,
            string description,
            int amount,
            string paymentMethod = "Не указано",
            string expenseCategory = "Другое",
            string relatedEmployeeName = "")
        {
            if (amount <= 0)
                return;

            paymentMethod = NormalizePaymentMethod(paymentMethod);
            expenseCategory = NormalizeExpenseCategory(expenseCategory);

            AddRecord(new CashRecord
            {
                CreatedAt = DateTime.Now,
                EmployeeName = employeeName,
                IncomeEmployeeName = employeeName,
                RelatedEmployeeName = relatedEmployeeName.Trim(),
                Type = CashRecordType.Expense,
                Title = title,
                Description = description,
                Amount = amount,
                Category = "Расходы",
                ExpenseCategory = expenseCategory,
                PaymentMethod = paymentMethod,
                PlaceName = ""
            });
        }

        public static void AddSalaryPayment(
            string ownerName,
            string employeeName,
            int amount,
            string paymentMethod,
            string description = "")
        {
            employeeName = employeeName.Trim();

            if (amount <= 0)
                return;

            if (string.IsNullOrWhiteSpace(employeeName))
                return;

            AddExpense(
                employeeName: ownerName,
                title: $"Зарплата: {employeeName}",
                description: description,
                amount: amount,
                paymentMethod: paymentMethod,
                expenseCategory: "Зарплата",
                relatedEmployeeName: employeeName
            );
        }

        public static void AddCorrection(
            string employeeName,
            string title,
            string description,
            int amount)
        {
            if (amount == 0)
                return;

            AddRecord(new CashRecord
            {
                CreatedAt = DateTime.Now,
                EmployeeName = employeeName,
                IncomeEmployeeName = employeeName,
                RelatedEmployeeName = "",
                Type = CashRecordType.Correction,
                Title = title,
                Description = description,
                Amount = amount,
                Category = "Коррекция",
                ExpenseCategory = "",
                PaymentMethod = "Не указано",
                PlaceName = ""
            });
        }

        public static int GetTotalForToday()
        {
            return GetCashIncomeTotalByPeriod(DateTime.Today, DateTime.Today.AddDays(1));
        }

        public static int GetGameTotalForToday()
        {
            return GetTotalByPeriodAndCategory(
                DateTime.Today,
                DateTime.Today.AddDays(1),
                "Игры"
            );
        }

        public static int GetProductsAndServicesTotalForToday()
        {
            return GetTotalByPeriodAndCategory(
                DateTime.Today,
                DateTime.Today.AddDays(1),
                "Товары и услуги"
            );
        }

        public static int GetShortageTotalForToday()
        {
            return GetTotalByPeriodAndCategory(
                DateTime.Today,
                DateTime.Today.AddDays(1),
                "Недостачи"
            );
        }

        public static int GetCorrectionTotalForToday()
        {
            return GetTotalByPeriodAndCategory(
                DateTime.Today,
                DateTime.Today.AddDays(1),
                "Коррекция"
            );
        }

        public static int GetExpenseTotalForToday()
        {
            return GetTotalByPeriodAndCategory(
                DateTime.Today,
                DateTime.Today.AddDays(1),
                "Расходы"
            );
        }

        public static int GetTotalByPeriod(DateTime fromInclusive, DateTime toExclusive)
        {
            return _records
                .Where(record =>
                    record.CreatedAt >= fromInclusive &&
                    record.CreatedAt < toExclusive)
                .Sum(record => record.Amount);
        }

        public static int GetCashIncomeTotalByPeriod(DateTime fromInclusive, DateTime toExclusive)
        {
            return _records
                .Where(record =>
                    record.CreatedAt >= fromInclusive &&
                    record.CreatedAt < toExclusive &&
                    record.Category != "Недостачи" &&
                    record.Category != "Расходы")
                .Sum(record => record.Amount);
        }

        public static int GetExpenseTotalByPeriod(DateTime fromInclusive, DateTime toExclusive)
        {
            return GetTotalByPeriodAndCategory(fromInclusive, toExclusive, "Расходы");
        }

        public static int GetClubExpenseTotalByPeriod(DateTime fromInclusive, DateTime toExclusive)
        {
            return _records
                .Where(record =>
                    record.CreatedAt >= fromInclusive &&
                    record.CreatedAt < toExclusive &&
                    IsClubExpense(record))
                .Sum(record => record.Amount);
        }

        public static int GetSalaryTotalByPeriod(DateTime fromInclusive, DateTime toExclusive)
        {
            return GetExpenseTotalByPeriodAndExpenseCategory(
                fromInclusive,
                toExclusive,
                "Зарплата"
            );
        }

        public static int GetSalaryTotalByPeriodForEmployee(
            DateTime fromInclusive,
            DateTime toExclusive,
            string employeeName)
        {
            employeeName = employeeName.Trim();

            return _records
                .Where(record =>
                    record.CreatedAt >= fromInclusive &&
                    record.CreatedAt < toExclusive &&
                    record.Category == "Расходы" &&
                    record.ExpenseCategory == "Зарплата" &&
                    record.RelatedEmployeeName.Equals(employeeName, StringComparison.OrdinalIgnoreCase))
                .Sum(record => record.Amount);
        }

        public static int GetCashExpenseTotalByPeriod(DateTime fromInclusive, DateTime toExclusive)
        {
            return _records
                .Where(record =>
                    record.CreatedAt >= fromInclusive &&
                    record.CreatedAt < toExclusive &&
                    record.Category == "Расходы" &&
                    record.PaymentMethod == "Наличные")
                .Sum(record => record.Amount);
        }

        public static int GetClubCashExpenseTotalByPeriod(DateTime fromInclusive, DateTime toExclusive)
        {
            return _records
                .Where(record =>
                    record.CreatedAt >= fromInclusive &&
                    record.CreatedAt < toExclusive &&
                    record.PaymentMethod == "Наличные" &&
                    IsClubExpense(record))
                .Sum(record => record.Amount);
        }

        public static int GetCashlessExpenseTotalByPeriod(DateTime fromInclusive, DateTime toExclusive)
        {
            return _records
                .Where(record =>
                    record.CreatedAt >= fromInclusive &&
                    record.CreatedAt < toExclusive &&
                    record.Category == "Расходы" &&
                    record.PaymentMethod == "Безнал")
                .Sum(record => record.Amount);
        }

        public static int GetClubCashlessExpenseTotalByPeriod(DateTime fromInclusive, DateTime toExclusive)
        {
            return _records
                .Where(record =>
                    record.CreatedAt >= fromInclusive &&
                    record.CreatedAt < toExclusive &&
                    record.PaymentMethod == "Безнал" &&
                    IsClubExpense(record))
                .Sum(record => record.Amount);
        }

        public static int GetExpenseTotalByPeriodAndExpenseCategory(
            DateTime fromInclusive,
            DateTime toExclusive,
            string expenseCategory)
        {
            expenseCategory = NormalizeExpenseCategory(expenseCategory);

            return _records
                .Where(record =>
                    record.CreatedAt >= fromInclusive &&
                    record.CreatedAt < toExclusive &&
                    record.Category == "Расходы" &&
                    record.ExpenseCategory == expenseCategory)
                .Sum(record => record.Amount);
        }

        public static List<CashRecord> GetExpenseRecordsByExpenseCategory(
            DateTime fromInclusive,
            DateTime toExclusive,
            string expenseCategory)
        {
            expenseCategory = NormalizeExpenseCategory(expenseCategory);

            return _records
                .Where(record =>
                    record.CreatedAt >= fromInclusive &&
                    record.CreatedAt < toExclusive &&
                    record.Category == "Расходы" &&
                    record.ExpenseCategory == expenseCategory)
                .OrderByDescending(record => record.CreatedAt)
                .ToList();
        }

        public static List<CashRecord> GetSalaryRecordsByPeriod(
            DateTime fromInclusive,
            DateTime toExclusive)
        {
            return GetExpenseRecordsByExpenseCategory(
                fromInclusive,
                toExclusive,
                "Зарплата"
            );
        }

        public static int GetShortageTotalByPeriod(DateTime fromInclusive, DateTime toExclusive)
        {
            return GetTotalByPeriodAndCategory(fromInclusive, toExclusive, "Недостачи");
        }

        public static int GetTotalByPeriodAndCategory(
            DateTime fromInclusive,
            DateTime toExclusive,
            string category)
        {
            return _records
                .Where(record =>
                    record.CreatedAt >= fromInclusive &&
                    record.CreatedAt < toExclusive &&
                    record.Category == category)
                .Sum(record => record.Amount);
        }

        public static List<CashRecord> GetRecordsByPeriod(
            DateTime fromInclusive,
            DateTime toExclusive)
        {
            return _records
                .Where(record =>
                    record.CreatedAt >= fromInclusive &&
                    record.CreatedAt < toExclusive)
                .OrderByDescending(record => record.CreatedAt)
                .ToList();
        }

        public static List<CashRecord> GetRecordsByPeriodAndCategory(
            DateTime fromInclusive,
            DateTime toExclusive,
            string category)
        {
            return _records
                .Where(record =>
                    record.CreatedAt >= fromInclusive &&
                    record.CreatedAt < toExclusive &&
                    record.Category == category)
                .OrderByDescending(record => record.CreatedAt)
                .ToList();
        }

        public static List<CashRecord> GetExpenseRecordsByPaymentMethod(
            DateTime fromInclusive,
            DateTime toExclusive,
            string paymentMethod)
        {
            paymentMethod = NormalizePaymentMethod(paymentMethod);

            return _records
                .Where(record =>
                    record.CreatedAt >= fromInclusive &&
                    record.CreatedAt < toExclusive &&
                    record.Category == "Расходы" &&
                    record.PaymentMethod == paymentMethod)
                .OrderByDescending(record => record.CreatedAt)
                .ToList();
        }

        public static List<string> GetDefaultExpenseCategories()
        {
            return new List<string>
            {
                "Аренда",
                "Ток",
                "Интернет",
                "Уборка",
                "Ремонт",
                "Реклама",
                "Патент",
                "Мусор",
                "Подписка",
                "Закупка",
                "Зарплата",
                "Другое"
            };
        }

        private static bool IsClubExpense(CashRecord record)
        {
            return record.Category == "Расходы" &&
                   !NonClubExpenseCategories.Contains(record.ExpenseCategory ?? "");
        }

        public static string NormalizePaymentMethod(string paymentMethod)
        {
            if (paymentMethod == "Наличные")
                return "Наличные";

            if (paymentMethod == "Безнал")
                return "Безнал";

            return "Не указано";
        }

        public static string NormalizeExpenseCategory(string expenseCategory)
        {
            if (string.IsNullOrWhiteSpace(expenseCategory))
                return "Другое";

            expenseCategory = expenseCategory.Trim();

            var categories = GetDefaultExpenseCategories();

            var match = categories.FirstOrDefault(category =>
                category.Equals(expenseCategory, StringComparison.OrdinalIgnoreCase)
            );

            if (match != null)
                return match;

            return expenseCategory;
        }

        public static bool DeleteRecord(Guid id, string category = "")
        {
            var record = _records.FirstOrDefault(item => item.Id == id);

            if (record == null)
                return false;

            if (!string.IsNullOrWhiteSpace(category) &&
                !record.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            _records.Remove(record);
            Save();

            return true;
        }

        public static int RenameExpenseCategoryByPeriod(
            DateTime fromInclusive,
            DateTime toExclusive,
            string oldExpenseCategory,
            string newExpenseCategory)
        {
            oldExpenseCategory = NormalizeExpenseCategory(oldExpenseCategory);
            newExpenseCategory = NormalizeExpenseCategory(newExpenseCategory);

            int changed = 0;

            foreach (var record in _records.Where(record =>
                         record.CreatedAt >= fromInclusive &&
                         record.CreatedAt < toExclusive &&
                         record.Category == "Расходы" &&
                         record.ExpenseCategory == oldExpenseCategory))
            {
                record.ExpenseCategory = newExpenseCategory;
                changed++;
            }

            if (changed > 0)
                Save();

            return changed;
        }

        public static int DeleteExpenseCategoryByPeriod(
            DateTime fromInclusive,
            DateTime toExclusive,
            string expenseCategory)
        {
            expenseCategory = NormalizeExpenseCategory(expenseCategory);

            var records = _records
                .Where(record =>
                    record.CreatedAt >= fromInclusive &&
                    record.CreatedAt < toExclusive &&
                    record.Category == "Расходы" &&
                    record.ExpenseCategory == expenseCategory)
                .ToList();

            foreach (var record in records)
                _records.Remove(record);

            if (records.Count > 0)
                Save();

            return records.Count;
        }

        public static int UpdateExpenseCategoryTotalByPeriod(
            DateTime fromInclusive,
            DateTime toExclusive,
            string expenseCategory,
            int newTotalAmount)
        {
            if (newTotalAmount < 0)
                newTotalAmount = 0;

            expenseCategory = NormalizeExpenseCategory(expenseCategory);

            var records = _records
                .Where(record =>
                    record.CreatedAt >= fromInclusive &&
                    record.CreatedAt < toExclusive &&
                    record.Category == "Расходы" &&
                    record.ExpenseCategory == expenseCategory)
                .OrderByDescending(record => record.CreatedAt)
                .ToList();

            if (records.Count == 0)
                return 0;

            int currentTotal = records.Sum(record => record.Amount);
            int delta = newTotalAmount - currentTotal;
            var latest = records[0];
            latest.Amount += delta;

            if (latest.Amount < 0)
                throw new InvalidOperationException("Новая сумма слишком маленькая для корректировки последней записи.");

            Save();

            return latest.Amount;
        }

        public static void Clear()
        {
            _records.Clear();
            CashStorageService.Clear();
        }
    }
}
