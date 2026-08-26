using ClubTimerXbox.Models;
using ClubTimerXbox.Services;

internal sealed class NextUpdateWorkflowTestSuite
{
    private int _passed;

    public void Run()
    {
        Test("legacy unpaid line migrates without losing creator", LegacyUnpaidMigration);
        Test("unpaid line is not financial revenue", UnpaidLineIsNotRevenue);
        Test("payment belongs to employee who received money", PaymentUsesActualReceiver);
        Test("debt responsibility transfers independently", DebtResponsibilityTransfers);
        Test("shift acceptance requires products cash and debts", ShiftAcceptanceRequiresAllBranches);
        Test("paid popularity excludes active debt and avoids duplicate session payment", PaidPopularityIsExact);
        Test("legacy immediate sale contributes without duplicating linked payment", LegacyImmediatePopularityMigratesOnce);
        Test("stock sends zero quantity products to the bottom", StockOrderUsesAvailabilityThenPopularity);
        Test("purchase catalog ignores current stock", PurchaseOrderUsesPopularityOnly);
        Test("sales catalog keeps available products above sold out products", SalesCatalogUsesAvailabilityThenPopularity);
        Test("arbitrary amount and minutes use one tariff formula", FlexibleTimeUsesTariffFormula);
        Test("game payments stay with their actual receivers", GamePaymentsUseActualReceivers);
        Test("refund consumes newest game payment first", RefundConsumesLatestPaymentFirst);
        Test("legacy prepaid amount stays older than new added time", LegacyPrepaidStaysFirst);
        Test("transferred game debt is not counted for target session", TransferredDebtStaysWithSourceSession);
        Test("late opening reassignment cancels original effect", ReassignmentCancelsOriginal);
        Test("late opening reassignment grants full equivalent duration", ReassignmentKeepsFullDuration);
        Test("cash difference below 500 does not force recount", SmallCashDifferenceProceeds);
        Test("cash difference of 500 forces recount before ledger", LargeCashDifferenceForcesRecount);
        Test("cash recount accepts the confirmed amount after the lock", CashRecountAcceptsConfirmedAmount);
        Test("cash recount button stays locked for one minute", CashRecountUsesHiddenOneMinuteLock);
        Test("repeat within ten minutes keeps the previous employee responsible", EarlyRepeatKeepsPreviousEmployee);
        Test("repeat after ten minutes belongs to the current employee", LateRepeatUsesCurrentEmployee);
        Test("repeat does not restart the responsibility window", RepeatDoesNotExtendResponsibilityWindow);
        Test("initial cash stays provisional until the ten minute window ends", InitialCashUsesFixedProvisionalWindow);
        Test("repeat replaces provisional cash instead of creating a second event", RepeatReplacesProvisionalCash);
        Test("provisional cash survives restart scheduling and becomes due once", ProvisionalCashUsesPersistedDeadline);
        Test("daily employee earnings reconcile exactly to monthly components", DailyEmployeeEarningsMatchMonthlyTotals);

        Console.WriteLine();
        Console.WriteLine($"PASS: {_passed} next update workflow scenarios.");
    }

    private static void LegacyUnpaidMigration()
    {
        var line = new GameSessionSaleLine
        {
            EmployeeName = "Арген",
            SettlementSchemaVersion = 0,
            IsPaid = false
        };

        Assert(SessionSaleSettlementService.NormalizeActiveUnpaidLine(line), "migration flag");
        Equal(SessionSaleSettlementService.CurrentSchemaVersion, line.SettlementSchemaVersion, "schema");
        Equal("Арген", line.CreatedByEmployeeName, "creator");
        Assert(!line.IsPaid, "migration must not invent payment");
    }

    private static void UnpaidLineIsNotRevenue()
    {
        var line = NewSaleLine("Мирбек", false);
        Assert(!SessionSaleSettlementService.IsFinanciallyPaid(line), "active debt became revenue");
    }

