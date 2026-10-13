using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace ElGuerre.Tendero.Ucp;

/// <summary>
/// <c>GET /.well-known/ucp</c> — what this shop can do, composed from the
/// capability registry and never written by hand.
///
/// **Derivation is the whole point.** A hand-written manifest at a fixed path
/// rots the first time a slice moves and nothing catches it, which is the
/// failure this design exists to make impossible: a capability holds
/// `typeof(SearchProductsQuery)`, so deleting the slice is a build error, and
/// this endpoint reads the registry rather than a file.
///
/// **Anonymous, and this one is not our decision to make.** The specification
/// requires the profile to be publicly accessible with no authentication:
/// discovery that needs a credential is not discovery. It still says so in code,
/// because the endpoint-policy test asks every route to state who may call it.
/// </summary>
public static class UcpProfileEndpoint
{
    public static IEndpointConventionBuilder MapTenderoUcp(this IEndpointRouteBuilder endpoints)
    {
        MapExtensionSchemas(endpoints);
        return endpoints
            .MapGet(UcpSpecification.WellKnownPath, Ok<UcpProfile> (HttpContext http, Signing.IMerchantSigner signer, IOptions<Negotiation.UcpOptions> options) =>
            {
                http.Response.Headers.CacheControl = UcpSpecification.PublishedCaching;
                return TypedResults.Ok(Compose($"{http.Request.Scheme}://{http.Request.Host}", signer.PublishedKeys, options.Value.OwnSchemasUrl));
            })
            .AllowAnonymous()
            // Out of the OpenAPI document, like `/mcp` and for a sharper reason:
            // this document POINTS AT schemas rather than being described by
            // ours, and the committed OpenAPI document generates the storefront's
            // TypeScript types, which have no use for a discovery profile.
            .WithMetadata(new ExcludeFromDescriptionAttribute());
    }

    /// <summary>
    /// <c>GET /ucp/schemas/{name}</c>: the schemas of the shop's own extensions,
    /// embedded in this assembly beside the code that keeps them. Anonymous, like the
    /// profile that points at them — a contract nobody may read is not one.
    /// </summary>
    private static void MapExtensionSchemas(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/ucp/schemas/{name}", IResult (string name, HttpContext http) =>
            {
                if (typeof(UcpProfileEndpoint).Assembly.GetManifestResourceStream($"ucp-schema:{name}") is not { } schema)
                    return Results.NotFound();

                http.Response.Headers.CacheControl = UcpSpecification.PublishedCaching;
                return Results.Stream(schema, "application/schema+json");
            })
            .AllowAnonymous()
            .WithMetadata(new ExcludeFromDescriptionAttribute());

    /// <summary>
    /// The profile for a shop reachable at <paramref name="baseUrl"/>.
    ///
    /// Public and pure so it can be read in a test without a server, which is
    /// what makes the committed snapshot a comparison rather than a screenshot.
    /// The keys are the deployment's, so they are handed in — and so is where
    /// the shop's own schemas are published (<paramref name="ownSchemas"/>),
    /// which is not where the shop is served unless nobody said otherwise: a
    /// platform accepts `com.elguerre.tendero.*` only from a host under that
    /// name's domain (<see cref="UcpAuthority"/>).
    /// </summary>
    public static UcpProfile Compose(string baseUrl, IReadOnlyList<Signing.Jwk>? keys = null, string? ownSchemas = null)
    {
        var host = baseUrl.TrimEnd('/');
        var published = ownSchemas ?? host;

        return new UcpProfile
        {
            Ucp = new UcpDocument
            {
                Version = UcpSpecification.Version,
                Services = new Dictionary<string, IReadOnlyList<UcpService>>(StringComparer.Ordinal)
                {
                    [UcpSpecification.ShoppingService] =
                    [
                        new UcpService
                        {
                            Version = UcpSpecification.Version,
                            Spec = $"{UcpSpecification.BaseUrl}/catalog/mcp/",

                            // One binding, and it is honest because the binding
                            // was implemented before this line was written: the
                            // three tools UCP names, with the shapes it defines.
                            // A `rest` entry would claim a REST binding nobody
                            // here has built.
                            Transport = "mcp",
                            Endpoint = $"{host}/mcp"
                        },
                        new UcpService
                        {
                            Version = UcpSpecification.Version,
                            Spec = $"{UcpSpecification.BaseUrl}/catalog/rest/",

                            // The second binding, and the reason a service is a
                            // LIST rather than an object: one capability, two
                            // ways in. Both were implemented before either was
                            // declared here.
                            Transport = "rest",
                            Endpoint = $"{host}{Catalog.UcpCatalogRestBinding.BasePath}"
                        }
                    ]
                },
                Capabilities = UcpCapabilities.Declared.ToDictionary(
                    capability => capability.Id,
                    IReadOnlyList<UcpCapabilityDeclaration> (capability) =>
                    [
                        new UcpCapabilityDeclaration
                        {
                            Version = capability.Version,
                            Spec = UcpCapabilities.Absolute(capability.Spec, published),
                            Schema = UcpCapabilities.Absolute(capability.Schema, published),
                            Extends = capability.Extends,
                            Config = capability.Config
                        }
                    ],
                    StringComparer.Ordinal),

                // One handler, and a sandbox one (ADR 0038 §6, P11-1): the fake
                // provider, which a checkout offers to a test credential only. A
                // live agent's checkout ends in requires_escalation until a real
                // provider takes agents' money — the honest statement of what this
                // shop settles.
                PaymentHandlers = new Dictionary<string, IReadOnlyList<object>>(StringComparer.Ordinal)
                {
                    [Checkout.UcpCheckoutMapper.SandboxHandlerName] = [Checkout.UcpCheckoutMapper.SandboxHandler],
                }
            },
            Keys = keys is { Count: > 0 } ? keys : null
        };
    }
}
