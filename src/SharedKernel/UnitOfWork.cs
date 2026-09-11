namespace ElGuerre.Tendero.SharedKernel;

/// <summary>
/// Commits the unit of work. Pending domain events travel to the outbox table in
/// THIS same transaction (invariant 7): whoever does not call here has published
/// nothing.
///
/// It lived in <c>Catalog.Ports</c> while the catalogue was its only user, and
/// moved here when Inventory became the third context that had to save something
/// — the repository's own rule, which already moved <c>IProductRepository</c>
/// out of a slice and the culture-negotiation chain out of two endpoints: what a
/// second consumer needs goes down or out, never sideways.
///
/// It belongs in the SharedKernel rather than in a context because a unit of
/// work is not a catalogue concept, an inventory concept or an ordering one. It
/// is the transaction, and there is exactly one.
/// </summary>
public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken ct);
}

/// <summary>
/// Another writer got there first: a unique index refused this unit of work
/// because a concurrent one already committed the same claim.
///
/// It lives here, beside the port, so a handler can answer it — "look again
/// and retry" — without knowing that the store is Postgres or that EF is the
/// one who noticed. The persistence adapter is what translates to it.
/// </summary>
public sealed class ConcurrentWriteException(string constraint, Exception inner)
    : Exception($"Another change reached '{constraint}' first.", inner)
{
    public string Constraint { get; } = constraint;
}
