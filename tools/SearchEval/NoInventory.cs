using ElGuerre.Tendero.Inventory.Ports;

namespace ElGuerre.Tendero.SearchEval;

/// <summary>
/// The relevance corpus has no warehouses, and says so out loud.
///
/// The indexer asks how much of each SKU can be sold so the document can carry
/// <c>inStock</c>. This gate measures ranking against an annotated golden set,
/// and neither the annotations nor the query mention availability — so the
/// honest answer here is "no inventory", not a number invented to make the
/// field look populated.
///
/// It exists as a named type rather than as a missing registration because the
/// missing registration is what broke the gate: the indexer grew the dependency
/// in phase 4, the tool composes its own container, and nothing failed until
/// somebody ran it. A fake with a name is a decision; an absent one is an
/// oversight waiting to be repeated.
/// </summary>
internal sealed class NoInventory : IAvailabilityReader
{
    public Task<IReadOnlyDictionary<string, int>> AvailableAsync(
        IReadOnlyCollection<string> skus, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<string, int>>(
            new Dictionary<string, int>(StringComparer.Ordinal));
}
