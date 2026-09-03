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
    /// The id alone, untracked, on the unique index.
    ///
    /// It runs on every authenticated request, so it projects a single column
    /// rather than materialising an aggregate — and it never creates anything.
    /// A principal accessor that registered an account as a side effect of
    /// answering "who is this" would make a GET a write.
    ///
    /// A SUPERSEDED customer answers with the id that survived it, so a person
    /// who shopped as a guest before signing in keeps working through whichever
    /// id their session happens to hold.
    /// </summary>
    public async Task<CustomerId?> ForSubjectAsync(string subject, CancellationToken ct = default)
    {
        var found = await context.Customers
            .AsNoTracking()
            .Where(customer => customer.Subject == subject)
            .Select(customer => new { customer.Id, customer.SupersededBy })
            .FirstOrDefaultAsync(ct);

        return found is null ? null : found.SupersededBy ?? found.Id;
    }
}
