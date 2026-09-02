using ElGuerre.Tendero.Pricing.Adapters;
using ElGuerre.Tendero.Pricing.Engine;
using ElGuerre.Tendero.Pricing.Ports;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ElGuerre.Tendero.Pricing;

public static class PricingServiceCollectionExtensions
{
    /// <summary>
    /// Everything the pricing context needs, minus the one adapter it cannot
    /// own: <see cref="IPricedItemReader"/> reads the catalogue, so it is
    /// registered by whoever knows both — Persistence today.
    ///
    /// Leaving that registration out is deliberate. If this method registered a
    /// catalogue reader, Pricing would reference Catalog, and the engine would
    /// stop being testable without a database — which is the property the whole
    /// context is shaped around.
    /// </summary>
    public static IServiceCollection AddPricing(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);

        services.Configure<PricingSeedOptions>(configuration.GetSection(PricingSeedOptions.SectionName));
        services.Configure<FlatVatOptions>(configuration.GetSection(FlatVatOptions.SectionName));

        // Singletons: both cache their file, and the projection asks for them
        // once per quote. They change about as often as the attribute
        // definitions do, which is to say never at runtime — yet.
        services.TryAddSingleton<IPriceListReader, SeedFilePriceListReader>();
        services.TryAddSingleton<IPromotionReader, SeedFilePromotionReader>();

        // Pure functions, so a singleton holds no state worth worrying about.
        services.TryAddSingleton<IPriceResolver, PriceListResolver>();
        services.TryAddSingleton<IPromotionEngine, CombiningPromotionEngine>();

        // Keyed adapters (ADR 0003). A third calculator is a class and a line
        // here, and not one `if` in the quoting slice.
        services.AddKeyedSingleton<ITaxCalculator, FlatVatTaxCalculator>(FlatVatTaxCalculator.Key);
        services.AddKeyedSingleton<ITaxCalculator, ZeroTaxCalculator>(ZeroTaxCalculator.Key);
        services.TryAddSingleton<ITaxCalculatorRegistry, KeyedTaxCalculatorRegistry>();

        return services;
    }
}
