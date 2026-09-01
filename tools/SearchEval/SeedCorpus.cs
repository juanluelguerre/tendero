using ElGuerre.Tendero.Catalog.Connectors;
using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Search.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace ElGuerre.Tendero.SearchEval;

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

    /// <summary>
    /// El MISMO mapeo que usa la importación —<see cref="ExternalProductMapper"/>—
    /// y no una copia con un comentario prometiendo que son iguales. Si el import
    /// cambia y esto no, la puerta de calidad puntúa un sistema que no existe.
    ///
    /// Lo único que añade es <c>Publish()</c>: sólo lo Activo es buscable y aquí
    /// no hay cola de revisión que lo publique. Y lo único que omite son las
    /// imágenes: no son campo buscable, así que no mueven el ranking, y meter el
    /// almacén por medio sólo añade formas de fallar que no son la que se mide.
    /// </summary>
    private static Product BuildProduct(ExternalProduct external, string source)
    {
        var product = external.ToNewProduct(source);
        product.Publish();
        return product;
    }
}
