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
