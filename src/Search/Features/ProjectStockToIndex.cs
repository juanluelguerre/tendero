using ElGuerre.Tendero.Inventory.Domain;
using ElGuerre.Tendero.Search.Contracts;

namespace ElGuerre.Tendero.Search.Features.ProjectStockToIndex;

/// <summary>
/// A stock movement changes the search document.
///
/// The product did not change and neither did its price, but whether it can be
/// bought did — and `inStock` is a field on the document, so somebody has to
/// rewrite it. Without this handler the index would be right at import time and
/// slowly become a catalogue of things that cannot be shipped.
///
/// It reindexes the whole product rather than patching one field: the document
/// is a projection and rebuilding it is the operation the repository already has
/// (ADR 0012). A partial update would be a second way to write a document, and
/// two ways to write one is how they diverge.
///
/// It arrives by the same outbox as everything else, so a shelf count typed into
/// the backoffice and an order placed by an agent reach the index down the same
/// path — which is the property that makes "the index is a disposable
/// projection" true rather than aspirational.
/// </summary>
public sealed class ProjectStockOnLevelChanged(
    IProductReader products, IProductIndexer indexer) : IDomainEventHandler<StockLevelChanged>
{
    public async Task HandleAsync(StockLevelChanged domainEvent, CancellationToken cancellationToken)
    {
        // Inventory speaks in SKUs and knows nothing about products; Search knows
        // both, which is why the lookup lives here and not in a handler inside
        // Inventory. This is ADR 0007's widened rule doing its job.
        var product = await products.FindBySkuAsync(domainEvent.Sku, cancellationToken);

        if (product is null)
            return;

        await ProductIndexProjection.ApplyAsync(indexer, product, cancellationToken);
    }
}
