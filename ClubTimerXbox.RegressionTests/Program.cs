using System.Text.Json;
using ClubTimerXbox.Models;
using ClubTimerXbox.Services;

string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
string testRoot = Path.Combine(appData, "ClubTimerXbox");
string reconciliationPath = Path.Combine(testRoot, "cash_reconciliation.json");
Directory.CreateDirectory(testRoot);

var rawLossId = Guid.NewGuid();
var verificationId = Guid.NewGuid();
var now = DateTime.Now;
var initialItems = new List<CashReconciliationItem>
{
    new()
    {
        Id = rawLossId,
        CreatedAt = now.AddMinutes(-2),
        Kind = CashReconciliationKind.CashlessShortage,
        Status = CashReconciliationStatus.Resolved,
        Origin = CashReconciliationOrigin.Unknown,
        Amount = 0,
        OriginalAmount = 250,
        ResolvedAmount = 250,
        ExpectedAmount = 250,
        ActualAmount = 0,
        SuspectedEmployeeName = "Сталбек",
        Title = "Сырые потери",
        Note = "Итоговая сырая корректировка после баланса.",
        ResolvedAt = now,
        ResolvedBy = "Система",
        ResolutionNote =
            "Закрыто новой полной сверкой безнала. Актуальная разница записана отдельной карточкой."
    },
    new()
    {
        Id = verificationId,
        CreatedAt = now.AddMinutes(-1),
        Kind = CashReconciliationKind.CashlessShortage,
        Status = CashReconciliationStatus.Open,
        Origin = CashReconciliationOrigin.Unknown,
        Amount = 10,
        OriginalAmount = 10,
        ExpectedAmount = 100,
        ActualAmount = 90,
        Title = "Недостача безнала",
        Note = "Обычная проверка безнала."
    }
};

File.WriteAllText(
    reconciliationPath,
    JsonSerializer.Serialize(initialItems, new JsonSerializerOptions { WriteIndented = true }));

bool reopened = CashReconciliationService.TryReopenKnownSupersededRawDifference(
    rawLossId,
    expectedOriginalAmount: 250,
    suspectedEmployeeName: "Сталбек");

Assert(reopened, "Ошибочно закрытая сырая потеря должна восстановиться.");
Assert(
    CashReconciliationService.Items.Single(item => item.Id == rawLossId).Origin ==
        CashReconciliationOrigin.BalanceRawDifference,
    "Старая сырая карточка должна получить правильное происхождение при миграции.");
Assert(
    CashReconciliationService.GetOpenSmallCashlessShortages(
        now.Date,
        now.Date.AddDays(1)).All(item => item.Id != rawLossId),
    "Сырая потеря не должна считаться маленькой ошибкой типа оплаты.");

int superseded = CashReconciliationService.SupersedeOpenCashlessVerifications(
    now.Date,
    now.Date.AddDays(1),
    "Новая полная сверка безнала.");

Assert(superseded == 1, "Должна закрыться ровно одна проверочная карточка.");

var rawLoss = CashReconciliationService.Items.Single(item => item.Id == rawLossId);
var verification = CashReconciliationService.Items.Single(item => item.Id == verificationId);

Assert(
    rawLoss.Status == CashReconciliationStatus.Open && rawLoss.Amount == 250,
    "Сырая потеря 250 сом не должна закрываться новой сверкой.");
Assert(
    verification.Status == CashReconciliationStatus.Resolved,
    "Старая проверочная карточка должна закрываться новой сверкой.");

var extra = CashReconciliationService.AddCashlessVerification(
    expectedAmount: 100,
    actualAmount: 102,
    amount: 2,
    status: CashReconciliationStatus.Open,
    note: "Новый излишек 2 сом.");

int netted = CashReconciliationService.NetOpenMoneyCorrections(
    now.Date,
    now.Date.AddDays(1),
    "Система",
    "Регрессионная проверка взаимного зачёта.");

Assert(netted == 2, "Должны взаимно зачесться только 2 сом.");
Assert(rawLoss.Amount == 248, "После зачёта должна остаться потеря 248 сом.");
Assert(
    extra.Status == CashReconciliationStatus.Resolved && extra.Amount == 0,
    "Излишек 2 сом должен быть полностью использован.");

Console.WriteLine("PASS: raw loss 250 survives supersede and becomes 248 after netting extra 2.");

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
