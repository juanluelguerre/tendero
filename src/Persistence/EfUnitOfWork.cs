using ElGuerre.Tendero.Catalog.Ports;

namespace ElGuerre.Tendero.Persistence;

/// <summary>
/// Committing the unit of work goes through TenderoDbContext.SaveChangesAsync,
/// which is where the domain events are drained into the outbox in the same
/// transaction. That is why the port exposes nothing else: there is no way to
/// save without publishing what the aggregate raised.
/// </summary>
internal sealed class EfUnitOfWork(TenderoDbContext context) : IUnitOfWork
{
    public Task SaveChangesAsync(CancellationToken ct) => context.SaveChangesAsync(ct);
}