    private static void PaymentUsesActualReceiver()
    {
        var line = NewSaleLine("Мирбек", false);
        var paymentId = Guid.NewGuid();
        var paidAt = new DateTime(2026, 8, 26, 14, 30, 0);

        SessionSaleSettlementService.MarkPaid(
            line,
            paymentId,
            paidAt,
            "employee-2",
            "Арген",
            Guid.NewGuid());

        Assert(SessionSaleSettlementService.IsFinanciallyPaid(line), "payment not confirmed");
        Equal("Мирбек", SessionSaleSettlementService.GetCreatedByEmployeeName(line), "creator history");
        Equal("Арген", SessionSaleSettlementService.GetFinancialEmployeeName(line), "financial employee");
        Equal(paidAt, SessionSaleSettlementService.GetFinancialOccurredAt(line), "financial time");
        Equal(paymentId, line.PaymentRecordId!.Value, "payment link");
    }

    private static void DebtResponsibilityTransfers()
    {
        var line = NewSaleLine("Мирбек", false);
        var shiftId = Guid.NewGuid();
        var acceptedAt = new DateTime(2026, 8, 26, 15, 0, 0);

        SessionSaleSettlementService.AcceptDebtResponsibility(
            line,
            "Арген",
            shiftId,
            acceptedAt);

        Equal("Мирбек", line.CreatedByEmployeeName, "creator must remain");
        Equal("Арген", line.DebtResponsibleEmployeeName, "debt owner");
        Equal(shiftId, line.DebtResponsibleShiftId!.Value, "debt shift");
        Assert(!line.IsPaid, "acceptance must not create cash");
    }

    private static void ShiftAcceptanceRequiresAllBranches()
    {
        var state = new ShiftAcceptanceStatus
        {
            IsRequired = true,
            ProductsAccepted = true,
            CashAccepted = true,
            DebtAcceptanceRequired = true,
            DebtsAccepted = false
        };

        Assert(!state.IsCompleted, "debt branch was skipped");
        state.DebtsAccepted = true;
        Assert(state.IsCompleted, "three completed branches must finish acceptance");
    }

    private static void SmallCashDifferenceProceeds()
    {
        var state = new ShiftAcceptanceStatus();
        var now = new DateTime(2026, 8, 26, 10, 0, 0);
        var decision = CashAcceptanceRecountPolicy.Evaluate(
            state,
            "acceptance-1",
            expectedAmount: 1000,
            actualAmount: 501,
            now);

        Equal(CashRecountDecision.Proceed, decision, "decision");
        Assert(!state.CashRecountRequired, "recount flag");
    }

    private static void LargeCashDifferenceForcesRecount()
    {
        var state = new ShiftAcceptanceStatus();
        var now = new DateTime(2026, 8, 26, 10, 0, 0);
        var decision = CashAcceptanceRecountPolicy.Evaluate(
            state,
            "acceptance-1",
            expectedAmount: 100,
            actualAmount: 600,
            now);

        Equal(CashRecountDecision.RecountRequired, decision, "decision");
        Assert(state.CashRecountRequired, "recount flag");
        Equal(600, state.CashRecountFirstAmount, "first amount");
        Equal(now.AddMinutes(1), state.CashRecountUnlockAt!.Value, "unlock time");
    }

    private static void CashRecountAcceptsConfirmedAmount()
    {
        var state = new ShiftAcceptanceStatus();
        var now = new DateTime(2026, 8, 26, 10, 0, 0);
        CashAcceptanceRecountPolicy.Evaluate(
            state,
            "acceptance-1",
            expectedAmount: 100,
            actualAmount: 1000,
            now);

        var repeatedDuringLock = CashAcceptanceRecountPolicy.Evaluate(
            state,
            "acceptance-1",
            expectedAmount: 100,
            actualAmount: 1000,
            now.AddSeconds(30));
        Equal(CashRecountDecision.Locked, repeatedDuringLock, "same amount during lock");
        Assert(state.CashRecountRequired, "lock keeps recount active");

        var confirmed = CashAcceptanceRecountPolicy.Evaluate(
            state,
            "acceptance-1",
            expectedAmount: 100,
            actualAmount: 1000,
            now.AddMinutes(1));
        Equal(CashRecountDecision.Proceed, confirmed, "confirmed same amount");
        Assert(!state.CashRecountRequired, "confirmed amount clears recount");
    }

