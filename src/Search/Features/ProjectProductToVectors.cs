using System.Diagnostics.Metrics;
using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Search.Contracts;
using Microsoft.Extensions.Logging;

namespace ElGuerre.Tendero.Search.Features.ProjectProductToVectors;

// The vector projection, on the same outbox and the same events as the lexical
// one (P8-4), keeping the same rule: ProductIndexProjection.
//
// **It never fails a message, and that is invariant 8 applied to the write
// path.** The outbox delivers one event to its handlers in turn and retries the
// whole message when one throws, so a vector handler that threw would re-run the
// lexical projection five times and dead-letter it — the lexical index held
// hostage by the layer that is allowed to be down. A failure here is logged and
// counted instead, and what it costs is written down rather than discovered: the
// product's vectors stay as they were until the next ProductUpserted, a
// `POST /api/search/reindex?target=vector`, or a worker that starts to an empty
// collection. Search does not show the stale vector a price or a stock level,
// because a hybrid hit is always built from the lexical index.

public sealed class ProjectProductVectorsOnUpserted(
    IProductReader products,
    IProductVectorIndexer vectors,
    ILogger<ProjectProductVectorsOnUpserted> logger) : IDomainEventHandler<ProductUpserted>
{
    public async Task HandleAsync(ProductUpserted domainEvent, CancellationToken ct)
    {
        // Outside the guard: the catalogue not answering is not the vector
        // layer's failure to swallow, and the lexical handler would meet it too.
        var product = await products.GetByIdAsync(domainEvent.ProductId, ct);
        if (product is null)
            return; // the lexical projection already warns about a missing product

        try
        {
            await ProductIndexProjection.ApplyAsync(vectors, product, ct);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            VectorProjectionFailures.Record(logger, exception, product.Id);
        }
    }
}

public sealed class RemoveProductVectorsOnArchived(
    IProductVectorIndexer vectors,
    ILogger<RemoveProductVectorsOnArchived> logger) : IDomainEventHandler<ProductArchived>
{
    public async Task HandleAsync(ProductArchived domainEvent, CancellationToken ct)
    {
        try
        {
            await vectors.RemoveAsync(domainEvent.ProductId, ct);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // An archived product whose vector survives cannot surface: a hybrid
            // hit is hydrated from the lexical index, which filters on Active.
            VectorProjectionFailures.Record(logger, exception, domainEvent.ProductId);
        }
    }
}

/// <summary>A swallowed failure with somewhere to surface: a warning and a counter.</summary>
internal static class VectorProjectionFailures
{
    private static readonly Meter Meter = new(TelemetrySources.Search);

    private static readonly Counter<long> Failed = Meter.CreateCounter<long>(
        "tendero.search.vector_projection.failed",
        description: "Product changes the vector layer could not take; the lexical index took them.");

    public static void Record(ILogger logger, Exception exception, ProductId productId)
    {
        Failed.Add(1);

        // The unavailable layer is the expected case — a model server still
        // downloading, a vector layer nobody started — and its reason is the
        // whole story. Anything else gets its stack.
        if (exception is SearchUnavailableException)
            logger.LogWarning(
                "Product {ProductId} was not projected into the vector layer: {Reason}. " +
                "POST /api/search/reindex?target=vector catches it up",
                productId, exception.Message);
        else
            logger.LogWarning(exception,
                "Product {ProductId} was not projected into the vector layer. " +
                "POST /api/search/reindex?target=vector catches it up",
                productId);
    }
}
