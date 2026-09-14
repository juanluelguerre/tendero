using ElGuerre.Tendero.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ElGuerre.Tendero.Persistence;

/// <summary>
/// Committing the unit of work goes through TenderoDbContext.SaveChangesAsync,
/// which is where the domain events are drained into the outbox in the same
/// transaction. That is why the port exposes nothing else: there is no way to
/// save without publishing what the aggregate raised.
///
/// A unique index that refuses the write is translated rather than leaked: to a
/// handler it is "somebody else got there first" (<see cref="ConcurrentWriteException"/>),
/// which it can answer, and not an EF exception it must not know about. Two
/// returns racing for the last unit of an order are the first case that needs
/// it (ReturnRequest.Number).
/// </summary>
internal sealed class EfUnitOfWork(TenderoDbContext context) : IUnitOfWork
{
    private const string UniqueViolation = "23505";

    public async Task SaveChangesAsync(CancellationToken ct)
    {
        try
        {
            await context.SaveChangesAsync(ct);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException { SqlState: UniqueViolation } postgres)
        {
            throw new ConcurrentWriteException(postgres.ConstraintName ?? "a unique index", exception);
        }
    }
}
