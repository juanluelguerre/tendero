using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

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
    public static IEndpointConventionBuilder MapTenderoUcp(this IEndpointRouteBuilder endpoints) =>
        endpoints
            .MapGet(UcpSpecification.WellKnownPath, Ok<UcpProfile> (HttpContext http) =>
                TypedResults.Ok(Compose($"{http.Request.Scheme}://{http.Request.Host}")))
            .AllowAnonymous()
            // Out of the OpenAPI document, like `/mcp` and for a sharper reason:
            // this document POINTS AT schemas rather than being described by
            // ours, and the committed OpenAPI document generates the storefront's
            // TypeScript types, which have no use for a discovery profile.
            .WithMetadata(new ExcludeFromDescriptionAttribute());

    /// <summary>
    /// The profile for a shop reachable at <paramref name="baseUrl"/>.
    ///
    /// Public and pure so it can be read in a test without a server, which is
    /// what makes the committed snapshot a comparison rather than a screenshot.
    /// </summary>
    public static UcpProfile Compose(string baseUrl)
    {
        var host = baseUrl.TrimEnd('/');

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
                            Spec = capability.Spec,
                            Schema = capability.Schema
                        }
                    ],
                    StringComparer.Ordinal),

                // Required of a business profile, and empty: this shop settles no
                // payments over UCP until phase 11. Empty says that; absent would
                // make the document invalid.
                PaymentHandlers = new Dictionary<string, IReadOnlyList<object>>(StringComparer.Ordinal)
            }
        };
    }
}
