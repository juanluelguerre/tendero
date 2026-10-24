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
    : ICustomerRepository, ICustomerDirectory, IImportedCustomers
{
    /// <summary>Tracked, like the other finds: the import links or leaves who it finds (phase 16, S2b).</summary>
    public Task<Customer?> FindByOriginAsync(CustomerOrigin origin, CancellationToken ct) =>
        context.Customers.FirstOrDefaultAsync(
            customer => customer.ImportedFrom != null
                && customer.ImportedFrom.Source == origin.Source
                && customer.ImportedFrom.ExternalId == origin.ExternalId,
            ct);

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
    /// No superseded customer can answer here, and the aggregate is why: only a
    /// guest — a customer with no subject — can be superseded, and a superseded
    /// customer can never link one. This used to follow <c>SupersededBy</c> to
    /// a survivor anyway, in a branch no row could ever reach.
    /// </summary>
    public Task<KnownCustomer?> ForSubjectAsync(string subject, CancellationToken ct = default) =>
        context.Customers
            .AsNoTracking()
            .Where(customer => customer.Subject == subject)
            .Select(customer => new KnownCustomer(customer.Id, customer.Segment))
            .FirstOrDefaultAsync(ct);
}
