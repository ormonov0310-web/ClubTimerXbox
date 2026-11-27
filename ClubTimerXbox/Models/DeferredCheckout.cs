using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json.Serialization;

namespace ClubTimerXbox.Models;

internal sealed record CheckoutActor(string EmployeeId, Guid ShiftId)
{
    [JsonIgnore]
    public bool IsSpecified => !string.IsNullOrWhiteSpace(EmployeeId) && ShiftId != Guid.Empty;
}

internal sealed record DeferredCheckoutLine(
    Guid SourceGameSessionId,
    Guid? SourceSaleLineId,
    string SourcePlaceName,
    string Name,
    int Quantity,
    int UnitPrice,
    int PurchasePrice,
    string Category,
    string ItemType,
    string CreatedByEmployeeName = "",
    DateTime? SourceCreatedAt = null)
{
    public static DeferredCheckoutLine Freeze(CheckoutItem item) => new(
        item.SourceGameSessionId ?? Guid.Empty, item.SourceSaleLineId, item.SourcePlaceName,
        item.Name, item.Quantity, item.UnitPrice, item.PurchasePrice, item.Category, item.ItemType,
        item.CreatedByEmployeeName, item.SourceCreatedAt);

    public CheckoutItem ToCheckoutItem() => new()
    {
        SourceGameSessionId = SourceGameSessionId,
        SourceSaleLineId = SourceSaleLineId,
        SourcePlaceName = SourcePlaceName,
        Name = Name,
        Quantity = Quantity,
        UnitPrice = UnitPrice,
        PurchasePrice = PurchasePrice,
        Category = Category,
        ItemType = ItemType,
        CreatedByEmployeeName = CreatedByEmployeeName,
        SourceCreatedAt = SourceCreatedAt
    };
}

internal sealed record CheckoutResponsibility(CheckoutActor Actor, DateTimeOffset AcceptedAt);

internal sealed record DeferredPaymentIntent(
    Guid PaymentId,
    CheckoutActor Actor,
    DateTimeOffset AcceptedAt,
    int CashAmount,
    int MBankAmount);

internal sealed record DeferredCheckout(
    Guid Id,
    Guid GameSessionId,
    string PlaceName,
    long StopNumber,
    DateTimeOffset StoppedAt,
    CheckoutActor StoppedBy,
    ImmutableArray<DeferredCheckoutLine> Lines,
    ImmutableArray<CheckoutResponsibility> Handovers,
    long Revision,
    DeferredPaymentIntent? PaymentIntent,
    bool IsPaid)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? DeferredAt { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? RemindedAt { get; init; }

    public bool IsReminderDue(DateTimeOffset now) =>
        !IsPaid && PaymentIntent is null && RemindedAt is null &&
        now - (DeferredAt ?? StoppedAt) >= TimeSpan.FromMinutes(15);

    [JsonIgnore]
    public int Total => checked((int)Lines.Sum(line => (long)line.Quantity * line.UnitPrice));

    [JsonIgnore]
    public CheckoutActor Responsible => Handovers.IsEmpty ? StoppedBy : Handovers[^1].Actor;
}
