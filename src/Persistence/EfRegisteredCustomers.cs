using ElGuerre.Tendero.Ordering.Ports;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace ElGuerre.Tendero.Persistence;

/// <summary>
/// Ordering's one question about Accounts, answered from the customers table: a
/// customer has an account when it carries a subject. A guest is a customer
/// with none (P7-1), and a superseded guest never gains one.
/// </summary>
internal sealed class EfRegisteredCustomers(TenderoDbContext context) : IRegisteredCustomers
{
    public Task<bool> HasAccountAsync(CustomerId customer, CancellationToken cancellationToken = default) =>
        context.Customers
            .AsNoTracking()
            .AnyAsync(candidate => candidate.Id == customer && candidate.Subject != null, cancellationToken);
}
