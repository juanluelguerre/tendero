using System.Security.Claims;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace ElGuerre.Tendero.Api;

/// <summary>Los tres roles del laboratorio, en un sitio.</summary>
public static class TenderoRoles
{
    public const string Shopper = "shopper";
    public const string Shopkeeper = "shopkeeper";
    public const string Agent = "agent";
}

public static class AuthenticationExtensions
{
    /// <summary>
    /// La mitad CLIENTE de la identidad, y es la que nunca es falsa. Valida
    /// firma, emisor, audiencia y caducidad contra el JWKS que publique quien
    /// sea el emisor — el de desarrollo hoy, Keycloak después. Cambiar de uno a
    /// otro es cambiar `Authentication:Authority`, no este código.
    /// </summary>
    public static IServiceCollection AddTenderoAuthentication(
        this IServiceCollection services, IConfiguration configuration)
    {
        var authority = configuration["Authentication:Authority"]
            ?? throw new InvalidOperationException(
                "Authentication:Authority is missing. Point it at the issuer's base URL — " +
                "the development issuer publishes one, and so does Keycloak.");

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = authority;
                options.Audience = configuration["Authentication:Audience"] ?? "tendero-api";

                // El emisor de desarrollo habla HTTP en local. Es la ÚNICA
                // concesión, y está atada al entorno, no a una constante.
                options.RequireHttpsMetadata =
                    !string.Equals(configuration["Authentication:AllowHttpMetadata"], "true",
                        StringComparison.OrdinalIgnoreCase);

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    RoleClaimType = ClaimTypes.Role,
                    NameClaimType = "name",
                    // Sin esto, un token caducado sigue valiendo cinco minutos y
                    // el test que lo comprueba pasa por accidente.
                    ClockSkew = TimeSpan.Zero
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(TenderoPolicyNames.Shopper, policy =>
                policy.RequireRole(TenderoRoles.Shopper))
            .AddPolicy(TenderoPolicyNames.Shopkeeper, policy =>
                policy.RequireRole(TenderoRoles.Shopkeeper))
            .AddPolicy(TenderoPolicyNames.AgentOrShopper, policy =>
                policy.RequireRole(TenderoRoles.Shopper, TenderoRoles.Agent));

        services.AddHttpContextAccessor();
        services.AddScoped<IPrincipalAccessor, HttpPrincipalAccessor>();

        return services;
    }
}

/// <summary>
/// Traduce el <see cref="ClaimsPrincipal"/> de la petición al principal del
/// dominio. Es la única clase que sabe que existen los claims: un handler
/// pregunta por <see cref="CommercePrincipal"/> y funciona igual sobre HTTP,
/// sobre MCP o dentro del worker.
/// </summary>
internal sealed class HttpPrincipalAccessor(IHttpContextAccessor accessor) : IPrincipalAccessor
{
    public CommercePrincipal Current
    {
        get
        {
            var user = accessor.HttpContext?.User;
            if (user?.Identity?.IsAuthenticated != true)
                return CommercePrincipal.Anonymous;

            var subject = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
            var agent = user.FindFirstValue("agent_id");

            return new CommercePrincipal(
                Customer: null, // Accounts todavía no existe: llega en la fase 7.
                Agent: agent is null ? null : new AgentId(agent),
                Subject: subject,
                Roles: [.. user.FindAll(ClaimTypes.Role).Select(claim => claim.Value)]);
        }
    }
}