    private static void CashRecountUsesHiddenOneMinuteLock()
    {
        var state = new ShiftAcceptanceStatus();
        var now = new DateTime(2026, 8, 26, 10, 0, 0);
        CashAcceptanceRecountPolicy.Evaluate(
            state,
            "acceptance-1",
            expectedAmount: 100,
            actualAmount: 1000,
            now);

        Assert(
            CashAcceptanceRecountPolicy.IsLocked(
                state,
                "acceptance-1",
                now.AddSeconds(59)),
            "lock ended early");

        var blocked = CashAcceptanceRecountPolicy.Evaluate(
            state,
            "acceptance-1",
            expectedAmount: 100,
            actualAmount: 100,
            now.AddSeconds(59));
        Equal(CashRecountDecision.Locked, blocked, "locked decision");

        Assert(
            !CashAcceptanceRecountPolicy.IsLocked(
                state,
                "acceptance-1",
                now.AddMinutes(1)),
            "lock did not end at one minute");
    }

    private static void EarlyRepeatKeepsPreviousEmployee()
    {
        var acceptedAt = new DateTime(2026, 8, 26, 10, 0, 0);
        var state = new ShiftAcceptanceStatus
        {
            ProductsAccepted = true,
            CashAccepted = true,
            InitialProductsAndCashAcceptedAt = acceptedAt
        };

        string responsible = ShiftAcceptanceCorrectionPolicy.ResolveResponsibleEmployee(
            state,
            "Сотрудник 1",
            "Сотрудник 2",
            acceptedAt.AddMinutes(9).AddSeconds(59));
        Equal("Сотрудник 1", responsible, "responsible employee");
    }

    private static void LateRepeatUsesCurrentEmployee()
    {
        var acceptedAt = new DateTime(2026, 8, 26, 10, 0, 0);
        var state = new ShiftAcceptanceStatus
        {
            ProductsAccepted = true,
            CashAccepted = true,
            InitialProductsAndCashAcceptedAt = acceptedAt
        };

        string responsible = ShiftAcceptanceCorrectionPolicy.ResolveResponsibleEmployee(
            state,
            "Сотрудник 1",
            "Сотрудник 2",
            acceptedAt.AddMinutes(10));
        Equal("Сотрудник 2", responsible, "responsible employee");
    }

    private static void RepeatDoesNotExtendResponsibilityWindow()
    {
        var acceptedAt = new DateTime(2026, 8, 26, 10, 0, 0);
        var state = new ShiftAcceptanceStatus
        {
            ProductsAccepted = true,
            CashAccepted = true
        };

        ShiftAcceptanceCorrectionPolicy.CaptureInitialProductsAndCashCompletion(
            state,
            acceptedAt,
            isCorrectionAttempt: false);
        ShiftAcceptanceCorrectionPolicy.CaptureInitialProductsAndCashCompletion(
            state,
            acceptedAt.AddMinutes(8),
            isCorrectionAttempt: true);

        Equal(
            acceptedAt,
            state.InitialProductsAndCashAcceptedAt!.Value,
            "fixed window start");
    }

    private static void InitialCashUsesFixedProvisionalWindow()
    {
        var acceptedAt = new DateTime(2026, 8, 26, 10, 0, 0);
        var state = new ShiftAcceptanceStatus
        {
            NewEmployeeName = "Новый",
            ResponsibleEmployeeName = "Старый",
            InitialProductsAndCashAcceptedAt = acceptedAt
        };

        Assert(
            ShiftAcceptanceCorrectionPolicy.ShouldStageInitialCashAcceptance(
                state,
                "shift-1",
                acceptedAt.AddMinutes(9).AddSeconds(59)),
            "provisional window ended early");
        Assert(
            !ShiftAcceptanceCorrectionPolicy.ShouldStageInitialCashAcceptance(
                state,
                "shift-1",
                acceptedAt.AddMinutes(10)),
            "exact ten minute boundary must be final");
    }

