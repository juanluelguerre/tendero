using ElGuerre.Tendero.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ElGuerre.Tendero.Persistence;

/// <summary>
/// The idempotency keys on Postgres, on their OWN context — the audit writer's
/// reason, the other way round. A claim has to be visible to a second request
/// before the first one's transaction commits, or two requests racing on one
/// key both find it free; and an answer has to be kept whether or not the
/// request's own unit of work is still open.
///
/// The claim is the primary key: the insert that wins is the request that runs,
/// and the database is what decides, not a read followed by a write.
/// </summary>
internal sealed class EfIdempotencyStore(IDbContextFactory<TenderoDbContext> contexts, TimeProvider clock) : IIdempotencyStore
{
    public async Task<IdempotencyClaim> ClaimAsync(string scope, string key, string fingerprint, CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();

        await using (var claiming = await contexts.CreateDbContextAsync(cancellationToken))
        {
            claiming.IdempotencyRecords.Add(new IdempotencyRecord { Scope = scope, Key = key, Fingerprint = fingerprint, At = now });
            try
            {
                await claiming.SaveChangesAsync(cancellationToken);
                return new IdempotencyClaim.Fresh();
            }
            catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // Somebody has the key: what it is depends on what they did with it.
            }
        }

        await using var context = await contexts.CreateDbContextAsync(cancellationToken);
        var known = await context.IdempotencyRecords.AsNoTracking()
            .SingleOrDefaultAsync(record => record.Scope == scope && record.Key == key, cancellationToken);

        // Released between the insert and the read: the key is free, so claim it.
        if (known is null)
            return await ClaimAsync(scope, key, fingerprint, cancellationToken);

        if (known.Fingerprint != fingerprint)
            return new IdempotencyClaim.Mismatched();
        if (known.Status is { } status)
            return new IdempotencyClaim.Replayed(status, known.Body!);
        if (now - known.At < Idempotency.Lease)
            return new IdempotencyClaim.InFlight();

        // A claim nobody finished, past its lease. Taken over only if it is
        // still the claim that was read: of two takers, the update that finds
        // the old instant is the one that runs.
        var taken = await context.IdempotencyRecords
            .Where(record => record.Scope == scope && record.Key == key && record.Status == null && record.At == known.At)
            .ExecuteUpdateAsync(set => set.SetProperty(record => record.At, now), cancellationToken);

        return taken == 1 ? new IdempotencyClaim.Fresh() : new IdempotencyClaim.InFlight();
    }

    public async Task CompleteAsync(string scope, string key, int status, string body, CancellationToken cancellationToken = default)
    {
        await using var context = await contexts.CreateDbContextAsync(cancellationToken);

        await context.IdempotencyRecords
            .Where(record => record.Scope == scope && record.Key == key)
            .ExecuteUpdateAsync(set => set
                .SetProperty(record => record.Status, status)
                .SetProperty(record => record.Body, body), cancellationToken);
    }

    /// <summary>Only a claim is released: an answer, once kept, is what the key means.</summary>
    public async Task ReleaseAsync(string scope, string key, CancellationToken cancellationToken = default)
    {
        await using var context = await contexts.CreateDbContextAsync(cancellationToken);

        await context.IdempotencyRecords
            .Where(record => record.Scope == scope && record.Key == key && record.Status == null)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
