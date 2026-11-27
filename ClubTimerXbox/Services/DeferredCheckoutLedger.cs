using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services;

internal sealed class DeferredCheckoutLedger(string filePath, string clubId)
{
    private const int SchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        RespectRequiredConstructorParameters = true
    };

    private readonly string _filePath = Path.GetFullPath(filePath);
    private readonly string _clubId = !string.IsNullOrWhiteSpace(clubId)
        ? clubId
        : throw new ArgumentException("A club ID is required.", nameof(clubId));

    public ImmutableArray<DeferredCheckout> GetOutstanding() => Outstanding(Load().Checkouts);

    private static ImmutableArray<DeferredCheckout> Outstanding(ImmutableArray<DeferredCheckout> entries) =>
        entries.Where(entry => !entry.IsPaid)
            .OrderBy(entry => entry.StoppedAt).ThenBy(entry => entry.StopNumber).ToImmutableArray();

    public DeferredCheckout? Find(Guid id) => Load().Checkouts.FirstOrDefault(entry => entry.Id == id);

    public static DeferredCheckoutLedger ForCurrentClub()
    {
        var identity = PcIdentityService.Current;
        return new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ClubTimerXbox", "deferred_checkouts.json"),
            string.IsNullOrWhiteSpace(identity.ClubId) ? identity.InstallationId : identity.ClubId);
    }

    public DeferredCheckout Defer(
        Guid id, Guid gameSessionId, string placeName, DateTimeOffset stoppedAt, CheckoutActor stoppedBy,
        bool isOpenMode, IEnumerable<DeferredCheckoutLine> lines, DateTimeOffset? deferredAt = null)
    {
        if (!isOpenMode)
            throw new InvalidOperationException("Only open-mode stops can defer their checkout.");

        placeName = placeName.Trim();
        var frozenLines = lines.ToImmutableArray();
        return Update(id, (current, all) =>
        {
            if (current is not null)
            {
                if (current.GameSessionId != gameSessionId || !SamePlace(current.PlaceName, placeName) ||
                    current.StoppedAt != stoppedAt || current.StoppedBy != stoppedBy ||
                    !current.Lines.SequenceEqual(frozenLines))
                    throw new InvalidOperationException("Deferred checkout ID is already in use.");

                return current;
            }

            if (all.Any(entry => entry.GameSessionId == gameSessionId))
                throw new InvalidOperationException("This stopped game already has a checkout.");

            long previousNumber = all.Where(entry => SamePlace(entry.PlaceName, placeName))
                .Select(entry => entry.StopNumber).DefaultIfEmpty(-1).Max();

            return new DeferredCheckout(id, gameSessionId, placeName, checked(previousNumber + 1),
                stoppedAt, stoppedBy, frozenLines, [], 0, null, false)
            {
                DeferredAt = deferredAt is { } confirmedAt && confirmedAt < stoppedAt ? stoppedAt : deferredAt
            };
        });
    }

    public (ImmutableArray<DeferredCheckout> Outstanding, ImmutableArray<DeferredCheckout> Reminded)
        TakeDueReminders(IReadOnlySet<Guid> candidates, DateTimeOffset now)
    {
        using var writeLock = OpenWriteLock();
        var file = Load();
        var updated = file.Checkouts.ToBuilder();
        var reminded = ImmutableArray.CreateBuilder<DeferredCheckout>();

        for (int index = 0; index < updated.Count; index++)
        {
            var entry = updated[index];
            if (!candidates.Contains(entry.Id) || !entry.IsReminderDue(now))
                continue;

            // This flag does not change the amount, responsible shift or payment revision.
            var marked = entry with { RemindedAt = now };
            updated[index] = marked;
            reminded.Add(marked);
        }

        if (reminded.Count == 0)
            return (Outstanding(file.Checkouts), []);

        var next = updated.ToImmutable();
        Save(file with { Checkouts = next });
        return (Outstanding(next), reminded.ToImmutable());
    }

    public DeferredCheckout AcceptResponsibility(
        Guid id, long expectedRevision, CheckoutActor actor, DateTimeOffset acceptedAt) =>
        Update(id, (current, _) =>
        {
            var entry = RequireExisting(current);
            var acceptance = new CheckoutResponsibility(actor, acceptedAt);
            if (!entry.Handovers.IsEmpty && entry.Handovers[^1] == acceptance)
                return entry;

            RequireEditable(entry, expectedRevision);
            return entry with { Handovers = entry.Handovers.Add(acceptance), Revision = checked(entry.Revision + 1) };
        });

    public int AcceptAllResponsibility(ImmutableArray<DeferredCheckout> reviewed, CheckoutActor actor,
        DateTimeOffset acceptedAt)
    {
        using var writeLock = OpenWriteLock();
        var file = Load();
        var pending = file.Checkouts.Where(entry => !entry.IsPaid).ToArray();
        if (!actor.IsSpecified || pending.Length != reviewed.Length ||
            pending.Any(entry => entry.PaymentIntent is not null ||
                !reviewed.Any(old => old.Id == entry.Id && old.Revision == entry.Revision)))
            throw new InvalidOperationException("Pending checkouts changed during acceptance.");

        var updated = file.Checkouts.ToBuilder();
        int count = 0;
        for (int index = 0; index < updated.Count; index++)
        {
            var entry = updated[index];
            if (entry.IsPaid || !entry.Handovers.IsEmpty && entry.Handovers[^1].Actor == actor)
                continue;
            updated[index] = entry with
            {
                Handovers = entry.Handovers.Add(new(actor, acceptedAt)),
                Revision = checked(entry.Revision + 1)
            };
            count++;
        }
        if (count > 0)
            Save(file with { Checkouts = updated.ToImmutable() });
        return count;
    }

    public DeferredCheckout PreparePayment(
        Guid id, long expectedRevision, CheckoutActor reviewedBy, CheckoutActor currentActor,
        DeferredPaymentIntent intent)
    {
        if (!currentActor.IsSpecified || currentActor != reviewedBy || intent.Actor != currentActor)
            throw new InvalidOperationException("The employee or shift changed while checkout was open.");

        return Update(id, (current, _) =>
        {
            var entry = RequireExisting(current);
            if (entry.PaymentIntent == intent)
                return entry;

            RequireEditable(entry, expectedRevision);
            return entry with { PaymentIntent = intent, Revision = checked(entry.Revision + 1) };
        });
    }

    // The caller must first verify that the receipt and all linked postings have been durably saved.
    public DeferredCheckout ConfirmPersistedPayment(Guid id, DeferredPaymentIntent persistedIntent) =>
        Update(id, (current, _) =>
        {
            var entry = RequireExisting(current);
            if (entry.PaymentIntent is null || entry.PaymentIntent != persistedIntent)
                throw new InvalidOperationException("The persisted payment does not match the reserved checkout.");

            return entry.IsPaid ? entry : entry with { IsPaid = true, Revision = checked(entry.Revision + 1) };
        });

    private DeferredCheckout Update(
        Guid id, Func<DeferredCheckout?, ImmutableArray<DeferredCheckout>, DeferredCheckout> change)
    {
        using var writeLock = OpenWriteLock();
        var file = Load();
        var current = file.Checkouts.FirstOrDefault(entry => entry.Id == id);
        var next = change(current, file.Checkouts);
        if (ReferenceEquals(current, next))
            return next;

        var updated = current is null ? file.Checkouts.Add(next) : file.Checkouts.Replace(current, next);
        Save(file with { Checkouts = updated });
        return next;
    }

    private FileStream OpenWriteLock()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        return new FileStream(_filePath + ".lock", FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
    }

    private void Save(LedgerFile file)
    {
        Validate(file.Checkouts);
        SessionStorageVersion.RequireFor(_filePath);
        AtomicFileStorageService.WriteAllText(_filePath, JsonSerializer.Serialize(file, JsonOptions), flushToDisk: true);
    }

    private LedgerFile Load()
    {
        string json;
        try
        {
            json = File.ReadAllText(_filePath);
        }
        catch (FileNotFoundException)
        {
            return new(SchemaVersion, _clubId, []);
        }
        catch (DirectoryNotFoundException)
        {
            return new(SchemaVersion, _clubId, []);
        }

        LedgerFile? file;
        try
        {
            file = JsonSerializer.Deserialize<LedgerFile>(json, JsonOptions);
        }
        catch (JsonException error)
        {
            throw new InvalidDataException("Cannot read the deferred-checkout ledger.", error);
        }

        if (file is null || file.Version != SchemaVersion || file.ClubId != _clubId || file.Checkouts.IsDefault)
            throw new InvalidDataException("Invalid deferred-checkout ledger schema or club.");

        Validate(file.Checkouts);
        return file;
    }

    private static void Validate(ImmutableArray<DeferredCheckout> entries)
    {
        var ids = new HashSet<Guid>();
        var games = new HashSet<Guid>();
        var places = new HashSet<(string, long)>();
        var sales = new HashSet<Guid>();
        var gameCharges = new HashSet<Guid>();
        var payments = new HashSet<Guid>();
        foreach (var entry in entries)
        {
            if (entry is null || entry.Id == Guid.Empty || entry.GameSessionId == Guid.Empty ||
                !ids.Add(entry.Id) || !games.Add(entry.GameSessionId) ||
                string.IsNullOrWhiteSpace(entry.PlaceName) || entry.PlaceName != entry.PlaceName.Trim() ||
                entry.StopNumber < 0 || !places.Add((entry.PlaceName.ToUpperInvariant(), entry.StopNumber)) ||
                entry.StoppedAt == default || entry.StoppedBy?.IsSpecified != true ||
                entry.DeferredAt < entry.StoppedAt || entry.RemindedAt < (entry.DeferredAt ?? entry.StoppedAt) ||
                entry.Lines.IsDefaultOrEmpty || entry.Handovers.IsDefault)
                throw new InvalidDataException("Invalid or duplicate deferred checkout.");

            long total = 0;
            foreach (var line in entry.Lines)
            {
                if (line is null || line.SourceGameSessionId == Guid.Empty || string.IsNullOrWhiteSpace(line.SourcePlaceName) ||
                    string.IsNullOrWhiteSpace(line.Name) || line.Quantity <= 0 || line.UnitPrice < 0 ||
                    line.PurchasePrice < 0 || line.Category is null || line.ItemType is null ||
                    (line.SourceSaleLineId is { } saleId
                        ? saleId == Guid.Empty || !sales.Add(saleId)
                        : !gameCharges.Add(line.SourceGameSessionId)))
                    throw new InvalidDataException("Invalid or duplicate deferred checkout line.");

                total += (long)line.Quantity * line.UnitPrice;
                if (total > int.MaxValue)
                    throw new InvalidDataException("Deferred checkout total is out of range.");
            }

            var lastEventAt = entry.StoppedAt;
            foreach (var handover in entry.Handovers)
            {
                if (handover?.Actor?.IsSpecified != true || handover.AcceptedAt < lastEventAt)
                    throw new InvalidDataException("Invalid checkout responsibility timeline.");
                lastEventAt = handover.AcceptedAt;
            }

            if (total <= 0 || (entry.IsPaid && entry.PaymentIntent is null) ||
                entry.Revision != entry.Handovers.Length + (entry.PaymentIntent is null ? 0L : 1L) + (entry.IsPaid ? 1L : 0L))
                throw new InvalidDataException("Invalid deferred checkout state.");

            if (entry.PaymentIntent is { } payment &&
                (payment.PaymentId == Guid.Empty || !payments.Add(payment.PaymentId) ||
                    payment.Actor?.IsSpecified != true || payment.AcceptedAt < lastEventAt ||
                    payment.CashAmount < 0 || payment.MBankAmount < 0 || (long)payment.CashAmount + payment.MBankAmount != total))
                throw new InvalidDataException("Invalid or duplicate deferred payment intent.");
        }
    }

    private static bool SamePlace(string left, string right) =>
        left.Equals(right, StringComparison.OrdinalIgnoreCase);

    private static DeferredCheckout RequireExisting(DeferredCheckout? checkout) =>
        checkout ?? throw new InvalidOperationException("Deferred checkout no longer exists.");

    private static void RequireEditable(DeferredCheckout entry, long expectedRevision)
    {
        if (entry.Revision != expectedRevision || entry.PaymentIntent is not null || entry.IsPaid)
            throw new InvalidOperationException("Deferred checkout changed or its payment is already reserved.");
    }

    private sealed record LedgerFile(int Version, string ClubId, ImmutableArray<DeferredCheckout> Checkouts);
}
