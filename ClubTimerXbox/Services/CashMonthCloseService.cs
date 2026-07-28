using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Threading;

namespace ClubTimerXbox.Services
{
    public sealed class CashMonthCloseState
    {
        public string ActivatedMonthKey { get; set; } = "";

        public List<string> ClosedMonthKeys { get; set; } = new();

        public string PendingMonthKey { get; set; } = "";

        public List<CashAccountingAssignment> PendingAssignments { get; set; } = new();
    }

    public static class CashMonthCloseService
    {
        private static readonly string FolderPath =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ClubTimerXbox"
            );

        private static readonly string FilePath =
            Path.Combine(FolderPath, "cash_constitution_month_close.json");

        private static readonly CashMonthCloseState State = Load();

        private static DispatcherTimer? _timer;

        public static void Start()
        {
            DateTime now = DateTime.Now;
            string currentMonthKey = MonthKey(
                new DateTime(now.Year, now.Month, 1)
            );

            if (string.IsNullOrWhiteSpace(State.ActivatedMonthKey))
            {
                State.ActivatedMonthKey = currentMonthKey;
                Save();
            }
            else
            {
                RunCatchUp(now);
            }

            _timer ??= new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(1)
            };
            _timer.Tick -= TimerOnTick;
            _timer.Tick += TimerOnTick;
            _timer.Start();
        }

        public static void RunCatchUp(DateTime now)
        {
            CompletePendingAssignments();

            if (!TryParseMonthKey(State.ActivatedMonthKey, out DateTime activatedMonth))
                return;

            DateTime currentMonth = new(now.Year, now.Month, 1);
            for (DateTime month = activatedMonth;
                 month < currentMonth;
                 month = month.AddMonths(1))
            {
                CloseOnce(month);
            }

            if (now >= currentMonth.AddMonths(1).AddMinutes(-2))
                CloseOnce(currentMonth);
        }

        private static void TimerOnTick(object? sender, EventArgs e)
        {
            try
            {
                RunCatchUp(DateTime.Now);
            }
            catch
            {
                // На следующей минуте или запуске закрытие будет повторено.
            }
        }

        private static void CloseOnce(DateTime monthStart)
        {
            string key = MonthKey(monthStart);
            if (State.ClosedMonthKeys.Contains(key, StringComparer.OrdinalIgnoreCase))
                return;

            DateTime nextMonthStart = monthStart.AddMonths(1);
            var workedHours = EmployeeService
                .GetAllEmployees()
                .ToDictionary(
                    employee => employee.Name,
                    employee => Math.Max(
                        0,
                        EmployeeStatsService
                            .GetSummary(employee.Name, monthStart)
                            .MonthWorkTime
                            .TotalHours
                    ),
                    StringComparer.OrdinalIgnoreCase
                );
            var result = CashReconciliationService.CloseConstitutionMonth(
                monthStart,
                nextMonthStart,
                workedHours
            );
            if (result.IsDeferred)
                return;

            State.PendingMonthKey = key;
            State.PendingAssignments = result.Assignments.ToList();
            Save();
            CompletePendingAssignments();
        }

        private static void CompletePendingAssignments()
        {
            if (string.IsNullOrWhiteSpace(State.PendingMonthKey))
                return;

            string key = State.PendingMonthKey;
            foreach (var assignment in State.PendingAssignments)
            {
                string marker =
                    $"[cash-month-close:{key}:{assignment.EmployeeName}:{assignment.Amount}]";
                string description =
                    $"Автоматическое закрытие кассы за {key}.\n" +
                    $"Рабочие часы использованы для пропорционального распределения.\n" +
                    $"Сумма: {assignment.Amount} сом.\n" +
                    marker;

                if (!CashService.Records.Any(record =>
                        record.Description.Contains(marker, StringComparison.Ordinal)))
                {
                    CashService.AddShortage(
                        checkedByEmployeeName: "Система",
                        responsibleEmployeeName: assignment.EmployeeName,
                        title: "Остаток потерь при закрытии месяца",
                        description: description,
                        amount: assignment.Amount
                    );
                }
                if (!EmployeeLossService.Items.Any(item =>
                        item.Description.Contains(marker, StringComparison.Ordinal)))
                {
                    EmployeeLossService.AddLoss(
                        responsibleEmployeeName: assignment.EmployeeName,
                        checkedByEmployeeName: "Система",
                        lossType: "Закрытие месяца",
                        title: "Остаток потерь кассы",
                        description: description,
                        amount: assignment.Amount,
                        note: "Распределено по рабочим часам Конституцией кассы",
                        lossKind: "money",
                        isFixed: true
                    );
                }
            }

            State.ClosedMonthKeys.Add(key);
            State.PendingMonthKey = "";
            State.PendingAssignments.Clear();
            Save();
        }

        private static string MonthKey(DateTime monthStart)
        {
            return monthStart.ToString("yyyy-MM");
        }

        private static bool TryParseMonthKey(string value, out DateTime monthStart)
        {
            monthStart = default;
            if (string.IsNullOrWhiteSpace(value) ||
                value.Length != 7 ||
                value[4] != '-' ||
                !int.TryParse(value[..4], out int year) ||
                !int.TryParse(value[5..], out int month) ||
                year < 2000 ||
                month < 1 ||
                month > 12)
            {
                return false;
            }

            monthStart = new DateTime(year, month, 1);
            return true;
        }

        private static CashMonthCloseState Load()
        {
            try
            {
                if (!File.Exists(FilePath))
                    return new CashMonthCloseState();

                return JsonSerializer.Deserialize<CashMonthCloseState>(
                           File.ReadAllText(FilePath))
                       ?? new CashMonthCloseState();
            }
            catch
            {
                return new CashMonthCloseState();
            }
        }

        private static void Save()
        {
            Directory.CreateDirectory(FolderPath);
            File.WriteAllText(
                FilePath,
                JsonSerializer.Serialize(
                    State,
                    new JsonSerializerOptions { WriteIndented = true }
                )
            );
        }
    }
}
