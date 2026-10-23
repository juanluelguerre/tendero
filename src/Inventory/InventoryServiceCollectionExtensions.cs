using ElGuerre.Tendero.Inventory.Adapters;
using ElGuerre.Tendero.Inventory.Features.StockHealth;
using ElGuerre.Tendero.Inventory.Ledger;
using ElGuerre.Tendero.Inventory.Ports;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ElGuerre.Tendero.Inventory;

public static class InventoryServiceCollectionExtensions
{
    /// <summary>
    /// Everything the inventory context needs except the two ports it cannot
    /// own: <see cref="IStockRepository"/> and <see cref="IAvailabilityReader"/>
    /// are EF Core, so Persistence registers them — the same division Pricing
    /// makes with its catalogue reader.
    /// </summary>
    public static IServiceCollection AddInventory(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);

        services.Configure<InventoryOptions>(configuration.GetSection(InventoryOptions.SectionName));
        services.Configure<WarehouseSeedOptions>(
            configuration.GetSection(WarehouseSeedOptions.SectionName));

        // Singleton: two rows that nothing can change at runtime, read once per
        // reservation.
        services.TryAddSingleton<IWarehouseReader, SeedFileWarehouseReader>();

        // Keyed adapters (ADR 0003). A third policy — ship from the fullest,
        // prefer the warehouse that is open latest — is a class and a line here.
        services.AddKeyedSingleton<IAllocationStrategy, PriorityFirstAllocation>(PriorityFirstAllocation.Key);
        services.AddKeyedSingleton<IAllocationStrategy, SingleWarehouseAllocation>(SingleWarehouseAllocation.Key);
        services.TryAddSingleton<IAllocationStrategyRegistry, KeyedAllocationStrategyRegistry>();

        // Scoped: it writes through the unit of work, which is the DbContext.
        services.AddScoped<IStockLedger, StockLedger>();

        // The stock sources (phase 16, S2a): each registers keyed by its name,
        // from the host (AddShopify); the registry lists what is there.
        services.TryAddScoped<IStockSourceRegistry, KeyedStockSourceRegistry>();

        // What Steward may read of the stock (phase 12, ADR 0054).
        services.AddScoped<IReadTool, StockToRestockTool>();

        return services;
    }
}
