using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ElGuerre.Tendero.Ucp.Ap2;
using ElGuerre.Tendero.Ucp.Checkout;
using ElGuerre.Tendero.Ucp.Negotiation;
using ElGuerre.Tendero.Ucp.Signing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ElGuerre.Tendero.Ucp;

public static class UcpServiceCollectionExtensions
{
    /// <summary>
    /// What UCP's transactional half needs (P11-1): its options, and the port that
    /// fetches platform profiles to negotiate — over an HttpClient that never
    /// follows a redirect, so a trusted host cannot send the fetch somewhere else.
    /// And AP2's (P11-4): the shop's signing key, and the checkout mandate
    /// verifiers keyed by the <c>vct</c> they verify.
    /// </summary>
    public static IServiceCollection AddTenderoUcp(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<UcpOptions>(configuration.GetSection(UcpOptions.SectionName));
        services.AddHttpClient<IPlatformProfiles, HttpPlatformProfiles>(client => client.Timeout = TimeSpan.FromSeconds(5))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });

        services.Configure<MerchantSigningOptions>(configuration.GetSection(MerchantSigningOptions.SectionName));
        services.AddSingleton<IMerchantSigner, LocalMerchantSigner>();
        services.AddKeyedSingleton<ICheckoutMandateVerifier, ClosedCheckoutMandateVerifier>(ClosedCheckoutMandateVerifier.Vct);
        services.AddSingleton<CheckoutMandates>();
        services.AddScoped<UcpCheckoutOperations>();
        services.AddSingleton<DevelopmentPlatforms>();
        return services;
    }

    /// <summary>
    /// **Development only**: a platform profile for each agent the development
    /// issuer seeds, published by this process — the dev-issuer pattern applied to
    /// UCP's negotiation. A real platform publishes its own profile on its own host,
    /// and <c>Ucp:TrustedPlatformHosts</c> names it.
    /// </summary>
    public static RouteGroupBuilder MapDevelopmentPlatformProfiles(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/dev-platforms/{agent}").AllowAnonymous().WithMetadata(new ExcludeFromDescriptionAttribute());

        // `?ap2=false` is a platform that does not speak AP2's mandates: the same
        // agent, negotiating the checkout without them — both paths, one seed.
        group.MapGet("/profile.json", (string agent, bool? ap2, DevelopmentPlatforms platforms) => Results.Json(new DevelopmentPlatformProfile
        {
            Ucp = new DevelopmentPlatformEnvelope
            {
                Version = UcpSpecification.Version,
                Capabilities = UcpCapabilities.Declared
                    .Where(capability => ap2 is not false || capability.Id != Capabilities.Ap2MandateCapability.Name)
                    .ToDictionary(
                        capability => capability.Id,
                        IReadOnlyList<DevelopmentPlatformCapability> (capability) =>
                            [new DevelopmentPlatformCapability { Version = capability.Version, Spec = capability.Spec, Schema = capability.Schema, Extends = capability.Extends }]),
            },
            Keys = [platforms.KeyOf(agent)],
        }));

        // The platform's side of AP2 (P11-4): given a checkout the shop signed, the
        // platform — on the person's consent, which here is implicit — answers a
        // closed checkout mandate over it. `lifetime` (seconds) lets a test ask for
        // one that has already expired.
        group.MapPost("/checkout-mandates", (string agent, JsonObject checkout, int? lifetime, HttpContext http, DevelopmentPlatforms platforms, IMerchantSigner shop) =>
            platforms.SignCheckoutMandate(agent, checkout, shop.PublishedKeys, $"{http.Request.Scheme}://{http.Request.Host}",
                    lifetime is { } seconds ? TimeSpan.FromSeconds(seconds) : null) is { } mandate
                ? Results.Json(new { checkout_mandate = mandate })
                : Results.Json(new UcpProtocolError(Ap2ErrorCodes.MerchantAuthorizationInvalid, "The checkout is not signed by this shop: a platform does not ask a person to approve it."), statusCode: 422));

        return group;
    }

    private sealed record DevelopmentPlatformProfile
    {
        [JsonPropertyName("ucp")]
        public required DevelopmentPlatformEnvelope Ucp { get; init; }

        [JsonPropertyName("keys")]
        public required IReadOnlyList<Jwk> Keys { get; init; }
    }

    private sealed record DevelopmentPlatformEnvelope
    {
        [JsonPropertyName("version")]
        public required string Version { get; init; }

        [JsonPropertyName("capabilities")]
        public required IReadOnlyDictionary<string, IReadOnlyList<DevelopmentPlatformCapability>> Capabilities { get; init; }

        [JsonPropertyName("payment_handlers")]
        public IReadOnlyDictionary<string, object> PaymentHandlers { get; init; } = new Dictionary<string, object>();
    }

    private sealed record DevelopmentPlatformCapability
    {
        [JsonPropertyName("version")]
        public required string Version { get; init; }

        [JsonPropertyName("spec")]
        public required string Spec { get; init; }

        [JsonPropertyName("schema")]
        public required string Schema { get; init; }

        [JsonPropertyName("extends")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Extends { get; init; }
    }
}
