using System.Security.Claims;
using ElGuerre.Tendero.Accounts.Ports;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;

namespace ElGuerre.Tendero.Api;

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
                policy.RequireRole(TenderoRoles.Shopper, TenderoRoles.Agent))
            // An agent's role and an agent named — by `agent_id` or by RFC 8693's
            // `act` when it acts for a person (P11-3).
            .AddPolicy(TenderoPolicyNames.Agent, policy =>
                policy.RequireRole(TenderoRoles.Agent).RequireAssertion(context =>
                    context.User.HasClaim(claim => claim.Type is TenderoClaims.Agent or TenderoClaims.Actor)));

        services.AddHttpContextAccessor();
        services.AddScoped<IPrincipalAccessor, HttpPrincipalAccessor>();

        // And who acted, for the business metrics: replaces the kernel's
        // system default, whichever of the two was registered first.
        services.RemoveAll<IActorAccessor>();
        services.AddScoped<IActorAccessor, HttpActorAccessor>();

        return services;
    }
}

/// <summary>
/// Translates the request's <see cref="ClaimsPrincipal"/> into the domain's
/// principal. It is the only class that knows claims exist: a handler asks for
/// <see cref="CommercePrincipal"/> and works the same over HTTP, over MCP or
/// inside the worker.
/// </summary>
internal sealed class HttpPrincipalAccessor(
    IHttpContextAccessor accessor,
    ICustomerDirectory customers,
    IAgentDirectory agents) : IPrincipalAccessor
{
    /// <summary>
    /// Resolved once per request and then remembered.
    ///
    /// <c>Current</c> is a property, and a handler may read it several times —
    /// the pricing engine asks for the segment, the audit step asks who acted.
    /// Without this, each read is a database round trip for an answer that
    /// cannot have changed inside one request.
    ///
    /// The C# 14 <c>field</c> keyword holds the memo: the getter is
    /// null-resilient, so the compiler treats the backing field as nullable and
    /// the property stays non-nullable, with no hand-declared field beside it.
    /// </summary>
    public CommercePrincipal Current => field ??= Resolve();

    /// <summary>`act` is a JSON object whose `sub` is the actor; anything else names nobody.</summary>
    private static string? ActorOf(string? act)
    {
        if (string.IsNullOrWhiteSpace(act))
            return null;

        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(act);
            return document.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object
                   && document.RootElement.TryGetProperty("sub", out var sub)
                   && sub.ValueKind == System.Text.Json.JsonValueKind.String
                ? sub.GetString()
                : null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private CommercePrincipal Resolve()
    {
        var user = accessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true)
            return CommercePrincipal.Anonymous;

        var subject = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        // The agent is named by the development issuer's `agent_id`, or by RFC
        // 8693's `act` claim when a token acts for a person (P11-3) — the claim
        // Keycloak's token-exchange delegation emits, so swapping issuers changes
        // nothing here.
        var agent = user.FindFirstValue(TenderoClaims.Agent) ?? ActorOf(user.FindFirstValue(TenderoClaims.Actor));

        // Null until `LinkIdentity` has run, and deliberately so: this is a
        // READ. An accessor that created a customer on first sight would turn
        // every GET into a write — a page view would register an account, and
        // the row would appear with no audit entry, because queries are not
        // audited. Registration is a command the storefront calls once after
        // login.
        //
        // Blocking on the lookup is the honest shape here: the property is
        // synchronous because it must work over MCP and inside the outbox
        // worker, where there is no request to await on. One indexed two-column
        // read, once per request, is the price of that.
        var known = subject is null
            ? null
            : customers.ForSubjectAsync(subject).GetAwaiter().GetResult();

        return new CommercePrincipal(
            Customer: known?.Id,
            Agent: agent is null ? null : new AgentId(agent),
            Subject: subject,
            // `preferred_username` first because that is what OIDC defines for
            // it and what Keycloak sends; `name` because that is what the
            // development issuer sends; the subject last, so a token carrying
            // neither still names somebody rather than nobody.
            DisplayName: user.FindFirstValue("preferred_username")
                ?? user.FindFirstValue(ClaimTypes.Name)
                ?? subject,
            Roles: [.. user.FindAll(ClaimTypes.Role).Select(claim => claim.Value)])
        {
            // The tariff is a fact the shop holds about the customer, so it is
            // read here and nowhere else — never from a request body.
            Segment = known?.Segment,

            // A test credential is the issuer's statement, signed, like the agent
            // id beside it (ADR 0038).
            IsTest = user.FindFirstValue(TenderoClaims.Mode) == TenderoClaims.TestMode,

            // What the shop thinks of the agent, beside who it is (P11-2): the same
            // shape of read as the customer's segment, on the primary key. An agent
            // nobody registered is Unknown — never a row created by being seen.
            Standing = agent is null
                ? null
                : agents.StandingOfAsync(new AgentId(agent)).GetAwaiter().GetResult() ?? AgentStanding.Unknown,
        };
    }
}
