using ElGuerre.Tendero.Accounts.Domain;
using ElGuerre.Tendero.Accounts.Ports;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace ElGuerre.Tendero.Persistence;

/// <summary>
/// The adapter for Accounts' two ports.
///
/// They are one class for the reason the product repository is: two objects over
/// the same DbContext works and lies about how many adapters there are. But the
/// two interfaces stay apart, because they answer to different callers with
/// different rules — the directory is a projection the principal accessor uses
/// on every request and is forbidden from writing.
/// </summary>
internal sealed class EfCustomerRepository(TenderoDbContext context)
    : ICustomerRepository, ICustomerDirectory
{
    // WITH tracking: whoever loads a customer from a slice does it to change one.
    public Task<Customer?> FindByIdAsync(CustomerId id, CancellationToken ct) =>
        context.Customers.FirstOrDefaultAsync(customer => customer.Id == id, ct);

    public Task<Customer?> FindBySubjectAsync(string subject, CancellationToken ct) =>
        context.Customers.FirstOrDefaultAsync(customer => customer.Subject == subject, ct);

    public void Add(Customer customer) => context.Customers.Add(customer);

    /// <summary>
    /// The id and the segment, untracked, on the unique index.
    ///
    /// It runs on every authenticated request, so it projects two columns
    /// rather than materialising an aggregate — and it never creates anything.
    /// A principal accessor that registered an account as a side effect of
    /// answering "who is this" would make a GET a write.
    ///
    /// A SUPERSEDED customer answers with the one that survived it, id and
    /// segment both, so a person who shopped as a guest before signing in keeps
    /// working through whichever id their session happens to hold.
    /// </summary>
    public async Task<KnownCustomer?> ForSubjectAsync(string subject, CancellationToken ct = default)
    {
        var found = await context.Customers
            .AsNoTracking()
            .Where(customer => customer.Subject == subject)
            .Select(customer => new { customer.Id, customer.Segment, customer.SupersededBy })
            .FirstOrDefaultAsync(ct);

        if (found is null)
            return null;

        if (found.SupersededBy is not { } survivor)
            return new KnownCustomer(found.Id, found.Segment);

        var segment = await context.Customers
            .AsNoTracking()
            .Where(customer => customer.Id == survivor)
            .Select(customer => customer.Segment)
            .FirstAsync(ct);

        return new KnownCustomer(survivor, segment);
    }
}
