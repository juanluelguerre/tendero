using ElGuerre.Tendero.Ordering.Features.CheckoutSessions;

namespace ElGuerre.Tendero.Ucp.Capabilities;

/// <summary>
/// UCP's checkout (P11-1): five operations over one session, served over REST and
/// MCP by the same operations class. Declared only once both bindings existed,
/// the rule the catalogue set.
/// </summary>
public sealed class CheckoutCapability : IUcpCapability
{
    public const string Name = "dev.ucp.shopping.checkout";

    public string Id => Name;

    public string Version => UcpSpecification.Version;

    public string Spec => $"{UcpSpecification.BaseUrl}/checkout/";

    public string Schema => $"{UcpSpecification.SchemaBaseUrl}/shopping/checkout.json";

    public IReadOnlyList<Type> Slices =>
    [
        typeof(OpenCheckoutSessionCommand), typeof(UpdateCheckoutSessionCommand), typeof(GetCheckoutSessionQuery),
        typeof(CompleteCheckoutSessionCommand), typeof(CancelCheckoutSessionCommand),
    ];
}

/// <summary>
/// Shipping, as UCP's fulfillment extension describes it: one method, one
/// destination the agent writes, one group, the rate provider's options. Pickup
/// and several packages are the extension's too, and Tendero offers neither.
/// </summary>
public sealed class FulfillmentCapability : IUcpCapability
{
    public const string Name = "dev.ucp.shopping.fulfillment";

    public string Id => Name;

    public string Version => UcpSpecification.Version;

    public string Spec => $"{UcpSpecification.BaseUrl}/fulfillment/";

    public string Schema => $"{UcpSpecification.SchemaBaseUrl}/shopping/fulfillment.json";

    public string? Extends => CheckoutCapability.Name;

    public IReadOnlyList<Type> Slices => [typeof(UpdateCheckoutSessionCommand)];
}

/// <summary>Discount codes on a checkout, and what the promotion engine applied — automatic ones included.</summary>
public sealed class DiscountCapability : IUcpCapability
{
    public const string Name = "dev.ucp.shopping.discount";

    public string Id => Name;

    public string Version => UcpSpecification.Version;

    public string Spec => $"{UcpSpecification.BaseUrl}/discount/";

    public string Schema => $"{UcpSpecification.SchemaBaseUrl}/shopping/discount.json";

    public string? Extends => CheckoutCapability.Name;

    public IReadOnlyList<Type> Slices => [typeof(UpdateCheckoutSessionCommand)];
}
