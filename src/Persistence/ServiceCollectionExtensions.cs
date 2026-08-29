using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tendero.Catalog.Features.ImportProducts;
using Tendero.Search.Features.ProjectProductToIndex;

namespace Tendero.Persistence;

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

        services.AddScoped<EfProductRepository>();
        services.AddScoped<IProductRepository>(sp => sp.GetRequiredService<EfProductRepository>());
        services.AddScoped<IProductReader>(sp => sp.GetRequiredService<EfProductRepository>());
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();

        return services;
    }
}
