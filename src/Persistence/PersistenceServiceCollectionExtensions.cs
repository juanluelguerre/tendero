using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.Pricing.Ports;
using ElGuerre.Tendero.Search.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ElGuerre.Tendero.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    /// <summary>
    /// Registra el DbContext y los adaptadores de los puertos. Los slices siguen
    /// sin saber que existe EF Core: sólo ven IProductRepository/IUnitOfWork.
    /// </summary>
    public static IServiceCollection AddTenderoPersistence(
        this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<TenderoDbContext>(options => options.UseNpgsql(connectionString));

        // Un adaptador, dos puertos, UNA instancia por scope: registrar cada
        // interfaz por separado daría dos objetos sobre el mismo DbContext, que
        // funciona pero miente sobre cuántos adaptadores hay.
        services.AddScoped<EfProductRepository>();
        services.AddScoped<IProductRepository>(services => services.GetRequiredService<EfProductRepository>());
        services.AddScoped<IProductReader>(services => services.GetRequiredService<EfProductRepository>());
        services.AddScoped<IProductCatalogReader>(services => services.GetRequiredService<EfProductRepository>());
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();

        // The catalogue facts pricing needs. Registered here rather than in
        // AddPricing because this is the only project that knows both contexts.
        services.AddScoped<IPricedItemReader, EfPricedItemReader>();

        return services;
    }
}
