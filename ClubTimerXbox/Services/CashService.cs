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

        public static int RenameEmployeeReferences(
            string oldEmployeeName,
            string newEmployeeName)
        {
            int changed = 0;

            foreach (var record in _records)
            {
                bool recordChanged = false;

                if (EmployeeReferenceRenameService.Matches(record.EmployeeName, oldEmployeeName))
                {
                    record.EmployeeName = newEmployeeName;
                    recordChanged = true;
                }

                if (EmployeeReferenceRenameService.Matches(record.IncomeEmployeeName, oldEmployeeName))
                {
                    record.IncomeEmployeeName = newEmployeeName;
                    recordChanged = true;
                }

                if (EmployeeReferenceRenameService.Matches(record.RelatedEmployeeName, oldEmployeeName))
                {
                    record.RelatedEmployeeName = newEmployeeName;
                    recordChanged = true;
                }

                if (!recordChanged)
                    continue;

                record.Title = EmployeeReferenceRenameService.RenameText(
                    record.Title,
                    oldEmployeeName,
                    newEmployeeName);
                record.Description = EmployeeReferenceRenameService.RenameText(
                    record.Description,
                    oldEmployeeName,
                    newEmployeeName);
                changed++;
            }

            if (changed > 0)
                Save();

            return changed;
        }

        public static void AddRecord(CashRecord record)
        {
            if (record.CreatedAt == default)
                record.CreatedAt = ClubClock.Current.LocalNow;
            if (record.BusinessOccurredAt == default)
                record.BusinessOccurredAt = record.CreatedAt;
            if (string.IsNullOrWhiteSpace(record.BusinessDateKey))
            {
                record.BusinessDateKey = BusinessCalendarService
                    .GetBusinessDay(record.BusinessOccurredAt)
                    .Key;
            }
            if (string.IsNullOrWhiteSpace(record.BusinessMonthKey))
            {
                record.BusinessMonthKey = BusinessCalendarService
                    .GetBusinessMonth(record.BusinessOccurredAt)
                    .Key;
            }

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
            Guid? gameSessionId = null,
            DateTime? businessOccurredAt = null,
            Guid? paymentRecordId = null)
        {
            if (amount <= 0)
                return;

            AddRecord(new CashRecord
            {
                CreatedAt = ClubClock.Current.LocalNow,
                EmployeeName = employeeName,
                IncomeEmployeeName = incomeEmployeeName,
                BusinessOccurredAt = businessOccurredAt ?? ClubClock.Current.LocalNow,
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
                PaymentRecordId = paymentRecordId,
                IsAttachedToGameSession = gameSessionId != null
            });
        }

        public static void AddProductOrServiceIncome(
            string employeeName,
            string title,
            string description,
            int amount,
            string placeName = "",
            Guid? gameSessionId = null,
            Guid? paymentRecordId = null)
        {
            if (amount <= 0)
                return;

            AddRecord(new CashRecord
            {
                CreatedAt = ClubClock.Current.LocalNow,
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
                PaymentRecordId = paymentRecordId,
                IsAttachedToGameSession = gameSessionId != null
            });
        }

        public static void AddShortage(
            string checkedByEmployeeName,
            string responsibleEmployeeName,
            string title,
            string description,
            int amount,
            DateTime? businessOccurredAt = null)
        {
            if (amount <= 0)
                return;

            AddRecord(new CashRecord
            {
                CreatedAt = ClubClock.Current.LocalNow,
                BusinessOccurredAt = businessOccurredAt ?? ClubClock.Current.LocalNow,
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
                    IsInBusinessPeriod(record, fromInclusive, toExclusive) &&
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
            string relatedEmployeeName = "",
            string accountingMonthKey = "")
        {
            if (amount <= 0)
                return;

            paymentMethod = NormalizePaymentMethod(paymentMethod);
            expenseCategory = NormalizeExpenseCategory(expenseCategory);
            accountingMonthKey = NormalizeMonthKey(accountingMonthKey);

            AddRecord(new CashRecord
            {
                CreatedAt = ClubClock.Current.LocalNow,
                EmployeeName = employeeName,
                IncomeEmployeeName = employeeName,
                RelatedEmployeeName = relatedEmployeeName.Trim(),
                AccountingMonthKey = accountingMonthKey,
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
            string description = "",
            string salaryMonthKey = "")
        {
            employeeName = employeeName.Trim();
            salaryMonthKey = NormalizeSalaryMonthKey(salaryMonthKey);

            if (amount <= 0)
                return;

            if (string.IsNullOrWhiteSpace(employeeName))
                return;

            paymentMethod = NormalizePaymentMethod(paymentMethod);

            AddRecord(new CashRecord
            {
                CreatedAt = ClubClock.Current.LocalNow,
                EmployeeName = ownerName,
                IncomeEmployeeName = ownerName,
                RelatedEmployeeName = employeeName,
                SalaryMonthKey = salaryMonthKey,
                Type = CashRecordType.Expense,
                Title = $"Зарплата: {employeeName}",
                Description = description,
                Amount = amount,
                Category = "Расходы",
                ExpenseCategory = "Зарплата",
                PaymentMethod = paymentMethod,
                PlaceName = ""
            });
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
                CreatedAt = ClubClock.Current.LocalNow,
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
            var day = CurrentBusinessDay();
            return GetCashIncomeTotalByPeriod(day.StartInclusive, day.EndExclusive);
        }

        public static int GetGameTotalForToday()
        {
            return GetTotalByPeriodAndCategory(
                CurrentBusinessDay().StartInclusive,
                CurrentBusinessDay().EndExclusive,
                "Игры"
            );
        }

        public static int GetProductsAndServicesTotalForToday()
        {
            return GetTotalByPeriodAndCategory(
                CurrentBusinessDay().StartInclusive,
                CurrentBusinessDay().EndExclusive,
                "Товары и услуги"
            );
        }

        public static int GetShortageTotalForToday()
        {
            return GetTotalByPeriodAndCategory(
                CurrentBusinessDay().StartInclusive,
                CurrentBusinessDay().EndExclusive,
                "Недостачи"
            );
        }

        public static int GetCorrectionTotalForToday()
        {
            return GetTotalByPeriodAndCategory(
                CurrentBusinessDay().StartInclusive,
                CurrentBusinessDay().EndExclusive,
                "Коррекция"
            );
        }

        public static int GetExpenseTotalForToday()
        {
            return GetTotalByPeriodAndCategory(
                CurrentBusinessDay().StartInclusive,
                CurrentBusinessDay().EndExclusive,
                "Расходы"
            );
        }

        public static int GetTotalByPeriod(DateTime fromInclusive, DateTime toExclusive)
        {
            return _records
                .Where(record => IsInBusinessPeriod(record, fromInclusive, toExclusive))
                .Sum(record => record.Amount);
        }

        public static int GetCashIncomeTotalByPeriod(DateTime fromInclusive, DateTime toExclusive)
        {
            return _records
                .Where(record =>
                    IsInBusinessPeriod(record, fromInclusive, toExclusive) &&
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
                    IsInBusinessPeriod(record, fromInclusive, toExclusive) &&
                    IsClubExpense(record))
                .Sum(record => record.Amount);
        }

        public static bool HasClubExpenseRecordsByPeriod(
            DateTime fromInclusive,
            DateTime toExclusive)
        {
            return _records.Any(record =>
                IsInBusinessPeriod(record, fromInclusive, toExclusive) &&
                IsClubExpense(record));
        }

        public static int GetSalaryTotalByPeriod(DateTime fromInclusive, DateTime toExclusive)
        {
            return GetSalaryRecordsByPeriod(fromInclusive, toExclusive)
                .Sum(record => record.Amount);
        }

        public static int GetSalaryTotalByPeriodForEmployee(
            DateTime fromInclusive,
            DateTime toExclusive,
            string employeeName)
        {
            employeeName = employeeName.Trim();

            return GetSalaryRecordsByPeriod(fromInclusive, toExclusive)
                .Where(record => record.RelatedEmployeeName.Equals(
                    employeeName,
                    StringComparison.OrdinalIgnoreCase))
                .Sum(record => record.Amount);
        }

        public static int GetCashExpenseTotalByPeriod(DateTime fromInclusive, DateTime toExclusive)
        {
            return _records
                .Where(record =>
                    IsInBusinessPeriod(record, fromInclusive, toExclusive) &&
                    record.Category == "Расходы" &&
                    record.PaymentMethod == "Наличные")
                .Sum(record => record.Amount);
        }

        public static int GetClubCashExpenseTotalByPeriod(DateTime fromInclusive, DateTime toExclusive)
        {
            return _records
                .Where(record =>
                    IsInBusinessPeriod(record, fromInclusive, toExclusive) &&
                    record.PaymentMethod == "Наличные" &&
                    IsClubExpense(record))
                .Sum(record => record.Amount);
        }

        public static int GetCashlessExpenseTotalByPeriod(DateTime fromInclusive, DateTime toExclusive)
        {
            return _records
                .Where(record =>
                    IsInBusinessPeriod(record, fromInclusive, toExclusive) &&
                    record.Category == "Расходы" &&
                    record.PaymentMethod == "Безнал")
                .Sum(record => record.Amount);
        }

        public static int GetClubCashlessExpenseTotalByPeriod(DateTime fromInclusive, DateTime toExclusive)
        {
            return _records
                .Where(record =>
                    IsInBusinessPeriod(record, fromInclusive, toExclusive) &&
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
                    IsInBusinessPeriod(record, fromInclusive, toExclusive) &&
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
                    IsInBusinessPeriod(record, fromInclusive, toExclusive) &&
                    record.Category == "Расходы" &&
                    record.ExpenseCategory == expenseCategory)
                .OrderByDescending(record => record.CreatedAt)
                .ToList();
        }

        public static List<CashRecord> GetSalaryRecordsByPeriod(
            DateTime fromInclusive,
            DateTime toExclusive)
        {
            return _records
                .Where(record => IsSalaryRecord(record) &&
                                 IsSalaryRecordInPeriod(record, fromInclusive, toExclusive))
                .OrderByDescending(record => record.CreatedAt)
                .ToList();
        }

        public static List<CashRecord> GetOwnerWithdrawRecordsByPeriod(
            DateTime fromInclusive,
            DateTime toExclusive)
        {
            return _records
                .Where(record =>
                    record.Category == "Расходы" &&
                    record.ExpenseCategory == "Владелец" &&
                    IsOwnerWithdrawalInPeriod(record, fromInclusive, toExclusive))
                .OrderByDescending(GetOwnerWithdrawalDisplayTime)
                .ToList();
        }

        public static DateTime GetOwnerWithdrawalDisplayTime(CashRecord record)
        {
            if (record.Category != "Расходы" ||
                record.ExpenseCategory != "Владелец" ||
                string.IsNullOrWhiteSpace(record.AccountingMonthKey) ||
                !TryParseMonthKey(record.AccountingMonthKey, out DateTime accountingMonth))
            {
                return record.CreatedAt;
            }

            string physicalMonthKey = BusinessCalendarService
                .GetBusinessMonth(record.CreatedAt)
                .Key;
            if (record.AccountingMonthKey.Equals(
                    physicalMonthKey,
                    StringComparison.OrdinalIgnoreCase))
            {
                return record.CreatedAt;
            }

            return accountingMonth.AddMonths(1).AddSeconds(-1);
        }

        public static bool IsOwnerWithdrawalInPeriod(
            CashRecord record,
            DateTime fromInclusive,
            DateTime toExclusive)
        {
            return IsAccountingRecordInPeriod(record, fromInclusive, toExclusive);
        }

        public static bool IsPriorMonthExpense(CashRecord record, DateTime monthStart)
        {
            if (record.Category != "Расходы")
                return false;

            string monthKey = record.ExpenseCategory switch
            {
                "Владелец" => record.AccountingMonthKey,
                "Зарплата" => record.SalaryMonthKey,
                _ => ""
            };

            if (string.IsNullOrWhiteSpace(monthKey) ||
                !TryParseMonthKey(monthKey, out DateTime accountingMonth))
            {
                return false;
            }

            var accountingMonthStart = new DateTime(accountingMonth.Year, accountingMonth.Month, 1);
            return accountingMonthStart < monthStart.Date;
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
                    IsInBusinessPeriod(record, fromInclusive, toExclusive) &&
                    record.Category == category)
                .Sum(record => record.Amount);
        }

        public static List<CashRecord> GetRecordsByPeriod(
            DateTime fromInclusive,
            DateTime toExclusive)
        {
            return _records
                .Where(record => IsInBusinessPeriod(record, fromInclusive, toExclusive))
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
                    IsRecordInReportPeriod(record, fromInclusive, toExclusive) &&
                    record.Category == category)
                .OrderByDescending(record =>
                    record.ExpenseCategory == "Владелец"
                        ? GetOwnerWithdrawalDisplayTime(record)
                        : record.CreatedAt)
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
                    IsInBusinessPeriod(record, fromInclusive, toExclusive) &&
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

        private static BusinessPeriodRange CurrentBusinessDay()
        {
            return BusinessCalendarService.GetBusinessDay(ClubClock.Current.LocalNow);
        }

        public static DateTime GetBusinessTime(CashRecord record)
        {
            return record.BusinessOccurredAt == default
                ? record.CreatedAt
                : record.BusinessOccurredAt;
        }

        private static bool IsInBusinessPeriod(
            CashRecord record,
            DateTime fromInclusive,
            DateTime toExclusive)
        {
            DateTime occurredAt = GetBusinessTime(record);
            return occurredAt >= fromInclusive && occurredAt < toExclusive;
        }

        private static bool IsRecordInReportPeriod(
            CashRecord record,
            DateTime fromInclusive,
            DateTime toExclusive)
        {
            return record.Category == "Расходы" && record.ExpenseCategory == "Владелец"
                ? IsOwnerWithdrawalInPeriod(record, fromInclusive, toExclusive)
                : IsInBusinessPeriod(record, fromInclusive, toExclusive);
        }

        private static bool IsSalaryRecord(CashRecord record)
        {
            return record.Category == "Расходы" &&
                   record.ExpenseCategory == "Зарплата";
        }

        private static bool IsSalaryRecordInPeriod(
            CashRecord record,
            DateTime fromInclusive,
            DateTime toExclusive)
        {
            if (IsWholeMonthPeriod(fromInclusive, toExclusive) &&
                !string.IsNullOrWhiteSpace(record.SalaryMonthKey) &&
                TryParseSalaryMonthKey(record.SalaryMonthKey, out _))
            {
                return record.SalaryMonthKey.Equals(
                    fromInclusive.ToString("yyyy-MM"),
                    StringComparison.OrdinalIgnoreCase);
            }

            return IsInBusinessPeriod(record, fromInclusive, toExclusive);
        }

        private static bool IsAccountingRecordInPeriod(
            CashRecord record,
            DateTime fromInclusive,
            DateTime toExclusive)
        {
            if (!string.IsNullOrWhiteSpace(record.AccountingMonthKey) &&
                TryParseMonthKey(record.AccountingMonthKey, out _))
            {
                DateTime accountingTime = GetOwnerWithdrawalDisplayTime(record);
                return accountingTime >= fromInclusive && accountingTime < toExclusive;
            }

            return IsInBusinessPeriod(record, fromInclusive, toExclusive);
        }

        private static bool IsWholeMonthPeriod(DateTime fromInclusive, DateTime toExclusive)
        {
            return fromInclusive.Day == 1 &&
                   (fromInclusive.TimeOfDay == TimeSpan.Zero ||
                    fromInclusive.TimeOfDay == TimeSpan.FromHours(
                        BusinessCalendarService.BusinessDayStartHour)) &&
                   toExclusive == fromInclusive.AddMonths(1);
        }

        private static string NormalizeSalaryMonthKey(string salaryMonthKey)
        {
            return NormalizeMonthKey(salaryMonthKey);
        }

        private static bool TryParseSalaryMonthKey(string salaryMonthKey, out DateTime monthStart)
        {
            return TryParseMonthKey(salaryMonthKey, out monthStart);
        }

        private static string NormalizeMonthKey(string monthKey)
        {
            monthKey = (monthKey ?? "").Trim();

            return TryParseMonthKey(monthKey, out var monthStart)
                ? monthStart.ToString("yyyy-MM")
                : "";
        }

        private static bool TryParseMonthKey(string monthKey, out DateTime monthStart)
        {
            monthStart = default;
            monthKey = (monthKey ?? "").Trim();

            if (monthKey.Length != 7 || monthKey[4] != '-')
                return false;

            if (!int.TryParse(monthKey.Substring(0, 4), out int year))
                return false;

            if (!int.TryParse(monthKey.Substring(5, 2), out int month))
                return false;

            if (year < 2000 || month < 1 || month > 12)
                return false;

            monthStart = new DateTime(year, month, 1);
            return true;
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

        public static bool TryCorrectKnownShortage(
            Guid id,
            int incorrectAmount,
            int correctedAmount,
            string responsibleEmployeeName)
        {
            var record = _records.FirstOrDefault(item => item.Id == id);

            if (record == null ||
                record.Category != "Недостачи" ||
                record.Amount != incorrectAmount ||
                !record.RelatedEmployeeName.Equals(
                    responsibleEmployeeName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            record.Amount = correctedAmount;
            record.Description = record.Description.Replace(
                $"{incorrectAmount} сом",
                $"{correctedAmount} сом",
                StringComparison.Ordinal);
            Save();
            return true;
        }

        public static bool TryDeleteKnownShortage(
            Guid id,
            int expectedAmount,
            string responsibleEmployeeName)
        {
            var record = _records.FirstOrDefault(item => item.Id == id);

            if (record == null)
                return false;

            if (record.Category != "Недостачи" ||
                record.Amount != expectedAmount ||
                !record.RelatedEmployeeName.Equals(
                    responsibleEmployeeName,
                    StringComparison.OrdinalIgnoreCase))
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
                         IsInBusinessPeriod(record, fromInclusive, toExclusive) &&
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
                    IsInBusinessPeriod(record, fromInclusive, toExclusive) &&
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
                    IsInBusinessPeriod(record, fromInclusive, toExclusive) &&
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
