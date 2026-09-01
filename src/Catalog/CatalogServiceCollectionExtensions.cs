using ElGuerre.Tendero.Catalog.Adapters;
using ElGuerre.Tendero.Catalog.Connectors;
using ElGuerre.Tendero.Catalog.Connectors.Seed;
using ElGuerre.Tendero.Catalog.Ports;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ElGuerre.Tendero.Catalog;

public static class CatalogServiceCollectionExtensions
{
    /// <summary>
    /// Registro keyed de los conectores de catálogo (ADR 0003). Un origen nuevo
    /// es una línea más aquí y ni un `if` en ImportProductsHandler.
    /// </summary>
    public static IServiceCollection AddCatalog(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SeedConnectorOptions>(configuration.GetSection(SeedConnectorOptions.SectionName));
        services.AddKeyedScoped<ICatalogSourceConnector, SeedCatalogConnector>(SeedCatalogConnector.Key);

        // El registro es lo único que habla con el contenedor: los slices reciben
        // el puerto, no el IServiceProvider.
        services.AddScoped<ICatalogSourceRegistry, KeyedCatalogSourceRegistry>();

        // Almacén de imágenes: un puerto, y hoy un solo adaptador. El de S3
        // entra cuando el sistema de ficheros deje de bastar, sin tocar nada más.
        services.Configure<FileSystemImageStoreOptions>(
            configuration.GetSection(FileSystemImageStoreOptions.SectionName));
        services.AddSingleton<IImageStore, FileSystemImageStore>();
        services.AddHttpClient<IExternalImageReader, ExternalImageReader>();

        return services;
    }
}
