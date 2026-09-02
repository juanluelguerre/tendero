using System.Security.Claims;
using Carter;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace ElGuerre.Tendero.DevIssuer;

public sealed record DevTokenResponse(string AccessToken, string TokenType, int ExpiresIn);

public sealed record DevIdentityView(string Subject, string Name, string Role, bool IsAgent);

/// <summary>
/// The three endpoints that make this a real OIDC issuer rather than a shortcut:
/// discovery, JWKS and token. The first two are what let the API use
/// <c>AddJwtBearer</c> with real signature validation — the client half is never
/// fake, only who signs is.
///
/// Everything hangs off <c>/dev-issuer</c> and is registered only in Development.
/// </summary>
public sealed class DevIssuerModule : ICarterModule
{
    /// <summary>
    /// The base path is a constant and not an option: Carter forbids a module
    /// from having constructor dependencies (the CARTER1 analyzer), and routes
    /// are declared at registration, before there is a container to pull
    /// configuration from. A development issuer does not need its path to be
    /// configurable; what does — identities, audience, lifetime — is resolved
    /// per request.
    /// </summary>
    public const string BasePath = "/dev-issuer";

    // ExcludeFromDescription on all of them: the committed document is the API's
    // contract, and an issuer that only exists in Development is not part of it.
    // If it went in, the document would differ between environments and the
    // contract test would start measuring the environment instead of the contract.
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        const string path = BasePath;

        // Discovery. An OIDC client looks exactly here, and takes the jwks_uri
        // from it to verify signatures with.
        app.MapGet($"{path}/.well-known/openid-configuration",
            (HttpContext http) =>
            {
                var issuer = Issuer(http, path);
                return TypedResults.Ok(new Dictionary<string, object>
                {
                    ["issuer"] = issuer,
                    ["jwks_uri"] = $"{issuer}/.well-known/jwks.json",
                    ["token_endpoint"] = $"{issuer}/connect/token",
                    ["grant_types_supported"] = new[] { "password", "client_credentials" },
                    ["id_token_signing_alg_values_supported"] = new[] { SecurityAlgorithms.RsaSha256 },
                    ["scopes_supported"] = new[] { "openid", "profile" },
                    ["subject_types_supported"] = new[] { "public" }
                });
            })
            .AllowAnonymous()
            .ExcludeFromDescription()
            .WithTags("DevIssuer")
            .WithName("DevIssuerDiscovery");

        app.MapGet($"{path}/.well-known/jwks.json",
            (DevSigningKey key) => TypedResults.Ok(new { keys = new[] { key.PublicJsonWebKey() } }))
            .AllowAnonymous()
            .ExcludeFromDescription()
            .WithTags("DevIssuer")
            .WithName("DevIssuerKeys");

        // Who can be asked for. It exists so the backoffice login is a picker
        // rather than a form asking for a password that does not exist.
        app.MapGet($"{path}/identities",
            (IOptions<DevIssuerOptions> options) => TypedResults.Ok(options.Value.Identities
                .Select(i => new DevIdentityView(i.Subject, i.Name, i.Role, i.IsAgent))
                .ToArray()))
            .AllowAnonymous()
            .ExcludeFromDescription()
            .WithTags("DevIssuer")
            .WithName("DevIssuerIdentities");

        // `password` for people, `client_credentials` for agents: they are the
        // two flows Keycloak will serve later, and using them now saves the
        // client from changing when the issuer does.
        app.MapPost($"{path}/connect/token",
            async Task<Results<Ok<DevTokenResponse>, BadRequest<string>>> (
                HttpContext http, IOptions<DevIssuerOptions> options, DevSigningKey key) =>
            {
                // The form is read by hand instead of with [FromForm] over a
                // complex type: that binding returned 400 without saying why, and
                // the contract here is OAuth, which is
                // application/x-www-form-urlencoded.
                if (!http.Request.HasFormContentType)
                    return TypedResults.BadRequest("Expected application/x-www-form-urlencoded.");

                var form = await http.Request.ReadFormAsync();

                var subject = form["grant_type"].ToString() switch
                {
                    "client_credentials" => form["client_id"].ToString(),
                    "password" => form["username"].ToString(),
                    _ => null
                };

                if (string.IsNullOrWhiteSpace(subject))
                    return TypedResults.BadRequest(
                        "grant_type must be 'password' (with username) or 'client_credentials' (with client_id).");

                var identity = options.Value.Identities
                    .FirstOrDefault(i => string.Equals(i.Subject, subject, StringComparison.OrdinalIgnoreCase));

                if (identity is null)
                    return TypedResults.BadRequest(
                        $"Unknown identity '{subject}'. Seeded: {string.Join(", ", options.Value.Identities.Select(i => i.Subject))}.");

                return TypedResults.Ok(Mint(identity, Issuer(http, path), options.Value, key));
            })
            .AllowAnonymous()
            .DisableAntiforgery()
            .ExcludeFromDescription()
            .WithTags("DevIssuer")
            .WithName("DevIssuerToken");
    }

    private static DevTokenResponse Mint(
        DevIdentity identity, string issuer, DevIssuerOptions options, DevSigningKey key)
    {
        var claims = new Dictionary<string, object>
        {
            [JwtRegisteredClaimNames.Sub] = identity.Subject,
            [JwtRegisteredClaimNames.Name] = identity.Name,
            [ClaimTypes.Role] = identity.Role
        };

        // An agent carries its own identifier AS WELL AS the subject: it is a
        // different principal, not a person with another role (ADR 0022, pending).
        if (identity.IsAgent)
            claims["agent_id"] = identity.Subject;

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = options.Audience,
            Claims = claims,
            Expires = DateTime.UtcNow.Add(options.TokenLifetime),
            SigningCredentials = new SigningCredentials(key.SecurityKey, SecurityAlgorithms.RsaSha256)
        };

        return new DevTokenResponse(
            new JsonWebTokenHandler().CreateToken(descriptor),
            "Bearer",
            (int)options.TokenLifetime.TotalSeconds);
    }

    /// <summary>
    /// The issuer is composed from the request rather than configured: the port
    /// changes between Aspire, a bare `dotnet run` and WebApplicationFactory, and
    /// a fixed issuer would fail validation the moment it did not match.
    /// </summary>
    private static string Issuer(HttpContext http, string path) =>
        $"{http.Request.Scheme}://{http.Request.Host}{path}";
}

