using System.Text.Json.Serialization;
using ElGuerre.Tendero.Ucp.Negotiation;
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
    /// </summary>
    public static IServiceCollection AddTenderoUcp(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<UcpOptions>(configuration.GetSection(UcpOptions.SectionName));
        services.AddHttpClient<IPlatformProfiles, HttpPlatformProfiles>(client => client.Timeout = TimeSpan.FromSeconds(5))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        return services;
    }

    /// <summary>
    /// **Development only**: a platform profile for each agent the development
    /// issuer seeds, published by this process — the dev-issuer pattern applied to
    /// UCP's negotiation. A real platform publishes its own profile on its own host,
    /// and <c>Ucp:TrustedPlatformHosts</c> names it.
    /// </summary>
    public static IEndpointConventionBuilder MapDevelopmentPlatformProfiles(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/dev-platforms/{agent}/profile.json", (string agent) => Results.Json(new DevelopmentPlatformProfile
        {
            Ucp = new DevelopmentPlatformEnvelope
            {
                Version = UcpSpecification.Version,
                Capabilities = UcpCapabilities.Declared.ToDictionary(
                        capability => capability.Id,
                        IReadOnlyList<DevelopmentPlatformCapability> (capability) =>
                            [new DevelopmentPlatformCapability { Version = capability.Version, Spec = capability.Spec, Schema = capability.Schema, Extends = capability.Extends }]),
            },
        }))
            .AllowAnonymous()
            .WithMetadata(new ExcludeFromDescriptionAttribute());

    private sealed record DevelopmentPlatformProfile
    {
        [JsonPropertyName("ucp")]
        public required DevelopmentPlatformEnvelope Ucp { get; init; }
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
