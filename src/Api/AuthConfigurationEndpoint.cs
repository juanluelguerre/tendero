using Carter;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace ElGuerre.Tendero.Api;

/// <summary>
/// Which issuer this API trusts, said out loud so a browser cannot get it wrong.
///
/// **It exists because the swap broke both frontends and nothing noticed.**
/// Pointing `Authentication:Authority` at Keycloak is one line (ADR 0017), and
/// the sign-in screens kept asking the development issuer for a token. The API
/// answered 401 to every call, the interceptor signed the user out, and the
/// guard sent them back to the door — on every navigation.
///
/// The alternative was a build-time flag in each app, set in two places and
/// wrong whenever somebody changed one. This cannot disagree: the API reads the
/// same configuration it validates against.
///
/// **It no longer lists who can sign in, and that is the point of the redirect.**
/// It used to, so that a screen could render a picker and post a password grant.
/// The issuer renders its own page now — Keycloak its login form, the
/// development issuer a list of three buttons — so the shop never learns who
/// exists and never handles a credential.
///
/// It lives in `src/Api` and not in a slice because it describes THIS PROCESS
/// rather than any context's data — the same reason `/health` is not a slice.
/// </summary>
public sealed record AuthConfiguration(
    /// <summary>`dev-issuer` or `keycloak`. For a screen that wants to name it;
    /// nothing branches on it.</summary>
    string Issuer,

    /// <summary>
    /// The OIDC authority. The client appends `/.well-known/openid-configuration`
    /// and takes every endpoint from there, which is why this is the only URL
    /// that has to travel.
    /// </summary>
    string Authority,

    /// <summary>
    /// The public client both apps identify as. It is the same string for both
    /// issuers on purpose: the realm declares it and the development issuer does
    /// not check it, so the client configuration does not change with the signer.
    /// </summary>
    string ClientId);

public sealed class AuthConfigurationEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/auth/config",
            Ok<AuthConfiguration> (IConfiguration configuration) =>
            {
                var authority = configuration["Authentication:Authority"] ?? string.Empty;

                // Keycloak's authority ends in /realms/<name>; the development
                // issuer's ends in /dev-issuer. Reading the URL rather than a
                // second setting keeps this from disagreeing with the thing it
                // describes.
                var issuer = authority.Contains("/realms/", StringComparison.OrdinalIgnoreCase)
                    ? "keycloak"
                    : "dev-issuer";

                return TypedResults.Ok(new AuthConfiguration(issuer, authority, "tendero-app"));
            })
            // Anonymous by an explicit decision: it is what somebody who is NOT
            // signed in needs in order to sign in, and it says nothing a token
            // would protect — the issuer's URL is in every token it mints.
            .AllowAnonymous()
            .WithTags("Authentication")
            .WithName("AuthConfiguration");
    }
}
