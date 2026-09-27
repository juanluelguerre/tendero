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

/// <summary>
/// AP2's mandates on UCP's checkout (P11-4): once negotiated, every checkout the
/// shop answers carries its signature, <c>ap2.merchant_authorization</c>, and no
/// checkout completes without a checkout mandate — an SD-JWT the platform signed
/// over that signed checkout. The shop verifies the one format AP2 v0.2 names,
/// <c>dc+sd-jwt</c>, in the Trusted Platform Provider flow (analysis §9).
/// </summary>
public sealed class Ap2MandateCapability : IUcpCapability
{
    public const string Name = "dev.ucp.common.payment.ap2_mandate";

    public string Id => Name;

    public string Version => UcpSpecification.Version;

    public string Spec => $"{UcpSpecification.BaseUrl}/payment/extensions/ap2-mandates/";

    public string Schema => $"{UcpSpecification.SchemaBaseUrl}/common/payment_ap2_mandate.json";

    public string? Extends => CheckoutCapability.Name;

    public IReadOnlyDictionary<string, object>? Config { get; } = new Dictionary<string, object>
    {
        ["vp_formats_supported"] = new Dictionary<string, object> { ["dc+sd-jwt"] = new Dictionary<string, object>() },
    };

    public IReadOnlyList<Type> Slices => [typeof(CompleteCheckoutSessionCommand)];
}
