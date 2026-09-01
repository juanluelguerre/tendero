using Microsoft.Extensions.DependencyInjection;
using Tendero.Catalog.Connectors;
using Tendero.Catalog.Ports;

namespace Tendero.Catalog.Adapters;

/// <summary>
/// El único sitio del catálogo que habla con el contenedor de dependencias. Los
/// conectores se registran como keyed services por su nombre de origen (ADR
/// 0003), así que añadir Shopify sigue siendo una línea de registro y ni un
/// <c>if</c> — pero el <c>IServiceProvider</c> se queda aquí, detrás del puerto,
/// en vez de viajar hasta el handler.
/// </summary>
internal sealed class KeyedCatalogSourceRegistry(IServiceProvider services) : ICatalogSourceRegistry
{
    // La lista sale del contenedor, no de una constante: una constante y un
    // registro son dos verdades que divergen el día que alguien añada la segunda
    // sin tocar la primera.
    public IReadOnlyCollection<string> Sources =>
        [.. services.GetKeyedServices<ICatalogSourceConnector>(KeyedService.AnyKey)
            .Select(connector => connector.Source)
            .Order(StringComparer.Ordinal)];

    public ICatalogSourceConnector Get(string source) =>
        services.GetKeyedService<ICatalogSourceConnector>(source)
        ?? throw new UnknownCatalogSourceException(source, Sources);
}