    private static void RepeatReplacesProvisionalCash()
    {
        var items = new List<CashAcceptanceItem>();
        var firstAt = new DateTime(2026, 8, 26, 10, 0, 0);
        CashAcceptanceProvisionalPolicy.Upsert(
            items,
            "shift-1",
            "shift-1",
            "Новый",
            "Старый",
            100,
            600,
            "Первый ввод",
            firstAt);
        CashAcceptanceProvisionalPolicy.Upsert(
            items,
            "shift-1",
            "shift-1:cash-correction:2",
            "Новый",
            "Старый",
            100,
            100,
            "Исправленный ввод",
            firstAt.AddMinutes(5));

        Equal(1, items.Count, "financial event count");
        Equal(100, items[0].ActualCashAmount, "latest actual cash");
        Equal(0, items[0].Difference, "latest difference");
        Equal(2, items[0].AttemptKeys.Count, "technical attempt history");
        Equal(firstAt.AddMinutes(5), items[0].CreatedAt, "financial event time");
    }

    private static void ProvisionalCashUsesPersistedDeadline()
    {
        var items = new List<CashAcceptanceItem>();
        var acceptedAt = new DateTime(2026, 8, 26, 10, 0, 0);
        CashAcceptanceProvisionalPolicy.Upsert(
            items,
            "shift-1",
            "shift-1",
            "Новый",
            "Старый",
            100,
            100,
            "Приёмка",
            acceptedAt);
        Assert(
            CashAcceptanceProvisionalPolicy.Schedule(
                items,
                "shift-1",
                acceptedAt.AddMinutes(10)),
            "deadline was not persisted");

        Equal(
            0,
            CashAcceptanceProvisionalPolicy.GetDue(
                items,
                acceptedAt.AddMinutes(9).AddSeconds(59)).Count,
            "event became due early");
        Equal(
            1,
            CashAcceptanceProvisionalPolicy.GetDue(
                items,
                acceptedAt.AddMinutes(10)).Count,
            "event is not due at the boundary");
    }

    private static void DailyEmployeeEarningsMatchMonthlyTotals()
    {
        var days = new List<AutoSalaryDayEarning>
        {
            new AutoSalaryDayEarning
            {
                Date = new DateTime(2026, 12, 31),
                TimeAmount = 33,
                TimeRatingLostAmount = 3,
                TimeRatingPercents = new List<int> { 90 },
                GameAmount = 10,
                GameRatingEarnedAmount = 2,
                GameRatingPercents = new List<int> { 110 },
                ProductServiceBonusAmount = 2,
                OtherBonusAmount = 5
            },
            new AutoSalaryDayEarning
            {
                Date = new DateTime(2026, 12, 30),
                TimeAmount = 34,
                TimeRatingEarnedAmount = 1,
                TimeRatingPercents = new List<int> { 105 },
                GameAmount = 11,
                GameRatingLostAmount = 1,
                GameRatingPercents = new List<int> { 95 },
                ProductServiceBonusAmount = 3,
                OtherBonusAmount = 4
            }
        };

        EmployeeDailyEarningReconciler.Reconcile(
            days,
            new DateTime(2026, 12, 1),
            timeTarget: 70,
            gameTarget: 20,
            productBonusTarget: 6,
            otherBonusTarget: 10,
            timeRatingEarnedTarget: 1,
            timeRatingLostTarget: 3,
            gameRatingEarnedTarget: 2,
            gameRatingLostTarget: 1);

        Equal(70, days.Sum(item => item.TimeAmount), "time total");
        Equal(20, days.Sum(item => item.GameAmount), "game total");
        Equal(6, days.Sum(item => item.ProductServiceBonusAmount), "product bonus total");
        Equal(10, days.Sum(item => item.OtherBonusAmount), "other bonus total");
        Equal(106, days.Sum(item => item.TotalAmount), "gross daily total");
        Equal(72, days.Sum(item => item.TimeBaseAmount), "time baseline total");
        Equal(19, days.Sum(item => item.GameBaseAmount), "game baseline total");
        Equal(1, days.Sum(item => item.TimeRatingEarnedAmount), "time rating gain");
        Equal(3, days.Sum(item => item.TimeRatingLostAmount), "time rating loss");
        Equal(2, days.Sum(item => item.GameRatingEarnedAmount), "game rating gain");
        Equal(1, days.Sum(item => item.GameRatingLostAmount), "game rating loss");
        Equal(
            days[0].ProductServiceBonusAmount + days[0].OtherBonusAmount,
            days[0].BonusAmount,
            "combined bonus");
    }

