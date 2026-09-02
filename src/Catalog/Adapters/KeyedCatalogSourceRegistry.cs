using ElGuerre.Tendero.Catalog.Connectors;
using ElGuerre.Tendero.Catalog.Ports;
using Microsoft.Extensions.DependencyInjection;

namespace ElGuerre.Tendero.Catalog.Adapters;

/// <summary>
/// The only place in the catalogue that talks to the DI container. Connectors
/// register as keyed services under their source name (ADR 0003), so adding
/// Shopify is still one line of registration and not one <c>if</c> — but the
/// <c>IServiceProvider</c> stays here, behind the port, instead of travelling as
/// far as the handler.
/// </summary>
internal sealed class KeyedCatalogSourceRegistry(IServiceProvider services) : ICatalogSourceRegistry
{
    // The list comes from the container and not from a constant: a constant and
    // a registry are two truths that diverge the day somebody adds the second
    // without touching the first.
    public IReadOnlyCollection<string> Sources =>
        [.. services.GetKeyedServices<ICatalogSourceConnector>(KeyedService.AnyKey)
            .Select(connector => connector.Source)
            .Order(StringComparer.Ordinal)];

    public ICatalogSourceConnector Get(string source) =>
        services.GetKeyedService<ICatalogSourceConnector>(source)
        ?? throw new UnknownCatalogSourceException(source, Sources);
}
