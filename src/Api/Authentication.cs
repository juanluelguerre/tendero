using System.Security.Claims;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace ElGuerre.Tendero.Api;

/// <summary>The laboratory's three roles, in one place.</summary>
public static class TenderoRoles
{
    public const string Shopper = "shopper";
    public const string Shopkeeper = "shopkeeper";
    public const string Agent = "agent";
}

public static class AuthenticationExtensions
{
    /// <summary>
    /// Identity's CLIENT half, and the half that is never fake. It validates the
    /// signature, the issuer, the audience and the expiry against whatever JWKS
    /// the issuer publishes — the development one today, Keycloak later. Going
    /// from one to the other is changing `Authentication:Authority`, not this code.
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

                // The development issuer speaks HTTP locally. It is the ONLY
                // concession, and it is tied to the environment, not to a constant.
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
                    // Without this an expired token is still good for five
                    // minutes, and the test that checks it passes by accident.
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
/// Translates the request's <see cref="ClaimsPrincipal"/> into the domain's
/// principal. It is the only class that knows claims exist: a handler asks for
/// <see cref="CommercePrincipal"/> and works the same over HTTP, over MCP or
/// inside the worker.
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
                Customer: null, // Accounts does not exist yet: it arrives in phase 7.
                Agent: agent is null ? null : new AgentId(agent),
                Subject: subject,
                Roles: [.. user.FindAll(ClaimTypes.Role).Select(claim => claim.Value)]);
        }
    }
}
