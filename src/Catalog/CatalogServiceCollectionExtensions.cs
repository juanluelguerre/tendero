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
    /// Keyed registration of the catalogue connectors (ADR 0003). A new source is
    /// one more line here and not one `if` in ImportProductsHandler.
    /// </summary>
    public static IServiceCollection AddCatalog(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SeedConnectorOptions>(configuration.GetSection(SeedConnectorOptions.SectionName));
        services.AddKeyedScoped<ICatalogSourceConnector, SeedCatalogConnector>(SeedCatalogConnector.Key);

        // Registration is the only thing that talks to the container: slices
        // receive the port, not the IServiceProvider.
        // The clock is here too and not only in AddTenderoCqrs: the catalogue
        // stamps products, and the quality gate composes AddCatalog without the
        // dispatcher. TryAdd, so registering it twice is not a problem.
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<ICatalogSourceRegistry, KeyedCatalogSourceRegistry>();

        services.AddCatalogReaders(configuration);

        // The image store: a port, and today a single adapter. The S3 one enters
        // when the file system stops being enough, without touching anything else.
        services.Configure<FileSystemImageStoreOptions>(
            configuration.GetSection(FileSystemImageStoreOptions.SectionName));
        services.AddSingleton<IImageStore, FileSystemImageStore>();
        services.AddHttpClient<IExternalImageReader, ExternalImageReader>();

        return services;
    }

    /// <summary>
    /// The catalogue's READ half: what the attributes mean and what the
    /// categories are called in each language.
    ///
    /// It is separate because it is not only needed by whoever imports products.
    /// **Anybody who projects a product needs it**, and the indexing worker
    /// projects without importing anything: it has no connectors, no image store
    /// and no reason to have them — but without this it cannot render "navy
    /// blue" into the English index.
    ///
    /// Their being one method is what left the worker unable to start: the API
    /// called AddCatalog and worked, the worker did not and blew up building its
    /// container.
    /// </summary>
    public static IServiceCollection AddCatalogReaders(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);

        // Singleton: they change very rarely and the index projection asks for
        // them once per product per culture.
        services.Configure<AttributeSeedOptions>(
            configuration.GetSection(AttributeSeedOptions.SectionName));
        services.TryAddSingleton<IAttributeDefinitionReader, SeedFileAttributeDefinitionReader>();

        services.Configure<CategorySeedOptions>(
            configuration.GetSection(CategorySeedOptions.SectionName));
        services.TryAddSingleton<ICategoryReader, SeedFileCategoryReader>();

        return services;
    }
}
