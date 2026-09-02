using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.Pricing.Ports;
using ElGuerre.Tendero.Search.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ElGuerre.Tendero.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    /// <summary>
    /// Registers the DbContext and the ports' adapters. Slices still do not know
    /// EF Core exists: all they see is IProductRepository/IUnitOfWork.
    /// </summary>
    public static IServiceCollection AddTenderoPersistence(
        this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<TenderoDbContext>(options => options.UseNpgsql(connectionString));

        // One adapter, two ports, ONE instance per scope: registering each
        // interface separately would give two objects over the same DbContext,
        // which works but lies about how many adapters there are.
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