    private static void PaidPopularityIsExact()
    {
        var immediate = new PaymentRecord
        {
            Items = new List<CheckoutItem>
            {
                Product("Pepsi", 2),
                Product("Вода", 1)
            }
        };
        var sessionPayment = new PaymentRecord
        {
            GameSessionId = Guid.NewGuid(),
            Items = new List<CheckoutItem> { Product("Coca-Cola", 4) }
        };
        var paidAttached = NewSaleLine("Арген", true, "Coca-Cola", 4);
        var unpaidAttached = NewSaleLine("Арген", false, "Pepsi", 20);
        var legacyPaid = NewSaleLine("Арген", true, "Вода", 3);
        legacyPaid.SettlementSchemaVersion = 1;
        legacyPaid.PaymentRecordId = null;
        legacyPaid.PaidAt = null;
        legacyPaid.PaidByEmployeeName = "";

        var quantities = ProductPopularityService.CalculateLifetimePaidQuantities(
            new[] { immediate, sessionPayment },
            new[]
            {
                new GameSessionLogItem
                {
                    SaleLines = new List<GameSessionSaleLine>
                    {
                        paidAttached,
                        unpaidAttached,
                        legacyPaid
                    }
                }
            });

        Equal(4, quantities["Coca-Cola"], "paid attached sale");
        Equal(2, quantities["Pepsi"], "unpaid line excluded");
        Equal(4, quantities["Вода"], "immediate plus compatible legacy history");
    }

    private static void LegacyImmediatePopularityMigratesOnce()
    {
        var payment = new PaymentRecord
        {
            Items = new List<CheckoutItem> { Product("Pepsi", 2) }
        };
        var legacy = new CashRecord
        {
            Category = "Товары и услуги",
            Title = "Pepsi",
            Description = "Товар продан сразу. Количество: 3. Итого: 300 сом.",
            Amount = 300
        };
        var linkedCopy = new CashRecord
        {
            Category = "Товары и услуги",
            Title = "Pepsi",
            Description = "Товар продан сразу. Количество: 2.",
            Amount = 200,
            PaymentRecordId = Guid.NewGuid()
        };

        var quantities = ProductPopularityService.CalculateLifetimePaidQuantities(
            new[] { payment },
            Array.Empty<GameSessionLogItem>(),
            name => name == "Pepsi",
            new[] { legacy, linkedCopy });

        Equal(5, quantities["Pepsi"], "legacy plus new payment");
    }

    private static void StockOrderUsesAvailabilityThenPopularity()
    {
        var popularity = Popularity();
        var ordered = ProductPopularityService.OrderStock(Stock(), popularity)
            .Select(item => item.ProductName)
            .ToArray();

        SequenceEqual(new[] { "Pepsi", "Fanta", "Coca-Cola", "Вода" }, ordered, "stock order");
    }

    private static void PurchaseOrderUsesPopularityOnly()
    {
        var ordered = ProductPopularityService.OrderPurchaseCatalog(Stock(), Popularity())
            .Select(item => item.ProductName)
            .ToArray();

        SequenceEqual(new[] { "Coca-Cola", "Pepsi", "Вода", "Fanta" }, ordered, "purchase order");
    }

    private static void SalesCatalogUsesAvailabilityThenPopularity()
    {
        var items = Stock()
            .Select(item => new SaleItem
            {
                Name = item.ProductName,
                Type = SaleItemType.Product,
                StockQuantity = item.Quantity
            });
        var ordered = ProductPopularityService.OrderSalesCatalog(items, Popularity())
            .Select(item => item.Name)
            .ToArray();

        SequenceEqual(new[] { "Pepsi", "Fanta", "Coca-Cola", "Вода" }, ordered, "sales catalog order");
    }

