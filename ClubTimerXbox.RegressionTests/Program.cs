using ClubTimerXbox.Models;
using ClubTimerXbox.Services;
using System.Text.Json;

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
        Test("Новый излишек закрывает старую известную потерю последней", NewExtraSettlesOlderConfirmedLast);
        Test("Внутри одного типа излишек закрывает старые карты раньше новых", ExtraUsesOldestWithinType);
        Test("В клубе остаётся одна активная карта излишка", OnlyOneOpenExtraCard);
        Test("Одновременно разрешены несколько карт недостачи", MultipleShortageCards);
        Test("Повторная корректировка идемпотентна", RepeatedCorrectionIsIdempotent);
        Test("Повторная сверка безнала не создаёт дубль", RepeatedCashlessVerificationIsRecognized);
        Test("Нулевая сверка также защищена от повторного выполнения", RepeatedZeroVerificationIsRecognized);
        Test("Новая приёмка открывает новый цикл сверки", NewCashAcceptanceRequiresNewCashlessVerification);
        Test("Нулевая приёмка тоже открывает новый цикл сверки", ZeroAcceptanceRequiresNewVerification);
        Test("Неизвестная потеря переживает повторную корректировку", UnknownLossSurvivesRepeatedCorrection);
        Test("Корректировка создаёт только не представленную карточками сумму", CorrectionCreatesOnlyMissingDifference);
        Test("Ручной штраф меньше потери уменьшает карту", ManualLossPartiallyConsumesShortage);
        Test("Частичный штраф не меняет статус и сотрудника остатка", PartialLossKeepsRemainingResponsibility);
        Test("Частичные штрафы разным сотрудникам не перезаписываются", PartialLossAllocationsRemainSeparate);
        Test("Ручной штраф расходует известные, подозреваемые и неизвестные по порядку", ManualLossUsesConstitutionPriority);
        Test("Повтор ID ручного штрафа не выполняет его второй раз", ManualLossOperationIdIsIdempotent);
        Test("Ручной штраф ровно по потере закрывает карту", ManualLossExactlyClosesShortage);
        Test("Ручной штраф сверх потери создаёт излишек", ManualLossOverageCreatesExtra);
        Test("Сверхштраф создаёт вклад излишка с отдельным источником", ManualLossOverageHasDedicatedOrigin);
        Test("Свободный ручной штраф хранит равную проводку потери и излишка", FreeManualLossIsBalanced);
        Test("Реальная цепочка 1811 оставляет только 42 излишка", RealPartialPenaltyThenLaterExtra);
        Test("Сверка оформляет только известный остаток после приоритетов", VerificationFormalizesOnlyConfirmedRemainder);
        Test("Положительный остаток месяца архивируется", PositiveMonthClose);
        Test("Минус месяца распределяется 99 к 1 по часам", NegativeMonthCloseDistribution);
        Test("Закрытие месяца распределяет только чистый минус", MonthCloseDistributesNetShortage);
        Test("Месяц без рабочих часов не теряет отрицательный остаток", MonthCloseWithoutHoursIsDeferred);
        Test("Повтор закрытия месяца после перезапуска возвращает тот же результат", MonthCloseSurvivesRestart);
        Test("Закрытые карты не воскресают после контрольной точки", ClosedCardsNeverReturn);
        Test("Контрольная точка учитывает выживший разбор без зеркальной карты", CheckpointBaselinePreventsMirroredDifference);
        Test("Нулевая контрольная точка заменяет предыдущий ненулевой снимок", ZeroCheckpointReplacesPreviousBaseline);
        Test("Перезапуск сохраняет частичные назначения и статус остатка", RestartPreservesPartialAllocations);
        Test("Случайные последовательности сохраняют инварианты", RandomizedInvariantTest);

        Test("Корректировка оформляет ожидающую подозреваемую недостачу", CorrectionFormalizesAwaitingSuspected);
        Test("Корректировка сохраняет ожидающую неизвестную недостачу", CorrectionKeepsAwaitingUnknown);
        Test("Старые закрытые карты мигрируют без повторного открытия", LegacyClosedCardMigrationIsStable);
        Test("Контрольная точка не отражает старую недостачу в излишек", NegativeCheckpointDoesNotMirror);
        Test("Предварительный этап корректировки не ставит контрольную точку", PreliminaryCorrectionHasNoCheckpoint);
        Test("Финальная точка команды сохраняется один раз вместе с фактами", CorrectionCommitIsIdempotent);

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

    private void NewExtraSettlesOlderConfirmedLast()
    {
        var items = NewLedger();
        AddReadyShortage(
            items,
            100,
            CashResponsibilityLevel.Confirmed,
            Now.AddHours(-1),
            responsible: "Ответственный"
        );

        var result = CashConstitutionEngine.RecordCashlessVerification(
            items,
            MonthStart,
            NextMonthStart,
            Now,
            1000,
            1100,
            "",
            "Новый излишек"
        );

        Equal(0, result.Breakdown, "Разбор");
        Equal(0, result.Assignments.Count, "Штрафы");
        Equal(0, Open(items).Count, "Активные карты");
    }

    private void ExtraUsesOldestWithinType()
    {
        var items = NewLedger();
        Guid oldId = AddReadyShortage(
            items,
            70,
            CashResponsibilityLevel.Unknown,
            Now.AddHours(-2)
        );
        Guid newId = AddReadyShortage(
            items,
            70,
            CashResponsibilityLevel.Unknown,
            Now.AddHours(-1)
        );

        CashConstitutionEngine.RecordCashlessVerification(
            items,
            MonthStart,
            NextMonthStart,
            Now,
            1000,
            1100,
            "",
            "Излишек 100"
        );

        Equal(0, Open(items).Count(item => item.Id == oldId), "Старая карта");
        Equal(40, Open(items).Single(item => item.Id == newId).Amount, "Новая карта");
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

    private void RepeatedCashlessVerificationIsRecognized()
    {
        var items = NewLedger();
        CashConstitutionEngine.RecordCashlessVerification(
            items,
            MonthStart,
            NextMonthStart,
            Now,
            22792,
            20922,
            "",
            "Первая сверка"
        );

        bool alreadyVerified = CashConstitutionEngine.HasCurrentCashlessVerification(
            items,
            MonthStart,
            NextMonthStart,
            22792,
            20922
        );

        Assert(alreadyVerified, "Повторная сверка должна использовать существующее расследование.");
        int countBeforeRepeat = items.Count;
        var repeated = CashConstitutionEngine.RecordCashlessVerification(
            items,
            MonthStart,
            NextMonthStart,
            Now.AddSeconds(1),
            22792,
            20922,
            "",
            "Повторная сверка"
        );
        Equal(
            1,
            items.Count(item =>
                item.IsTechnicalEvent &&
                item.Origin == CashReconciliationOrigin.CashlessVerification),
            "Количество событий сверки безнала"
        );
        Equal(countBeforeRepeat, items.Count, "Количество записей после повтора");
        Equal(0, repeated.Assignments.Count, "Повторные штрафы");
    }

    private void NewCashAcceptanceRequiresNewCashlessVerification()
    {
        var items = NewLedger();
        CashConstitutionEngine.RecordCashlessVerification(
            items,
            MonthStart,
            NextMonthStart,
            Now,
            22792,
            20922,
            "",
            "Первая сверка"
        );
        CashConstitutionEngine.RecordCashAcceptance(
            items,
            MonthStart,
            NextMonthStart,
            Now.AddMinutes(1),
            "Новый",
            "Старый",
            500,
            490,
            "Новая приёмка"
        );

        bool alreadyVerified = CashConstitutionEngine.HasCurrentCashlessVerification(
            items,
            MonthStart,
            NextMonthStart,
            22792,
            20922
        );

        Assert(!alreadyVerified, "После новой приёмки нужна новая сверка безнала.");
    }

    private void RepeatedZeroVerificationIsRecognized()
    {
        var items = NewLedger();
        CashConstitutionEngine.RecordCashlessVerification(
            items, MonthStart, NextMonthStart, Now, 1000, 1000, "", "Ноль");
        int countBeforeRepeat = items.Count;

        CashConstitutionEngine.RecordCashlessVerification(
            items, MonthStart, NextMonthStart, Now.AddSeconds(1), 1000, 1000, "", "Повтор");

        Equal(countBeforeRepeat, items.Count, "Количество событий");
        Equal(
            1,
            items.Count(item =>
                item.IsTechnicalEvent &&
                item.Origin == CashReconciliationOrigin.CashlessVerification),
            "Журнал сверки"
        );
    }

    private void ZeroAcceptanceRequiresNewVerification()
    {
        var items = NewLedger();
        CashConstitutionEngine.RecordCashlessVerification(
            items, MonthStart, NextMonthStart, Now, 1000, 1000, "", "Сверка");
        CashConstitutionEngine.RecordCashAcceptance(
            items,
            MonthStart,
            NextMonthStart,
            Now.AddMinutes(1),
            "Новый",
            "Старый",
            1000,
            1000,
            "Нулевая приёмка"
        );

        Assert(
            !CashConstitutionEngine.HasCurrentCashlessVerification(
                items, MonthStart, NextMonthStart, 1000, 1000),
            "Даже нулевая новая приёмка требует новой сверки."
        );
    }

    private void CorrectionFormalizesAwaitingSuspected()
    {
        var items = NewLedger();
        CashConstitutionEngine.RecordCashlessVerification(
            items,
            MonthStart,
            NextMonthStart,
            Now,
            expectedAmount: 100,
            actualAmount: 90,
            suspectedEmployeeName: "Админ 1",
            note: "Проверка"
        );

        var result = CashConstitutionEngine.ApplyCorrection(
            items,
            MonthStart,
            NextMonthStart,
            Now.AddMinutes(1),
            checkpointNumber: 10
        );

        Equal(10, result.Assignments.Sum(item => item.Amount), "Оформленная сумма");
        Equal("Админ 1", result.Assignments.Single().EmployeeName, "Сотрудник");
        Equal(0, Open(items).Count(IsShortage), "Открытые недостачи");
    }

    private void CorrectionKeepsAwaitingUnknown()
    {
        var items = NewLedger();
        CashConstitutionEngine.RecordCashlessVerification(
            items,
            MonthStart,
            NextMonthStart,
            Now,
            expectedAmount: 100,
            actualAmount: 90,
            suspectedEmployeeName: "",
            note: "Проверка"
        );

        var result = CashConstitutionEngine.ApplyCorrection(
            items,
            MonthStart,
            NextMonthStart,
            Now.AddMinutes(1),
            checkpointNumber: 11
        );

        Equal(0, result.Assignments.Sum(item => item.Amount), "Оформленная сумма");
        var shortage = Open(items).Single(IsShortage);
        Equal(10, shortage.Amount, "Остаток");
        Equal(CashResponsibilityLevel.Unknown, shortage.ResponsibilityLevel, "Тип");
        Equal(CashReconciliationStage.Ready, shortage.Stage, "Стадия");
    }

    private void LegacyClosedCardMigrationIsStable()
    {
        Guid cardId = Guid.NewGuid();
        var items = new List<CashReconciliationItem>
        {
            new()
            {
                AccountingSchemaVersion = 2,
                Id = cardId,
                InvestigationId = cardId,
                CreatedAt = Now,
                Kind = CashReconciliationKind.CashShortage,
                Origin = CashReconciliationOrigin.CashAcceptance,
                Status = CashReconciliationStatus.Resolved,
                Stage = CashReconciliationStage.Ready,
                ResponsibilityLevel = CashResponsibilityLevel.Confirmed,
                Amount = 0,
                OriginalAmount = 100,
                FormalizedAmount = 100,
                PostedFormalizedAmount = 100,
                ResponsibleEmployeeName = "Старый сотрудник",
                Resolution = CashReconciliationResolution.FormalizedLoss,
                ResolvedAt = Now.AddMinutes(1)
            }
        };

        CashConstitutionEngine.Normalize(items, MonthStart, NextMonthStart);
        CashConstitutionEngine.Normalize(items, MonthStart, NextMonthStart);

        var card = items.Single(item => item.Id == cardId);
        Equal(3, card.AccountingSchemaVersion, "Версия схемы");
        Equal(CashReconciliationStatus.Resolved, card.Status, "Статус");
        Equal(0, card.Amount, "Остаток");
        Equal(1, card.LossAllocations.Count, "Исторические назначения");
        Equal(100, card.LossAllocations.Single().Amount, "Сумма назначения");
        Equal(100, card.LossAllocations.Single().PostedAmount, "Проведённая сумма");
        Equal(0, Open(items).Count, "Открытые карты");
    }

    private void NegativeCheckpointDoesNotMirror()
    {
        var items = NewLedger();
        AddReadyShortage(items, 100, CashResponsibilityLevel.Unknown, Now);
        CashConstitutionEngine.ApplyCorrection(
            items,
            MonthStart,
            NextMonthStart,
            Now.AddMinutes(1),
            checkpointNumber: 20
        );

        Equal(
            -100,
            CashConstitutionEngine.GetLatestCheckpointBreakdown(
                items,
                MonthStart,
                NextMonthStart),
            "Разбор на контрольной точке"
        );
        int missing = CashConstitutionEngine.CalculateUnrepresentedDifference(
            observedDifference: 100,
            alreadyFormalizedLosses: 0,
            representedCycleDifference: 0,
            checkpointBreakdown: -100
        );
        Equal(0, missing, "Зеркальная карта");
    }

    private void PreliminaryCorrectionHasNoCheckpoint()
    {
        var items = NewLedger();
        AddReadyShortage(
            items,
            25,
            CashResponsibilityLevel.Unknown,
            Now
        );

        CashConstitutionEngine.ApplyCorrection(
            items,
            MonthStart,
            NextMonthStart,
            Now.AddMinutes(1),
            checkpointNumber: 0
        );

        Equal(
            0,
            items.Count(item =>
                item.IsTechnicalEvent &&
                item.Origin == CashReconciliationOrigin.CorrectionCheckpoint),
            "Контрольные точки"
        );
        Equal(
            0,
            CashConstitutionEngine.GetLatestCheckpointBreakdown(
                items,
                MonthStart,
                NextMonthStart),
            "Снимок разбора"
        );
    }

    private void CorrectionCommitIsIdempotent()
    {
        var items = NewLedger();
        AddReadyShortage(
            items,
            25,
            CashResponsibilityLevel.Unknown,
            Now
        );
        const string operationId = "correction-command-1";

        CashConstitutionEngine.ApplyCorrection(
            items,
            MonthStart,
            NextMonthStart,
            Now.AddMinutes(1),
            checkpointNumber: 30,
            operationId: operationId,
            actualCashAtCheckpoint: 100,
            actualCashlessAtCheckpoint: 200
        );
        CashConstitutionEngine.ApplyCorrection(
            items,
            MonthStart,
            NextMonthStart,
            Now.AddMinutes(2),
            checkpointNumber: 31,
            operationId: operationId,
            actualCashAtCheckpoint: 999,
            actualCashlessAtCheckpoint: 999
        );

        var marker = items.Single(item =>
            item.IsTechnicalEvent &&
            item.Origin == CashReconciliationOrigin.CorrectionCheckpoint &&
            item.OperationId == operationId);
        Equal(30L, marker.CheckpointNumber, "Номер контрольной точки");
        Equal(100, marker.ExpectedAmount, "Факт наличных");
        Equal(200, marker.ActualAmount, "Факт безнала");
        Equal(-25, marker.AmountAtCheckpoint, "Разбор");
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

    private void PartialLossKeepsRemainingResponsibility()
    {
        var items = NewLedger();
        Guid id = AddReadyShortage(
            items,
            100,
            CashResponsibilityLevel.Suspected,
            Now,
            suspect: "Админ 1"
        );

        CashConstitutionEngine.ApplyManualLoss(
            items,
            MonthStart,
            NextMonthStart,
            Now.AddMinutes(1),
            "Админ 2",
            40
        );

        var remaining = Open(items).Single(item => item.Id == id);
        Equal(60, remaining.Amount, "Остаток");
        Equal(CashResponsibilityLevel.Suspected, remaining.ResponsibilityLevel, "Тип");
        Equal("Админ 1", remaining.SuspectedEmployeeName, "Подозреваемый");
        Equal("", remaining.ResponsibleEmployeeName, "Ответственный");
    }

    private void PartialLossAllocationsRemainSeparate()
    {
        var items = NewLedger();
        Guid id = AddReadyShortage(
            items,
            1000,
            CashResponsibilityLevel.Suspected,
            Now,
            suspect: "Исходный подозреваемый"
        );

        CashConstitutionEngine.ApplyManualLoss(
            items, MonthStart, NextMonthStart, Now.AddMinutes(1), "Сталбек", 300);
        CashConstitutionEngine.ApplyManualLoss(
            items, MonthStart, NextMonthStart, Now.AddMinutes(2), "Мирбек", 200);

        var card = items.Single(item => item.Id == id);
        Equal(500, card.Amount, "Остаток");
        Equal(CashResponsibilityLevel.Suspected, card.ResponsibilityLevel, "Тип остатка");
        Equal("Исходный подозреваемый", card.SuspectedEmployeeName, "Подозреваемый");
        Equal(2, card.LossAllocations.Count, "Назначения");
        Equal(300, card.LossAllocations.Single(item => item.EmployeeName == "Сталбек").Amount, "Сталбек");
        Equal(200, card.LossAllocations.Single(item => item.EmployeeName == "Мирбек").Amount, "Мирбек");
    }

    private void ManualLossUsesConstitutionPriority()
    {
        var items = NewLedger();
        Guid confirmedId = AddReadyShortage(
            items,
            100,
            CashResponsibilityLevel.Confirmed,
            Now,
            responsible: "Админ 1"
        );
        Guid suspectedId = AddReadyShortage(
            items,
            100,
            CashResponsibilityLevel.Suspected,
            Now.AddMinutes(1),
            suspect: "Админ 1"
        );
        Guid unknownId = AddReadyShortage(
            items,
            100,
            CashResponsibilityLevel.Unknown,
            Now.AddMinutes(2)
        );

        CashConstitutionEngine.ApplyManualLoss(
            items, MonthStart, NextMonthStart, Now.AddMinutes(3), "Админ 1", 150);

        Equal(0, Open(items).Count(item => item.Id == confirmedId), "Известная карта");
        Equal(50, Open(items).Single(item => item.Id == suspectedId).Amount, "Подозрение");
        Equal(100, Open(items).Single(item => item.Id == unknownId).Amount, "Неизвестная");

        CashConstitutionEngine.ApplyManualLoss(
            items, MonthStart, NextMonthStart, Now.AddMinutes(4), "Админ 2", 100);

        Equal(0, Open(items).Count(item => item.Id == suspectedId), "Подозрение после второго штрафа");
        Equal(50, Open(items).Single(item => item.Id == unknownId).Amount, "Итог неизвестной");
        Equal(CashResponsibilityLevel.Unknown, items.Single(item => item.Id == unknownId).ResponsibilityLevel, "Тип остатка");
    }

    private void ManualLossOperationIdIsIdempotent()
    {
        var items = NewLedger();
        AddReadyShortage(items, 100, CashResponsibilityLevel.Unknown, Now);

        var first = CashConstitutionEngine.ApplyManualLoss(
            items,
            MonthStart,
            NextMonthStart,
            Now.AddMinutes(1),
            "Админ",
            40,
            "command-105103"
        );
        var repeated = CashConstitutionEngine.ApplyManualLoss(
            items,
            MonthStart,
            NextMonthStart,
            Now.AddMinutes(2),
            "Админ",
            40,
            "command-105103"
        );

        Equal(40, first.Assignments.Sum(item => item.Amount), "Первое назначение");
        Equal(0, repeated.Assignments.Count, "Повторные назначения");
        Equal(60, Open(items).Single(IsShortage).Amount, "Остаток");
        Equal(
            1,
            items.SelectMany(item => item.LossAllocations).Count(),
            "Количество назначений"
        );
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

    private void ManualLossOverageHasDedicatedOrigin()
    {
        var items = NewLedger();
        AddReadyShortage(items, 100, CashResponsibilityLevel.Unknown, Now);

        CashConstitutionEngine.ApplyManualLoss(
            items, MonthStart, NextMonthStart, Now.AddMinutes(1), "Выбранный", 130);

        var contribution = Open(items)
            .Single(IsExtra)
            .ExtraContributions
            .Single(item => item.Amount > 0);
        Equal(CashReconciliationOrigin.OwnerPenaltyOverage, contribution.Origin, "Источник");
        Equal(30, contribution.Amount, "Излишек");
    }

    private void RealPartialPenaltyThenLaterExtra()
    {
        var items = NewLedger();
        Guid shortageId = AddReadyShortage(
            items,
            1811,
            CashResponsibilityLevel.Suspected,
            Now.AddHours(-2),
            suspect: "Мирбек"
        );

        CashConstitutionEngine.RecordRawDifference(
            items,
            MonthStart,
            NextMonthStart,
            Now.AddHours(-1),
            353,
            0,
            353,
            "",
            "",
            "Ранее найденные излишки"
        );
        CashConstitutionEngine.ApplyManualLoss(
            items, MonthStart, NextMonthStart, Now.AddMinutes(-30), "Сталбек", 300);
        CashConstitutionEngine.ApplyManualLoss(
            items, MonthStart, NextMonthStart, Now.AddMinutes(-20), "Мирбек", 200);

        var beforeVerification = items.Single(item => item.Id == shortageId);
        Equal(958, beforeVerification.Amount, "Остаток до сверки");
        Equal(CashResponsibilityLevel.Suspected, beforeVerification.ResponsibilityLevel, "Тип до сверки");

        var result = CashConstitutionEngine.RecordCashlessVerification(
            items,
            MonthStart,
            NextMonthStart,
            Now,
            1000,
            2000,
            "",
            "Новый излишек 1000"
        );

        Equal(42, result.Breakdown, "Итоговый излишек");
        Equal(0, result.Assignments.Count, "Новые штрафы");
        Equal(0, Open(items).Count(IsShortage), "Открытые недостачи");
        Equal(42, Open(items).Single(IsExtra).Amount, "Остаток карты излишка");
        Equal(300, beforeVerification.LossAllocations.Single(item => item.EmployeeName == "Сталбек").Amount, "Сталбек");
        Equal(200, beforeVerification.LossAllocations.Single(item => item.EmployeeName == "Мирбек").Amount, "Мирбек");
    }

    private void VerificationFormalizesOnlyConfirmedRemainder()
    {
        var items = NewLedger();
        AddReadyShortage(items, 50, CashResponsibilityLevel.Unknown, Now.AddHours(-3));
        AddReadyShortage(
            items,
            50,
            CashResponsibilityLevel.Suspected,
            Now.AddHours(-2),
            suspect: "Подозреваемый"
        );
        AddReadyShortage(
            items,
            100,
            CashResponsibilityLevel.Confirmed,
            Now.AddHours(-1),
            responsible: "Ответственный"
        );

        var result = CashConstitutionEngine.RecordCashlessVerification(
            items,
            MonthStart,
            NextMonthStart,
            Now,
            1000,
            1120,
            "",
            "Излишек 120"
        );

        Equal(120, result.SettledAmount, "Зачтено");
        Equal(1, result.Assignments.Count, "Оформления");
        Equal("Ответственный", result.Assignments[0].EmployeeName, "Сотрудник");
        Equal(80, result.Assignments[0].Amount, "Остаток известной карты");
        Equal(0, result.Breakdown, "Разбор");
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

    private void MonthCloseSurvivesRestart()
    {
        var items = NewLedger();
        AddReadyShortage(items, 100, CashResponsibilityLevel.Unknown, Now);

        var hours = new Dictionary<string, double>
        {
            ["Работник 1"] = 3,
            ["Работник 2"] = 1
        };
        DateTime closeAt = NextMonthStart.AddMinutes(-2);
        var first = CashConstitutionEngine.CloseMonth(
            items, MonthStart, NextMonthStart, closeAt, hours);

        string json = JsonSerializer.Serialize(items);
        var restored = JsonSerializer.Deserialize<List<CashReconciliationItem>>(json)
            ?? throw new InvalidOperationException("Не удалось восстановить журнал.");
        var second = CashConstitutionEngine.CloseMonth(
            restored, MonthStart, NextMonthStart, closeAt.AddSeconds(30), hours);

        Equal(-100, second.ClosingBreakdown, "Итог месяца");
        Equal(100, second.Assignments.Sum(item => item.Amount), "Сумма назначений");
        Assert(
            first.Assignments.Select(item => item.AllocationId).OrderBy(id => id)
                .SequenceEqual(
                    second.Assignments.Select(item => item.AllocationId).OrderBy(id => id)),
            "После перезапуска изменились идентификаторы назначений."
        );
        var marker = restored.Single(item =>
            item.IsTechnicalEvent &&
            item.Origin == CashReconciliationOrigin.MonthClose);
        Equal(
            marker.LossAllocations.Sum(item => item.Amount),
            marker.LossAllocations.Sum(item => item.PostedAmount),
            "Месячный акт не должен попадать в обычное восстановление проводок"
        );
        Equal(0, Open(restored).Count, "Открытые карты");
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

    private void CheckpointBaselinePreventsMirroredDifference()
    {
        var items = NewLedger();
        AddReadyExtra(items, 100, Now);
        CashConstitutionEngine.ApplyCorrection(
            items,
            MonthStart,
            NextMonthStart,
            Now.AddMinutes(1),
            105103
        );

        Equal(
            100,
            CashConstitutionEngine.GetLatestCheckpointBreakdown(
                items, MonthStart, NextMonthStart),
            "Разбор на контрольной точке"
        );

        int missing = CashConstitutionEngine.CalculateUnrepresentedDifference(
            observedDifference: -100,
            alreadyFormalizedLosses: 0,
            representedCycleDifference: 0,
            checkpointBreakdown: 100
        );
        Equal(0, missing, "Зеркальная карта");
    }

    private void ZeroCheckpointReplacesPreviousBaseline()
    {
        var items = NewLedger();
        AddReadyExtra(items, 100, Now);
        CashConstitutionEngine.ApplyCorrection(
            items, MonthStart, NextMonthStart, Now.AddMinutes(1), 1);

        CashConstitutionEngine.RecordRawDifference(
            items,
            MonthStart,
            NextMonthStart,
            Now.AddMinutes(2),
            -100,
            100,
            0,
            "",
            "Подозреваемый",
            "Новая недостача"
        );
        CashConstitutionEngine.ApplyCorrection(
            items, MonthStart, NextMonthStart, Now.AddMinutes(3), 2);

        Equal(
            0,
            CashConstitutionEngine.GetLatestCheckpointBreakdown(
                items, MonthStart, NextMonthStart),
            "Новый снимок"
        );
    }

    private void RestartPreservesPartialAllocations()
    {
        var items = NewLedger();
        Guid id = AddReadyShortage(
            items,
            100,
            CashResponsibilityLevel.Suspected,
            Now,
            suspect: "Админ 1"
        );
        CashConstitutionEngine.ApplyManualLoss(
            items, MonthStart, NextMonthStart, Now.AddMinutes(1), "Админ 2", 40);

        string json = JsonSerializer.Serialize(items);
        var restored = JsonSerializer.Deserialize<List<CashReconciliationItem>>(json)
            ?? throw new InvalidOperationException("Не удалось восстановить журнал.");
        CashConstitutionEngine.Normalize(restored, MonthStart, NextMonthStart);

        var card = restored.Single(item => item.Id == id);
        Equal(60, card.Amount, "Остаток");
        Equal(CashResponsibilityLevel.Suspected, card.ResponsibilityLevel, "Тип");
        Equal("Админ 1", card.SuspectedEmployeeName, "Подозреваемый");
        Equal(1, card.LossAllocations.Count, "Назначения");
        Equal("Админ 2", card.LossAllocations[0].EmployeeName, "Получатель штрафа");
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
        Equal(
            0,
            items.Count(item =>
                (item.LossAllocations ?? new List<CashLossAllocation>())
                    .Sum(allocation => Math.Max(0, allocation.Amount)) !=
                Math.Max(0, item.FormalizedAmount) &&
                item.AccountingSchemaVersion >= 3 &&
                item.Resolution != CashReconciliationResolution.MonthClosed),
            "Сумма назначений не равна оформленной сумме"
        );
        Equal(
            items.SelectMany(item => item.LossAllocations ?? new List<CashLossAllocation>())
                .Count(),
            items.SelectMany(item => item.LossAllocations ?? new List<CashLossAllocation>())
                .Select(item => item.Id)
                .Distinct()
                .Count(),
            "Уникальные ID назначений"
        );
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
