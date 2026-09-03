using ElGuerre.Tendero.Accounts.Ports;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.Inventory.Ports;
using ElGuerre.Tendero.Ordering.Ports;
using ElGuerre.Tendero.Pricing.Ports;
using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.SharedKernel;
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

        // A FACTORY as well as the scoped context, and only the audit writer
        // uses it. An audit row written on the command's own context shares its
        // transaction and vanishes when the command fails — so the log would
        // hold every success and no denial, which is the inverse of what an
        // audit log is for.
        //
        // SCOPED, not singleton, and the correction is worth the line: what
        // separates the audit row from the command is a different CONTEXT
        // INSTANCE, not a different DI lifetime. Registering the factory as a
        // singleton reads as "it must outlive the request" and buys nothing —
        // `AddDbContext` registers its options as scoped, so the container
        // refuses to construct it at all. `WorkerContainerTests` caught it,
        // which is what that test exists for.
        services.AddDbContextFactory<TenderoDbContext>(
            options => options.UseNpgsql(connectionString),
            lifetime: ServiceLifetime.Scoped);

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

        // One adapter, two inventory ports, one instance per scope — the same
        // arrangement as the product repository, and for the same reason.
        services.AddScoped<EfStockRepository>();
        services.AddScoped<IStockRepository>(services => services.GetRequiredService<EfStockRepository>());
        services.AddScoped<IAvailabilityReader>(services => services.GetRequiredService<EfStockRepository>());

        services.AddScoped<IOrderRepository, EfOrderRepository>();
        services.AddScoped<IOrderReader, EfOrderReader>();
        services.AddScoped<ICartRepository, EfCartRepository>();
        services.AddScoped<IReturnRequestRepository, EfReturnRequestRepository>();

        // The Ordering side of the catalogue crossing, the sibling of
        // IPricedItemReader: values out, no entity shared (ADR 0014).
        services.AddScoped<IPurchasableReader, EfPurchasableReader>();

        // The dispatcher's audit step runs wherever a command does, including
        // the outbox worker — which opens a scope per batch, so scoped is
        // exactly right and nothing needs to outlive a request.
        services.AddScoped<IAuditWriter, EfAuditWriter>();

        // One adapter, two Accounts ports, one instance per scope.
        services.AddScoped<EfCustomerRepository>();
        services.AddScoped<ICustomerRepository>(services => services.GetRequiredService<EfCustomerRepository>());
        services.AddScoped<ICustomerDirectory>(services => services.GetRequiredService<EfCustomerRepository>());

        return services;
    }
}
