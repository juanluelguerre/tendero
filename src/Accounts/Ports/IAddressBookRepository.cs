using ElGuerre.Tendero.Accounts.Domain;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Accounts.Ports;

/// <summary>A customer's address book as an aggregate, for the commands that change it (P15-2).</summary>
public interface IAddressBookRepository
{
    Task<AddressBook?> FindAsync(CustomerId customer, CancellationToken ct = default);

    void Add(AddressBook book);
}

/// <summary>
/// A customer's address book as the page and checkout read it: untracked, never
/// saved back — a port of its own, as the trail's reader is.
/// </summary>
public interface IAddressBookReader
{
    /// <summary>Null for a customer who never kept an address — empty, as far as anybody can tell.</summary>
    Task<AddressBook?> ForCustomerAsync(CustomerId customer, CancellationToken ct = default);
}
