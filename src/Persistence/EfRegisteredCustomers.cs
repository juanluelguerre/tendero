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

/// <summary>
/// Ordering's other question about Accounts (phase 16, S2b): which customer each of
/// a store's buyers was imported as, by the origin columns Accounts keeps. Erased
/// customers keep their origin and still own their orders, as every erased
/// customer does.
/// </summary>
internal sealed class EfImportedCustomerIds(TenderoDbContext context) : IImportedCustomerIds
{
    public async Task<IReadOnlyDictionary<string, CustomerId>> ForAsync(
        string source, IReadOnlyCollection<string> externalIds, CancellationToken cancellationToken = default)
    {
        var rows = await context.Customers
            .AsNoTracking()
            .Where(customer => customer.ImportedFrom != null
                && customer.ImportedFrom.Source == source
                && externalIds.Contains(customer.ImportedFrom.ExternalId))
            .Select(customer => new { customer.ImportedFrom!.ExternalId, customer.Id })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(row => row.ExternalId, row => row.Id, StringComparer.Ordinal);
    }
}
