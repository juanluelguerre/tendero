using ElGuerre.Tendero.Catalog.Adapters;
using ElGuerre.Tendero.Catalog.Connectors;
using ElGuerre.Tendero.Catalog.Connectors.Seed;
using ElGuerre.Tendero.Catalog.Ports;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
        // El reloj también aquí y no sólo en AddTenderoCqrs: el catálogo sella
        // productos, y la puerta de calidad monta AddCatalog sin el dispatcher.
        // TryAdd, así que registrarlo dos veces no es un problema.
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<ICatalogSourceRegistry, KeyedCatalogSourceRegistry>();

        services.AddCatalogReaders(configuration);

        // Almacén de imágenes: un puerto, y hoy un solo adaptador. El de S3
        // entra cuando el sistema de ficheros deje de bastar, sin tocar nada más.
        services.Configure<FileSystemImageStoreOptions>(
            configuration.GetSection(FileSystemImageStoreOptions.SectionName));
        services.AddSingleton<IImageStore, FileSystemImageStore>();
        services.AddHttpClient<IExternalImageReader, ExternalImageReader>();

        return services;
    }

    /// <summary>
    /// La mitad de LECTURA del catálogo: qué significan los atributos y cómo se
    /// llaman las categorías en cada idioma.
    ///
    /// Está separada porque no sólo la necesita quien importa productos.
    /// **Cualquiera que proyecte un producto la necesita**, y el worker de
    /// indexación proyecta sin importar nada: no tiene conectores, ni almacén de
    /// imágenes, ni razón para tenerlos — pero sin esto no puede renderizar
    /// "navy blue" en el índice inglés.
    ///
    /// Que existieran juntas es lo que dejó al worker sin arrancar: la API
    /// llamaba a AddCatalog y funcionaba, el worker no y reventaba al construir
    /// el contenedor.
    /// </summary>
    public static IServiceCollection AddCatalogReaders(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);

        // Singleton: cambian con muy poca frecuencia y la proyección al índice
        // las pide una vez por producto y por cultura.
        services.Configure<AttributeSeedOptions>(
            configuration.GetSection(AttributeSeedOptions.SectionName));
        services.TryAddSingleton<IAttributeDefinitionReader, SeedFileAttributeDefinitionReader>();

        services.Configure<CategorySeedOptions>(
            configuration.GetSection(CategorySeedOptions.SectionName));
        services.TryAddSingleton<ICategoryReader, SeedFileCategoryReader>();

        return services;
    }
}
