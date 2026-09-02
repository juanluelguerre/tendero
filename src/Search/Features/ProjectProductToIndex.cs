using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Search.Contracts;
using Microsoft.Extensions.Logging;

namespace ElGuerre.Tendero.Search.Features.ProjectProductToIndex;

// These handlers are invoked by the Outbox processor (the background worker),
// NOT by the HTTP request: the import finishes quickly and indexing follows
// behind it, with retries. Eventual consistency, assumed and measurable (outbox
// lag as a metric in Grafana).

// IProductReader lives in Contracts.cs: ReindexProducts needs the same port, and
// a slice cannot reference another slice.

public sealed class ProjectProductOnUpserted(
    IProductReader products,
    IProductIndexer indexer,
    ILogger<ProjectProductOnUpserted> logger) : IDomainEventHandler<ProductUpserted>
{
    public async Task HandleAsync(ProductUpserted domainEvent, CancellationToken ct)
    {
        var product = await products.GetByIdAsync(domainEvent.ProductId, ct);
        if (product is null)
        {
            logger.LogWarning("Product {ProductId} not found while projecting to index", domainEvent.ProductId);
            return;
        }

        // Only what is active is searchable; a Draft that has not passed review
        // is not indexed, and if it was indexed and went back to Draft, it is
        // removed. The rule is shared with ReindexProducts: see
        // ProductIndexProjection.
        await ProductIndexProjection.ApplyAsync(indexer, product, ct);
    }
}

public sealed class RemoveProductOnArchived(
    IProductIndexer indexer) : IDomainEventHandler<ProductArchived>
{
    public Task HandleAsync(ProductArchived domainEvent, CancellationToken ct) =>
        indexer.RemoveAsync(domainEvent.ProductId, ct);
}
