using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace ElGuerre.Tendero.DevIssuer;

/// <summary>
/// RFC 8693 token exchange, delegation only (P11-3): an agent presents a person's
/// access token (<c>subject_token</c>) and its own (<c>actor_token</c>), and gets one
/// token that says both — <c>sub</c> is the person, <c>act.sub</c> and <c>agent_id</c>
/// the agent. The API then sees a principal with a customer AND an agent, the pair
/// <c>CommercePrincipal</c> was shaped for: an order placed with it is the person's,
/// and the audit row names the agent that placed it.
///
/// The same claim a real issuer emits: Keycloak's token-exchange delegation (an
/// experimental feature since 26.7) puts the actor in <c>act</c>, so the API reads
/// <c>act.sub</c> as well as the development issuer's <c>agent_id</c>, and swapping
/// issuers changes nothing downstream (invariant 10).
///
/// **Delegation, never impersonation.** The token keeps the actor visible; there is
/// no exchange here that turns an agent into the person with nobody the wiser.
/// </summary>
public static class TokenExchange
{
    public const string GrantType = "urn:ietf:params:oauth:grant-type:token-exchange";

    public const string AccessTokenType = "urn:ietf:params:oauth:token-type:access_token";

    public static bool TryExchange(
        string subjectToken, string actorToken, string issuer, DevIssuerOptions options, DevSigningKey key,
        out DevTokenResponse? delegated, out string? refusal)
    {
        (delegated, refusal) = (null, null);

        if (Read(subjectToken, issuer, options, key) is not { } person || Read(actorToken, issuer, options, key) is not { } actor)
        {
            // One answer for every bad token, the rule the code flow follows.
            refusal = "invalid_grant";
            return false;
        }

        if (person.TryGetValue("agent_id", out _))
        {
            refusal = "invalid_request: the subject token is an agent's; an agent acts for a person, not for another agent.";
            return false;
        }

        if (!actor.TryGetValue("agent_id", out var agentId) || agentId is not string agent)
        {
            refusal = "invalid_request: the actor token is not an agent's.";
            return false;
        }

        // The narrower of the two lifetimes: the delegation cannot outlive either
        // the person's consent or the agent's own credential.
        var expires = Math.Min(Expiry(person), Expiry(actor));

        var claims = new Dictionary<string, object>
        {
            [JwtRegisteredClaimNames.Sub] = person["sub"],
            [JwtRegisteredClaimNames.Name] = person.TryGetValue("name", out var name) ? name : person["sub"],
            // The token acts as the agent, not as the person: it opens the agent's
            // surface (UCP) and not the person's account pages.
            [System.Security.Claims.ClaimTypes.Role] = "agent",
            ["agent_id"] = agent,
            ["act"] = new Dictionary<string, object> { ["sub"] = agent },
        };

        // A test credential stays a test credential when it acts for someone (ADR 0038).
        if (actor.TryGetValue("tendero_mode", out var mode))
            claims["tendero_mode"] = mode;

        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = options.Audience,
            Claims = claims,
            Expires = DateTimeOffset.FromUnixTimeSeconds(expires).UtcDateTime,
            SigningCredentials = new SigningCredentials(key.SecurityKey, SecurityAlgorithms.RsaSha256),
        });

        delegated = new DevTokenResponse(
            token, "Bearer", (int)Math.Max(0, expires - DateTimeOffset.UtcNow.ToUnixTimeSeconds()), IssuedTokenType: AccessTokenType);
        return true;
    }

    /// <summary>A token this issuer signed, for this audience, still valid — or nothing.</summary>
    private static IDictionary<string, object>? Read(string token, string issuer, DevIssuerOptions options, DevSigningKey key)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        var result = new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = issuer,
            ValidAudience = options.Audience,
            IssuerSigningKey = key.SecurityKey,
            ClockSkew = TimeSpan.Zero,
        }).GetAwaiter().GetResult();

        return result.IsValid ? result.Claims : null;
    }

    private static long Expiry(IDictionary<string, object> claims) =>
        claims.TryGetValue("exp", out var exp) ? Convert.ToInt64(exp, System.Globalization.CultureInfo.InvariantCulture) : 0;
}
