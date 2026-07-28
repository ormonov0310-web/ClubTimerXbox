using ClubTimerXbox.Models;
using ClubTimerXbox.Services;

var suite = new CashConstitutionTestSuite();
suite.Run();

internal sealed class CashConstitutionTestSuite
{
    private static readonly DateTime MonthStart = new(2026, 7, 1);
    private static readonly DateTime NextMonthStart = MonthStart.AddMonths(1);
    private static readonly DateTime Now = new(2026, 7, 28, 12, 0, 0);

    private int _passed;

    public void Run()
    {
        Test("Связанная сверка полностью закрывает ошибку типа оплаты", PairedFullSettlement);
        Test("Связанная сверка закрывает только часть и оформляет остаток", PairedPartialSettlement);
        Test("Старый излишек не спасает подтверждённую новую недостачу", OldExtraDoesNotRescueConfirmedLoss);
        Test("Остаток связанного излишка пополняет общую карту", PairedExtraRemainderJoinsPool);
        Test("Свободный излишек сначала закрывает неизвестную потерю", ExtraSettlesUnknownFirst);
        Test("После неизвестной потери излишек закрывает подозреваемую", ExtraSettlesSuspectedSecond);
        Test("В клубе остаётся одна активная карта излишка", OnlyOneOpenExtraCard);
        Test("Одновременно разрешены несколько карт недостачи", MultipleShortageCards);
        Test("Повторная корректировка идемпотентна", RepeatedCorrectionIsIdempotent);
        Test("Неизвестная потеря переживает повторную корректировку", UnknownLossSurvivesRepeatedCorrection);
        Test("Корректировка создаёт только не представленную карточками сумму", CorrectionCreatesOnlyMissingDifference);
        Test("Ручной штраф меньше потери уменьшает карту", ManualLossPartiallyConsumesShortage);
        Test("Ручной штраф ровно по потере закрывает карту", ManualLossExactlyClosesShortage);
        Test("Ручной штраф сверх потери создаёт излишек", ManualLossOverageCreatesExtra);
        Test("Свободный ручной штраф хранит равную проводку потери и излишка", FreeManualLossIsBalanced);
        Test("Положительный остаток месяца архивируется", PositiveMonthClose);
        Test("Минус месяца распределяется 99 к 1 по часам", NegativeMonthCloseDistribution);
        Test("Закрытие месяца распределяет только чистый минус", MonthCloseDistributesNetShortage);
        Test("Месяц без рабочих часов не теряет отрицательный остаток", MonthCloseWithoutHoursIsDeferred);
        Test("Закрытые карты не воскресают после контрольной точки", ClosedCardsNeverReturn);
        Test("Случайные последовательности сохраняют инварианты", RandomizedInvariantTest);

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
        Equal(1, result.Assignments.Count, "Число штрафов");
        Equal("Старый", result.Assignments[0].EmployeeName, "Ответственный");
        Equal(60, result.Assignments[0].Amount, "Оформленный остаток");
        Equal(0, result.Breakdown, "Разбор");
    }

    private void OldExtraDoesNotRescueConfirmedLoss()
    {
        var items = NewLedger();
        AddReadyExtra(items, 100, Now.AddHours(-1));

        CashConstitutionEngine.RecordCashAcceptance(
            items, MonthStart, NextMonthStart, Now,
            "Новый", "Старый", 1000, 900, "Приёмка");
        var result = CashConstitutionEngine.RecordCashlessVerification(
            items, MonthStart, NextMonthStart, Now.AddMinutes(1),
            1000, 1000, "", "Сверка");

        Equal(1, result.Assignments.Count, "Число штрафов");
        Equal(100, result.Assignments[0].Amount, "Штраф");
        Equal(100, result.Breakdown, "Старый излишек");
        Equal(100, Open(items).Single(IsExtra).Amount, "Активная карта излишка");
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

    private void CorrectionCreatesOnlyMissingDifference()
    {
        Equal(
            -322,
            CashConstitutionEngine.CalculateUnrepresentedDifference(-322, 0, 0),
            "Новая потеря"
        );
        Equal(
            0,
            CashConstitutionEngine.CalculateUnrepresentedDifference(-322, 0, -322),
            "Потеря уже представлена карточкой"
        );
        Equal(
            0,
            CashConstitutionEngine.CalculateUnrepresentedDifference(-322, 322, 0),
            "Потеря уже оформлена"
        );
        Equal(
            0,
            CashConstitutionEngine.CalculateUnrepresentedDifference(0, 100, 100),
            "Старый излишек сохраняется после оформления новой потери"
        );
        Equal(
            -22,
            CashConstitutionEngine.CalculateUnrepresentedDifference(-322, 200, -100),
            "Часть оформлена и часть представлена"
        );
        Equal(
            0,
            CashConstitutionEngine.CalculateUnrepresentedDifference(2, 0, 2),
            "Излишек уже представлен"
        );
        Equal(
            502,
            CashConstitutionEngine.CalculateUnrepresentedDifference(2, 500, 0),
            "Оформленная потеря и физический плюс сохраняются раздельно"
        );
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
        Equal(0, items.Count(item =>
            item.Status == CashReconciliationStatus.Resolved && item.Amount != 0),
            "Закрытая карта с активной суммой");
        Assert(Open(items).Count(IsExtra) <= 1, "Больше одной активной карты излишка.");
        Assert(items.All(item =>
            item.OriginalAmount <= 0 ||
            item.Amount + item.ResolvedAmount + item.FormalizedAmount <= item.OriginalAmount ||
            IsExtra(item)),
            "Недостача создала деньги.");
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
