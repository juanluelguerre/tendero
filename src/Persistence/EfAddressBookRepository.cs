using ElGuerre.Tendero.Accounts.Domain;
using ElGuerre.Tendero.Accounts.Ports;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace ElGuerre.Tendero.Persistence;

/// <summary>A customer's address book as an aggregate (P15-2): what the commands load, change and save.</summary>
internal sealed class EfAddressBookRepository(TenderoDbContext context) : IAddressBookRepository
{
    // WITH tracking: whoever loads a book does it to change it.
    public Task<AddressBook?> FindAsync(CustomerId customer, CancellationToken ct = default) =>
        context.AddressBooks.FirstOrDefaultAsync(book => book.CustomerId == customer, ct);

    public void Add(AddressBook book) => context.AddressBooks.Add(book);
}

/// <summary>A customer's address book as the page and checkout read it: untracked, never saved back.</summary>
internal sealed class EfAddressBookReader(TenderoDbContext context) : IAddressBookReader
{
    public Task<AddressBook?> ForCustomerAsync(CustomerId customer, CancellationToken ct = default) =>
        context.AddressBooks.AsNoTracking().FirstOrDefaultAsync(book => book.CustomerId == customer, ct);
}
