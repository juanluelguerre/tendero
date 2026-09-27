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

    /// <summary>
    /// Which provider a TEST order pays through (ADR 0038): the fake, whatever the
    /// shop takes real money with. The day <see cref="PaymentProvider"/> is a real
    /// one, a rehearsal still moves nothing.
    /// </summary>
    public string TestPaymentProvider { get; set; } = FakePaymentProvider.Key;

    /// <summary>
    /// Whether an agent's live checkout is settled by the shop (P11-1). False until
    /// a real payment provider takes agents' money: until then a live agent's
    /// session ends in <c>requires_escalation</c> and a person pays on the
    /// storefront, which is the honest statement of what the shop can settle. A
    /// test credential's rehearsal settles through the sandbox handler either way.
    /// </summary>
    public bool AgentsPayLive { get; set; }

    /// <summary>The provider for an order of this mode — the one place that choice is made.</summary>
    public string ProviderFor(Domain.OrderMode mode) =>
        mode == Domain.OrderMode.Test ? TestPaymentProvider : PaymentProvider;
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

        // Who may reach an order through its id. Scoped, because the principal
        // it asks about is the request's.
        services.AddScoped<OrderAccess>();
        services.AddScoped<OrderPlacement>();

        // The fake is resolvable unkeyed too, because the webhook demo has to be
        // able to SIGN a payload and only the concrete type can do that. A real
        // adapter would not need it.
        services.TryAddSingleton(provider =>
            (FakePaymentProvider)provider.GetRequiredKeyedService<IPaymentProvider>(FakePaymentProvider.Key));

        return services;
    }
}
