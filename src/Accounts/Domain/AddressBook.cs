using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Accounts.Domain;

/// <summary>An entry's identity inside its book: what the page names when it edits, removes or chooses one.</summary>
public readonly record struct AddressEntryId(Guid Value)
{
    public static AddressEntryId New() => new(Guid.CreateVersion7());
    public override string ToString() => Value.ToString();
}

/// <summary>What a default is for. A closed set (ADR 0043).</summary>
public enum AddressUse
{
    Delivery,
    Billing,
}

/// <summary>
/// One address the shopper keeps: the kernel's <see cref="SharedKernel.Address"/>,
/// unchanged, and a label in the shopper's own words (*Home*, *Work*, *Mum's*).
/// The label is their data, not the shop's voice, so it is a plain string and
/// not <c>LocalizedText</c>: invariant 6 is about what the shop says.
/// </summary>
public sealed record AddressEntry(AddressEntryId Id, string Label, Address Address);

/// <summary>
/// A signed-in customer's address book (P15-2, docs/specs/shopper-account.md).
///
/// **Its own aggregate, keyed by the customer**, as <see cref="BrowsingHistory"/>
/// is: its invariant — one default of each kind, a ceiling — is its own, and
/// <c>Customer</c> is read on every authenticated request, which an address book
/// inside it would make heavier for nothing.
///
/// **A default removed leaves none.** Another entry is never promoted: a default
/// applied without being shown is an order sent to the wrong house, so the next
/// checkout asks. The first entry is not made a default by itself either, for the
/// same reason — a default is something the shopper chose.
///
/// **An edit changes the entry, never an order.** An order froze the
/// <see cref="SharedKernel.Address"/> it was sent to, a value (ADR 0002); the
/// book replaces its entry's value and the order keeps its own.
/// </summary>
public sealed class AddressBook : AggregateRoot
{
    /// <summary>Laboratory scale, and a guard on the endpoint, not a number anyone will reach.</summary>
    public const int MaxEntries = 10;

    /// <summary>Room for *Casa de mis padres en el pueblo*, and not for a paragraph.</summary>
    public const int MaxLabelLength = 40;

    private List<AddressEntry> entries = [];

    public CustomerId CustomerId { get; private set; }

    /// <summary>Oldest first: the order the shopper added them in, which is the order they find them in.</summary>
    public IReadOnlyList<AddressEntry> Entries => this.entries;

    public AddressEntryId? DeliveryDefault { get; private set; }

    public AddressEntryId? BillingDefault { get; private set; }

    public bool IsFull => this.entries.Count >= MaxEntries;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    private AddressBook() { } // EF Core

    /// <summary>A customer's book before anything was kept: empty, with no defaults.</summary>
    public static AddressBook StartFor(CustomerId customer, TimeProvider clock)
    {
        var now = clock.GetUtcNow();
        return new AddressBook { CustomerId = customer, CreatedAt = now, UpdatedAt = now };
    }

    /// <summary>
    /// A new entry, last. Throws when the book is full — a handler refuses before
    /// it gets here (<see cref="IsFull"/>), so reaching it is a bug, not a request.
    /// </summary>
    public AddressEntry Add(TimeProvider clock, string label, Address address, bool delivery = false, bool billing = false)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (IsFull)
            throw new InvalidOperationException($"An address book holds {MaxEntries} entries at most.");

        var entry = new AddressEntry(AddressEntryId.New(), Label(label), address);
        this.entries.Add(entry);
        Choose(entry.Id, delivery, billing);
        UpdatedAt = clock.GetUtcNow();

        return entry;
    }

    /// <summary>
    /// The entry's label and address replaced, and its two defaults set as the form
    /// says: a box unticked on the current default leaves none of that kind. False
    /// when there is no such entry.
    /// </summary>
    public bool Edit(TimeProvider clock, AddressEntryId id, string label, Address address, bool delivery, bool billing)
    {
        ArgumentNullException.ThrowIfNull(address);

        var at = this.entries.FindIndex(entry => entry.Id == id);
        if (at < 0)
            return false;

        this.entries[at] = this.entries[at] with { Label = Label(label), Address = address };
        Choose(id, delivery, billing);
        UpdatedAt = clock.GetUtcNow();

        return true;
    }

    /// <summary>The entry made the default for <paramref name="use"/>, replacing whichever was. False when there is no such entry.</summary>
    public bool UseFor(TimeProvider clock, AddressEntryId id, AddressUse use)
    {
        if (!Holds(id))
            return false;

        if (use == AddressUse.Delivery)
            DeliveryDefault = id;
        else
            BillingDefault = id;

        UpdatedAt = clock.GetUtcNow();
        return true;
    }

    /// <summary>The entry removed, and with it any default it was; nothing takes its place. False when there was none.</summary>
    public bool Remove(TimeProvider clock, AddressEntryId id)
    {
        if (this.entries.RemoveAll(entry => entry.Id == id) == 0)
            return false;

        if (DeliveryDefault == id)
            DeliveryDefault = null;
        if (BillingDefault == id)
            BillingDefault = null;

        UpdatedAt = clock.GetUtcNow();
        return true;
    }

    private bool Holds(AddressEntryId id) => this.entries.Exists(entry => entry.Id == id);

    private void Choose(AddressEntryId id, bool delivery, bool billing)
    {
        if (delivery)
            DeliveryDefault = id;
        else if (DeliveryDefault == id)
            DeliveryDefault = null;

        if (billing)
            BillingDefault = id;
        else if (BillingDefault == id)
            BillingDefault = null;
    }

    private static string Label(string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);

        var trimmed = label.Trim();
        return trimmed.Length <= MaxLabelLength
            ? trimmed
            : throw new ArgumentException($"A label is {MaxLabelLength} characters at most.", nameof(label));
    }
}
