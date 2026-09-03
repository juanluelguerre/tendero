using ElGuerre.Tendero.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace ElGuerre.Tendero.Persistence;

/// <summary>
/// Writes an audit entry on its OWN context, outside whatever transaction the
/// command used.
///
/// That separate context is the whole design, not an implementation detail. The
/// obvious version — reuse the scoped <c>TenderoDbContext</c> — means the audit
/// row shares the command's transaction, so it disappears exactly when the
/// command fails. The log would then hold every success and no denial, which is
/// the inverse of what it is for: the row you most want is the one whose
/// transaction rolled back.
///
/// The cost is stated rather than hidden. A process that dies between the
/// command committing and this returning loses a row, and there is no outbox
/// behind it. An audit log complete about refusals and occasionally short of a
/// success is the better of the two failures, and a second outbox would buy
/// durability for a table nobody reads in real time.
/// </summary>
internal sealed class EfAuditWriter(IDbContextFactory<TenderoDbContext> contexts) : IAuditWriter
{
    public async Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        await using var context = await contexts.CreateDbContextAsync(cancellationToken);

        context.AuditEntries.Add(entry);
        await context.SaveChangesAsync(cancellationToken);
    }
}