    private static void FlexibleTimeUsesTariffFormula()
    {
        var tariff = new TariffSettings { OneHourPrice = 120 };
        Equal(150, TariffService.CalculatePriceByMinutes(tariff, 75), "75 minutes");
        Equal(75 * 60, TariffService.CalculateSecondsByAmount(tariff, 150), "150 som");
        Equal(1, TariffService.CalculatePriceBySeconds(tariff, 1), "rounding boundary");
    }

    private static void GamePaymentsUseActualReceivers()
    {
        Guid sessionId = Guid.NewGuid();
        var payments = new[]
        {
            GamePayment(sessionId, "Мирбек", 120, new DateTime(2026, 8, 26, 10, 0, 0)),
            GamePayment(sessionId, "Арген", 100, new DateTime(2026, 8, 26, 11, 0, 0))
        };

        var allocations = GamePaymentAttributionService.Allocate(payments, sessionId, 220);
        Equal(2, allocations.Count, "allocation count");
        Equal("Мирбек", allocations[0].Payment.EmployeeName, "first receiver");
        Equal(120, allocations[0].Amount, "first amount");
        Equal("Арген", allocations[1].Payment.EmployeeName, "second receiver");
        Equal(100, allocations[1].Amount, "second amount");
    }

    private static void RefundConsumesLatestPaymentFirst()
    {
        Guid sessionId = Guid.NewGuid();
        var payments = new[]
        {
            GamePayment(sessionId, "Мирбек", 120, new DateTime(2026, 8, 26, 10, 0, 0)),
            GamePayment(sessionId, "Арген", 100, new DateTime(2026, 8, 26, 11, 0, 0))
        };

        var allocations = GamePaymentAttributionService.Allocate(payments, sessionId, 150);
        Equal(120, allocations[0].Amount, "oldest payment retained");
        Equal(30, allocations[1].Amount, "newest payment partially retained");
    }

    private static void TransferredDebtStaysWithSourceSession()
    {
        Guid sourceSessionId = Guid.NewGuid();
        Guid targetSessionId = Guid.NewGuid();
        var payment = GamePayment(targetSessionId, "Арген", 100, new DateTime(2026, 8, 26, 12, 0, 0));
        payment.Items.Add(new CheckoutItem
        {
            Name = "Перенесённая игра",
            Quantity = 1,
            UnitPrice = 80,
            Category = "Игры",
            SourceGameSessionId = sourceSessionId
        });

        var target = GamePaymentAttributionService.Allocate(new[] { payment }, targetSessionId, 180);
        Equal(100, target.Sum(item => item.Amount), "target own game only");
    }

    private static void LegacyPrepaidStaysFirst()
    {
        Guid sessionId = Guid.NewGuid();
        var session = new GameSessionLogItem
        {
            Id = sessionId,
            IsOpenMode = false,
            PaidAmount = 120
        };
        var addedTime = GamePayment(
            sessionId,
            "Арген",
            100,
            new DateTime(2026, 8, 26, 11, 0, 0));
        addedTime.OperationTitle = "Добавить время";

        int legacy = GamePaymentAttributionService.GetLegacyPrepaidAllocation(
            session,
            new[] { addedTime },
            150);
        var newer = GamePaymentAttributionService.Allocate(
            new[] { addedTime },
            sessionId,
            150 - legacy);

        Equal(120, legacy, "legacy initial tariff");
        Equal(30, newer.Sum(item => item.Amount), "new added time after refund");
    }

    private static void ReassignmentCancelsOriginal()
    {
        var original = OpeningPenalty();
        EmployeeRatingReassignmentService.CancelOriginal(original, "Переназначено владельцем");

        Equal(EmployeeRatingEventStatus.CancelledAsError, original.Status, "original status");
        Equal(original.EffectiveFrom, original.EndedAt!.Value, "original must have no rating effect");
    }

