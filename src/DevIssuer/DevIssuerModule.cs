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
/// Los tres endpoints que hacen de esto un emisor OIDC de verdad y no un
/// atajo: discovery, JWKS y token. Los dos primeros son lo que permite que la
/// API use <c>AddJwtBearer</c> con validación real de firma — la mitad cliente
/// nunca es falsa, sólo lo es quién firma.
///
/// Todo cuelga de <c>/dev-issuer</c> y se registra sólo en Development.
/// </summary>
public sealed class DevIssuerModule : ICarterModule
{
    /// <summary>
    /// La ruta base es una constante y no una opción: Carter prohíbe que un
    /// módulo tenga dependencias en el constructor (analizador CARTER1), y las
    /// rutas se declaran al registrar, antes de que haya contenedor del que
    /// sacar la configuración. Un emisor de desarrollo no necesita que su ruta
    /// sea configurable; lo que sí lo es —identidades, audiencia, caducidad—
    /// se resuelve por petición.
    /// </summary>
    public const string BasePath = "/dev-issuer";

    // ExcludeFromDescription en todos: el documento commiteado es el contrato de
    // la API, y un emisor que sólo existe en Development no forma parte de él.
    // Si entrase, el documento diferiría entre entornos y el test de contrato
    // pasaría a medir el entorno en vez del contrato.
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        const string path = BasePath;

        // Discovery. Un cliente OIDC lo busca exactamente aquí, y de él saca el
        // jwks_uri con el que verificará las firmas.
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

        // Quién se puede pedir. Existe para que el login del backoffice sea un
        // selector en vez de un formulario que pide una contraseña que no hay.
        app.MapGet($"{path}/identities",
            (IOptions<DevIssuerOptions> options) => TypedResults.Ok(options.Value.Identities
                .Select(i => new DevIdentityView(i.Subject, i.Name, i.Role, i.IsAgent))
                .ToArray()))
            .AllowAnonymous()
            .ExcludeFromDescription()
            .WithTags("DevIssuer")
            .WithName("DevIssuerIdentities");

        // `password` para personas, `client_credentials` para agentes: son los
        // dos flujos que Keycloak servirá después, y usarlos ya evita que el
        // cliente tenga que cambiar cuando cambie el emisor.
        app.MapPost($"{path}/connect/token",
            async Task<Results<Ok<DevTokenResponse>, BadRequest<string>>> (
                HttpContext http, IOptions<DevIssuerOptions> options, DevSigningKey key) =>
            {
                // El formulario se lee a mano en vez de con [FromForm] sobre un
                // tipo complejo: ese binding devolvía 400 sin decir por qué, y
                // aquí el contrato es OAuth, que es application/x-www-form-urlencoded.
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

        // Un agente lleva su propio identificador ADEMÁS del sujeto: es un
        // principal distinto, no una persona con otro rol (ADR 0022, pendiente).
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
    /// El emisor se compone de la petición en vez de configurarse: el puerto
    /// cambia entre Aspire, un `dotnet run` suelto y WebApplicationFactory, y un
    /// issuer fijo haría fallar la validación en cuanto no coincidiese.
    /// </summary>
    private static string Issuer(HttpContext http, string path) =>
        $"{http.Request.Scheme}://{http.Request.Host}{path}";
}

