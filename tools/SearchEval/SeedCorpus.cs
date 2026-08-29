using Microsoft.Extensions.DependencyInjection;
using Tendero.Catalog.Connectors;
using Tendero.Catalog.Domain;
using Tendero.Search.Contracts;

namespace Tendero.SearchEval;

/// <summary>
/// Indexa el catálogo semilla directamente desde el conector, sin pasar por
/// Postgres ni por el outbox. La evaluación mide RELEVANCIA: meter la
/// persistencia en medio sólo añadiría formas de fallar que no son la que se
/// está midiendo. Lo que llega al índice es el mismo
/// <c>ProductSearchDocument</c> que produce la aplicación.
/// </summary>
public sealed class SeedCorpus(IServiceProvider services)
{
    /// <summary>Devuelve el mapa id interno -> id del origen. El índice guarda
    /// GUIDs; el golden set anota ids del origen, y este mapa los reconcilia.</summary>
    public async Task<IReadOnlyDictionary<string, string>> IndexAsync(
        string source, CancellationToken cancellationToken)
    {
        var connector = services.GetRequiredKeyedService<ICatalogSourceConnector>(source);
        var indexer = services.GetRequiredService<IProductIndexer>();

        var byInternalId = new Dictionary<string, string>(StringComparer.Ordinal);

        await foreach (var external in connector.StreamProductsAsync(cancellationToken))
        {
            var product = BuildProduct(external, connector.Source);
            await indexer.IndexAsync(product, cancellationToken);
            byInternalId[product.Id.ToString()] = external.ExternalId;
        }

        return byInternalId;
    }

    // Mismos pasos y mismo orden que ImportProductsHandler, más Publish: sólo lo
    // Activo es buscable, y aquí no hay cola de revisión que lo publique.
    private static Product BuildProduct(ExternalProduct external, string source)
    {
        var product = Product.Create(external.LocalizedName, external.Price, external.LocalizedDescription);
        product.UpdateDetails(
            external.LocalizedName, external.LocalizedDescription, external.Brand, external.Category);
        product.LinkExternal(source, external.ExternalId);

        foreach (var url in external.ImageUrls)
            product.AddImage(url);

        foreach (var (name, value) in external.Attributes)
            product.SetAttribute(name, value);

        product.Publish();

        return product;
    }
}
