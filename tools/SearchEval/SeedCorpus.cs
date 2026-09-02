using ElGuerre.Tendero.Catalog.Connectors;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Search.Contracts;
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
    public async Task<IReadOnlyDictionary<string, string>> IndexAsync(
        string source, CancellationToken cancellationToken)
    {
        var connector = services.GetRequiredKeyedService<ICatalogSourceConnector>(source);
        var indexer = services.GetRequiredService<IProductIndexer>();

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
            byInternalId[product.Id.ToString()] = external.ExternalId;
        }

        return byInternalId;
    }

    /// <summary>
    /// The SAME mapping the import uses — <see cref="ExternalProductMapper"/> —
    /// and not a copy with a comment promising they are the same. If the import
    /// changes and this does not, the quality gate scores a system that does not
    /// exist.
    ///
    /// The only thing it adds is <c>Publish()</c>: only Active is searchable and
    /// there is no review queue here to publish it. And the only thing it omits
    /// is the images: they are not a searchable field, so they do not move the
    /// ranking, and putting the store in the middle only adds ways to fail that
    /// are not the thing being measured.
    /// </summary>
    private static Product BuildProduct(
        ExternalProduct external, string source, AttributeDefinitions definitions)
    {
        var product = external.ToNewProduct(source, Clock, definitions);
        product.Publish(Clock);
        return product;
    }
}
