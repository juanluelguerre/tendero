namespace ElGuerre.Tendero.Accounts.Ports;

/// <summary>
/// The customers' inbound port (phase 16, S2b, P16-17): a store a merchant is
/// leaving tells who bought from it — their email, name, addresses and marketing
/// consent as the store recorded it. Each source registers as a keyed service
/// under its name, like the catalogue's and the stock's, and the ImportCustomers
/// slice knows only this interface. No password comes: no platform exports one.
/// </summary>
public interface ICustomerSourceConnector
{
    /// <summary>A stable lowercase identifier: "shopify"…</summary>
    string Source { get; }

    IAsyncEnumerable<SourceCustomer> StreamCustomersAsync(CancellationToken cancellationToken = default);

    /// <summary>What the source could not hand over cleanly, known once the stream has been read.</summary>
    IReadOnlyList<CustomerNote> SourceNotes => [];
}

/// <param name="ExternalId">The source's id for them: what a re-import and their orders find them by.</param>
/// <param name="Culture">The language the store wrote to them in, when it says.</param>
/// <param name="Marketing">Their consent as the store recorded it, with its date; null when it says nothing.</param>
public sealed record SourceCustomer(
    string ExternalId,
    string? Email,
    string? FirstName,
    string? LastName,
    string? Culture,
    SourceConsent? Marketing,
    IReadOnlyList<SourceAddress> Addresses);

public sealed record SourceConsent(bool Given, DateTimeOffset At);

/// <param name="IsDefault">The store's default address, which becomes the delivery default here.</param>
public sealed record SourceAddress(
    string Recipient, string Line1, string? Line2, string City, string? Region, string PostalCode, string CountryCode,
    string? Phone, bool IsDefault);

/// <summary>What did not come across cleanly from a customer source.</summary>
public enum CustomerNoteCode
{
    /// <summary>The store withheld a field the app was not granted (Shopify's protected customer data).</summary>
    FieldRedacted,

    /// <summary>A customer without an email: nobody can sign in as them or be written to, so they were not imported.</summary>
    NoEmail,

    /// <summary>A customer the shop already had, by email: the same person, linked to the store's id and not imported twice.</summary>
    AlreadyHere,

    /// <summary>No identity could be provisioned for them: they came as a guest the shop can still address.</summary>
    IdentityNotProvisioned,

    /// <summary>An address the shop could not read (no street, no city…): the customer came without it.</summary>
    AddressUnreadable,

    /// <summary>A customer erased here asked to be forgotten: the import does not bring them back.</summary>
    Erased,
}

/// <param name="About">The source's id for the customer, or null for the source as a whole.</param>
public sealed record CustomerNote(CustomerNoteCode Code, string? About, string? Detail);

/// <summary>The customer sources registered, by name; the only place that talks to the container.</summary>
public interface ICustomerSourceRegistry
{
    IReadOnlyCollection<string> Sources { get; }

    ICustomerSourceConnector Get(string source);
}
