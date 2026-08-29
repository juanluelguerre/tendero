using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tendero.Catalog.Connectors;
using Tendero.Catalog.Connectors.Seed;

namespace Tendero.Catalog;

public static class CatalogServiceCollectionExtensions
{
    /// <summary>
    /// Registro keyed de los conectores de catálogo (ADR 0003). Un origen nuevo
    /// es una línea más aquí y ni un `if` en ImportProductsHandler.
    /// </summary>
    public static IServiceCollection AddCatalog(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SeedConnectorOptions>(configuration.GetSection(SeedConnectorOptions.SectionName));
        services.AddKeyedScoped<ICatalogSourceConnector, SeedCatalogConnector>("seed");

        return services;
    }
}
