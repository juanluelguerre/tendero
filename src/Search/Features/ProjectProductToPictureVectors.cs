using System.Diagnostics.Metrics;
using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.Search.Images;
using Microsoft.Extensions.Logging;

namespace ElGuerre.Tendero.Search.Features.ProjectProductToPictureVectors;

// The picture projection (phase 13a, P13-3), on the same outbox and the same
// events as the lexical one and the words', keeping the same rule:
// ProductIndexProjection — on sale is in, anything else is out.
//
// **It never fails a message**, for the reason the words' projection does not
// (invariant 8 on the write path): the outbox retries a whole message when one
// handler throws, and the lexical projection is another handler of the same
// one. Here that covers more than a store that is down — a picture the model
// cannot decode is this layer's failure too, and it must not dead-letter a
// price change. A failure is logged and counted, and what it costs is written
// down: the product's pictures stay as they were until its next ProductUpserted,
// a `POST /api/search/reindex?target=pictures`, or a worker that starts to an
// empty collection.
//
// What it costs when nothing fails is nothing: a picture is one point keyed on
// its id, so an upsert for a price finds every picture already there.

public sealed class ProjectProductPicturesOnUpserted(
    IProductReader products,
    IPictureVectorIndexer pictures,
    ILogger<ProjectProductPicturesOnUpserted> logger) : IDomainEventHandler<ProductUpserted>
{
    public async Task HandleAsync(ProductUpserted domainEvent, CancellationToken ct)
    {
        // Outside the guard: the catalogue not answering is not this layer's
        // failure to swallow, and the lexical handler would meet it too.
        var product = await products.GetByIdAsync(domainEvent.ProductId, ct);
        if (product is null)
            return; // the lexical projection already warns about a missing product

        try
        {
            await ProductIndexProjection.ApplyAsync(pictures, product, ct);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            PictureProjectionFailures.Record(logger, exception, product.Id);
        }
    }
}

public sealed class RemoveProductPicturesOnArchived(
    IPictureVectorIndexer pictures,
    ILogger<RemoveProductPicturesOnArchived> logger) : IDomainEventHandler<ProductArchived>
{
    public async Task HandleAsync(ProductArchived domainEvent, CancellationToken ct)
    {
        try
        {
            await pictures.RemoveAsync(domainEvent.ProductId, ct);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // An archived product whose picture survives cannot surface: a visual
            // hit is hydrated from the lexical index, which filters on Active.
            PictureProjectionFailures.Record(logger, exception, domainEvent.ProductId);
        }
    }
}

/// <summary>A swallowed failure with somewhere to surface: a warning and a counter.</summary>
internal static class PictureProjectionFailures
{
    private static readonly Meter Meter = new(TelemetrySources.Search);

    private static readonly Counter<long> Failed = Meter.CreateCounter<long>(
        "tendero.search.picture_projection.failed",
        unit: "{change}",
        description: "Product changes the picture vectors could not take; the lexical index took them.");

    public static void Record(ILogger logger, Exception exception, ProductId productId)
    {
        Failed.Add(1);

        // The unavailable store is the expected case and its reason is the whole
        // story. Anything else — a file that is not a picture — gets its stack.
        if (exception is SearchUnavailableException)
            logger.LogWarning(
                "The pictures of product {ProductId} were not projected into the picture vectors: {Reason}. " +
                "POST /api/search/reindex?target=pictures catches it up",
                productId, exception.Message);
        else
            logger.LogWarning(exception,
                "The pictures of product {ProductId} were not projected into the picture vectors. " +
                "POST /api/search/reindex?target=pictures catches it up",
                productId);
    }
}
