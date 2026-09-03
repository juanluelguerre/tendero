using ElGuerre.Tendero.Ordering.Adapters;
using ElGuerre.Tendero.Ordering.Ports;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ElGuerre.Tendero.Ordering;

public sealed class CheckoutOptions
{
    public const string SectionName = "Checkout";

    /// <summary>Which shipping provider answers. A configuration value and not an
    /// `if`: a second carrier is a class and a registration.</summary>
    public string ShippingProvider { get; set; } = FlatRateShipping.Key;

    /// <summary>Which provider takes the money. `fake` until a real one is
    /// wired; the code around it does not change when it is.</summary>
    public string PaymentProvider { get; set; } = FakePaymentProvider.Key;
}

public static class OrderingServiceCollectionExtensions
{
    /// <summary>
    /// Everything `Ordering` needs except the ports it cannot own: the two
    /// repositories and <see cref="IPurchasableReader"/> are EF Core, so
    /// Persistence registers them — the same division `Inventory` and `Pricing`
    /// already make.
    /// </summary>
    public static IServiceCollection AddOrdering(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);

        services.Configure<CheckoutOptions>(configuration.GetSection(CheckoutOptions.SectionName));
        services.Configure<FlatRateShippingOptions>(
            configuration.GetSection(FlatRateShippingOptions.SectionName));
        services.Configure<ZoneRateShippingOptions>(
            configuration.GetSection(ZoneRateShippingOptions.SectionName));
        services.Configure<FakePaymentOptions>(configuration.GetSection(FakePaymentOptions.SectionName));
        services.Configure<Features.Returns.ReturnOptions>(
            configuration.GetSection(Features.Returns.ReturnOptions.SectionName));

        // Keyed adapters (ADR 0003). Both shipping providers are registered even
        // though one is configured: the backoffice shows what is available, and
        // the contract suite runs against every key rather than the chosen one.
        services.AddKeyedSingleton<IShippingRateProvider, FlatRateShipping>(FlatRateShipping.Key);
        services.AddKeyedSingleton<IShippingRateProvider, ZoneRateShipping>(ZoneRateShipping.Key);
        services.TryAddSingleton<IShippingRateProviderRegistry, KeyedShippingRateProviderRegistry>();

        services.AddKeyedSingleton<IPaymentProvider, FakePaymentProvider>(FakePaymentProvider.Key);
        services.TryAddSingleton<IPaymentProviderRegistry, KeyedPaymentProviderRegistry>();

        // The one file that knows Pricing exists. Scoped, because the dispatcher
        // it delegates to resolves scoped handlers.
        services.AddScoped<ICartPricer, PricingCartPricer>();

        // The fake is resolvable unkeyed too, because the webhook demo has to be
        // able to SIGN a payload and only the concrete type can do that. A real
        // adapter would not need it.
        services.TryAddSingleton(provider =>
            (FakePaymentProvider)provider.GetRequiredKeyedService<IPaymentProvider>(FakePaymentProvider.Key));

        return services;
    }
}
