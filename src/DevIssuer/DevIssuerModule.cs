using System.Security.Claims;
using System.Text.Json.Serialization;
using Carter;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace ElGuerre.Tendero.DevIssuer;

/// <summary>
/// The token response, in the field names RFC 6749 specifies.
///
/// **The names are explicit because this is a wire format and not a DTO of
/// ours.** The API serialises camelCase, so without these attributes this record
/// answered `accessToken` where every OIDC client on earth reads `access_token`.
/// Nothing failed: the client found no token, stayed signed out, and the route
/// guard sent the person back to the door — on every attempt, forever.
///
/// It survived because both halves agreed with each other and with nobody else:
/// the shop read `accessToken` because we wrote it, and the contract test read
/// `accessToken` because it was written against the same adapter. The Keycloak
/// half of that same suite reads `access_token` two hundred lines further down,
/// which is the divergence that should have been the clue.
/// </summary>
/// <param name="IdToken">
/// Present only for the authorization code flow: it is what the CLIENT
/// validates, where the access token is what the API validates.
/// </param>
public sealed record DevTokenResponse(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("token_type")] string TokenType,
    [property: JsonPropertyName("expires_in")] int ExpiresIn,
    [property: JsonPropertyName("id_token")] string? IdToken = null);

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
                    ["authorization_endpoint"] = $"{issuer}/connect/authorize",
                    ["token_endpoint"] = $"{issuer}/connect/token",
                    ["end_session_endpoint"] = $"{issuer}/connect/logout",
                    ["response_types_supported"] = new[] { "code" },
                    ["grant_types_supported"] =
                        new[] { "authorization_code", "password", "client_credentials" },
                    // S256 only. `plain` is in RFC 7636 and is a challenge that
                    // challenges nothing; advertising it teaches a client that
                    // it is acceptable, and the client is the half we keep.
                    ["code_challenge_methods_supported"] = new[] { "S256" },
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

        // THE LOGIN PAGE, and the reason this issuer stopped being a token
        // vending machine.
        //
        // The shop used to sign in with a `password` grant, which meant the
        // browser posted a username and a password to the issuer itself. That is
        // the one flow an identity provider exists to remove: the application
        // handles the credential. OAuth 2.1 drops it, and a fake that only
        // speaks it is not standing in for anything Keycloak does.
        //
        // So the fake grew the real endpoint. It renders a page, the person
        // picks who they are, and it redirects back with a code — the same three
        // steps Keycloak performs, with its login form replaced by a list of
        // buttons because there are no passwords here and inventing some would
        // be the scope creep phase 0 warned about.
        app.MapGet($"{path}/connect/authorize",
            Results<ContentHttpResult, BadRequest<string>> (
                HttpContext http, IOptions<DevIssuerOptions> options) =>
            {
                var query = http.Request.Query;
                var redirectUri = query["redirect_uri"].ToString();
                var challenge = query["code_challenge"].ToString();

                // Refusals BEFORE anything is rendered, and none of them redirect:
                // bouncing an error to a redirect_uri we have not validated is
                // how an open redirect is built.
                if (!string.Equals(query["response_type"].ToString(), "code", StringComparison.Ordinal))
                    return TypedResults.BadRequest("response_type must be 'code'.");

                if (string.IsNullOrWhiteSpace(redirectUri))
                    return TypedResults.BadRequest("redirect_uri is required.");

                if (string.IsNullOrWhiteSpace(challenge))
                    return TypedResults.BadRequest("code_challenge is required: this issuer requires PKCE.");

                if (!string.Equals(query["code_challenge_method"].ToString(), "S256", StringComparison.Ordinal))
                    return TypedResults.BadRequest("code_challenge_method must be 'S256'.");

                if (!IsLoopback(redirectUri))
                    return TypedResults.BadRequest("redirect_uri must be a loopback address.");

                return TypedResults.Content(
                    PickerPage(options.Value.Identities, http.Request.QueryString.Value ?? string.Empty),
                    "text/html; charset=utf-8");
            })
            .AllowAnonymous()
            .ExcludeFromDescription()
            .WithTags("DevIssuer")
            .WithName("DevIssuerAuthorize");

        // What a button on that page hits. It mints the code and bounces back to
        // the application, which is the only place a redirect is issued.
        app.MapGet($"{path}/connect/authorize/pick",
            Results<RedirectHttpResult, BadRequest<string>> (
                HttpContext http, IOptions<DevIssuerOptions> options, DevAuthorizationCodes codes) =>
            {
                var query = http.Request.Query;
                var subject = query["subject"].ToString();
                var redirectUri = query["redirect_uri"].ToString();

                if (!IsLoopback(redirectUri))
                    return TypedResults.BadRequest("redirect_uri must be a loopback address.");

                var identity = options.Value.Identities.FirstOrDefault(
                    i => string.Equals(i.Subject, subject, StringComparison.OrdinalIgnoreCase));

                if (identity is null)
                    return TypedResults.BadRequest($"Unknown identity '{subject}'.");

                var code = codes.Issue(
                    identity.Subject,
                    redirectUri,
                    query["code_challenge"].ToString(),
                    query["nonce"].ToString() is { Length: > 0 } nonce ? nonce : null);

                var separator = redirectUri.Contains('?', StringComparison.Ordinal) ? '&' : '?';
                var state = query["state"].ToString();

                return TypedResults.Redirect(
                    $"{redirectUri}{separator}code={Uri.EscapeDataString(code)}" +
                    $"&state={Uri.EscapeDataString(state)}");
            })
            .AllowAnonymous()
            .ExcludeFromDescription()
            .WithTags("DevIssuer")
            .WithName("DevIssuerAuthorizePick");

        // Signing out. There is no server-side session to end — the token is the
        // whole session — so this exists to honour the discovery document and to
        // send the browser back where the client asked.
        app.MapGet($"{path}/connect/logout",
            Results<RedirectHttpResult, BadRequest<string>> (HttpContext http) =>
            {
                var target = http.Request.Query["post_logout_redirect_uri"].ToString();

                if (string.IsNullOrWhiteSpace(target))
                    return TypedResults.BadRequest("post_logout_redirect_uri is required.");

                return IsLoopback(target)
                    ? TypedResults.Redirect(target)
                    : TypedResults.BadRequest("post_logout_redirect_uri must be a loopback address.");
            })
            .AllowAnonymous()
            .ExcludeFromDescription()
            .WithTags("DevIssuer")
            .WithName("DevIssuerLogout");

        // `password` for people, `client_credentials` for agents: they are the
        // two flows Keycloak will serve later, and using them now saves the
        // client from changing when the issuer does.
        app.MapPost($"{path}/connect/token",
            async Task<Results<Ok<DevTokenResponse>, BadRequest<string>>> (
                HttpContext http, IOptions<DevIssuerOptions> options, DevSigningKey key,
                DevAuthorizationCodes codes) =>
            {
                // The form is read by hand instead of with [FromForm] over a
                // complex type: that binding returned 400 without saying why, and
                // the contract here is OAuth, which is
                // application/x-www-form-urlencoded.
                if (!http.Request.HasFormContentType)
                    return TypedResults.BadRequest("Expected application/x-www-form-urlencoded.");

                var form = await http.Request.ReadFormAsync();

                var grant = form["grant_type"].ToString();

                // The authorization code path is separate because it has to
                // VERIFY something before it knows the subject, where the other
                // two are told it. Everything it checks — single use, expiry,
                // the redirect URI, the PKCE verifier — lives in the code store,
                // which is the type that owns the challenge.
                if (grant == "authorization_code")
                {
                    if (!codes.TryRedeem(
                            form["code"].ToString(),
                            form["redirect_uri"].ToString(),
                            form["code_verifier"].ToString(),
                            out var redeemed))
                    {
                        // One answer for every failure. Saying WHICH check failed
                        // tells whoever is guessing which half they got right.
                        return TypedResults.BadRequest("invalid_grant");
                    }

                    // `TryRedeem` returning true is what makes this non-null,
                    // and the compiler cannot see that through an out parameter
                    // on a different type. Named rather than suppressed twice.
                    var grantedTo = redeemed!;

                    var authorized = options.Value.Identities.First(
                        i => string.Equals(i.Subject, grantedTo.Subject, StringComparison.OrdinalIgnoreCase));

                    return TypedResults.Ok(Mint(
                        authorized, Issuer(http, path), options.Value, key,
                        clientId: form["client_id"].ToString(), nonce: grantedTo.Nonce));
                }

                var subject = grant switch
                {
                    "client_credentials" => form["client_id"].ToString(),
                    "password" => form["username"].ToString(),
                    _ => null
                };

                if (string.IsNullOrWhiteSpace(subject))
                    return TypedResults.BadRequest(
                        "grant_type must be 'authorization_code', 'password' (with username) " +
                        "or 'client_credentials' (with client_id).");

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
        DevIdentity identity, string issuer, DevIssuerOptions options, DevSigningKey key,
        string? clientId = null, string? nonce = null)
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

        var handler = new JsonWebTokenHandler();
        var accessToken = handler.CreateToken(descriptor);

        // The id_token, and only for the code flow.
        //
        // It is what the client validates the sign-in with — the audience is the
        // CLIENT and not the API, which is the distinction that trips people up:
        // an access token is for the resource server, an id_token is for the
        // application that asked. The `at_hash` binds the two, so a client cannot
        // be handed an id_token from one exchange and an access token from
        // another.
        var idToken = clientId is null
            ? null
            : handler.CreateToken(new SecurityTokenDescriptor
            {
                Issuer = issuer,
                Audience = clientId,
                Claims = new Dictionary<string, object>(claims)
                {
                    [JwtRegisteredClaimNames.Nonce] = nonce ?? string.Empty,
                    ["at_hash"] = AtHash(accessToken)
                },
                Expires = DateTime.UtcNow.Add(options.TokenLifetime),
                SigningCredentials = new SigningCredentials(key.SecurityKey, SecurityAlgorithms.RsaSha256)
            });

        return new DevTokenResponse(
            accessToken,
            "Bearer",
            (int)options.TokenLifetime.TotalSeconds,
            idToken);
    }

    /// <summary>
    /// `at_hash` from OpenID Connect core: base64url of the LEFT HALF of the
    /// SHA-256 of the access token. The left half, not the whole digest — a
    /// client library that checks this will reject the token over the other one,
    /// and the error it prints says nothing about halves.
    /// </summary>
    private static string AtHash(string accessToken)
    {
        var digest = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.ASCII.GetBytes(accessToken));

        return Base64UrlEncoder.Encode(digest.AsSpan(0, digest.Length / 2).ToArray());
    }

    /// <summary>
    /// Where this issuer is willing to send a browser back to.
    ///
    /// **It is the only thing standing between a development issuer and an open
    /// redirect**, so it is a whitelist by shape rather than a pattern: loopback
    /// only. The two apps run on 4200 and 4201 and a test server runs on
    /// whatever it likes, so the port is not fixed — the host is.
    /// </summary>
    private static bool IsLoopback(string redirectUri) =>
        Uri.TryCreate(redirectUri, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https"
        && (uri.IsLoopback || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The login page. It is deliberately the plainest HTML in the repository:
    /// it is served by the issuer, not by the shop, so it shares no tokens, no
    /// fonts and no build with either app — and a development issuer that grew a
    /// stylesheet would be a development issuer growing a frontend.
    ///
    /// The identities are the same three the realm seeds. There is no password
    /// field because there are no passwords, and adding one would be the scope
    /// creep `AddDevIssuer` names as this thing's retirement condition.
    /// </summary>
    private static string PickerPage(IEnumerable<DevIdentity> identities, string queryString)
    {
        var buttons = string.Join(Environment.NewLine, identities.Select(identity =>
            $"""
             <a class="identity" href="/dev-issuer/connect/authorize/pick{queryString}&subject={Uri.EscapeDataString(identity.Subject)}">
               <strong>{System.Net.WebUtility.HtmlEncode(identity.Name)}</strong>
               <span>{System.Net.WebUtility.HtmlEncode(identity.Role)}</span>
             </a>
             """));

        // `$$"""` and not `$"""`: the CSS below is full of braces, and in a
        // singly-interpolated raw string every one of them is an interpolation
        // hole. Two dollars means an interpolation takes TWO braces and a CSS
        // rule takes one, which is the way round that suits a stylesheet.
        return $$"""
                <!doctype html>
                <html lang="en">
                <head>
                  <meta charset="utf-8">
                  <meta name="viewport" content="width=device-width, initial-scale=1">
                  <title>Tendero — development issuer</title>
                  <style>
                    body { font: 16px system-ui, sans-serif; margin: 0; display: grid;
                           place-items: center; min-height: 100vh; background: #faece7; color: #2c2c2a; }
                    main { width: min(28rem, 90vw); }
                    h1 { font-size: 1.25rem; margin: 0 0 .25rem; }
                    p { margin: 0 0 1.5rem; color: #6b6a65; font-size: .875rem; }
                    .identity { display: flex; justify-content: space-between; align-items: center;
                                padding: .875rem 1rem; margin-bottom: .5rem; background: #fff;
                                border: 1px solid #e4dad5; border-radius: 8px;
                                text-decoration: none; color: inherit; }
                    .identity:hover { border-color: #d85a30; }
                    .identity:focus-visible { outline: 2px solid #d85a30; outline-offset: 2px; }
                    .identity span { font-size: .75rem; color: #6b6a65;
                                     font-family: ui-monospace, monospace; }
                  </style>
                </head>
                <body>
                  <main>
                    <h1>Who are you?</h1>
                    <p>The development issuer signs for these three. No passwords — that is
                       what makes it a fixture rather than an authorization server.</p>
                    {{buttons}}
                  </main>
                </body>
                </html>
                """;
    }

    /// <summary>
    /// The issuer is composed from the request rather than configured: the port
    /// changes between Aspire, a bare `dotnet run` and WebApplicationFactory, and
    /// a fixed issuer would fail validation the moment it did not match.
    /// </summary>
    private static string Issuer(HttpContext http, string path) =>
        $"{http.Request.Scheme}://{http.Request.Host}{path}";
}