    private static void ReassignmentKeepsFullDuration()
    {
        var original = OpeningPenalty();
        var now = original.EffectiveFrom.AddHours(4);
        var employee = new Employee
        {
            EmployeeId = "employee-2",
            Name = "Арген",
            IsActive = true
        };

        var replacement = EmployeeRatingReassignmentService.CreateReplacement(
            original,
            employee,
            "opening-rating:2026-08-26:reassigned",
            "Переназначено владельцем",
            now,
            100);

        Equal("Арген", replacement.EmployeeName, "new employee");
        Equal(95, replacement.TargetPercent, "equivalent rating effect");
        Equal(TimeSpan.FromHours(12), replacement.ScheduledUntil - replacement.EffectiveFrom, "full duration");
        Equal(now, replacement.EffectiveFrom, "new effect start");
    }

    private static GameSessionSaleLine NewSaleLine(
        string creator,
        bool paid,
        string name = "Вода",
        int quantity = 1)
    {
        var line = new GameSessionSaleLine
        {
            SettlementSchemaVersion = SessionSaleSettlementService.CurrentSchemaVersion,
            EmployeeName = creator,
            CreatedByEmployeeName = creator,
            ItemName = name,
            ItemType = SaleItemType.Product,
            UnitPrice = 100,
            Quantity = quantity,
            TotalAmount = 100 * quantity,
            IsPaid = false
        };

        if (paid)
        {
            SessionSaleSettlementService.MarkPaid(
                line,
                Guid.NewGuid(),
                new DateTime(2026, 8, 26, 12, 0, 0),
                "employee-1",
                creator,
                Guid.NewGuid());
        }

        return line;
    }

    private static CheckoutItem Product(string name, int quantity)
    {
        return new CheckoutItem
        {
            Name = name,
            Quantity = quantity,
            UnitPrice = 100,
            Category = "Товар",
            ItemType = SaleItemType.Product.ToString()
        };
    }

    private static PaymentRecord GamePayment(
        Guid sessionId,
        string employeeName,
        int amount,
        DateTime createdAt)
    {
        return new PaymentRecord
        {
            CreatedAt = createdAt,
            EmployeeName = employeeName,
            GameSessionId = sessionId,
            Items = new List<CheckoutItem>
            {
                new CheckoutItem
                {
                    Name = "Игровое время",
                    Quantity = 1,
                    UnitPrice = amount,
                    Category = "Игры",
                    SourceGameSessionId = sessionId
                }
            },
            TotalAmount = amount,
            CashAmount = amount
        };
    }

    private static IReadOnlyDictionary<string, int> Popularity()
    {
        return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Coca-Cola"] = 40,
            ["Pepsi"] = 30,
            ["Вода"] = 20,
            ["Fanta"] = 10
        };
    }

    private static ProductStockItem[] Stock()
    {
        return new[]
        {
            new ProductStockItem { ProductName = "Coca-Cola", Quantity = 0 },
            new ProductStockItem { ProductName = "Pepsi", Quantity = 10 },
            new ProductStockItem { ProductName = "Вода", Quantity = 0 },
            new ProductStockItem { ProductName = "Fanta", Quantity = 10 }
        };
    }

    private static EmployeeRatingEvent OpeningPenalty()
    {
        var start = new DateTime(2026, 8, 26, 12, 0, 0);
        return new EmployeeRatingEvent
        {
            EmployeeId = "employee-1",
            EmployeeName = "Мирбек",
            Branch = EmployeeRatingBranch.Time,
            RuleCode = "TIME_FIRST_OPEN_LATE",
            RuleVersion = 1,
            Direction = EmployeeRatingEffectDirection.Penalty,
            ChangePercent = 5,
            BasePercentAtCreation = 100,
            SourceId = "opening-rating:2026-08-26",
            SourceType = "FirstClubOpening",
            Title = "Опоздание",
            Description = "Клуб открыт поздно.",
            CreatedAt = start,
            EffectiveFrom = start,
            ScheduledUntil = start.AddHours(12),
            TargetPercent = 95,
            Status = EmployeeRatingEventStatus.Active
        };
    }

    private void Test(string name, Action action)
    {
        try
        {
            action();
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

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{label}: expected {expected}, actual {actual}.");
    }

    private static void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual, string label)
    {
        if (!expected.SequenceEqual(actual))
            throw new InvalidOperationException($"{label}: expected [{string.Join(", ", expected)}], actual [{string.Join(", ", actual)}].");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
