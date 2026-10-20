using ElGuerre.Tendero.Accounts.Domain;
using ElGuerre.Tendero.Accounts.Ports;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Accounts.PersonalData;

/// <summary>
/// The address book's share of what the shop holds about a person (P15-9): every
/// entry as the shopper sees it, and — when the account goes — forgotten whole.
/// Orders keep the addresses they froze; that copy is the order's, not the book's.
/// </summary>
public sealed class AddressBookPersonalData(IAddressBookRepository books) : IPersonalDataHolder
{
    public string Section => "addresses";

    public async Task<object?> ExportAsync(CustomerId customer, CancellationToken cancellationToken = default) =>
        await books.FindAsync(customer, cancellationToken) is { } book
            ? book.Entries.Select(entry => new
            {
                entry.Label,
                entry.Address,
                DeliveryDefault = book.DeliveryDefault == entry.Id,
                BillingDefault = book.BillingDefault == entry.Id,
            }).ToList()
            : null;

    public async Task<Erasure> EraseAsync(CustomerId customer, CancellationToken cancellationToken = default)
    {
        if (await books.FindAsync(customer, cancellationToken) is not { } book)
            return new Erasure(Section, Forgotten: false, Kept: false);

        books.Remove(book);
        return new Erasure(Section, Forgotten: true, Kept: false);
    }
}

/// <summary>The trail's share (P15-9): what was looked at and searched, and whether it was on; forgotten whole.</summary>
public sealed class BrowsingHistoryPersonalData(IBrowsingHistoryRepository histories, TimeProvider clock) : IPersonalDataHolder
{
    public string Section => "browsingHistory";

    public async Task<object?> ExportAsync(CustomerId customer, CancellationToken cancellationToken = default)
    {
        if (await histories.FindAsync(customer, cancellationToken) is not { } history)
            return null;

        var trail = BrowsingTrail.Of(history, clock.GetUtcNow());
        return new { Enabled = trail.IsEnabled, Viewed = trail.Views, Searched = trail.Searches };
    }

    public async Task<Erasure> EraseAsync(CustomerId customer, CancellationToken cancellationToken = default)
    {
        if (await histories.FindAsync(customer, cancellationToken) is not { } history)
            return new Erasure(Section, Forgotten: false, Kept: false);

        histories.Remove(history);
        return new Erasure(Section, Forgotten: true, Kept: false);
    }
}
