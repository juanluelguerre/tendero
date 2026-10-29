using ElGuerre.Tendero.Catalog.Connectors;
using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.Search.Images;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace ElGuerre.Tendero.SearchEval;

/// <summary>
/// Indexes the seed catalogue straight from the connector, without going through
/// Postgres or the outbox. The evaluation measures RELEVANCE: putting
/// persistence in the middle would only add ways to fail that are not the thing
/// being measured. What reaches the index is the same
/// <c>ProductSearchDocument</c> the application produces.
/// </summary>
public sealed class SeedCorpus(IServiceProvider services)
{
    // The gate's corpus does not depend on time: it stamps with the real clock.
    private static readonly TimeProvider Clock = TimeProvider.System;

    /// <summary>Returns the internal id -> source id map. The index stores GUIDs;
    /// the golden set annotates source ids, and this map reconciles them.</summary>
    /// <param name="withPictures">
    /// Also read each product's pictures and embed them (phase 13a): only when
    /// the run measures what pictures rank. The lexical and hybrid numbers do not
    /// move either way — a picture is not a searchable field of theirs.
    /// </param>
    public async Task<IReadOnlyDictionary<string, string>> IndexAsync(
        string source, bool withVectors, bool withPictures, CancellationToken cancellationToken)
    {
        var connector = services.GetRequiredKeyedService<ICatalogSourceConnector>(source);
        var indexer = services.GetRequiredService<IProductIndexer>();

        // The vector projection the worker runs, through its port: the same text,
        // the same point ids, the same refusal of a model nobody pinned.
        var vectors = withVectors ? services.GetRequiredService<IProductVectorIndexer>() : null;

        // The SAME definitions the import uses. Without them the corpus would
        // store "azul marino" as plain text and the gate would be measuring a
        // different system from the one that runs.
        var definitions = await services
            .GetRequiredService<IAttributeDefinitionReader>()
            .AllAsync(cancellationToken);

        var byInternalId = new Dictionary<string, string>(StringComparer.Ordinal);

        await foreach (var external in connector.StreamProductsAsync(cancellationToken))
        {
            var product = BuildProduct(external, connector.Source, definitions);
            await indexer.IndexAsync(product, cancellationToken);

            if (vectors is not null)
                await vectors.IndexAsync(product, cancellationToken);

            if (withPictures)
                await IndexPicturesAsync(product, external, cancellationToken);

            byInternalId[product.Id.ToString()] = JudgedAs(external, connector.Source);
        }

        return byInternalId;
    }

    /// <summary>
    /// The id the golden set judges a product by. The seed's is its item id; a
    /// product read back from the Shopify round trip carries Shopify's own number,
    /// and its handle is the item id lowercased (tools/shopify-seed/seed.mjs), so
    /// the same judgments score it (phase 16, P16-6).
    /// </summary>
    private static string JudgedAs(ExternalProduct external, string source) =>
        source == "shopify" && external.Handle is { Length: > 0 } handle ? handle.ToUpperInvariant() : external.ExternalId;

    /// <summary>
    /// The pictures the source lists, through the import's own reader and the
    /// worker's own projection: the same bytes, the same point ids. One the
    /// reader cannot open is skipped, as the import skips it.
    /// </summary>
    private async Task IndexPicturesAsync(Product product, ExternalProduct external, CancellationToken cancellationToken)
    {
        var reader = services.GetRequiredService<IExternalImageReader>();
        var store = services.GetRequiredService<IImageStore>();

        foreach (var image in external.Images)
        {
            if (await reader.OpenAsync(image, cancellationToken) is not { } content)
                continue;

            await using var stream = content.Content;
            product.AddImage(Clock, await store.SaveAsync(stream, content.ContentType, cancellationToken));
        }

        await services.GetRequiredService<IPictureVectorIndexer>().IndexAsync(product, cancellationToken);
    }

    /// <summary>
    /// The SAME mapping the import uses — <see cref="ExternalProductMapper"/> —
    /// and not a copy with a comment promising they are the same. If the import
    /// changes and this does not, the quality gate scores a system that does not
    /// exist.
    ///
    /// The only thing it adds is <c>Publish()</c>: only Active is searchable and
    /// there is no review queue here to publish it. And the only thing it omits
    /// is the images: they are not a searchable field of the words' strategies,
    /// so they do not move their ranking. A run that measures the pictures adds
    /// them afterwards (<see cref="IndexPicturesAsync"/>), from memory.
    /// </summary>
    private static Product BuildProduct(
        ExternalProduct external, string source, AttributeDefinitions definitions)
    {
        var product = external.ToNewProduct(source, Clock, definitions);
        product.Publish(Clock);
        return product;
    }
}
