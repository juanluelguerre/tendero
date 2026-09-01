using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Search.Contracts;
using Microsoft.Extensions.Logging;

namespace ElGuerre.Tendero.Search.Features.ProjectProductToIndex;

// Estos handlers los invoca el procesador del Outbox (worker en segundo plano),
// NO el request HTTP: la importación termina rápido y la indexación va detrás,
// con reintentos. Consistencia eventual, asumida y medible (lag del outbox
// como métrica en Grafana).

// IProductReader vive en Contracts.cs: ReindexProducts necesita el mismo puerto,
// y un slice no puede referenciar a otro.

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

        // Solo lo activo es buscable; un Draft que aún no pasó revisión no se
        // indexa, y si estaba indexado y volvió a Draft, se retira. La regla la
        // comparte con ReindexProducts: ver ProductIndexProjection.
        await ProductIndexProjection.ApplyAsync(indexer, product, ct);
    }
}

public sealed class RemoveProductOnArchived(
    IProductIndexer indexer) : IDomainEventHandler<ProductArchived>
{
    public Task HandleAsync(ProductArchived domainEvent, CancellationToken ct) =>
        indexer.RemoveAsync(domainEvent.ProductId, ct);
}
